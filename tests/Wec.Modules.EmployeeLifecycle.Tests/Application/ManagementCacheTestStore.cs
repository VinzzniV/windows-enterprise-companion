using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

internal static class ManagementCacheTestStore
{
    internal static IServiceCredentialStore Empty()
    {
        IServiceCredentialStore store = Substitute.For<IServiceCredentialStore>();
        store.Read(ServiceCredentialKind.Kaspersky).Returns(Result.Success<StoredServiceCredential?>(null));
        return store;
    }
}
