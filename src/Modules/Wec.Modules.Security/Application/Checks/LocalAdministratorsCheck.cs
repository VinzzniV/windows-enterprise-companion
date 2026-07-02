using System.Text.RegularExpressions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed partial class LocalAdministratorsCheck : ISecurityCheck
{
    private const string CimV2Namespace = @"root\cimv2";
    private const string AdministratorsGroupSid = "S-1-5-32-544";

    // Well-known broad principals that do not belong in Administrators.
    // English and German names; membership is matched by name because
    // Win32_GroupUser exposes member references as text, not SIDs.
    private static readonly HashSet<string> RiskyMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Everyone", "Jeder",
        "Authenticated Users", "Authentifizierte Benutzer",
        "Users", "Benutzer",
        "Domain Users", "Domänen-Benutzer",
        "Guests", "Gäste",
        "Guest", "Gast",
        "INTERACTIVE", "INTERAKTIV",
    };

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public LocalAdministratorsCheck(IWmiQueryService wmiQueryService, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-LOCALADMINS";

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        // Resolve the group by well-known SID: the display name is localized
        Result<IReadOnlyList<WmiInstance>> group = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            $"SELECT Name, Domain FROM Win32_Group WHERE SID = '{AdministratorsGroupSid}' AND LocalAccount = TRUE",
            cancellationToken);

        if (group.IsFailure || group.Value.Count == 0)
        {
            Error error = group.IsFailure
                ? group.Error!
                : Error.NotFound("The local Administrators group (S-1-5-32-544) was not found.");
            return [NotRunFinding(error, capturedAtUtc)];
        }

        string groupDomain = EscapeWqlLiteral(group.Value[0].GetString("Domain") ?? Environment.MachineName);
        string groupName = EscapeWqlLiteral(group.Value[0].GetString("Name") ?? "Administrators");

        Result<IReadOnlyList<WmiInstance>> members = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = "
                + $"\"Win32_Group.Domain='{groupDomain}',Name='{groupName}'\"",
            cancellationToken);

        if (members.IsFailure)
        {
            return [NotRunFinding(members.Error!, capturedAtUtc)];
        }

        List<string> memberNames = members.Value
            .Select(member => ParseMemberName(member.GetString("PartComponent")))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();

        List<string> riskyMembers = memberNames
            .Where(name => RiskyMemberNames.Contains(NamePart(name)))
            .ToList();

        var findings = new List<SecurityFinding>
        {
            MembershipFinding(memberNames, capturedAtUtc),
        };

        if (riskyMembers.Count > 0)
        {
            findings.Add(RiskyMembersFinding(riskyMembers, capturedAtUtc));
        }

        return findings;
    }

    [GeneratedRegex("Domain=\"(?<domain>[^\"]*)\",Name=\"(?<name>[^\"]*)\"")]
    private static partial Regex MemberReferenceRegex();

    private static string? ParseMemberName(string? partComponent)
    {
        if (partComponent is null)
        {
            return null;
        }

        Match match = MemberReferenceRegex().Match(partComponent);
        return match.Success ? $"{match.Groups["domain"].Value}\\{match.Groups["name"].Value}" : null;
    }

    private static string NamePart(string qualifiedName) =>
        qualifiedName.Contains('\\', StringComparison.Ordinal)
            ? qualifiedName[(qualifiedName.IndexOf('\\', StringComparison.Ordinal) + 1)..]
            : qualifiedName;

    private static string EscapeWqlLiteral(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal);

    private SecurityFinding MembershipFinding(List<string> memberNames, DateTimeOffset capturedAtUtc) => new(
        $"{CheckId}-MEMBERSHIP",
        $"Local Administrators group has {memberNames.Count} members",
        "Documentation of the current local Administrators membership. Review whether every "
            + "entry still needs full administrative rights on this machine.",
        FindingSeverity.Info,
        FindingCategory.Accounts,
        "Local Administrators group",
        new Dictionary<string, string>
        {
            ["memberCount"] = memberNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["members"] = memberNames.Count > 0 ? string.Join("; ", memberNames) : "(none parsed)",
        },
        "Keep the group as small as possible; prefer just-in-time or dedicated admin accounts.",
        RequiredPrivilege: null,
        capturedAtUtc);

    private SecurityFinding RiskyMembersFinding(List<string> riskyMembers, DateTimeOffset capturedAtUtc) => new(
        $"{CheckId}-RISKY",
        "Broad principals are members of the local Administrators group",
        "Well-known broad groups or guest principals are members of Administrators. Every user "
            + "covered by them has full control of this machine.",
        FindingSeverity.Medium,
        FindingCategory.Accounts,
        "Local Administrators group",
        new Dictionary<string, string>
        {
            ["riskyMembers"] = string.Join("; ", riskyMembers),
            ["matchedBy"] = "well-known broad principal names (EN/DE)",
        },
        "Remove broad principals (Everyone, Users, Authenticated Users, Guests …) from the "
            + "Administrators group and grant admin rights to specific accounts only.",
        RequiredPrivilege: null,
        capturedAtUtc);

    private SecurityFinding NotRunFinding(Error error, DateTimeOffset capturedAtUtc) =>
        CheckFindings.NotRun(
            CheckId,
            "Local Administrators membership could not be read",
            FindingCategory.Accounts,
            "Local Administrators group",
            "Verify the Windows Management Instrumentation service and retry the scan.",
            error,
            capturedAtUtc);
}
