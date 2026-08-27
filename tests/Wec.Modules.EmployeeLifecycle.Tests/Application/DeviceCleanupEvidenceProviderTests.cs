using Wec.Core.Contracts;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class DeviceCleanupEvidenceProviderTests
{
    private static readonly DateTimeOffset AssessedAt =
        new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Project_PreservesSourceFactsFindingsCoverageAndOrder()
    {
        var result = new ItHygieneResult(
            AssessedAt,
            "corp.example",
            new EnvironmentSourceStates(
                new InventorySourceState(InventorySourceAvailability.Available),
                new InventorySourceState(InventorySourceAvailability.Partial, "Kaspersky was truncated."),
                new InventorySourceState(InventorySourceAvailability.NotConnected, "opsi is not configured."),
                new InventorySourceState(InventorySourceAvailability.Available)),
            EmptySummary,
            [Device("PC-B"), Device("PC-A")]);

        DeviceCleanupEvidenceSnapshot snapshot = DeviceCleanupEvidenceProvider.Project(result);

        Assert.Equal(AssessedAt, snapshot.AssessedAtUtc);
        Assert.Equal(["PC-A", "PC-B"], snapshot.Subjects.Select(subject => subject.SubjectKey));
        Assert.Equal(ActionEvidenceAvailability.Partial,
            snapshot.Sources.Single(source => source.Source == "Kaspersky").Availability);
        Assert.Equal("opsi is not configured.",
            snapshot.Sources.Single(source => source.Source == "opsi").Explanation);

        DeviceCleanupSubjectEvidence device = snapshot.Subjects[0];
        Assert.Equal("PC-A.corp.example", device.Host);
        Assert.Equal("CleanupCandidate", device.HygieneStatus);
        Assert.True(device.ActiveDirectory.Exists);
        Assert.False(device.ActiveDirectory.Enabled);
        Assert.Equal(AssessedAt.AddDays(-120), device.ActiveDirectory.LastLogonAtUtc);
        Assert.Equal("Clients/Retired", device.ActiveDirectory.OrganizationalUnit);
        Assert.Equal(AssessedAt.AddDays(-95), device.Kaspersky.LastSeenAtUtc);
        Assert.Equal("Depot-A", device.Opsi.DepotId);
        Assert.Equal(AssessedAt.AddDays(-80), device.Nessus.LastCompletedScanAtUtc);
        Assert.Equal("StaleAd", Assert.Single(device.Findings).Code);
    }

    private static HygieneDevice Device(string host) => new(
        host,
        $"{host}.corp.example",
        new AdDeviceData(
            true,
            false,
            $"{host}.corp.example",
            "Windows 11",
            null,
            $"CN={host},OU=Retired,OU=Clients,DC=corp,DC=example",
            "Clients/Retired",
            AssessedAt.AddDays(-120)),
        new KasperskyDeviceData(true, AssessedAt.AddDays(-95), "15.0", "21.0", "Retired"),
        new OpsiDeviceData(true, host, null, "Depot-A", AssessedAt.AddDays(-90), "4.3"),
        new NessusDeviceData(true, "asset", null, AssessedAt.AddDays(-80), 0, 0, 0, 0, 0, [], []),
        new HygieneAssessment(
            HygieneStatus.CleanupCandidate,
            [new HygieneFinding(HygieneFindingCode.StaleAd, HygieneFindingSeverity.Critical, "AD is stale.")]));

    private static readonly HygieneSummary EmptySummary = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
