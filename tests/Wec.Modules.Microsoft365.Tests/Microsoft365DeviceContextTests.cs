using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Modules.Microsoft365.Application;

namespace Wec.Modules.Microsoft365.Tests;

public sealed class Microsoft365DeviceContextTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string ObjectId = "22222222-2222-2222-2222-222222222222";
    private const string DeviceId = "33333333-3333-3333-3333-333333333333";
    private readonly IMicrosoft365Reader _reader = Substitute.For<IMicrosoft365Reader>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public Microsoft365DeviceContextTests()
    {
        _reader.Connection.Returns(new Microsoft365Connection(new(TenantId, ObjectId), true, "Account", []));
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
    }

    private Microsoft365Service Create() => new(_reader, _clock, Options.Create(new Microsoft365CacheOptions()));

    [Fact]
    public async Task EmptyCacheDistinguishesUnqueriedAndDisabledWithoutRemoteReads()
    {
        using var service = Create();
        var result = await service.ReadCachedAsync(TenantId, ObjectId, CancellationToken.None);
        Assert.All(result.Value.EntraReads, read => Assert.Equal(Microsoft365Availability.NotCached, read.State.Availability));
        Assert.Equal(Microsoft365Availability.NotEnabled, result.Value.Intune.State.Availability);
        Assert.Equal(Microsoft365Availability.NotCached, result.Value.RegisteredOwners!.State.Availability);
        await _reader.DidNotReceive().ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProjectionPreservesDuplicatesQueryProvenanceAndAllManagedRecords()
    {
        using var service = Create();
        _reader.Connection.Returns(new Microsoft365Connection(new(TenantId, ObjectId, EnableIntune: true), true, "Account", []));
        Microsoft365Device device = new(ObjectId, DeviceId, "Name", null, null, null, null, null);
        _reader.ReadAsync(new(Microsoft365Resource.Devices), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data
        {
            Devices = [device, device], Truncated = true, TotalCount = 10,
        }));
        _reader.ReadAsync(new(Microsoft365Resource.Device, ObjectId), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data
        {
            Devices = [device with { DisplayName = "Renamed" }],
        }));
        Microsoft365ManagedDevice managed = new(ObjectId, "Name", null, null, null, null, null, null, null, null, null, null, null, DeviceId);
        _reader.ReadAsync(new(Microsoft365Resource.ManagedDevices), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data
        {
            ManagedDevices = [managed, managed with { Id = DeviceId }],
        }));
        await service.ReadAsync(new(Microsoft365Resource.Devices), false, CancellationToken.None);
        await service.ReadAsync(new(Microsoft365Resource.Device, ObjectId), false, CancellationToken.None);
        await service.ReadAsync(new(Microsoft365Resource.ManagedDevices), false, CancellationToken.None);
        var context = (await service.ReadCachedAsync(TenantId, ObjectId, CancellationToken.None)).Value;
        Assert.Equal(2, context.EntraReads[0].Devices.Count);
        Assert.Equal(EvidenceCoverage.Partial, context.EntraReads[0].State.Coverage);
        Assert.Equal("Renamed", Assert.Single(context.EntraReads[1].Devices).DisplayName);
        Assert.Equal(2, context.Intune.Devices.Count);
        Assert.All(context.EntraReads, read => Assert.Equal(TenantId, read.State.TenantId));
    }

    [Fact]
    public async Task OwnerReadHasIndependentFailureAndIsNotTreatedAsEmptySuccess()
    {
        using var service = Create();
        _reader.ReadAsync(new(Microsoft365Resource.DeviceOwners, ObjectId), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365PermissionMissing, "Missing permission")));
        await service.ReadAsync(new(Microsoft365Resource.DeviceOwners, ObjectId), true, CancellationToken.None);
        var context = (await service.ReadCachedAsync(TenantId, ObjectId, CancellationToken.None)).Value;
        Assert.Equal(Microsoft365Availability.Unavailable, context.RegisteredOwners!.State.Availability);
        Assert.Null(context.RegisteredOwners.State.LoadedCount);
        Assert.Equal(ErrorCode.Microsoft365PermissionMissing, context.RegisteredOwners.State.LastAttemptError!.Code);
        Assert.All(context.EntraReads, read => Assert.Null(read.State.LastAttemptError));
    }

    [Fact]
    public async Task WrongTenantAndCancelledRequestCannotReadEvidence()
    {
        using var service = Create();
        Assert.True((await service.ReadCachedAsync(DeviceId, ObjectId, CancellationToken.None)).IsFailure);
        Assert.True((await service.ReadCachedAsync("invalid", ObjectId, CancellationToken.None)).IsFailure);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadCachedAsync(TenantId, ObjectId, cancellation.Token));
        await _reader.DidNotReceive().ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KnownIntuneDetailWorksWithoutLoadingInventory()
    {
        using var service = Create();
        _reader.Connection.Returns(new Microsoft365Connection(new(TenantId, ObjectId, EnableIntune: true), true, "Account", []));
        _reader.ReadAsync(new(Microsoft365Resource.ManagedDevice, ObjectId), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Microsoft365Data
            {
                ManagedDevices = [new(ObjectId, "Cloud only", null, null, null, null, null, null, null, null, null, null, null, DeviceId)],
            }));
        await service.ReadAsync(new(Microsoft365Resource.ManagedDevice, ObjectId), true, CancellationToken.None);
        var context = (await service.ReadCachedAsync(TenantId, null, CancellationToken.None, ObjectId)).Value;
        Assert.Equal("Cloud only", Assert.Single(Assert.Single(context.ManagedDetails).Devices).DeviceName);
        Assert.Equal(Microsoft365Availability.NotCached, context.Intune.State.Availability);
        await _reader.DidNotReceive().ReadAsync(new(Microsoft365Resource.ManagedDevices), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetentionAndDisconnectInvalidatePreviouslyProjectedData()
    {
        using var service = Create();
        _reader.ReadAsync(new(Microsoft365Resource.Devices), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data
        {
            Devices = [new(ObjectId, DeviceId, "Name", null, null, null, null, null)],
        }));
        await service.ReadAsync(new(Microsoft365Resource.Devices), false, CancellationToken.None);
        var before = (await service.ReadCachedAsync(TenantId, null, CancellationToken.None)).Value;
        _clock.UtcNow.Returns(_clock.UtcNow.AddHours(2));
        var expired = (await service.ReadCachedAsync(TenantId, null, CancellationToken.None)).Value;
        Assert.Empty(expired.EntraReads[0].Devices);
        Assert.True(expired.Revision > before.Revision);
        await service.DisconnectAsync(CancellationToken.None);
        var disconnected = (await service.ReadCachedAsync(TenantId, null, CancellationToken.None)).Value;
        Assert.True(disconnected.SessionRevision > before.SessionRevision);
        Assert.All(disconnected.EntraReads, read => Assert.Empty(read.Devices));
    }
}
