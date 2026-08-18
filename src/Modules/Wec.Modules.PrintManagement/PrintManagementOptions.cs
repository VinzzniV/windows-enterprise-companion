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

    /// <summary>Subnets (CIDR) printers are allowed to live in — anything else is flagged.</summary>
    public IReadOnlyList<string> PrinterSubnets { get; set; } =
        ["172.20.20.0/24", "172.21.18.0/24", "172.21.20.0/24"];

    /// <summary>Old/decommissioned subnets whose printers should be migrated to <see cref="PrinterSubnets"/>.</summary>
    public IReadOnlyList<string> LegacyPrinterSubnets { get; set; } = ["192.168.20.0/24"];

    /// <summary>
    /// Software pseudo-printers that must not appear in the inventory. Matched
    /// case-insensitively against the start of the queue name and the driver name,
    /// so version suffixes ("… Writer v4") are covered too.
    /// </summary>
    public IReadOnlyList<string> IgnoredQueues { get; set; } =
        ["Microsoft Print to PDF", "Microsoft XPS Document Writer", "PDFCreator"];

    /// <summary>DHCP server queried for reservations (host name or IP). Null/empty = ask per check.</summary>
    public string? DhcpServer { get; set; }

    /// <summary>How long a single DHCP reservation query may run before it is cancelled.</summary>
    public TimeSpan DhcpQueryTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
