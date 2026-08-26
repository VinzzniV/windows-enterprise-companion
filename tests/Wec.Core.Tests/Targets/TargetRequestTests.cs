using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Tests.Targets;

public sealed class TargetRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToScanTarget_WithoutHost_IsLocal(string? host)
    {
        var request = new TargetRequest(Host: host);

        ScanTarget target = request.ToScanTarget();

        Assert.True(target.IsLocal);
        Assert.Null(target.Host);
        Assert.Equal(Environment.MachineName, target.DisplayName);
    }

    [Fact]
    public void ToScanTarget_WithHost_IsRemoteAndTrimmed()
    {
        var request = new TargetRequest(Host: " pc-042.contoso.local ");

        ScanTarget target = request.ToScanTarget();

        Assert.False(target.IsLocal);
        Assert.Equal("pc-042.contoso.local", target.Host);
        Assert.Equal("pc-042.contoso.local", target.DisplayName);
    }

    [Fact]
    public void ToScanCredentials_WithoutUserName_IsCurrentUser()
    {
        var request = new TargetRequest(Host: "pc-042");

        Result<ScanCredentials> credentials = request.ToScanCredentials();

        Assert.True(credentials.IsSuccess);
        Assert.Equal(CredentialMode.CurrentUser, credentials.Value.Mode);
    }

    [Fact]
    public void ToScanCredentials_WithUserNameAndPassword_IsExplicit()
    {
        var request = new TargetRequest(
            Host: "pc-042",
            UserName: "svc-scan",
            Domain: "CONTOSO",
            Password: "s3cret");

        Result<ScanCredentials> credentials = request.ToScanCredentials();

        Assert.True(credentials.IsSuccess);
        Assert.Equal(CredentialMode.Explicit, credentials.Value.Mode);
        Assert.Equal("svc-scan", credentials.Value.UserName);
        Assert.Equal("CONTOSO", credentials.Value.Domain);
        Assert.Equal("s3cret", credentials.Value.Password);
    }

    [Fact]
    public void ToScanCredentials_UserNameWithoutPassword_IsInvalidRequest()
    {
        var request = new TargetRequest(Host: "pc-042", UserName: "svc-scan");

        Result<ScanCredentials> credentials = request.ToScanCredentials();

        Assert.True(credentials.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, credentials.Error!.Code);
    }

    [Fact]
    public void ToScanCredentials_ExplicitCredentialsForLocalTarget_IsUnsupported()
    {
        var request = new TargetRequest(UserName: "svc-scan", Password: "s3cret");

        Result<ScanCredentials> credentials = request.ToScanCredentials();

        Assert.True(credentials.IsFailure);
        Assert.Equal(ErrorCode.UnsupportedRemoteOperation, credentials.Error!.Code);
    }

    [Fact]
    public void ScanCredentials_ToString_DoesNotExposeThePassword()
    {
        ScanCredentials credentials = ScanCredentials.Explicit("admin", "CORP", "super-secret-value");

        string text = credentials.ToString();

        Assert.DoesNotContain("super-secret-value", text, StringComparison.Ordinal);
        Assert.Contains("admin", text, StringComparison.Ordinal);
        Assert.Contains("CORP", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanError_FromError_MapsCodeToPhase()
    {
        var dnsError = new Error(ErrorCode.DnsResolutionFailed, "no such host");
        var authError = new Error(ErrorCode.AuthenticationFailed, "rejected");
        var timeoutError = new Error(ErrorCode.ConnectionTimeout, "timed out");
        var queryError = new Error(ErrorCode.WmiUnavailable, "bad query");

        Assert.Equal(ScanPhase.Resolve, ScanError.FromError("pc", dnsError).Phase);
        Assert.Equal(ScanPhase.Authenticate, ScanError.FromError("pc", authError).Phase);
        Assert.Equal(ScanPhase.Connect, ScanError.FromError("pc", timeoutError).Phase);
        Assert.Equal(ScanPhase.Query, ScanError.FromError("pc", queryError).Phase);
    }
}
