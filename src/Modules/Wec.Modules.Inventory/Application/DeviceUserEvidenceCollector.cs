using System.Text.Json;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

public sealed class DeviceUserEvidenceCollector
{
    private const string CimV2Namespace = @"root\cimv2";
    private static readonly HashSet<int> BuiltInAccountRids = [500, 501, 503, 504];

    private readonly IWmiQueryService _wmiQueryService;
    private readonly InventoryOptions _options;
    private readonly ConnectionOptions _connectionOptions;

    public DeviceUserEvidenceCollector(
        IWmiQueryService wmiQueryService,
        Microsoft.Extensions.Options.IOptions<InventoryOptions> options,
        Microsoft.Extensions.Options.IOptions<RemoteScanOptions> remoteScanOptions)
    {
        _wmiQueryService = wmiQueryService;
        _options = options.Value;
        _connectionOptions = remoteScanOptions.Value.ToConnectionOptions();
    }

    public async Task<DeviceUserEvidence> CollectAsync(
        ScanTarget target,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        InteractiveCapture interactive = await CaptureInteractiveUserAsync(target, credentials, cancellationToken);
        ProfileCapture profiles = await CaptureProfilesAsync(target, credentials, cancellationToken);
        return new DeviceUserEvidence(
            interactive.State,
            interactive.User,
            interactive.Error,
            profiles.State,
            profiles.Profiles,
            profiles.Error,
            profiles.Truncated);
    }

    private Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        ScanTarget target,
        ScanCredentials credentials,
        string wqlQuery,
        CancellationToken cancellationToken) =>
        _wmiQueryService.QueryAsync(
            target,
            credentials,
            _connectionOptions,
            CimV2Namespace,
            wqlQuery,
            cancellationToken);

    private async Task<InteractiveCapture> CaptureInteractiveUserAsync(
        ScanTarget target,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> computerSystems = await QueryAsync(
            target,
            credentials,
            "SELECT UserName FROM Win32_ComputerSystem",
            cancellationToken);
        if (computerSystems.IsFailure)
        {
            return InteractiveCapture.Unavailable(ToCaptureError(
                computerSystems.Error!,
                "Interactive domain user evidence is unavailable."));
        }

        WmiInstance? computerSystem = computerSystems.Value.Count > 0 ? computerSystems.Value[0] : null;
        string? observedAccount = computerSystem?.GetString("UserName")?.Trim();
        if (!TrySplitAccount(observedAccount, out string domain, out string accountName))
        {
            return InteractiveCapture.Available(null);
        }

        string query = $"SELECT SID, Domain, Name, LocalAccount FROM Win32_UserAccount "
            + $"WHERE Domain = '{EscapeWqlLiteral(domain)}' AND Name = '{EscapeWqlLiteral(accountName)}'";
        Result<IReadOnlyList<WmiInstance>> accounts = await QueryAsync(
            target,
            credentials,
            query,
            cancellationToken);
        if (accounts.IsFailure)
        {
            return InteractiveCapture.Unavailable(ToCaptureError(
                accounts.Error!,
                "Interactive domain user identity could not be resolved."));
        }

        WmiInstance? account = accounts.Value.FirstOrDefault(instance =>
            string.Equals(instance.GetString("Domain"), domain, StringComparison.OrdinalIgnoreCase)
            && string.Equals(instance.GetString("Name"), accountName, StringComparison.OrdinalIgnoreCase));
        string? sid = account?.GetString("SID")?.Trim();
        if (account?.GetRawValue("LocalAccount") is not false || !IsEligibleUserSid(sid))
        {
            return InteractiveCapture.Available(null);
        }

        return InteractiveCapture.Available(new InteractiveDomainUserEvidence(sid!, domain, accountName));
    }

    private async Task<ProfileCapture> CaptureProfilesAsync(
        ScanTarget target,
        ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> profileInstances = await QueryAsync(
            target,
            credentials,
            "SELECT SID, Special, LastUseTime FROM Win32_UserProfile",
            cancellationToken);
        if (profileInstances.IsFailure)
        {
            return ProfileCapture.Unavailable(ToCaptureError(
                profileInstances.Error!,
                "Local profile evidence is unavailable."));
        }

        List<LocalUserProfileEvidence> eligibleProfiles = profileInstances.Value
            .Where(instance => instance.GetRawValue("Special") is false)
            .Select(instance => new LocalUserProfileEvidence(
                instance.GetString("SID")?.Trim() ?? string.Empty,
                ToUtcTimestamp(instance.GetRawValue("LastUseTime"))))
            .Where(profile => IsEligibleUserSid(profile.Sid))
            .GroupBy(profile => profile.Sid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(profile => profile.LastUseAtUtc).First())
            .OrderBy(profile => profile.Sid, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ProfileCapture.Available(
            [.. eligibleProfiles.Take(_options.MaxUserProfiles)],
            eligibleProfiles.Count > _options.MaxUserProfiles);
    }

    internal static bool IsEligibleUserSid(string? sid)
    {
        if (string.IsNullOrWhiteSpace(sid)
            || sid.Equals("S-1-5-18", StringComparison.OrdinalIgnoreCase)
            || sid.Equals("S-1-5-19", StringComparison.OrdinalIgnoreCase)
            || sid.Equals("S-1-5-20", StringComparison.OrdinalIgnoreCase)
            || sid.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase)
            || sid.StartsWith("S-1-5-82-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] components = sid.Split('-');
        bool hasSupportedAuthority = components.Length >= 8
            && components[0].Equals("S", StringComparison.OrdinalIgnoreCase)
            && components[1] == "1"
            && ((components[2] == "5" && components[3] == "21")
                || (components[2] == "12" && components[3] == "1"));
        if (!hasSupportedAuthority || components.Skip(4).Any(component => !uint.TryParse(component, out _)))
        {
            return false;
        }

        return !int.TryParse(components[^1], out int rid) || !BuiltInAccountRids.Contains(rid);
    }

    internal static DateTimeOffset? ToUtcTimestamp(object? value) => value switch
    {
        DateTimeOffset timestamp => timestamp.ToUniversalTime(),
        DateTime timestamp when timestamp.Kind == DateTimeKind.Utc => new DateTimeOffset(timestamp),
        DateTime timestamp when timestamp.Kind == DateTimeKind.Local => new DateTimeOffset(timestamp.ToUniversalTime()),
        DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
        _ => null,
    };

    private static bool TrySplitAccount(string? value, out string domain, out string accountName)
    {
        domain = string.Empty;
        accountName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int separator = value.IndexOf('\\');
        if (separator <= 0 || separator >= value.Length - 1)
        {
            return false;
        }

        domain = value[..separator].Trim();
        accountName = value[(separator + 1)..].Trim();
        return domain.Length > 0 && accountName.Length > 0;
    }

    private static string EscapeWqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static UserEvidenceCaptureError ToCaptureError(Error error, string message) => new(
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(error.Code.ToString()),
        message);

    private sealed record InteractiveCapture(
        UserEvidenceSourceState State,
        InteractiveDomainUserEvidence? User,
        UserEvidenceCaptureError? Error)
    {
        public static InteractiveCapture Available(InteractiveDomainUserEvidence? user) =>
            new(UserEvidenceSourceState.Available, user, null);

        public static InteractiveCapture Unavailable(UserEvidenceCaptureError error) =>
            new(UserEvidenceSourceState.Unavailable, null, error);
    }

    private sealed record ProfileCapture(
        UserEvidenceSourceState State,
        IReadOnlyList<LocalUserProfileEvidence>? Profiles,
        UserEvidenceCaptureError? Error,
        bool Truncated)
    {
        public static ProfileCapture Available(
            IReadOnlyList<LocalUserProfileEvidence> profiles,
            bool truncated) =>
            new(UserEvidenceSourceState.Available, profiles, null, truncated);

        public static ProfileCapture Unavailable(UserEvidenceCaptureError error) =>
            new(UserEvidenceSourceState.Unavailable, null, error, Truncated: false);
    }
}
