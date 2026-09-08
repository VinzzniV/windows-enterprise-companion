using Wec.Core.Contracts;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal static class HygieneAssessmentPolicy
{
    internal static HygieneAssessment Assess(
        AdComputerInventoryItem? ad,
        KasperskyComputer? ksc,
        OpsiComputerInventoryItem? opsi,
        NessusComputerInventoryItem? nessus,
        NessusComputerInventory nessusInventory,
        EnvironmentSourceStates sources,
        DateTimeOffset now,
        ItLifecycleOptions options,
        bool assessNessus)
    {
        var findings = new List<HygieneFinding>();
        bool canCompareKaspersky = CanCompare(sources.ActiveDirectory, sources.Kaspersky);
        bool canCompareOpsi = CanCompare(sources.ActiveDirectory, sources.Opsi);
        if (canCompareKaspersky && ad is { Enabled: true } && ksc is null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.MissingKaspersky,
                HygieneFindingSeverity.Warning,
                "Enabled in Active Directory, but no matching Kaspersky device was found."));
        }

        if (ksc is not null && string.IsNullOrWhiteSpace(ksc.AgentVersion))
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.MissingKasperskyAgent,
                HygieneFindingSeverity.Warning,
                "Kaspersky device is registered, but no Network Agent version was reported."));
        }

        if (ksc is not null && string.IsNullOrWhiteSpace(ksc.KesVersion))
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.MissingKes,
                HygieneFindingSeverity.Warning,
                "Kaspersky device is registered, but no KES version was reported."));
        }

        if (canCompareKaspersky && ad is null && ksc is not null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OrphanKaspersky,
                HygieneFindingSeverity.Warning,
                "Present in Kaspersky, but no matching Active Directory computer was found."));
        }

        AddStaleFinding(findings, HygieneFindingCode.StaleAd, "Active Directory last logon", ad?.LastLogonDate, now, options);
        AddStaleFinding(findings, HygieneFindingCode.StaleOpsi, "opsi last seen", opsi?.LastSeen, now, options);

        if (canCompareOpsi && ad is { Enabled: true } && IsWindowsClient(ad.OperatingSystem) && opsi is null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.MissingOpsi,
                HygieneFindingSeverity.Warning,
                "Active Windows client in Active Directory, but no matching opsi client was found."));
        }

        if (canCompareOpsi && ad is null && opsi is not null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OrphanOpsi,
                HygieneFindingSeverity.Warning,
                "Present in opsi, but no matching Active Directory computer was found."));
        }

        AddStaleFinding(findings, HygieneFindingCode.StaleKaspersky, "Kaspersky last seen", ksc?.LastSeen, now, options);

        if (ksc is not null && IsVersionOlder(ksc.AgentVersion, options.TargetAgentVersion))
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OutdatedAgent,
                HygieneFindingSeverity.Warning,
                $"Network Agent {ksc.AgentVersion} is below target {options.TargetAgentVersion}."));
        }

        if (ksc is not null && IsVersionOlder(ksc.KesVersion, options.TargetKesVersion))
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OutdatedKes,
                HygieneFindingSeverity.Warning,
                $"KES {ksc.KesVersion} is below target {options.TargetKesVersion}."));
        }

        if (assessNessus)
        {
            bool nessusAbsenceIsKnown = sources.Nessus.Availability == InventorySourceAvailability.Available;
            bool nessusEvidenceIsUsable = sources.Nessus.Availability is
                InventorySourceAvailability.Available or
                InventorySourceAvailability.Partial or
                InventorySourceAvailability.Truncated;
            if (nessusAbsenceIsKnown
                && ad is { Enabled: true }
                && IsWindows(ad.OperatingSystem)
                && (nessus is null || nessus.LastCompletedScanUtc is null)
                && !MatchesAny(ad.ComputerName, nessusInventory.MissingExcludedHostPatterns)
                && !MatchesAny(ad.DistinguishedName, nessusInventory.MissingExcludedOuPatterns))
            {
                findings.Add(new HygieneFinding(
                    HygieneFindingCode.MissingNessus,
                    HygieneFindingSeverity.Warning,
                    "Enabled Windows computer in Active Directory, but no matching Nessus asset was found."));
            }

            if (nessusEvidenceIsUsable && nessus?.LastCompletedScanUtc is not null)
            {
                AddStaleFinding(
                    findings,
                    HygieneFindingCode.StaleNessus,
                    "Nessus last completed scan",
                    nessus.LastCompletedScanUtc,
                    now,
                    nessusInventory.StaleWarningDays,
                    nessusInventory.StaleCriticalDays);
                if (nessus.Critical > 0)
                {
                    findings.Add(new HygieneFinding(
                        HygieneFindingCode.NessusCriticalVulnerabilities,
                        HygieneFindingSeverity.Critical,
                        $"Nessus reports {nessus.Critical} critical finding instance(s)."));
                }
                if (nessus.High > 0)
                {
                    findings.Add(new HygieneFinding(
                        HygieneFindingCode.NessusHighVulnerabilities,
                        HygieneFindingSeverity.Warning,
                        $"Nessus reports {nessus.High} high finding instance(s)."));
                }
            }
        }

        return new HygieneAssessment(DetermineStatus(findings, sources, assessNessus), findings);
    }

    internal static HygieneStatus DetermineStatus(
        IReadOnlyList<HygieneFinding> findings,
        EnvironmentSourceStates sources,
        bool requireNessus) =>
        findings.Any(finding => finding.Code == HygieneFindingCode.NessusCriticalVulnerabilities)
            ? HygieneStatus.Critical
            : findings.Any(finding => finding.Severity == HygieneFindingSeverity.Critical)
                ? HygieneStatus.CleanupCandidate
                : findings.Count > 0
                    ? HygieneStatus.Warning
                    : SourcesComplete(sources, requireNessus)
                        ? HygieneStatus.Healthy
                        : HygieneStatus.Incomplete;

    internal static bool IsVersionOlder(string? installed, string? target)
    {
        if (!TryParseVersion(installed, out int[] installedParts)
            || !TryParseVersion(target, out int[] targetParts))
        {
            return false;
        }

        int count = Math.Max(installedParts.Length, targetParts.Length);
        for (int index = 0; index < count; index++)
        {
            int installedPart = index < installedParts.Length ? installedParts[index] : 0;
            int targetPart = index < targetParts.Length ? targetParts[index] : 0;
            if (installedPart != targetPart)
            {
                return installedPart < targetPart;
            }
        }

        return false;
    }

    internal static bool SourcesComplete(EnvironmentSourceStates sources, bool requireNessus) =>
        sources.ActiveDirectory.Availability == InventorySourceAvailability.Available
        && sources.Kaspersky.Availability == InventorySourceAvailability.Available
        && sources.Opsi.Availability == InventorySourceAvailability.Available
        && (!requireNessus || sources.Nessus.Availability == InventorySourceAvailability.Available);

    private static bool CanCompare(InventorySourceState left, InventorySourceState right) =>
        left.Availability == InventorySourceAvailability.Available
        && right.Availability == InventorySourceAvailability.Available;

    private static bool IsWindowsClient(string? operatingSystem) =>
        !string.IsNullOrWhiteSpace(operatingSystem)
        && operatingSystem.Contains("Windows", StringComparison.OrdinalIgnoreCase)
        && !operatingSystem.Contains("Server", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindows(string? operatingSystem) =>
        !string.IsNullOrWhiteSpace(operatingSystem)
        && operatingSystem.Contains("Windows", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAny(string? value, IReadOnlyList<string>? patterns) =>
        !string.IsNullOrWhiteSpace(value)
        && (patterns ?? []).Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern)
            && value.Contains(pattern.Trim().Trim('*'), StringComparison.OrdinalIgnoreCase));

    private static void AddStaleFinding(
        List<HygieneFinding> findings,
        HygieneFindingCode code,
        string label,
        DateTimeOffset? timestamp,
        DateTimeOffset now,
        ItLifecycleOptions options) =>
        AddStaleFinding(findings, code, label, timestamp, now, options.StaleWarningDays, options.StaleCriticalDays);

    private static void AddStaleFinding(
        List<HygieneFinding> findings,
        HygieneFindingCode code,
        string label,
        DateTimeOffset? timestamp,
        DateTimeOffset now,
        int warningDays,
        int criticalDays)
    {
        if (timestamp is null)
        {
            return;
        }

        double ageDays = Math.Max(0, (now - timestamp.Value).TotalDays);
        if (ageDays > criticalDays)
        {
            findings.Add(new HygieneFinding(
                code,
                HygieneFindingSeverity.Critical,
                $"{label} was {Math.Floor(ageDays)} days ago (cleanup threshold: {criticalDays} days)."));
        }
        else if (ageDays > warningDays)
        {
            findings.Add(new HygieneFinding(
                code,
                HygieneFindingSeverity.Warning,
                $"{label} was {Math.Floor(ageDays)} days ago (warning threshold: {warningDays} days)."));
        }
    }

    private static bool TryParseVersion(string? value, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] tokens = value.Trim().Split('.');
        parts = new int[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
        {
            if (!int.TryParse(tokens[index], out parts[index]) || parts[index] < 0)
            {
                parts = [];
                return false;
            }
        }

        return true;
    }
}
