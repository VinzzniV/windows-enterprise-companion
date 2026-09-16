using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Microsoft.Extensions.Options;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

internal static class ManagementCacheTestStore
{
    internal static ItHygieneSnapshotCache CreateCache(IServiceCredentialStore? credentials = null,
        IOpsiComputerInventoryProvider? opsi = null) => new(credentials ?? Empty(),
            opsi ?? Substitute.For<IOpsiComputerInventoryProvider>(), Options.Create(new ItLifecycleOptions()));

    internal static IServiceCredentialStore Empty()
    {
        IServiceCredentialStore store = Substitute.For<IServiceCredentialStore>();
        store.Read(ServiceCredentialKind.Kaspersky).Returns(Result.Success<StoredServiceCredential?>(null));
        return store;
    }
}
