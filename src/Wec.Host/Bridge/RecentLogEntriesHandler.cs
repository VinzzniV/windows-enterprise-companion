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

public sealed record RecentLogEntriesRequest(int? Limit);

public sealed record LogEntry(
    string Timestamp,
    string Level,
    string Source,
    string Summary,
    string TechnicalDetails);

public sealed record RecentLogEntriesResponse(
    IReadOnlyList<LogEntry> Entries,
    string? Source,
    DateTimeOffset? ClearedAtUtc);

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
    private const int MaxContinuationLines = 40;
    // ponytail: 7 daily files ≈ one week of history; raise if ops needs more.
    private const int MaxLogFiles = 7;

    // Serilog outputTemplate: "yyyy-MM-dd HH:mm:ss.fff zzz [LVL] Source: message …"
    private static readonly Regex HeaderPattern = BuildHeaderPattern();
    private static readonly Regex LoggerSourcePattern = BuildLoggerSourcePattern();

    private static readonly HashSet<string> KeptLevels = new(StringComparer.Ordinal) { "WRN", "ERR", "FTL" };

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
        string directory = LogFiles.ResolveDirectory(_options);
        if (!Directory.Exists(directory))
        {
            return Result.Success(new RecentLogEntriesResponse([], null, null));
        }

        // Oldest of the kept files first so entries stay in chronological order.
        List<FileInfo> files = [.. new DirectoryInfo(directory)
            .GetFiles("wec-*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxLogFiles)
            .OrderBy(file => file.LastWriteTimeUtc)];
        if (files.Count == 0)
        {
            return Result.Success(new RecentLogEntriesResponse([], null, null));
        }

        DateTimeOffset? clearedAt = LogFiles.ReadClearMarker(directory);

        try
        {
            var lines = new List<string>();
            foreach (FileInfo file in files)
            {
                lines.AddRange(await ReadSharedLinesAsync(file.FullName, cancellationToken));
            }

            IReadOnlyList<LogEntry> entries = ParseWarnAndError(lines, limit, clearedAt);
            string source = files.Count == 1
                ? files[0].Name
                : $"{files.Count} log files ({files[0].Name} … {files[^1].Name})";
            return Result.Success(new RecentLogEntriesResponse(entries, source, clearedAt));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Reading the log files in {Directory} failed", directory);
            return Result.Failure<RecentLogEntriesResponse>(new Error(
                ErrorCode.InternalError, "The log files could not be read.") { Details = exception.Message });
        }
    }

    // Serilog keeps the current file open; share read+write to read it live.
    private static async Task<List<string>> ReadSharedLinesAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// Groups multi-line entries, keeps warnings/errors/fatals after the clear
    /// marker, newest first, capped at <paramref name="limit"/>.
    /// </summary>
    internal static IReadOnlyList<LogEntry> ParseWarnAndError(
        IReadOnlyList<string> lines, int limit, DateTimeOffset? clearedAtUtc = null)
    {
        var kept = new List<LogEntry>();
        string? timestamp = null;
        string? level = null;
        var message = new List<string>();

        void Flush()
        {
            if (level is not null && KeptLevels.Contains(level) && IsAfterClearMarker(timestamp, clearedAtUtc))
            {
                kept.Add(ToLogEntry(timestamp ?? string.Empty, level, message));
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
            }
            else if (level is not null && message.Count <= MaxContinuationLines)
            {
                // Continuation of the current entry (exception/stack lines).
                message.Add(line);
            }
        }
        Flush();

        // Newest first, capped.
        kept.Reverse();
        return kept.Count > limit ? kept.GetRange(0, limit) : kept;
    }

    private static LogEntry ToLogEntry(string timestamp, string level, List<string> message)
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

        return new LogEntry(timestamp, level, source, summary, technicalDetails);
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
}

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
