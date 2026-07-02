using System.Globalization;
using Serilog;
using Serilog.Events;

namespace Wec.Infrastructure.Logging;

public static class SerilogConfiguration
{
    public static void Configure(LoggerConfiguration loggerConfiguration, LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(options);

        string logDirectory = Environment.ExpandEnvironmentVariables(options.LogDirectory);

        loggerConfiguration
            .MinimumLevel.Is(options.MinimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "wec-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}");

#if DEBUG
        loggerConfiguration.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
#endif
    }
}
