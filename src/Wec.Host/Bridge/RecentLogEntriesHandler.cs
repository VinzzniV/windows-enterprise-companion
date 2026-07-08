using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Infrastructure.Logging;

namespace Wec.Host.Bridge;

public sealed record RecentLogEntriesRequest(int? Limit);

public sealed record LogEntry(string Timestamp, string Level, string Message);

public sealed record RecentLogEntriesResponse(IReadOnlyList<LogEntry> Entries, string? Source);

/// <summary>
/// Reads the newest Serilog file and returns its warning/error/fatal entries,
/// newest first — the "errors from scans and queries" feed for the Verwaltung
/// area. Read-only; no credentials.
/// </summary>
internal sealed partial class RecentLogEntriesHandler
    : IActionHandler<RecentLogEntriesRequest, RecentLogEntriesResponse>
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;
    private const int MaxContinuationLines = 40;

    // Serilog outputTemplate: "yyyy-MM-dd HH:mm:ss.fff zzz [LVL] Source: message …"
    private static readonly Regex HeaderPattern = BuildHeaderPattern();

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
        string directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.LogDirectory));
        if (!Directory.Exists(directory))
        {
            return Result.Success(new RecentLogEntriesResponse([], null));
        }

        FileInfo? newest = new DirectoryInfo(directory)
            .GetFiles("wec-*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is null)
        {
            return Result.Success(new RecentLogEntriesResponse([], null));
        }

        try
        {
            List<string> lines = await ReadSharedLinesAsync(newest.FullName, cancellationToken);
            return Result.Success(new RecentLogEntriesResponse(
                ParseWarnAndError(lines, limit), newest.Name));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Reading the log file {File} failed", newest.FullName);
            return Result.Failure<RecentLogEntriesResponse>(new Error(
                ErrorCode.InternalError, "The log file could not be read.") { Details = exception.Message });
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

    /// <summary>Groups multi-line entries, keeps warnings/errors/fatals, newest first, capped at <paramref name="limit"/>.</summary>
    internal static IReadOnlyList<LogEntry> ParseWarnAndError(IReadOnlyList<string> lines, int limit)
    {
        var kept = new List<LogEntry>();
        string? timestamp = null;
        string? level = null;
        var message = new List<string>();

        void Flush()
        {
            if (level is not null && KeptLevels.Contains(level))
            {
                kept.Add(new LogEntry(timestamp ?? string.Empty, level, string.Join('\n', message).TrimEnd()));
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

    [GeneratedRegex(@"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \S+) \[(?<lvl>[A-Z]{3})\] (?<msg>.*)$")]
    private static partial Regex BuildHeaderPattern();
}
