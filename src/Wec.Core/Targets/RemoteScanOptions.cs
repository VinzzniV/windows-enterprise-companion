using System.ComponentModel.DataAnnotations;

namespace Wec.Core.Targets;

public sealed class RemoteScanOptions
{
    public const string SectionName = "Wec:Remote";

    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [Range(1, 64)]
    public int MaxParallelScans { get; set; } = 4;

    public ConnectionOptions ToConnectionOptions() => new() { Timeout = ConnectionTimeout };
}
