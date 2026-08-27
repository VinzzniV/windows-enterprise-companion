using Wec.Core.Contracts;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class HygieneActionEvidenceProviderTests
{
    private static readonly DateTimeOffset AssessedAt =
        new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Project_PreservesFindingSourceAgeCoverageAndDeterministicOrder()
    {
        var result = new ItHygieneResult(
            AssessedAt,
            "corp.example",
            new EnvironmentSourceStates(
                new InventorySourceState(InventorySourceAvailability.Available),
                new InventorySourceState(InventorySourceAvailability.Partial, "Kaspersky result was truncated."),
                new InventorySourceState(InventorySourceAvailability.Available),
                new InventorySourceState(InventorySourceAvailability.Available)),
            EmptySummary,
            [Device("PC-B"), Device("PC-A")]);

        HygieneActionEvidenceSnapshot snapshot = HygieneActionEvidenceProvider.Project(result);

        Assert.Equal(AssessedAt, snapshot.AssessedAtUtc);
        Assert.Equal(["Active Directory", "Kaspersky", "opsi", "Nessus"],
            snapshot.Sources.Select(source => source.Source));
        Assert.Equal(ActionEvidenceAvailability.Partial,
            snapshot.Sources.Single(source => source.Source == "Kaspersky").Availability);
        Assert.Equal("Kaspersky result was truncated.",
            snapshot.Sources.Single(source => source.Source == "Kaspersky").Explanation);
        Assert.Equal("PC-A", snapshot.Findings[0].SubjectKey);

        HygieneActionEvidence ad = snapshot.Findings.Single(finding =>
            finding.SubjectKey == "PC-A" && finding.FindingCode == "StaleAd");
        Assert.Equal("Active Directory", ad.Source);
        Assert.Equal(AssessedAt.AddDays(-100), ad.EvidenceAtUtc);
        Assert.Equal(ActionEvidenceAvailability.Available, ad.Coverage);

        HygieneActionEvidence opsi = snapshot.Findings.Single(finding =>
            finding.SubjectKey == "PC-A" && finding.FindingCode == "MissingOpsi");
        Assert.Equal("opsi", opsi.Source);

        HygieneActionEvidence nessus = snapshot.Findings.Single(finding =>
            finding.SubjectKey == "PC-A" && finding.FindingCode == "NessusCriticalVulnerabilities");
        Assert.Equal("Nessus", nessus.Source);
        Assert.Equal(AssessedAt.AddDays(-2), nessus.EvidenceAtUtc);

        HygieneActionEvidence kaspersky = snapshot.Findings.Single(finding =>
            finding.SubjectKey == "PC-A" && finding.FindingCode == "OutdatedAgent");
        Assert.Equal("Kaspersky", kaspersky.Source);
        Assert.Equal(ActionEvidenceAvailability.Partial, kaspersky.Coverage);
        Assert.Equal("Kaspersky result was truncated.", kaspersky.CoverageExplanation);
    }

    private static HygieneDevice Device(string host) => new(
        host,
        $"{host}.corp.example",
        new AdDeviceData(true, true, $"{host}.corp.example", "Windows 11", null, null, null, AssessedAt.AddDays(-100)),
        new KasperskyDeviceData(true, AssessedAt.AddDays(-3), "15.0", "21.0", "Clients"),
        new OpsiDeviceData(false, null, null, null, null, null),
        new NessusDeviceData(true, "asset", null, AssessedAt.AddDays(-2), 1, 2, 0, 0, 0, [], []),
        new HygieneAssessment(HygieneStatus.Critical,
        [
            new HygieneFinding(HygieneFindingCode.StaleAd, HygieneFindingSeverity.Critical, "AD is stale."),
            new HygieneFinding(HygieneFindingCode.MissingOpsi, HygieneFindingSeverity.Warning, "opsi is missing."),
            new HygieneFinding(HygieneFindingCode.NessusCriticalVulnerabilities, HygieneFindingSeverity.Critical, "Critical findings."),
            new HygieneFinding(HygieneFindingCode.OutdatedAgent, HygieneFindingSeverity.Warning, "Agent is outdated."),
        ]));

    private static readonly HygieneSummary EmptySummary = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
