using System.ComponentModel.DataAnnotations;
using Serilog.Events;

namespace Wec.Infrastructure.Logging;

public sealed class LoggingOptions
{
    public const string SectionName = "Wec:Logging";

    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    [Required]
    public string LogDirectory { get; set; } = string.Empty;

    [Range(1, 365)]
    public int RetainedFileCountLimit { get; set; } = 14;

    [Range(1, 365)]
    public int RecentLogFileLimit { get; set; } = 7;

    [Range(65536, int.MaxValue)]
    public int RecentLogMaxBytesPerFile { get; set; } = 4194304;

    [Range(1, 1000)]
    public int RecentLogMaxContinuationLines { get; set; } = 40;
}
