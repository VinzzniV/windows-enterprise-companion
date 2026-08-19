using System.ComponentModel.DataAnnotations;
using Wec.Core.Configuration;

namespace Wec.Core.Targets;

public sealed class RemoteScanOptions
{
    public const string SectionName = "Wec:Remote";

    [PositiveTimeSpan]
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [Range(1, 64)]
    public int MaxParallelScans { get; set; } = 4;

    public ConnectionOptions ToConnectionOptions() => new() { Timeout = ConnectionTimeout };
}
