using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.Clients.Handlers;

namespace Wec.Modules.Clients.Tests;

public sealed class StoredObjectListsHandlerTests
{
    [Fact]
    public async Task FailedSourceDoesNotHideOtherAddressesAndRevisionChangesOnlyWithEvidence()
    {
        IStoredDeviceListProvider inventory = Substitute.For<IStoredDeviceListProvider>();
        IStoredDeviceListProvider security = Substitute.For<IStoredDeviceListProvider>();
        inventory.Source.Returns(StoredDeviceListSource.Inventory);
        security.Source.Returns(StoredDeviceListSource.Security);
        var now = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        inventory.ReadAsync(2, "pc", Arg.Any<CancellationToken>()).Returns(Result.Success(new StoredDeviceAddressPage(100, [new("42", "pc.a.example", "Device", now)])));
        security.ReadAsync(2, "pc", Arg.Any<CancellationToken>()).Returns(Result.Failure<StoredDeviceAddressPage>(new(ErrorCode.ServiceUnavailable, "Unavailable")));
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var handler = new StoredObjectListsHandler([inventory, security], new WecWorkspaceIdentity("workspace", "LOCAL"),
            Options.Create(new ObjectWorkingSetOptions { MaximumRecords = 2 }), clock);
        var first = (await handler.HandleAsync(new(" pc "), CancellationToken.None)).Value;
        Assert.Equal(100, first.Reads[0].TotalRecords);
        Assert.Single(first.Reads[0].Records);
        Assert.Null(first.Reads[1].TotalRecords);
        Assert.NotNull(first.Reads[1].Error);
        clock.UtcNow.Returns(now.AddMinutes(1));
        var second = (await handler.HandleAsync(new("pc"), CancellationToken.None)).Value;
        Assert.Equal(first.Reads[0].Revision, second.Reads[0].Revision);
        Assert.NotEqual(first.RetrievedAtUtc, second.RetrievedAtUtc);
        inventory.ReadAsync(2, "pc", Arg.Any<CancellationToken>()).Returns(Result.Success(new StoredDeviceAddressPage(100, [new("43", "pc.a.example", "Device", now)])));
        Assert.NotEqual(first.Reads[0].Revision, (await handler.HandleAsync(new("pc"), CancellationToken.None)).Value.Reads[0].Revision);
        await inventory.Received(3).ReadAsync(2, "pc", Arg.Any<CancellationToken>());
    }
}
