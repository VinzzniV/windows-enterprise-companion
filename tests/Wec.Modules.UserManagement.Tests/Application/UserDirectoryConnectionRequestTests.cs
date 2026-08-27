using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.UserManagement.Application;

namespace Wec.Modules.UserManagement.Tests.Application;

public sealed class UserDirectoryConnectionRequestTests
{
    [Fact]
    public void EmptyRequest_UsesCurrentIdentityAndNormalizesTargets()
    {
        Result<Wec.Core.Contracts.DirectoryUserReadConnection> result =
            new UserDirectoryConnectionRequest("  ", " dc01.corp.example ").ToConnection();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Domain);
        Assert.Equal("dc01.corp.example", result.Value.Server);
        Assert.Equal(CredentialMode.CurrentUser, result.Value.Credentials.Mode);
    }

    [Fact]
    public void DomainQualifiedAccount_IsCanonicalizedWithoutExposingThePassword()
    {
        Result<Wec.Core.Contracts.DirectoryUserReadConnection> result =
            new UserDirectoryConnectionRequest(
                Domain: "corp.example",
                UserName: " CORP\\reader ",
                UserDomain: "IGNORED",
                Password: "session-secret").ToConnection();

        Assert.True(result.IsSuccess);
        Assert.Equal("reader", result.Value.Credentials.UserName);
        Assert.Equal("CORP", result.Value.Credentials.Domain);
        Assert.DoesNotContain("session-secret", result.Value.Credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("session-secret", new UserDirectoryConnectionRequest(
            UserName: "CORP\\reader",
            Password: "session-secret").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitAccountWithoutPassword_IsRejected()
    {
        Result<Wec.Core.Contracts.DirectoryUserReadConnection> result =
            new UserDirectoryConnectionRequest(UserName: "reader").ToConnection();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
    }
}
