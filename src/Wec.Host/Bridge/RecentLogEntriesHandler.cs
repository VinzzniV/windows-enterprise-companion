using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Infrastructure.Logging;

namespace Wec.Host.Bridge;

public enum RecentLogLevelFilter
{
    All = 0,
    Errors,
}

public sealed record RecentLogEntriesRequest(int? Limit, RecentLogLevelFilter? LevelFilter = null);

public sealed record LogEntry(
    string Timestamp,
    string Level,
    string Source,
    string Summary,
    string TechnicalDetails,
    bool TechnicalDetailsTruncated);

public sealed record RecentLogReadCoverage(
    int AvailableFileCount,
    int EvaluatedFileCount,
    bool FileSelectionTruncated,
    long EvaluatedBytes,
    bool ByteWindowTruncated,
    int ResultLimit,
    int TotalMatched,
    bool ResultTruncated,
    int TruncatedDetailCount);

public sealed record RecentLogEntriesResponse(
    IReadOnlyList<LogEntry> Entries,
    string? Source,
    DateTimeOffset? ClearedAtUtc,
    RecentLogReadCoverage Coverage);

/// <summary>
/// Reads the recent Serilog files and returns their warning/error/fatal
/// entries, newest first — the "errors from scans and queries" feed for the
/// Verwaltung area. Entries before the clear marker (see
/// <see cref="ClearRecentLogEntriesHandler"/>) are hidden. Read-only; no credentials.
/// </summary>
internal sealed partial class RecentLogEntriesHandler
    : IActionHandler<RecentLogEntriesRequest, RecentLogEntriesResponse>
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;

    // Serilog outputTemplate: "yyyy-MM-dd HH:mm:ss.fff zzz [LVL] Source: message …"
    private static readonly Regex HeaderPattern = BuildHeaderPattern();
    private static readonly Regex LoggerSourcePattern = BuildLoggerSourcePattern();

    private static readonly HashSet<string> AllLevels = new(StringComparer.Ordinal) { "WRN", "ERR", "FTL" };
    private static readonly HashSet<string> ErrorLevels = new(StringComparer.Ordinal) { "ERR", "FTL" };

    private readonly LoggingOptions _options;
    private readonly ILogger<RecentLogEntriesHandler> _logger;

    public RecentLogEntriesHandler(IOptions<LoggingOptions> options, ILogger<RecentLogEntriesHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Module => "logs";

    public string Action => "recent";

    public async Task<Result<RecentLogEntriesResponse>> HandleAsync(
        RecentLogEntriesRequest payload, CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(payload.Limit ?? DefaultLimit, 1, MaxLimit);
        RecentLogLevelFilter levelFilter = payload.LevelFilter ?? RecentLogLevelFilter.All;
        string directory = LogFiles.ResolveDirectory(_options);
        if (!Directory.Exists(directory))
        {
            return Result.Success(EmptyResponse(limit));
        }

        List<FileInfo> availableFiles = [.. new DirectoryInfo(directory)
            .GetFiles("wec-*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)];
        List<FileInfo> files = [.. availableFiles
            .Take(_options.RecentLogFileLimit)
            .OrderBy(file => file.LastWriteTimeUtc)];
        if (files.Count == 0)
        {
            return Result.Success(EmptyResponse(limit));
        }

        DateTimeOffset? clearedAt = LogFiles.ReadClearMarker(directory);

        try
        {
            var lines = new List<string>();
            long evaluatedBytes = 0;
            bool byteWindowTruncated = false;
            foreach (FileInfo file in files)
            {
                LogFileReadResult read = await ReadSharedTailAsync(
                    file.FullName, _options.RecentLogMaxBytesPerFile, cancellationToken);
                lines.AddRange(read.Lines);
                evaluatedBytes += read.EvaluatedBytes;
                byteWindowTruncated |= read.Truncated;
            }

            ParsedLogEntries parsed = ParseWarnAndError(
                lines,
                limit,
                clearedAt,
                levelFilter,
                _options.RecentLogMaxContinuationLines);
            string source = files.Count == 1
                ? files[0].Name
                : $"{files.Count} log files ({files[0].Name} … {files[^1].Name})";
            return Result.Success(new RecentLogEntriesResponse(
                parsed.Entries,
                source,
                clearedAt,
                new RecentLogReadCoverage(
                    availableFiles.Count,
                    files.Count,
                    availableFiles.Count > files.Count,
                    evaluatedBytes,
                    byteWindowTruncated,
                    limit,
                    parsed.TotalMatched,
                    parsed.ResultTruncated,
                    parsed.Entries.Count(entry => entry.TechnicalDetailsTruncated))));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Reading the log files in {Directory} failed", directory);
            return Result.Failure<RecentLogEntriesResponse>(new Error(
                ErrorCode.InternalError, "The log files could not be read.") { Details = exception.Message });
        }
    }

    // Serilog keeps the current file open; share read+write to read it live.
    internal static async Task<LogFileReadResult> ReadSharedTailAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long length = stream.Length;
        long start = Math.Max(0, length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        if (start > 0)
        {
            await reader.ReadLineAsync(cancellationToken);
        }
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
        }
        return new LogFileReadResult(lines, length - start, start > 0);
    }

    /// <summary>
    /// Groups multi-line entries, keeps warnings/errors/fatals after the clear
    /// marker, newest first, capped at <paramref name="limit"/>.
    /// </summary>
    internal static ParsedLogEntries ParseWarnAndError(
        IReadOnlyList<string> lines,
        int limit,
        DateTimeOffset? clearedAtUtc = null,
        RecentLogLevelFilter levelFilter = RecentLogLevelFilter.All,
        int maxContinuationLines = 40)
    {
        var kept = new List<LogEntry>();
        string? timestamp = null;
        string? level = null;
        var message = new List<string>();
        bool technicalDetailsTruncated = false;
        HashSet<string> keptLevels = levelFilter == RecentLogLevelFilter.Errors ? ErrorLevels : AllLevels;

        void Flush()
        {
            if (level is not null && keptLevels.Contains(level) && IsAfterClearMarker(timestamp, clearedAtUtc))
            {
                kept.Add(ToLogEntry(timestamp ?? string.Empty, level, message, technicalDetailsTruncated));
            }
        }

        foreach (string line in lines)
        {
            Match header = HeaderPattern.Match(line);
            if (header.Success)
            {
                Flush();
                timestamp = header.Groups["ts"].Value;
                level = header.Groups["lvl"].Value;
                message = [header.Groups["msg"].Value];
                technicalDetailsTruncated = false;
            }
            else if (level is not null && message.Count - 1 < maxContinuationLines)
            {
                message.Add(line);
            }
            else if (level is not null)
            {
                technicalDetailsTruncated = true;
            }
        }
        Flush();

        kept.Reverse();
        bool resultTruncated = kept.Count > limit;
        return new ParsedLogEntries(
            resultTruncated ? kept.GetRange(0, limit) : kept,
            kept.Count,
            resultTruncated);
    }

    private static LogEntry ToLogEntry(
        string timestamp,
        string level,
        List<string> message,
        bool technicalDetailsTruncated)
    {
        string technicalDetails = string.Join('\n', message).TrimEnd();
        string firstLine = message.Count > 0 ? message[0].Trim() : string.Empty;
        string source = "Application";
        string summary = firstLine;

        int separator = firstLine.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0)
        {
            string candidateSource = firstLine[..separator];
            if (LoggerSourcePattern.IsMatch(candidateSource))
            {
                source = candidateSource;
                summary = WithoutStructuredMetadata(firstLine[(separator + 2)..].Trim());
            }
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = "No summary available.";
        }

        return new LogEntry(timestamp, level, source, summary, technicalDetails, technicalDetailsTruncated);
    }

    private static string WithoutStructuredMetadata(string summary)
    {
        int metadataStart = summary.LastIndexOf(" {", StringComparison.Ordinal);
        if (metadataStart < 0)
        {
            return summary;
        }

        string candidate = summary[(metadataStart + 1)..];
        try
        {
            using JsonDocument _ = JsonDocument.Parse(candidate);
            return summary[..metadataStart].TrimEnd();
        }
        catch (JsonException)
        {
            return summary;
        }
    }

    // Unparseable timestamps stay visible — hiding them would silently drop entries.
    private static bool IsAfterClearMarker(string? timestamp, DateTimeOffset? clearedAtUtc) =>
        clearedAtUtc is null
        || timestamp is null
        || !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
        || parsed > clearedAtUtc;

    [GeneratedRegex(@"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \S+) \[(?<lvl>[A-Z]{3})\] (?<msg>.*)$")]
    private static partial Regex BuildHeaderPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.+`]+$")]
    private static partial Regex BuildLoggerSourcePattern();

    private static RecentLogEntriesResponse EmptyResponse(int limit) => new(
        [],
        null,
        null,
        new RecentLogReadCoverage(0, 0, false, 0, false, limit, 0, false, 0));
}

internal sealed record LogFileReadResult(IReadOnlyList<string> Lines, long EvaluatedBytes, bool Truncated);

internal sealed record ParsedLogEntries(IReadOnlyList<LogEntry> Entries, int TotalMatched, bool ResultTruncated);

public sealed record ClearRecentLogEntriesRequest;

public sealed record ClearRecentLogEntriesResponse(DateTimeOffset ClearedAtUtc);

/// <summary>
/// "Clears" the error-log view by stamping a marker file — the log files
/// themselves are never touched (Serilog holds the current one open, and the
/// full history stays available on disk for diagnostics).
/// </summary>
internal sealed class ClearRecentLogEntriesHandler
    : IActionHandler<ClearRecentLogEntriesRequest, ClearRecentLogEntriesResponse>
{
    private readonly LoggingOptions _options;

    public ClearRecentLogEntriesHandler(IOptions<LoggingOptions> options)
    {
        _options = options.Value;
    }

    public string Module => "logs";

    public string Action => "clearRecent";

    public Task<Result<ClearRecentLogEntriesResponse>> HandleAsync(
        ClearRecentLogEntriesRequest payload, CancellationToken cancellationToken)
    {
        string directory = LogFiles.ResolveDirectory(_options);
        Directory.CreateDirectory(directory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        File.WriteAllText(LogFiles.ClearMarkerPath(directory), now.ToString("O", CultureInfo.InvariantCulture));
        return Task.FromResult(Result.Success(new ClearRecentLogEntriesResponse(now)));
    }
}

internal static class LogFiles
{
    private const string ClearMarkerFileName = "errorlog-cleared.marker";

    public static string ResolveDirectory(LoggingOptions options) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.LogDirectory));

    public static string ClearMarkerPath(string directory) => Path.Combine(directory, ClearMarkerFileName);

    public static DateTimeOffset? ReadClearMarker(string directory)
    {
        string path = ClearMarkerPath(directory);
        if (!File.Exists(path))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset marker)
            ? marker
            : null;
    }
}
