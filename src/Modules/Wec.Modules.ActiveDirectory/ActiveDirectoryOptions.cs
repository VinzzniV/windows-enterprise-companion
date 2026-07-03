using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.ActiveDirectory;

public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "Wec:ActiveDirectory";

    [Range(1, 10_000)]
    public int PageSize { get; set; } = 500;

    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Accounts whose lastLogonTimestamp is older than this count as inactive.</summary>
    public TimeSpan InactivityThreshold { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Upper bound for example accounts per hygiene rule; counts stay exact.</summary>
    [Range(1, 1_000)]
    public int ExampleLimit { get; set; } = 20;

    /// <summary>Upper bound for the computer search behind the multi-host scan pickers.</summary>
    [Range(1, 10_000)]
    public int ComputerSearchLimit { get; set; } = 500;
}
