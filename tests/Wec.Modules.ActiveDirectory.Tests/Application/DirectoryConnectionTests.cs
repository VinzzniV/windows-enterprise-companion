using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryConnectionRequestTests
{
    [Fact]
    public void EmptyRequest_YieldsDefaultConnection()
    {
        Result<DirectoryConnection> connection = new DirectoryConnectionRequest().ToConnection();

        Assert.True(connection.IsSuccess);
        Assert.Null(connection.Value.DomainOverride);
        Assert.Null(connection.Value.Server);
        Assert.Equal(CredentialMode.CurrentUser, connection.Value.Credentials.Mode);
    }

    [Fact]
    public void ExplicitCredentialsAndServer_AreCarried()
    {
        Result<DirectoryConnection> connection = new DirectoryConnectionRequest(
            Domain: " contoso.local ",
            Server: "dc01.contoso.local",
            UserName: "svc-audit",
            UserDomain: "CONTOSO",
            Password: "s3cret").ToConnection();

        Assert.True(connection.IsSuccess);
        Assert.Equal("contoso.local", connection.Value.DomainOverride);
        Assert.Equal("dc01.contoso.local", connection.Value.Server);
        Assert.Equal(CredentialMode.Explicit, connection.Value.Credentials.Mode);
        Assert.Equal("CONTOSO", connection.Value.Credentials.Domain);
    }

    [Fact]
    public void UserNameWithoutPassword_IsInvalidRequest()
    {
        Result<DirectoryConnection> connection =
            new DirectoryConnectionRequest(UserName: "svc-audit").ToConnection();

        Assert.True(connection.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, connection.Error!.Code);
    }

    [Fact]
    public void UpnUserName_KeepsNameAndDropsDomain()
    {
        Result<ScanCredentials> credentials = DirectoryConnectionRequest.NormalizeCredentials(
            " svc-audit@contoso.local ", credentialDomain: null, "pw", directoryDomain: null);

        Assert.True(credentials.IsSuccess);
        Assert.Equal("svc-audit@contoso.local", credentials.Value.UserName);
        // A domain next to a UPN would make LDAP treat the name as a SAM name
        Assert.Null(credentials.Value.Domain);
    }

    [Fact]
    public void DownLevelUserName_IsSplitIntoDomainAndUser()
    {
        Result<ScanCredentials> credentials = DirectoryConnectionRequest.NormalizeCredentials(
            @"CONTOSO\svc-audit", credentialDomain: "IGNORED", "pw", directoryDomain: null);

        Assert.True(credentials.IsSuccess);
        Assert.Equal("svc-audit", credentials.Value.UserName);
        Assert.Equal("CONTOSO", credentials.Value.Domain);
    }

    [Fact]
    public void PlainUserWithCredentialDomain_IsCarried()
    {
        Result<ScanCredentials> credentials = DirectoryConnectionRequest.NormalizeCredentials(
            "svc-audit", credentialDomain: "CONTOSO", "pw", directoryDomain: "other.example");

        Assert.True(credentials.IsSuccess);
        Assert.Equal("svc-audit", credentials.Value.UserName);
        Assert.Equal("CONTOSO", credentials.Value.Domain);
    }

    [Fact]
    public void PlainUserWithoutCredentialDomain_DefaultsToDirectoryDomain()
    {
        Result<ScanCredentials> credentials = DirectoryConnectionRequest.NormalizeCredentials(
            "svc-audit", credentialDomain: null, "pw", directoryDomain: "contoso.local");

        Assert.True(credentials.IsSuccess);
        Assert.Equal("contoso.local", credentials.Value.Domain);
    }

    [Fact]
    public void PlainUserWithoutAnyDomain_IsInvalidRequestNamingTheAcceptedForms()
    {
        Result<ScanCredentials> credentials = DirectoryConnectionRequest.NormalizeCredentials(
            "svc-audit", credentialDomain: null, "pw", directoryDomain: null);

        Assert.True(credentials.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, credentials.Error!.Code);
        Assert.Contains(@"DOMAIN\user", credentials.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\svc-audit")]
    [InlineData(@"CONTOSO\")]
    public void MalformedDownLevelName_IsInvalidRequest(string userName)
    {
        Result<ScanCredentials> credentials = DirectoryConnectionRequest.NormalizeCredentials(
            userName, credentialDomain: null, "pw", directoryDomain: null);

        Assert.True(credentials.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, credentials.Error!.Code);
    }

    [Fact]
    public void RequestWithDownLevelUserName_ProducesNormalizedConnection()
    {
        Result<DirectoryConnection> connection = new DirectoryConnectionRequest(
            Domain: "contoso.local",
            UserName: @"CONTOSO\svc-audit",
            Password: "pw").ToConnection();

        Assert.True(connection.IsSuccess);
        Assert.Equal("svc-audit", connection.Value.Credentials.UserName);
        Assert.Equal("CONTOSO", connection.Value.Credentials.Domain);
    }
}

public sealed class DomainContextServiceConnectionTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IDirectoryReader _directoryReader = Substitute.For<IDirectoryReader>();

    private DomainContextService CreateService() => new(
        _wmiQueryService,
        _directoryReader,
        Options.Create(new ActiveDirectoryOptions()));

    private void SetUpRootDse(string namingContext) =>
        _directoryReader.SearchAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>(
            [
                new DirectoryEntryData(string.Empty, new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["defaultNamingContext"] = [namingContext],
                }),
            ]));

    [Fact]
    public async Task DomainOverride_SkipsLocalDomainDetection()
    {
        SetUpRootDse("DC=other,DC=example");
        var connection = new DirectoryConnection(
            "other.example", "dc01.other.example", ScanCredentials.Explicit("svc", "OTHER", "pw"));

        Result<DomainContext> context = await CreateService().GetContextAsync(connection, CancellationToken.None);

        Assert.True(context.IsSuccess);
        Assert.True(context.Value.DomainJoined);
        Assert.Equal("other.example", context.Value.DomainName);
        await _wmiQueryService.DidNotReceive().QueryAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _directoryReader.Received(1).SearchAsync(
            Arg.Is<DirectorySearchQuery>(query =>
                query.Server == "dc01.other.example"
                && query.Credentials!.Mode == CredentialMode.Explicit),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingNamingContext_ReportsDiagnosticDetails()
    {
        _directoryReader.SearchAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([]));
        var connection = new DirectoryConnection("other.example", null, ScanCredentials.CurrentUser);

        Result<DomainContext> context = await CreateService().GetContextAsync(connection, CancellationToken.None);

        Assert.True(context.IsFailure);
        Assert.Equal(ErrorCode.DirectoryUnavailable, context.Error!.Code);
        Assert.Contains("naming context", context.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
