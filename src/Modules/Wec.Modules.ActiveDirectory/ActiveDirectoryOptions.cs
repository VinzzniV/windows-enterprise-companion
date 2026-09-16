using System.ComponentModel.DataAnnotations;
using Wec.Core.Configuration;

namespace Wec.Modules.ActiveDirectory;

public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "Wec:ActiveDirectory";

    [Range(1, 10_000)]
    public int PageSize { get; set; } = 500;

    [Range(100, 100_000)]
    public int MaximumSortedPageEntries { get; set; } = 10_000;

    [PositiveTimeSpan]
    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Accounts whose lastLogonTimestamp is older than this count as inactive.</summary>
    [PositiveTimeSpan]
    public TimeSpan InactivityThreshold { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Upper bound for example accounts per hygiene rule; counts stay exact.</summary>
    [Range(1, 1_000)]
    public int ExampleLimit { get; set; } = 20;

    /// <summary>Upper bound for the computer search behind the multi-host scan pickers.</summary>
    [Range(1, 10_000)]
    public int ComputerSearchLimit { get; set; } = 500;

    /// <summary>Upper bound for the OU-scoped user listing (Employee Lifecycle AD view).</summary>
    [Range(1, 10_000)]
    public int UserSearchLimit { get; set; } = 500;

    [Range(1, 1_000)]
    public int IdentityCacheMaximumEntries { get; set; } = 64;

    [PositiveTimeSpan]
    public TimeSpan IdentityCacheFreshFor { get; set; } = TimeSpan.FromMinutes(10);

    [PositiveTimeSpan]
    public TimeSpan IdentityCacheRetainFor { get; set; } = TimeSpan.FromHours(1);
}
