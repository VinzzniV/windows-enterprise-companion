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
        string[]? monitoredServices = null,
        int eventLogMaxEntries = 500) =>
        Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions
        {
            EventLogNames = eventLogNames ?? ["System"],
            EventLogErrorWarningThreshold = errorThreshold,
            EventLogMaxEntries = eventLogMaxEntries,
            MonitoredServices = monitoredServices ?? ["Dhcp", "Dnscache"],
        });

    public static WmiInstance Instance(params (string Name, object? Value)[] properties) =>
        new(properties.ToDictionary(property => property.Name, property => property.Value));

    public static void SetUpWmiQuery(
        this IWmiQueryService wmiQueryService,
        string classNameFragment,
        params WmiInstance[] instances) =>
        wmiQueryService
            .QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(classNameFragment, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(instances));

    public static EventLogEntrySummary Entry(string level, string provider = "TestProvider") =>
        new(provider, 1000, level, TestDefaults.Now);
}

public class EventLogSummaryDiagnosticTests
{
    private readonly IEventLogReader _eventLogReader = Substitute.For<IEventLogReader>();
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private EventLogSummaryDiagnostic CreateDiagnostic(
        string[]? logNames = null,
        int errorThreshold = 50,
        int maxEntries = 500) =>
        new(
            _eventLogReader,
            _wmiQueryService,
            SystemTestSetup.Options(logNames, errorThreshold, eventLogMaxEntries: maxEntries),
            TestDefaults.Clock());

    private void SetUpEntries(params EventLogEntrySummary[] entries) =>
        _eventLogReader
            .ReadRecentCriticalAndErrorEntries(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>())
            .Returns(Result.Success<IReadOnlyList<EventLogEntrySummary>>(entries));

    [Fact]
    public async Task QuietLog_ProducesPassWithCounts()
    {
        SetUpEntries(SystemTestSetup.Entry("Error"));

        DiagnosticResult result = Assert.Single(await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));
        Assert.Equal("WEC-DIAG-SYS-EVENTLOG", result.DiagnosticId);
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

    [Fact]
    public async Task RemoteTarget_QueriesErrorsThroughWmi()
    {
        _wmiQueryService.SetUpWmiQuery("Win32_NTLogEvent",
            SystemTestSetup.Instance(
                ("SourceName", "Service Control Manager"),
                ("EventCode", 7031L),
                ("EventType", 1L),
                ("TimeGenerated", new DateTime(2026, 7, 2, 16, 0, 0, DateTimeKind.Utc))));
        var remoteContext = new DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-042"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(remoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal("1", result.Evidence["errorEntries"]);
        Assert.Contains("Service Control Manager (x1)", result.Evidence["topProviders"], StringComparison.Ordinal);
        await _wmiQueryService.Received(1).QueryAsync(
            Arg.Is<Wec.Core.Targets.ScanTarget>(target => target.Host == "pc-042"),
            Arg.Any<Wec.Core.Targets.ScanCredentials>(),
            Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
            @"root\cimv2",
            Arg.Is<string>(query =>
                query.Contains("Logfile='System'", StringComparison.Ordinal)
                && query.Contains("EventType=1", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        _eventLogReader.DidNotReceiveWithAnyArgs()
            .ReadRecentCriticalAndErrorEntries(default!, default, default);
    }

    [Fact]
    public async Task RemoteWmiFailure_ProducesNotRunWithErrorCode()
    {
        _wmiQueryService.QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("unreachable")));
        var remoteContext = new DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-042"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(remoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal(nameof(ErrorCode.WmiUnavailable), result.Evidence["errorCode"]);
    }

    [Fact]
    public async Task RemoteResultCap_DoesNotHideAnElevatedErrorCount()
    {
        _wmiQueryService.SetUpWmiQuery("Win32_NTLogEvent",
            [.. Enumerable.Range(0, 3).Select(index => SystemTestSetup.Instance(
                ("SourceName", $"Provider {index}"),
                ("EventType", 1L),
                ("TimeGenerated", new DateTime(2026, 7, 2, 16, 0, 0, DateTimeKind.Utc))))]);
        var remoteContext = new DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-042"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic(errorThreshold: 2, maxEntries: 1)
                .EvaluateAsync(remoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal("3", result.Evidence["errorEntries"]);
        Assert.Equal("true", result.Evidence["truncated"]);
        Assert.Equal("3", result.Evidence["matchedEntries"]);
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
        Assert.Equal("WEC-DIAG-SYS-SERVICES", result.DiagnosticId);
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
