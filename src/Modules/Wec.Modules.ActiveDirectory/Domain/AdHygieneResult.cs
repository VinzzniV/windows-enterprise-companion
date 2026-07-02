namespace Wec.Modules.ActiveDirectory.Domain;

public sealed record AdAccountInfo(
    string Name,
    string DistinguishedName,
    DateTimeOffset? LastLogonUtc);

/// <summary>
/// One hygiene rule outcome. <see cref="MatchCount"/> is always exact;
/// <see cref="Examples"/> is bounded by the ExampleLimit option.
/// </summary>
public sealed record AdHygieneRule(
    string RuleId,
    string Title,
    int MatchCount,
    IReadOnlyList<AdAccountInfo> Examples,
    string Recommendation);

public sealed record PrivilegedGroupInfo(
    string GroupName,
    string DistinguishedName,
    int DirectMemberCount,
    IReadOnlyList<string> MemberDistinguishedNames);

public sealed record AdHygieneResult(
    bool DomainJoined,
    string? DomainName,
    IReadOnlyList<PrivilegedGroupInfo> PrivilegedGroups,
    IReadOnlyList<AdHygieneRule> Rules,
    DateTimeOffset CapturedAtUtc);
