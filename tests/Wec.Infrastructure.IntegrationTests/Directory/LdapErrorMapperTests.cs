using System.DirectoryServices.Protocols;
using Wec.Core.Results;
using Wec.Infrastructure.Directory;

namespace Wec.Infrastructure.IntegrationTests.Directory;

public sealed class LdapErrorMapperTests
{
    private const string Target = "contoso.local";

    [Fact]
    public void InvalidCredentials_MapsToAuthenticationFailed()
    {
        Error error = LdapErrorMapper.MapLdapException(49, "invalid credentials", Target);

        Assert.Equal(ErrorCode.AuthenticationFailed, error.Code);
        Assert.Contains("bind", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDown_MapsToDirectoryUnavailableNamingDcAndFirewall()
    {
        Error error = LdapErrorMapper.MapLdapException(81, "server down", Target);

        Assert.Equal(ErrorCode.DirectoryUnavailable, error.Code);
        Assert.Contains("389", error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalTimeout_MapsToConnectionTimeout()
    {
        Error error = LdapErrorMapper.MapLdapException(85, "timeout", Target);

        Assert.Equal(ErrorCode.ConnectionTimeout, error.Code);
    }

    [Fact]
    public void UnknownLdapError_KeepsDirectoryUnavailableWithCodeInDetails()
    {
        Error error = LdapErrorMapper.MapLdapException(1, "operations error", Target);

        Assert.Equal(ErrorCode.DirectoryUnavailable, error.Code);
        Assert.Contains("LDAP error 1", error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void InsufficientAccessRights_MapsToAccessDenied()
    {
        Error error = LdapErrorMapper.MapOperationResult(
            ResultCode.InsufficientAccessRights, "denied", Target);

        Assert.Equal(ErrorCode.AccessDenied, error.Code);
    }

    [Fact]
    public void NoSuchObject_MapsToNotFoundNamingContextMissing()
    {
        Error error = LdapErrorMapper.MapOperationResult(ResultCode.NoSuchObject, "no such object", Target);

        Assert.Equal(ErrorCode.NotFound, error.Code);
        Assert.Contains("naming context", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeLimitExceeded_MapsToConnectionTimeout()
    {
        Error error = LdapErrorMapper.MapOperationResult(ResultCode.TimeLimitExceeded, "slow", Target);

        Assert.Equal(ErrorCode.ConnectionTimeout, error.Code);
    }
}
