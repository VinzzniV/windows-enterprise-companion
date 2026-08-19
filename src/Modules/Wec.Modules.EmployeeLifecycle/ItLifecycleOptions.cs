using System.ComponentModel.DataAnnotations;
using Wec.Core.Configuration;

namespace Wec.Modules.EmployeeLifecycle;

public sealed class ItLifecycleOptions : IValidatableObject
{
    public const string SectionName = "Wec:ItLifecycle";

    [Range(1, 100_000)]
    public int InventoryLimit { get; set; } = 10_000;

    [Range(1, 3650)]
    public int StaleWarningDays { get; set; } = 60;

    [Range(1, 3650)]
    public int StaleCriticalDays { get; set; } = 90;

    public string TargetAgentVersion { get; set; } = string.Empty;

    public string TargetKesVersion { get; set; } = string.Empty;

    public KasperskyOptions Kaspersky { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StaleCriticalDays <= StaleWarningDays)
        {
            yield return new ValidationResult(
                "StaleCriticalDays must be greater than StaleWarningDays.",
                [nameof(StaleCriticalDays), nameof(StaleWarningDays)]);
        }

        if (Kaspersky.RequestTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "Kaspersky RequestTimeout must be greater than zero.",
                [nameof(Kaspersky)]);
        }

        if (Kaspersky.Port is < 1 or > 65_535)
        {
            yield return new ValidationResult(
                "Kaspersky Port must be between 1 and 65535.",
                [nameof(Kaspersky)]);
        }
    }
}

public sealed class KasperskyOptions
{
    public List<string> ExcludedAdministrationGroups { get; set; } =
        ["Nicht für Kaspersky geeignete Geräte"];

    public string Server { get; set; } = string.Empty;

    [Range(1, 65_535)]
    public int Port { get; set; } = 13_299;

    [PositiveTimeSpan]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Optional SHA-256/SHA-1 thumbprint for a private KSC server certificate.
    /// Normal platform certificate validation remains active when this is empty.
    /// </summary>
    public string TrustedCertificateThumbprint { get; set; } = string.Empty;
}
