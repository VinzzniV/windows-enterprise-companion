using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.PrintManagement;

public sealed class PrintManagementOptions
{
    public const string SectionName = "Wec:PrintManagement";

    /// <summary>SNMP read community — configuration only, never logged (ADR 0009).</summary>
    public string SnmpCommunity { get; set; } = "public";

    [Range(1, 65_535)]
    public int SnmpPort { get; set; } = 161;

    public TimeSpan SnmpTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Snapshots kept per print server (lease diffs need history).</summary>
    [Range(2, 1_000)]
    public int HistoryLimit { get; set; } = 50;

    /// <summary>Supplies at or below this percentage count as low (evaluated at capture time).</summary>
    [Range(1, 99)]
    public int LowTonerThresholdPercent { get; set; } = 15;

    /// <summary>
    /// Expected SMTP server the printers must send through, for the notification
    /// check. Supports a <c>{site}</c> token replaced by the device's site code
    /// (e.g. <c>{site}-srvmail.kauth.local</c>). Null/empty = only require a
    /// non-empty server, don't enforce a specific host.
    /// </summary>
    public string? ExpectedSmtpServer { get; set; }

    /// <summary>
    /// Expected recipient of the low-toner event report (the external service
    /// provider). Null/empty = only require some recipient, don't enforce which.
    /// </summary>
    public string? ExpectedEventRecipient { get; set; }
}
