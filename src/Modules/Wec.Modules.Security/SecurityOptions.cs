using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Wec:Security";

    /// <summary>How many scans the history view loads (diff needs at least 2).</summary>
    [Range(2, 500)]
    public int HistoryLimit { get; set; } = 20;
}
