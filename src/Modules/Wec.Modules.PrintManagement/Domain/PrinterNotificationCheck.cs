namespace Wec.Modules.PrintManagement.Domain;

public enum NotificationCheckStatus
{
    /// <summary>All rules satisfied.</summary>
    Ok,

    /// <summary>Reachable and read, but at least one rule failed.</summary>
    Warning,

    /// <summary>Could not be checked (unreachable, not a CCRX device, login refused).</summary>
    NotChecked,
}

/// <summary>One notification-configuration rule and whether the device satisfies it.</summary>
public sealed record NotificationRule(string Id, string Title, bool Passed, string Detail);

/// <summary>
/// Result of checking whether a printer is configured to notify the external
/// service provider: SMTP on with the right server, a sender address, and a
/// low-toner event report to a recipient.
/// </summary>
public sealed record PrinterNotificationCheck(
    string Host,
    NotificationCheckStatus Status,
    IReadOnlyList<NotificationRule> Rules,
    string? Error);
