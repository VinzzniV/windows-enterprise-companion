using Wec.Core.Privileges;

namespace Wec.Core.Results;

public sealed record Error(ErrorCode Code, string Message)
{
    public string? Details { get; init; }

    public PrivilegeLevel? RequiredPrivilege { get; init; }

    public static Error AccessDenied(string message, PrivilegeLevel requiredPrivilege) =>
        new(ErrorCode.AccessDenied, message) { RequiredPrivilege = requiredPrivilege };

    public static Error NotFound(string message) => new(ErrorCode.NotFound, message);

    public static Error WmiUnavailable(string message, string? details = null) =>
        new(ErrorCode.WmiUnavailable, message) { Details = details };
}
