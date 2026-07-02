using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.ActiveDirectory;

public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "Wec:ActiveDirectory";

    [Range(1, 10_000)]
    public int PageSize { get; set; } = 500;

    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
