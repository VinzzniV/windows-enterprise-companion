using System.Globalization;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class AccountPolicyCheck : ISecurityCheck
{
    private readonly ILocalAccountPolicyReader _accountPolicyReader;
    private readonly IClock _clock;
    private readonly SecurityOptions _options;

    public AccountPolicyCheck(
        ILocalAccountPolicyReader accountPolicyReader,
        IClock clock,
        IOptions<SecurityOptions> options)
    {
        _accountPolicyReader = accountPolicyReader;
        _clock = clock;
        _options = options.Value;
    }

    public string CheckId => "WEC-SEC-ACCOUNTPOLICY";

    public Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (!context.Target.IsLocal)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([CheckFindings.LocalOnly(
                CheckId,
                "Local account policy was not checked on the remote target",
                FindingCategory.Accounts,
                "Local account policy",
                context.Target.DisplayName,
                capturedAtUtc)]);
        }

        Result<LocalAccountPolicy> policy = _accountPolicyReader.ReadAccountPolicy();
        if (policy.IsFailure)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([CheckFindings.NotRun(
                CheckId,
                "Local account policy could not be read",
                FindingCategory.Accounts,
                "Local account policy",
                "Retry the scan; if it keeps failing check the Workstation service.",
                policy.Error!,
                capturedAtUtc)]);
        }

        var findings = new List<SecurityFinding>();

        if (policy.Value.MinPasswordLength < _options.MinimumPasswordLength)
        {
            findings.Add(new SecurityFinding(
                $"{CheckId}-PWLEN",
                $"Minimum password length is {policy.Value.MinPasswordLength}",
                "The local policy allows passwords shorter than the configured baseline. Short "
                    + "passwords fall quickly to offline cracking and spraying.",
                FindingSeverity.Medium,
                FindingCategory.Accounts,
                "Local account policy",
                new Dictionary<string, string>
                {
                    ["minPasswordLength"] = policy.Value.MinPasswordLength.ToString(CultureInfo.InvariantCulture),
                    ["baseline"] = _options.MinimumPasswordLength.ToString(CultureInfo.InvariantCulture),
                },
                $"Raise the minimum password length to at least {_options.MinimumPasswordLength} "
                    + "(secpol.msc > Account Policies, or via GPO on domain machines).",
                RequiredPrivilege: null,
                capturedAtUtc));
        }

        if (policy.Value.LockoutThreshold == 0)
        {
            findings.Add(new SecurityFinding(
                $"{CheckId}-NOLOCKOUT",
                "Account lockout is disabled",
                "The lockout threshold is 0: unlimited password attempts are possible, which "
                    + "makes online brute-force and spraying attacks practical.",
                FindingSeverity.Medium,
                FindingCategory.Accounts,
                "Local account policy",
                new Dictionary<string, string>
                {
                    ["lockoutThreshold"] = "0 (never locks)",
                },
                "Set a lockout threshold (commonly 5-10 attempts) with a sensible lockout duration.",
                RequiredPrivilege: null,
                capturedAtUtc));
        }

        return Task.FromResult<IReadOnlyList<SecurityFinding>>(findings);
    }
}
