using System.ComponentModel.DataAnnotations;
using Wec.Core.Configuration;

namespace Wec.Modules.PatchManagement;

public sealed class PatchManagementOptions
{
    public const string SectionName = "Wec:PatchManagement";

    /// <summary>opsiconfd host name or HTTPS URL used by the central session sign-in.</summary>
    public string OpsiServer { get; set; } = string.Empty;

    /// <summary>
    /// Depot the UI preselects (matched against depot id and description,
    /// case-insensitive substring). Depots double as locations (ADR 0008).
    /// </summary>
    public string DefaultDepotFilter { get; set; } = string.Empty;

    [PositiveTimeSpan]
    public TimeSpan OpsiRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>opsiconfd default port, used when the server input names none.</summary>
    [Range(1, 65_535)]
    public int DefaultServicePort { get; set; } = 4447;

    /// <summary>Explicit opt-in used when automatically restoring a saved opsi session.</summary>
    public bool TrustServerCertificate { get; set; }

    [Range(1, 10_000)]
    public int AuditHistoryLimit { get; set; } = 100;

    [PositiveTimeSpan]
    public TimeSpan ManufacturerCheckInterval { get; set; } = TimeSpan.FromDays(1);

    [PositiveTimeSpan]
    public TimeSpan ManufacturerRequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Account used by Windows OpenSSH for package operations. Authentication
    /// comes from ssh-agent or <see cref="SshIdentityFile"/>; no password is stored.
    /// </summary>
    [Required]
    public string SshUserName { get; set; } = "root";

    /// <summary>Optional private-key path. Empty uses ssh-agent/default OpenSSH identities.</summary>
    public string SshIdentityFile { get; set; } = string.Empty;

    [PositiveTimeSpan]
    public TimeSpan SshConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    [PositiveTimeSpan]
    public TimeSpan PackageCommandTimeout { get; set; } = TimeSpan.FromMinutes(30);

    [PositiveTimeSpan]
    public TimeSpan PackageTransferTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public Dictionary<string, PackageAutomationProfileOptions> PackageAutomationProfiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prepends sudo -n so an operation can never wait for a password prompt.</summary>
    public bool UseNonInteractiveSudo { get; set; }
}

public sealed class PackageAutomationProfileOptions
{
    public string ReleaseUrl { get; set; } = string.Empty;

    public string ArtifactUrlPattern { get; set; } = string.Empty;

    public string WorkbenchPath { get; set; } = string.Empty;

    public string InstallerRelativePath { get; set; } = string.Empty;

    public List<string> PreviousInstallerFileNames { get; set; } = [];

    public long MaximumArtifactBytes { get; set; } = 512L * 1024 * 1024;
}
