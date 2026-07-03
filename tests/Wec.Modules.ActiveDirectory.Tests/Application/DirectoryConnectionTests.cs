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
