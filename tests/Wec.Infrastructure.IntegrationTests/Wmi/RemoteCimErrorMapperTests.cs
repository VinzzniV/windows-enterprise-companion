using Microsoft.Management.Infrastructure;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Infrastructure.Wmi;

namespace Wec.Infrastructure.IntegrationTests.Wmi;

public sealed class RemoteCimErrorMapperTests
{
    private const string Target = "pc-042";

    [Theory]
    [InlineData(0x80338043u)]
    [InlineData(0x8033809Du)]
    public void WsmanCredentialRejection_MapsToAuthenticationFailed(uint wsmanErrorCode)
    {
        Error error = RemoteCimErrorMapper.Map(
            NativeErrorCode.Failed, wsmanErrorCode, "denied", isRemote: true, Target);

        Assert.Equal(ErrorCode.AuthenticationFailed, error.Code);
        Assert.Contains(Target, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteAccessDenied_MapsToAccessDeniedWithoutLocalPrivilegeHint()
    {
        Error error = RemoteCimErrorMapper.Map(
            NativeErrorCode.Failed, 0x80070005u, "denied", isRemote: true, Target);

        Assert.Equal(ErrorCode.AccessDenied, error.Code);
        // Elevating the scanning machine would not help — no requiredPrivilege
        Assert.Null(error.RequiredPrivilege);
        Assert.Contains("remote management rights", error.Details, StringComparison.Ordinal);
        Assert.Contains("NTLM", error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalAccessDenied_KeepsAccessDeniedWithRequiredPrivilege()
    {
        Error error = RemoteCimErrorMapper.Map(
            NativeErrorCode.AccessDenied, null, "denied", isRemote: false, Environment.MachineName);

        Assert.Equal(ErrorCode.AccessDenied, error.Code);
        Assert.Equal(PrivilegeLevel.Administrator, error.RequiredPrivilege);
    }

    [Theory]
    [InlineData(0x80338029u)]
    [InlineData(0x80338126u)]
    public void WsmanTimeouts_MapToConnectionTimeout(uint wsmanErrorCode)
    {
        Error error = RemoteCimErrorMapper.Map(
            NativeErrorCode.Failed, wsmanErrorCode, "timed out", isRemote: true, Target);

        Assert.Equal(ErrorCode.ConnectionTimeout, error.Code);
    }

    [Fact]
    public void WsmanCannotConnect_MapsToWinRmUnavailableNamingFirewallAndService()
    {
        Error error = RemoteCimErrorMapper.Map(
            NativeErrorCode.Failed, 0x80338012u, "cannot connect", isRemote: true, Target);

        Assert.Equal(ErrorCode.WinRmUnavailable, error.Code);
        Assert.Contains("firewall", error.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WinRM service", error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownRemoteFailure_MapsToWinRmUnavailableWithDetails()
    {
        Error error = RemoteCimErrorMapper.Map(
            NativeErrorCode.Failed, null, "boom", isRemote: true, Target);

        Assert.Equal(ErrorCode.WinRmUnavailable, error.Code);
        Assert.Equal("boom", error.Details);
    }

    [Fact]
    public void UnknownLocalFailure_KeepsWmiUnavailable()
    {
        Error error = RemoteCimErrorMapper.Map(
            NativeErrorCode.InvalidQuery, null, "bad query", isRemote: false, Environment.MachineName);

        Assert.Equal(ErrorCode.WmiUnavailable, error.Code);
        Assert.Equal("bad query", error.Details);
    }
}
