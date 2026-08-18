using NSubstitute;
using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class OpsiComputerInventoryProviderTests
{
    private static readonly OpsiConnection Connection = new(
        new Uri("https://opsi.example.test:4447/rpc"),
        "reader",
        "secret",
        TrustServerCertificate: true,
        TimeSpan.FromSeconds(30));

    [Fact]
    public async Task LoadAsync_UsesFilteredClientAgentQueryAndMapsInventory()
    {
        IOpsiClient client = Substitute.For<IOpsiClient>();
        OpsiSessionState session = ConnectedSession();
        DateTimeOffset lastSeen = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        client.GetClientsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiClientHost>>(
            [
                new("pc01.example.test", "Notebook", "depot01.example.test", lastSeen),
            ]));
        client.GetProductStatesAsync(Connection, "opsi-client-agent", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnClient>>(
            [
                new("opsi-client-agent", "pc01.example.test", "installed", "none", "successful", "4.3.8.1", "1", lastSeen),
            ]));

        var provider = new OpsiComputerInventoryProvider(client, session, Connector(client, session));
        Result<Wec.Core.Contracts.OpsiComputerInventory> result =
            await provider.LoadAsync(100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Wec.Core.Contracts.OpsiComputerInventoryItem computer = Assert.Single(result.Value.Computers);
        Assert.Equal("pc01.example.test", computer.ComputerName);
        Assert.Equal("Notebook", computer.Description);
        Assert.Equal("depot01.example.test", computer.DepotId);
        Assert.Equal("4.3.8.1", computer.ClientAgentVersion);
        await client.Received(1).GetProductStatesAsync(
            Connection,
            "opsi-client-agent",
            Arg.Any<CancellationToken>());
        await client.DidNotReceive().GetProductStatesAsync(
            Connection,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WithoutSessionDoesNotCallOpsi()
    {
        IOpsiClient client = Substitute.For<IOpsiClient>();
        var session = new OpsiSessionState();
        var provider = new OpsiComputerInventoryProvider(client, session, Connector(client, session));

        Result<Wec.Core.Contracts.OpsiComputerInventory> result =
            await provider.LoadAsync(100, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        await client.DidNotReceiveWithAnyArgs().GetClientsAsync(default!, default);
    }

    [Fact]
    public async Task LoadAsync_WithoutSessionConnectsWithSavedCredential()
    {
        IOpsiClient client = Substitute.For<IOpsiClient>();
        var session = new OpsiSessionState();
        var stored = new StoredServiceCredential("opsi-reader", null, "stored-secret");
        var options = new PatchManagementOptions
        {
            OpsiServer = "opsi.example.test",
            DefaultServicePort = 4447,
            TrustServerCertificate = true,
        };
        client.TestConnectionAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new OpsiServerInfo("4.3")));
        client.GetClientsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiClientHost>>([]));
        client.GetProductStatesAsync(
                Arg.Any<OpsiConnection>(),
                "opsi-client-agent",
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnClient>>([]));
        var provider = new OpsiComputerInventoryProvider(
            client,
            session,
            Connector(client, session, stored, options));

        Result<OpsiComputerInventory> result =
            await provider.LoadAsync(100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(session.Current);
        await client.Received(1).TestConnectionAsync(
            Arg.Is<OpsiConnection>(connection =>
                connection.ServiceUrl.Host == "opsi.example.test"
                && connection.UserName == "opsi-reader"
                && connection.Password == "stored-secret"
                && connection.TrustServerCertificate),
            Arg.Any<CancellationToken>());
        await client.Received(1).GetClientsAsync(
            Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_ReportsTruncationAtConfiguredLimit()
    {
        IOpsiClient client = Substitute.For<IOpsiClient>();
        OpsiSessionState session = ConnectedSession();
        client.GetClientsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiClientHost>>(
            [
                new("pc01.example.test", null, null, null),
                new("pc02.example.test", null, null, null),
            ]));
        client.GetProductStatesAsync(Connection, "opsi-client-agent", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnClient>>([]));

        var provider = new OpsiComputerInventoryProvider(client, session, Connector(client, session));
        Result<Wec.Core.Contracts.OpsiComputerInventory> result =
            await provider.LoadAsync(1, CancellationToken.None);

        Assert.True(result.Value.Truncated);
        Assert.Single(result.Value.Computers);
    }

    private static OpsiSessionState ConnectedSession()
    {
        var session = new OpsiSessionState();
        session.Set(new OpsiSession(Connection, new OpsiServerInfo("4.3")));
        return session;
    }

    private static OpsiSessionConnector Connector(
        IOpsiClient client,
        OpsiSessionState session,
        StoredServiceCredential? stored = null,
        PatchManagementOptions? options = null)
    {
        IServiceCredentialStore credentials = Substitute.For<IServiceCredentialStore>();
        credentials.Read(ServiceCredentialKind.Opsi)
            .Returns(Result.Success<StoredServiceCredential?>(stored));
        return new OpsiSessionConnector(
            client,
            session,
            credentials,
            Options.Create(options ?? new PatchManagementOptions()));
    }
}
