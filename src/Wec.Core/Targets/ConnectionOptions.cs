namespace Wec.Core.Targets;

public sealed record ConnectionOptions
{
    public static readonly ConnectionOptions Default = new();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
