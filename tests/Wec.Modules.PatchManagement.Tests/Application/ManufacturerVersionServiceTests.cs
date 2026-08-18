using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class ManufacturerVersionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ForcedCheck_PersistsNewVersionAndAuditEntry()
    {
        var sourceRepository = new InMemorySourceRepository(new ProductVersionSource(
            "firefox", "https://vendor.example/releases", "Version ([0-9.]+)", true,
            "128.0", Now.AddDays(-2), "SUCCESS", null));
        var auditRepository = new InMemoryAuditRepository();
        var client = Substitute.For<IVendorVersionClient>();
        client.GetLatestVersionAsync(Arg.Any<VendorVersionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("129.0"));
        ManufacturerVersionService service = CreateService(client, sourceRepository, auditRepository);

        VersionCheckOutcome outcome = Assert.Single(await service.CheckAsync(
            ["firefox"], force: true, CancellationToken.None));

        Assert.Equal("128.0", outcome.PreviousVersion);
        Assert.Equal("129.0", outcome.LatestVersion);
        Assert.Equal("129.0", Assert.Single(sourceRepository.Sources).LatestVersion);
        PatchAuditEntry audit = Assert.Single(auditRepository.Entries);
        Assert.Equal("MANUFACTURER_VERSION_CHECK", audit.Action);
        Assert.Contains("129.0", audit.PreviewJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DueCheckFailure_IsPersistedAndNeverSilent()
    {
        var sourceRepository = new InMemorySourceRepository(new ProductVersionSource(
            "firefox", "https://vendor.example/releases", "Version ([0-9.]+)", true,
            "128.0", Now.AddDays(-2), "SUCCESS", null));
        var auditRepository = new InMemoryAuditRepository();
        var client = Substitute.For<IVendorVersionClient>();
        client.GetLatestVersionAsync(Arg.Any<VendorVersionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(new Error(ErrorCode.ServiceUnavailable, "Vendor unavailable.")));
        ManufacturerVersionService service = CreateService(client, sourceRepository, auditRepository);

        VersionCheckOutcome outcome = Assert.Single(await service.CheckAsync(
            productIds: null, force: false, CancellationToken.None));

        Assert.Equal("FAILED", outcome.Status);
        Assert.Equal("Vendor unavailable.", Assert.Single(sourceRepository.Sources).LastError);
        Assert.Equal("FAILED", Assert.Single(auditRepository.Entries).Result);
    }

    private static ManufacturerVersionService CreateService(
        IVendorVersionClient client,
        IProductVersionSourceRepository sourceRepository,
        IPatchAuditRepository auditRepository)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ManufacturerVersionService(
            client,
            sourceRepository,
            auditRepository,
            clock,
            Options.Create(new PatchManagementOptions()));
    }

    private sealed class InMemorySourceRepository(ProductVersionSource source)
        : IProductVersionSourceRepository
    {
        public List<ProductVersionSource> Sources { get; } = [source];

        public Task<IReadOnlyList<ProductVersionSource>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductVersionSource>>([.. Sources]);

        public Task UpsertAsync(ProductVersionSource updated, CancellationToken cancellationToken)
        {
            Sources.RemoveAll(item => string.Equals(item.ProductId, updated.ProductId, StringComparison.OrdinalIgnoreCase));
            Sources.Add(updated);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string productId, CancellationToken cancellationToken)
        {
            Sources.RemoveAll(item => string.Equals(item.ProductId, productId, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuditRepository : IPatchAuditRepository
    {
        public List<PatchAuditEntry> Entries { get; } = [];

        public Task AddAsync(PatchAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PatchAuditEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PatchAuditEntry>>([.. Entries.Take(limit)]);

        public Task<PatchAuditEntry?> FindLatestAsync(
            string productId,
            string action,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entries
                .Where(entry => string.Equals(entry.ProductId, productId, StringComparison.OrdinalIgnoreCase)
                    && entry.Action == action)
                .OrderByDescending(entry => entry.TimestampUtc)
                .FirstOrDefault());
    }
}
