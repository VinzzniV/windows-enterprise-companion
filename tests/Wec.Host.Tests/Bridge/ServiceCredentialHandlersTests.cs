using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class ServiceCredentialHandlersTests
{
    [Fact]
    public async Task Status_ReturnsIdentityButNeverPassword()
    {
        var store = new MemoryCredentialStore();
        store.Save(ServiceCredentialKind.Kaspersky,
            new StoredServiceCredential("ksc-reader", "CORP", "top-secret"));
        var handler = new GetServiceCredentialStatusesHandler(store);

        Result<ServiceCredentialStatuses> result = await handler.HandleAsync(
            new GetServiceCredentialStatusesRequest(), CancellationToken.None);

        Assert.True(result.Value.Kaspersky.Saved);
        Assert.Equal("ksc-reader", result.Value.Kaspersky.UserName);
        Assert.Equal("CORP", result.Value.Kaspersky.Domain);
        Assert.DoesNotContain("secret", result.Value.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAndDelete_DelegateToSecureStore()
    {
        var store = new MemoryCredentialStore();
        var save = new SaveServiceCredentialHandler(store);
        var delete = new DeleteServiceCredentialHandler(store);

        Result<ServiceCredentialStatus> saved = await save.HandleAsync(
            new SaveServiceCredentialRequest(ServiceCredentialKind.Opsi, " opsi-reader ", null, "secret"),
            CancellationToken.None);
        Result<ServiceCredentialStatus> deleted = await delete.HandleAsync(
            new DeleteServiceCredentialRequest(ServiceCredentialKind.Opsi),
            CancellationToken.None);

        Assert.True(saved.Value.Saved);
        Assert.Equal("opsi-reader", saved.Value.UserName);
        Assert.False(deleted.Value.Saved);
        Assert.Null(store.Read(ServiceCredentialKind.Opsi).Value);
    }

    private sealed class MemoryCredentialStore : IServiceCredentialStore
    {
        private readonly Dictionary<ServiceCredentialKind, StoredServiceCredential> _values = [];

        public Result<StoredServiceCredential?> Read(ServiceCredentialKind kind) =>
            Result.Success<StoredServiceCredential?>(_values.GetValueOrDefault(kind));

        public Result<bool> Save(ServiceCredentialKind kind, StoredServiceCredential credential)
        {
            _values[kind] = credential;
            return Result.Success(true);
        }

        public Result<bool> Delete(ServiceCredentialKind kind) => Result.Success(_values.Remove(kind));
    }
}
