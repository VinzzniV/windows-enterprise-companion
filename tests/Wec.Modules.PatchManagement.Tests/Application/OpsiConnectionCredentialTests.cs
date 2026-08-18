using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Handlers;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class OpsiConnectionCredentialTests
{
    [Fact]
    public async Task Connect_UsesSavedCredentialWithoutReturningPassword()
    {
        IOpsiClient client = Substitute.For<IOpsiClient>();
        IServiceCredentialStore credentials = Substitute.For<IServiceCredentialStore>();
        credentials.Read(ServiceCredentialKind.Opsi).Returns(Result.Success<StoredServiceCredential?>(
            new StoredServiceCredential("opsi-reader", null, "stored-secret")));
        client.TestConnectionAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new OpsiServerInfo("4.3")));
        var handler = new ConnectOpsiHandler(
            client,
            new OpsiSessionState(),
            credentials,
            Options.Create(new PatchManagementOptions()));

        Result<OpsiConnectionStatusResult> result = await handler.HandleAsync(
            new OpsiConnectRequest("opsi.example.test", "", null, true, UseStoredCredential: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Connected);
        Assert.Equal("opsi-reader", result.Value.UserName);
        Assert.DoesNotContain("secret", result.Value.ToString(), StringComparison.OrdinalIgnoreCase);
        await client.Received(1).TestConnectionAsync(
            Arg.Is<OpsiConnection>(connection =>
                connection.UserName == "opsi-reader" && connection.Password == "stored-secret"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_RemembersCredentialOnlyAfterSuccessfulValidation()
    {
        IOpsiClient client = Substitute.For<IOpsiClient>();
        IServiceCredentialStore credentials = Substitute.For<IServiceCredentialStore>();
        credentials.Save(ServiceCredentialKind.Opsi, Arg.Any<StoredServiceCredential>())
            .Returns(Result.Success(true));
        client.TestConnectionAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new OpsiServerInfo("4.3")));
        var handler = new ConnectOpsiHandler(
            client,
            new OpsiSessionState(),
            credentials,
            Options.Create(new PatchManagementOptions()));

        Result<OpsiConnectionStatusResult> result = await handler.HandleAsync(
            new OpsiConnectRequest(
                "opsi.example.test", "opsi-reader", "new-secret", true,
                RememberCredential: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        credentials.Received(1).Save(
            ServiceCredentialKind.Opsi,
            Arg.Is<StoredServiceCredential>(credential =>
                credential.UserName == "opsi-reader" && credential.Password == "new-secret"));
    }
}
