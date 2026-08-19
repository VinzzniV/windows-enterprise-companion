using System.Globalization;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal enum OsLifecycleTrack
{
    HomePro,
    EnterpriseEducation,
    EnterpriseLtsc,
    IotEnterpriseLtsc,
}

internal sealed record OsLifecycleEntry(string Name, DateOnly LastSupportedDate);

internal sealed class OsSupportCheck : ISecurityCheck
{
    private const string CimV2Namespace = @"root\cimv2";
    private const string LifecycleSnapshotDate = "2026-08-19";

    // Offline snapshot derived from Microsoft's Windows release-health and
    // product-lifecycle tables. Build alone is deliberately not sufficient:
    // the same build can follow GA, Enterprise/Education, or LTSC servicing.
    private static readonly Dictionary<(int Build, OsLifecycleTrack Track), OsLifecycleEntry> LifecycleTable =
        new Dictionary<(int, OsLifecycleTrack), OsLifecycleEntry>
        {
            [(10240, OsLifecycleTrack.EnterpriseLtsc)] = Entry("Windows 10 Enterprise LTSB 2015", 2025, 10, 14),
            [(10240, OsLifecycleTrack.IotEnterpriseLtsc)] = Entry("Windows 10 IoT Enterprise LTSB 2015", 2025, 10, 14),
            [(14393, OsLifecycleTrack.EnterpriseLtsc)] = Entry("Windows 10 Enterprise LTSB 2016", 2026, 10, 13),
            [(14393, OsLifecycleTrack.IotEnterpriseLtsc)] = Entry("Windows 10 IoT Enterprise LTSB 2016", 2026, 10, 13),
            [(17763, OsLifecycleTrack.EnterpriseLtsc)] = Entry("Windows 10 Enterprise LTSC 2019", 2029, 1, 9),
            [(17763, OsLifecycleTrack.IotEnterpriseLtsc)] = Entry("Windows 10 IoT Enterprise LTSC 2019", 2029, 1, 9),
            [(19044, OsLifecycleTrack.HomePro)] = Entry("Windows 10 21H2 Home/Pro", 2023, 6, 13),
            [(19044, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 10 21H2 Enterprise/Education", 2024, 6, 11),
            [(19044, OsLifecycleTrack.EnterpriseLtsc)] = Entry("Windows 10 Enterprise LTSC 2021", 2027, 1, 12),
            [(19044, OsLifecycleTrack.IotEnterpriseLtsc)] = Entry("Windows 10 IoT Enterprise LTSC 2021", 2032, 1, 13),
            [(19045, OsLifecycleTrack.HomePro)] = Entry("Windows 10 22H2 Home/Pro", 2025, 10, 14),
            [(19045, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 10 22H2 Enterprise/Education", 2025, 10, 14),
            [(22000, OsLifecycleTrack.HomePro)] = Entry("Windows 11 21H2 Home/Pro", 2023, 10, 10),
            [(22000, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 11 21H2 Enterprise/Education", 2024, 10, 8),
            [(22621, OsLifecycleTrack.HomePro)] = Entry("Windows 11 22H2 Home/Pro", 2024, 10, 8),
            [(22621, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 11 22H2 Enterprise/Education", 2025, 10, 14),
            [(22631, OsLifecycleTrack.HomePro)] = Entry("Windows 11 23H2 Home/Pro", 2025, 11, 11),
            [(22631, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 11 23H2 Enterprise/Education", 2026, 11, 10),
            [(26100, OsLifecycleTrack.HomePro)] = Entry("Windows 11 24H2 Home/Pro", 2026, 10, 13),
            [(26100, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 11 24H2 Enterprise/Education", 2027, 10, 12),
            [(26100, OsLifecycleTrack.EnterpriseLtsc)] = Entry("Windows 11 Enterprise LTSC 2024", 2029, 10, 9),
            [(26100, OsLifecycleTrack.IotEnterpriseLtsc)] = Entry("Windows 11 IoT Enterprise LTSC 2024", 2034, 10, 10),
            [(26200, OsLifecycleTrack.HomePro)] = Entry("Windows 11 25H2 Home/Pro", 2027, 10, 12),
            [(26200, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 11 25H2 Enterprise/Education", 2028, 10, 10),
            [(28000, OsLifecycleTrack.HomePro)] = Entry("Windows 11 26H1 Home/Pro", 2028, 3, 14),
            [(28000, OsLifecycleTrack.EnterpriseEducation)] = Entry("Windows 11 26H1 Enterprise/Education", 2029, 3, 13),
        };

    private static readonly TimeZoneInfo MicrosoftLifecycleTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public OsSupportCheck(IWmiQueryService wmiQueryService, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-OSSUPPORT";

    public async Task<SecurityCheckResult> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> operatingSystems = await _wmiQueryService.QueryAsync(
            context,
            CimV2Namespace,
            "SELECT Caption, BuildNumber, OperatingSystemSKU, ProductType FROM Win32_OperatingSystem",
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (operatingSystems.IsFailure || operatingSystems.Value.Count == 0)
        {
            Error error = operatingSystems.IsFailure
                ? operatingSystems.Error!
                : Error.NotFound("Win32_OperatingSystem returned no instance.");
            return CheckFindings.NotRun(CheckId, error);
        }

        WmiInstance operatingSystem = operatingSystems.Value[0];
        string caption = operatingSystem.GetString("Caption") ?? "Unknown Windows";
        string buildNumberText = operatingSystem.GetString("BuildNumber") ?? string.Empty;
        long? skuValue = operatingSystem.GetInteger("OperatingSystemSKU");
        long? productType = operatingSystem.GetInteger("ProductType");

        if (!int.TryParse(buildNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int buildNumber))
        {
            return UnknownResult(
                caption,
                buildNumberText,
                skuValue,
                productType,
                track: null,
                "The operating-system build number is missing or invalid.");
        }

        if (productType != 1)
        {
            return UnknownResult(
                caption,
                buildNumberText,
                skuValue,
                productType,
                track: null,
                productType is null
                    ? "The Windows product type is missing."
                    : "The offline lifecycle table covers Windows client products only.");
        }

        OsLifecycleTrack? track = skuValue is >= int.MinValue and <= int.MaxValue
            ? TrackFromSku((int)skuValue.Value)
            : null;
        if (track is null)
        {
            return UnknownResult(
                caption,
                buildNumberText,
                skuValue,
                productType,
                track,
                skuValue is null
                    ? "The Windows edition SKU is missing."
                    : $"Windows edition SKU {skuValue.Value} is not mapped to a verified lifecycle track.");
        }

        if (!LifecycleTable.TryGetValue((buildNumber, track.Value), out OsLifecycleEntry? lifecycle))
        {
            return UnknownResult(
                caption,
                buildNumberText,
                skuValue,
                productType,
                track,
                "This build and lifecycle-track combination is not in the offline lifecycle table.");
        }

        if (capturedAtUtc <= EndOfSupportUtc(lifecycle.LastSupportedDate))
        {
            return SecurityCheckResult.Succeeded(CheckId);
        }

        return SecurityCheckResult.Succeeded(CheckId, [new SecurityFinding(
            $"{CheckId}-EOL",
            $"{lifecycle.Name} is past standard servicing",
            $"Microsoft's standard servicing for {lifecycle.Name} (build {buildNumber}) ended on "
                + $"{lifecycle.LastSupportedDate:yyyy-MM-dd}. Separately licensed extended update programs "
                + "may change the update entitlement for an individual device.",
            FindingSeverity.Medium,
            FindingCategory.OperatingSystem,
            caption,
            Evidence(caption, buildNumberText, skuValue, productType, track, lifecycle.LastSupportedDate),
            "Upgrade to a supported Windows release or verify and document the device's extended update entitlement.",
            RequiredPrivilege: null,
            capturedAtUtc)]);
    }

    private static OsLifecycleEntry Entry(string name, int year, int month, int day) =>
        new(name, new DateOnly(year, month, day));

    private static OsLifecycleTrack? TrackFromSku(int sku) => sku switch
    {
        // Professional, Core/Home, Pro for Workstations, Pro Education (including N variants).
        48 or 49 or 98 or 99 or 100 or 101 or 161 or 162 or 164 or 165 => OsLifecycleTrack.HomePro,

        // Enterprise, Education, Enterprise subscription/G, multi-session, and IoT Enterprise GA.
        4 or 27 or 70 or 72 or 84 or 121 or 122 or 140 or 141 or 171 or 172 or 175 or 188 =>
            OsLifecycleTrack.EnterpriseEducation,

        // Enterprise S/N and their evaluation variants identify LTSC/LTSB.
        125 or 126 or 129 or 130 => OsLifecycleTrack.EnterpriseLtsc,
        191 => OsLifecycleTrack.IotEnterpriseLtsc,
        _ => null,
    };

    private static DateTimeOffset EndOfSupportUtc(DateOnly lastSupportedDate)
    {
        DateTime endOfPacificDay = lastSupportedDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endOfPacificDay, MicrosoftLifecycleTimeZone));
    }

    private SecurityCheckResult UnknownResult(
        string caption,
        string buildNumber,
        long? sku,
        long? productType,
        OsLifecycleTrack? track,
        string reason) => SecurityCheckResult.DidNotRun(
        CheckId,
        Error.NotFound(
            $"{reason} Lifecycle is unknown for '{caption}', build '{buildNumber}', SKU "
            + $"'{sku?.ToString(CultureInfo.InvariantCulture) ?? "missing"}', product type "
            + $"'{productType?.ToString(CultureInfo.InvariantCulture) ?? "missing"}', track '{track?.ToString() ?? "Unknown"}'."));

    private static Dictionary<string, string> Evidence(
        string caption,
        string buildNumber,
        long? sku,
        long? productType,
        OsLifecycleTrack? track,
        DateOnly? lastSupportedDate)
    {
        var evidence = new Dictionary<string, string>
        {
            ["caption"] = caption,
            ["buildNumber"] = string.IsNullOrEmpty(buildNumber) ? "(missing)" : buildNumber,
            ["operatingSystemSku"] = sku?.ToString(CultureInfo.InvariantCulture) ?? "(missing)",
            ["productType"] = productType?.ToString(CultureInfo.InvariantCulture) ?? "(missing)",
            ["lifecycleTrack"] = track?.ToString() ?? "Unknown",
            ["servicingChannel"] = track switch
            {
                OsLifecycleTrack.EnterpriseLtsc or OsLifecycleTrack.IotEnterpriseLtsc => "LTSC",
                OsLifecycleTrack.HomePro or OsLifecycleTrack.EnterpriseEducation => "GeneralAvailability",
                _ => "Unknown",
            },
            ["lifecycleTable"] = $"offline Microsoft snapshot {LifecycleSnapshotDate}",
        };

        if (lastSupportedDate is not null)
        {
            evidence["endOfSupport"] = lastSupportedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return evidence;
    }
}
