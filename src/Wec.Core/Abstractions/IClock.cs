namespace Wec.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
