using Wec.Core.Results;

namespace Wec.Core.Abstractions;

/// <summary>Password and lockout policy effective on the local machine (SAM modals). Null MaxPasswordAge = passwords never expire.</summary>
public sealed record LocalAccountPolicy(
    int MinPasswordLength,
    TimeSpan? MaxPasswordAge,
    int PasswordHistoryLength,
    int LockoutThreshold,
    TimeSpan LockoutDuration);

/// <summary>Read-only view of the local account policy; local machine only.</summary>
public interface ILocalAccountPolicyReader
{
    Result<LocalAccountPolicy> ReadAccountPolicy();
}
