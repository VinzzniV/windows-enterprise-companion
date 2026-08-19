using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.Security.Application.Checks;

/// <summary>A third-party security product registered with Windows Security Center.</summary>
internal sealed record ActiveSecurityProduct(string DisplayName);

/// <summary>
/// Reads Windows Security Center (<c>root\SecurityCenter2</c>) — the same source
/// Windows itself uses to decide "is antivirus / a firewall present". Unlike the
/// Defender-native and Windows-Firewall-native WMI classes, it is aware of
/// third-party suites (e.g. Kaspersky), so a machine protected by such a suite is
/// not falsely reported as "antivirus/firewall off". A provider failure is
/// returned explicitly: falling back to a native provider would turn missing
/// Security Center coverage into a plausible but unverified result.
/// </summary>
internal static class SecurityCenterProducts
{
    private const string Namespace = @"root\SecurityCenter2";

    public static Task<Result<ActiveSecurityProduct?>> ActiveAntivirusAsync(
        IWmiQueryService wmiQueryService, SecurityScanContext context, CancellationToken cancellationToken) =>
        FirstActiveThirdPartyAsync(wmiQueryService, context, "AntiVirusProduct", cancellationToken);

    public static Task<Result<ActiveSecurityProduct?>> ActiveFirewallAsync(
        IWmiQueryService wmiQueryService, SecurityScanContext context, CancellationToken cancellationToken) =>
        FirstActiveThirdPartyAsync(wmiQueryService, context, "FirewallProduct", cancellationToken);

    private static async Task<Result<ActiveSecurityProduct?>> FirstActiveThirdPartyAsync(
        IWmiQueryService wmiQueryService,
        SecurityScanContext context,
        string className,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>>? result = await wmiQueryService.QueryAsync(
            context, Namespace, $"SELECT displayName, productState FROM {className}", cancellationToken);

        if (result is null)
        {
            return Result.Failure<ActiveSecurityProduct?>(new Error(
                ErrorCode.InternalError,
                "Windows Security Center returned no execution result."));
        }

        if (result.IsFailure)
        {
            return Result.Failure<ActiveSecurityProduct?>(result.Error!);
        }

        foreach (WmiInstance product in result.Value)
        {
            string displayName = product.GetString("displayName") ?? string.Empty;
            if (IsMicrosoftBuiltIn(displayName) || !IsEnabled(product.GetInteger("productState")))
            {
                continue;
            }

            return Result.Success<ActiveSecurityProduct?>(new ActiveSecurityProduct(displayName));
        }

        return Result.Success<ActiveSecurityProduct?>(null);
    }

    private static bool IsMicrosoftBuiltIn(string displayName) =>
        displayName.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
        || displayName.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase)
        || displayName.Contains("Windows Firewall", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// productState is a 24-bit mask, conventionally read as six hex digits
    /// [provider][scanner][signature]. The scanner byte's 0x10 bit is the
    /// on/off flag (0x10 = real-time protection active, 0x00 = off,
    /// 0x01 = snoozed).
    /// </summary>
    internal static bool IsEnabled(long? productState) =>
        productState is not null && ((productState.Value >> 8) & 0x10) == 0x10;
}
