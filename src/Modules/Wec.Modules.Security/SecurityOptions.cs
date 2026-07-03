using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Wec:Security";

    /// <summary>How many scans the history view loads (diff needs at least 2).</summary>
    [Range(2, 500)]
    public int HistoryLimit { get; set; } = 20;

    /// <summary>Defender updates several times a day; older signatures indicate a broken update pipeline.</summary>
    [Range(1, 365)]
    public int MaxDefenderSignatureAgeDays { get; set; } = 7;

    /// <summary>Days without any installed update (hotfix) before the patch level counts as stale.</summary>
    [Range(1, 730)]
    public int MaxDaysSinceLastInstalledUpdate { get; set; } = 60;

    /// <summary>Minimum local minimum-password-length policy before a finding is raised.</summary>
    [Range(1, 128)]
    public int MinimumPasswordLength { get; set; } = 8;
}
