using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Application.Diagnostics;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Tests.Application;

internal static class SystemTestSetup
{
    public static Microsoft.Extensions.Options.IOptions<DiagnosticsOptions> Options(
        string[]? eventLogNames = null,
        int errorThreshold = 50,
        string[]? monitoredServices = null) =>
        Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions
        {
            DnsProbeHostname = "cloudflare.com",
            EventLogNames = eventLogNames ?? ["System"],
            EventLogErrorWarningThreshold = errorThreshold,
            MonitoredServices = monitoredServices ?? ["Dhcp", "Dnscache"],
        });

    public static WmiInstance Instance(params (string Name, object? Value)[] properties) =>
        new(properties.ToDictionary(property => property.Name, property => property.Value));

    public static void SetUpWmiQuery(
        this IWmiQueryService wmiQueryService,
        string classNameFragment,
        params WmiInstance[] instances)
    {
        // Some diagnostics query through the target-aware overload, some
        // (local-only ones) through the local convenience overload
        wmiQueryService
            .QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(classNameFragment, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(instances));
        wmiQueryService
            .QueryAsync(
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(classNameFragment, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(instances));
    }

    public static EventLogEntrySummary Entry(string level, string provider = "TestProvider") =>
        new(provider, 1000, level, TestDefaults.Now);
}

public class DomainMembershipDiagnosticTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private DomainMembershipDiagnostic CreateDiagnostic() => new(_wmiQueryService, TestDefaults.Clock());

    [Fact]
    public async Task DomainJoined_ProducesPassWithDomainEvidence()
    {
        _wmiQueryService.SetUpWmiQuery("Win32_ComputerSystem", SystemTestSetup.Instance(
            ("DNSHostName", "PC01"), ("PartOfDomain", true), ("Domain", "corp.contoso.example")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal("corp.contoso.example", result.Evidence["domain"]);
    }

    [Fact]
    public async Task Workgroup_ProducesPassWithWorkgroupEvidenceAndHint()
    {
        _wmiQueryService.SetUpWmiQuery("Win32_ComputerSystem", SystemTestSetup.Instance(
            ("DNSHostName", "PC01"), ("PartOfDomain", false), ("Domain", "WORKGROUP"), ("Workgroup", "WORKGROUP")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal("WORKGROUP", result.Evidence["workgroup"]);
        Assert.NotEmpty(result.SuggestedNextSteps);
    }

    [Fact]
    public async Task WmiFailure_ProducesNotRun()
    {
        _wmiQueryService
            .QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("unreachable")));

        Assert.Equal(
            DiagnosticStatus.NotRun,
            Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None)).Status);
    }
}

public class TimeSynchronizationDiagnosticTests
{
    private readonly IRegistryReader _registryReader = Substitute.For<IRegistryReader>();
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private TimeSynchronizationDiagnostic CreateDiagnostic() =>
        new(_registryReader, _wmiQueryService, TestDefaults.Clock());

    private void SetUpConfiguration(string? syncType, string serviceState = "Running", string startMode = "Manual")
    {
        SetUpRegistryValue("Type", syncType);
        SetUpRegistryValue("NtpServer", "time.windows.com,0x9");
        _wmiQueryService.SetUpWmiQuery("Win32_Service", SystemTestSetup.Instance(
            ("Name", "W32Time"), ("State", serviceState), ("StartMode", startMode)));
    }

    private void SetUpRegistryValue(string valueName, object? value) =>
        _registryReader
            .ReadLocalMachineValueAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                valueName,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(value));

    private void SetUpRegistryFailure(string valueName, Error error) =>
        _registryReader
            .ReadLocalMachineValueAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                valueName,
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<object?>(error));

    [Fact]
    public async Task NoSyncConfigured_ProducesWarning()
    {
        SetUpConfiguration("NoSync");

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(result.SuggestedNextSteps, step => step.Contains("Kerberos", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServiceDisabled_ProducesWarning()
    {
        SetUpConfiguration("NTP", serviceState: "Stopped", startMode: "Disabled");

        Assert.Equal(
            DiagnosticStatus.Warning,
            Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task NtpConfiguredWithStoppedManualService_IsStillPass()
    {
        // Stopped + Manual (trigger start) is the normal state on workgroup machines
        SetUpConfiguration("NTP", serviceState: "Stopped", startMode: "Manual");

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal("Stopped (Manual)", result.Evidence["w32TimeService"]);
    }

    [Fact]
    public async Task RegistryAccessDenied_ProducesNotRunWithRequiredPrivilege()
    {
        SetUpRegistryFailure("Type", Error.AccessDenied("denied", PrivilegeLevel.Administrator));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal(PrivilegeLevel.Administrator, result.RequiredPrivilege);
    }

    [Fact]
    public async Task NtpServerRegistryFailure_ProducesNotRunInsteadOfPass()
    {
        SetUpRegistryValue("Type", "NTP");
        SetUpRegistryFailure("NtpServer", Error.AccessDenied("denied", PrivilegeLevel.Administrator));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal(PrivilegeLevel.Administrator, result.RequiredPrivilege);
        Assert.Equal(nameof(ErrorCode.AccessDenied), result.Evidence["errorCode"]);
        await _wmiQueryService.DidNotReceiveWithAnyArgs()
            .QueryAsync(default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task WmiFailure_ProducesNotRunInsteadOfPass()
    {
        SetUpRegistryValue("Type", "NTP");
        SetUpRegistryValue("NtpServer", "time.windows.com,0x9");
        _wmiQueryService
            .QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("unreachable")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal(nameof(ErrorCode.WmiUnavailable), result.Evidence["errorCode"]);
    }

    [Fact]
    public async Task MissingWindowsTimeService_ProducesWarningInsteadOfPass()
    {
        SetUpRegistryValue("Type", "NTP");
        SetUpRegistryValue("NtpServer", "time.windows.com,0x9");
        _wmiQueryService.SetUpWmiQuery("Win32_Service");

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal("not installed", result.Evidence["w32TimeService"]);
    }

    [Theory]
    [InlineData(null, "Manual")]
    [InlineData("Running", null)]
    public async Task IncompleteWindowsTimeServiceData_ProducesNotRun(string? state, string? startMode)
    {
        SetUpRegistryValue("Type", "NTP");
        SetUpRegistryValue("NtpServer", "time.windows.com,0x9");
        _wmiQueryService.SetUpWmiQuery("Win32_Service", SystemTestSetup.Instance(
            ("Name", "W32Time"), ("State", state), ("StartMode", startMode)));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
    }

    [Fact]
    public async Task MissingSynchronizationType_ProducesWarningInsteadOfPass()
    {
        SetUpConfiguration(null);

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task UnsupportedSynchronizationTypeValue_ProducesNotRun()
    {
        SetUpRegistryValue("Type", 1);

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal("Int32", result.Evidence["valueType"]);
    }

    [Fact]
    public async Task UnknownSynchronizationType_ProducesWarningInsteadOfPass()
    {
        SetUpConfiguration("CustomProvider");

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task StoppedAutomaticService_ProducesWarningInsteadOfPass()
    {
        SetUpConfiguration("NTP", serviceState: "Stopped", startMode: "Auto");

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task NtpTypeWithoutServer_ProducesWarningInsteadOfPass()
    {
        SetUpRegistryValue("Type", "NTP");
        SetUpRegistryValue("NtpServer", null);
        _wmiQueryService.SetUpWmiQuery("Win32_Service", SystemTestSetup.Instance(
            ("Name", "W32Time"), ("State", "Running"), ("StartMode", "Manual")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
    }

    [Fact]
    public async Task RemoteTarget_EvaluatesViaRemoteRegistryAndWmi()
    {
        // No LocalPerspective skip anymore: time sync is real machine state read remotely.
        SetUpConfiguration("NoSync");
        var remote = new DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-1.contoso.local"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(remote, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.DoesNotContain("only", result.Title, StringComparison.OrdinalIgnoreCase);
    }
}

public class EventLogSummaryDiagnosticTests
{
    private readonly IEventLogReader _eventLogReader = Substitute.For<IEventLogReader>();

    private EventLogSummaryDiagnostic CreateDiagnostic(
        string[]? logNames = null,
        int errorThreshold = 50) =>
        new(_eventLogReader, SystemTestSetup.Options(logNames, errorThreshold), TestDefaults.Clock());

    private void SetUpEntries(params EventLogEntrySummary[] entries) =>
        _eventLogReader
            .ReadRecentCriticalAndErrorEntries(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>())
            .Returns(Result.Success<IReadOnlyList<EventLogEntrySummary>>(entries));

    [Fact]
    public async Task QuietLog_ProducesPassWithCounts()
    {
        SetUpEntries(SystemTestSetup.Entry("Error"));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal("1", result.Evidence["errorEntries"]);
        Assert.Equal("0", result.Evidence["criticalEntries"]);
    }

    [Fact]
    public async Task CriticalEntries_ProduceWarning()
    {
        SetUpEntries(SystemTestSetup.Entry("Critical"), SystemTestSetup.Entry("Error"));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal("1", result.Evidence["criticalEntries"]);
    }

    [Fact]
    public async Task ErrorCountAboveThreshold_ProducesWarningWithTopProviders()
    {
        SetUpEntries([.. Enumerable.Range(0, 5).Select(_ => SystemTestSetup.Entry("Error", "NoisyDriver"))]);

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic(errorThreshold: 3).EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("NoisyDriver (x5)", result.Evidence["topProviders"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessDenied_ProducesNotRunWithRequiredPrivilege()
    {
        _eventLogReader
            .ReadRecentCriticalAndErrorEntries(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>())
            .Returns(Result.Failure<IReadOnlyList<EventLogEntrySummary>>(
                Error.AccessDenied("denied", PrivilegeLevel.Administrator)));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal(PrivilegeLevel.Administrator, result.RequiredPrivilege);
    }

    [Fact]
    public async Task MultipleConfiguredLogs_ProduceOneResultPerLog()
    {
        SetUpEntries();

        IReadOnlyList<DiagnosticResult> results = await CreateDiagnostic(["System", "Application"])
            .EvaluateAsync(DiagnosticContext.Local, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }
}

public class ServiceStatusDiagnosticTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private ServiceStatusDiagnostic CreateDiagnostic(string[]? monitoredServices = null) =>
        new(_wmiQueryService, SystemTestSetup.Options(monitoredServices: monitoredServices), TestDefaults.Clock());

    [Fact]
    public async Task AllServicesRunning_ProducesPassWithPerServiceEvidence()
    {
        _wmiQueryService.SetUpWmiQuery("Win32_Service",
            SystemTestSetup.Instance(("Name", "Dhcp"), ("State", "Running"), ("StartMode", "Auto")),
            SystemTestSetup.Instance(("Name", "Dnscache"), ("State", "Running"), ("StartMode", "Auto")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal("Running (Auto)", result.Evidence["Dhcp"]);
    }

    [Fact]
    public async Task StoppedAutomaticService_ProducesWarningNamingTheService()
    {
        _wmiQueryService.SetUpWmiQuery("Win32_Service",
            SystemTestSetup.Instance(("Name", "Dhcp"), ("State", "Stopped"), ("StartMode", "Auto")),
            SystemTestSetup.Instance(("Name", "Dnscache"), ("State", "Running"), ("StartMode", "Auto")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(result.SuggestedNextSteps, step => step.Contains("'Dhcp'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingService_ProducesWarning()
    {
        _wmiQueryService.SetUpWmiQuery("Win32_Service",
            SystemTestSetup.Instance(("Name", "Dnscache"), ("State", "Running"), ("StartMode", "Auto")));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal("not installed", result.Evidence["Dhcp"]);
    }

    [Fact]
    public async Task EmptyConfiguration_ProducesNotRunPointingAtTheOption()
    {
        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic(monitoredServices: []).EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Contains(result.SuggestedNextSteps, step =>
            step.Contains("MonitoredServices", StringComparison.Ordinal));
    }
}
