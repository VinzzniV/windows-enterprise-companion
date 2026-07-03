using System.Text.RegularExpressions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal enum AdminMemberKind
{
    User,
    Group,
    SystemAccount,
    Unknown,
}

internal sealed record AdminGroupMember(string Domain, string Name, AdminMemberKind Kind)
{
    public string QualifiedName => string.IsNullOrEmpty(Domain) ? Name : $@"{Domain}\{Name}";
}

internal sealed partial class LocalAdministratorsCheck : ISecurityCheck
{
    private const string CimV2Namespace = @"root\cimv2";
    private const string AdministratorsGroupSid = "S-1-5-32-544";

    // Well-known broad principals that do not belong in Administrators.
    // English and German names; membership is matched by name because
    // Win32_GroupUser exposes member references by Domain/Name, not SID.
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

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        // Resolve the group by well-known SID: the display name is localized
        Result<IReadOnlyList<WmiInstance>> group = await _wmiQueryService.QueryAsync(
            context,
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

        string groupDomain = group.Value[0].GetString("Domain") ?? context.Target.DisplayName;
        string groupName = group.Value[0].GetString("Name") ?? "Administrators";

        Result<IReadOnlyList<WmiInstance>> members = await _wmiQueryService.QueryAsync(
            context,
            CimV2Namespace,
            "SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = "
                + $"\"Win32_Group.Domain='{EscapeWqlLiteral(groupDomain)}',Name='{EscapeWqlLiteral(groupName)}'\"",
            cancellationToken);

        if (members.IsFailure)
        {
            return [NotRunFinding(members.Error!, capturedAtUtc)];
        }

        List<AdminGroupMember> parsedMembers = members.Value
            .Select(member => ParseMember(member.GetRawValue("PartComponent")))
            .Where(member => member is not null)
            .Select(member => member!)
            .ToList();

        List<AdminGroupMember> riskyMembers = parsedMembers
            .Where(member => RiskyMemberNames.Contains(member.Name))
            .ToList();

        var findings = new List<SecurityFinding>
        {
            MembershipFinding(parsedMembers, groupDomain, members.Value.Count, capturedAtUtc),
        };

        if (riskyMembers.Count > 0)
        {
            findings.Add(RiskyMembersFinding(riskyMembers, capturedAtUtc));
        }

        return findings;
    }

    /// <summary>
    /// Win32_GroupUser.PartComponent arrives in different shapes depending on
    /// the transport: a nested instance (CIM, normalized to WmiInstance by the
    /// seam) or a DMTF reference string (legacy WMI). Both are handled;
    /// anything else is skipped and surfaces via the unparsed count.
    /// </summary>
    internal static AdminGroupMember? ParseMember(object? partComponent) => partComponent switch
    {
        WmiInstance reference => FromReferenceInstance(reference),
        string referencePath => FromDmtfReferencePath(referencePath),
        _ => null,
    };

    private static AdminGroupMember? FromReferenceInstance(WmiInstance reference)
    {
        string? name = reference.GetString("Name");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return new AdminGroupMember(
            reference.GetString("Domain") ?? string.Empty,
            name,
            KindFromClassName(reference.GetString(WmiInstance.ClassNameProperty)));
    }

    // Matches e.g. \\PC\root\cimv2:Win32_UserAccount.Domain="CONTOSO",Name="jdoe"
    // and the unprefixed Win32_Group.Domain="PC",Name="Administrators" form.
    [GeneratedRegex("(?<class>Win32_\\w+)\\.Domain=\"(?<domain>[^\"]*)\",Name=\"(?<name>[^\"]*)\"")]
    private static partial Regex DmtfReferenceRegex();

    private static AdminGroupMember? FromDmtfReferencePath(string referencePath)
    {
        Match match = DmtfReferenceRegex().Match(referencePath);
        if (!match.Success)
        {
            return null;
        }

        return new AdminGroupMember(
            match.Groups["domain"].Value,
            match.Groups["name"].Value,
            KindFromClassName(match.Groups["class"].Value));
    }

    private static AdminMemberKind KindFromClassName(string? className) => className switch
    {
        "Win32_UserAccount" => AdminMemberKind.User,
        "Win32_Group" => AdminMemberKind.Group,
        "Win32_SystemAccount" => AdminMemberKind.SystemAccount,
        _ => AdminMemberKind.Unknown,
    };

    private static string DescribeMember(AdminGroupMember member, string groupDomain)
    {
        bool isLocalPrincipal = member.Domain.Equals(groupDomain, StringComparison.OrdinalIgnoreCase);
        string scope = member.Kind == AdminMemberKind.SystemAccount
            ? "built-in"
            : isLocalPrincipal ? "local" : "domain";
        string kind = member.Kind switch
        {
            AdminMemberKind.User => "user",
            AdminMemberKind.Group => "group",
            AdminMemberKind.SystemAccount => "account",
            _ => "principal",
        };
        return $"{member.QualifiedName} ({scope} {kind})";
    }

    private static string EscapeWqlLiteral(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal);

    private SecurityFinding MembershipFinding(
        List<AdminGroupMember> members,
        string groupDomain,
        int rawMemberCount,
        DateTimeOffset capturedAtUtc)
    {
        var evidence = new Dictionary<string, string>
        {
            ["memberCount"] = members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["members"] = members.Count > 0
                ? string.Join("; ", members.Select(member => DescribeMember(member, groupDomain)))
                : "(none parsed)",
        };
        int unparsedCount = rawMemberCount - members.Count;
        if (unparsedCount > 0)
        {
            evidence["unparsedMemberReferences"] = unparsedCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new SecurityFinding(
            $"{CheckId}-MEMBERSHIP",
            $"Local Administrators group has {members.Count} members",
            "Documentation of the current local Administrators membership. Review whether every "
                + "entry still needs full administrative rights on this machine.",
            FindingSeverity.Info,
            FindingCategory.Accounts,
            "Local Administrators group",
            evidence,
            "Keep the group as small as possible; prefer just-in-time or dedicated admin accounts.",
            RequiredPrivilege: null,
            capturedAtUtc);
    }

    private SecurityFinding RiskyMembersFinding(List<AdminGroupMember> riskyMembers, DateTimeOffset capturedAtUtc) => new(
        $"{CheckId}-RISKY",
        "Broad principals are members of the local Administrators group",
        "Well-known broad groups or guest principals are members of Administrators. Every user "
            + "covered by them has full control of this machine.",
        FindingSeverity.Medium,
        FindingCategory.Accounts,
        "Local Administrators group",
        new Dictionary<string, string>
        {
            ["riskyMembers"] = string.Join("; ", riskyMembers.Select(member => member.QualifiedName)),
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
