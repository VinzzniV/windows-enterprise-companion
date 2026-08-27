using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.UserManagement.Application;

public sealed record UserDirectoryConnectionRequest(
    string? Domain = null,
    string? Server = null,
    string? UserName = null,
    string? UserDomain = null,
    string? Password = null)
{
    internal Result<DirectoryUserReadConnection> ToConnection()
    {
        Result<ScanCredentials> credentials = ToCredentials();
        return credentials.IsFailure
            ? Result.Failure<DirectoryUserReadConnection>(credentials.Error!)
            : Result.Success(new DirectoryUserReadConnection(
                Normalize(Domain),
                Normalize(Server),
                credentials.Value));
    }

    private Result<ScanCredentials> ToCredentials()
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            return Result.Success(ScanCredentials.CurrentUser);
        }

        if (Password is null)
        {
            return InvalidCredentials("Explicit credentials require a password.");
        }

        return DirectoryScanCredentials.NormalizeExplicit(
            UserName, UserDomain, Password, Domain);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<ScanCredentials> InvalidCredentials(string message) =>
        Result.Failure<ScanCredentials>(new Error(ErrorCode.InvalidRequest, message));

    public override string ToString() =>
        $"UserDirectoryConnectionRequest {{ Domain = {Domain}, Server = {Server}, UserName = {UserName}, UserDomain = {UserDomain} }}";
}
