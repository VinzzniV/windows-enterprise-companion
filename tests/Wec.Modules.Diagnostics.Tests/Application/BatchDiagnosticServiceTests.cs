using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Domain;
using Wec.Modules.Diagnostics.Persistence;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Wec.Modules.Diagnostics.Tests.Application;

public sealed class BatchDiagnosticServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    private readonly IDiagnostic _diagnostic = Substitute.For<IDiagnostic>();
    private readonly IDiagnosticRunRepository _repository = Substitute.For<IDiagnosticRunRepository>();
    private readonly IBridgeEventPublisher _eventPublisher = Substitute.For<IBridgeEventPublisher>();
    private readonly List<BridgeEvent> _publishedEvents = [];

    public BatchDiagnosticServiceTests()
    {
        _diagnostic.DiagnosticId.Returns("TEST-HEALTH");
        _diagnostic.EvaluateAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                DiagnosticContext context = call.ArgAt<DiagnosticContext>(0);
                return Task.FromResult<IReadOnlyList<DiagnosticResult>>([new DiagnosticResult(
                    "TEST-HEALTH",
                    "Test health",
                    DiagnosticStatus.Pass,
                    DiagnosticCategory.System,
                    context.Target.DisplayName,
                    new Dictionary<string, string>(),
                    [],
                    null,
                    Now)]);
            });
        _repository.SaveAsync(
                Arg.Any<string>(),
                Arg.Any<DiagnosticRunResult>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _eventPublisher
            .When(publisher => publisher.Publish(Arg.Any<BridgeEvent>()))
            .Do(call =>
            {
                lock (_publishedEvents)
                {
                    _publishedEvents.Add(call.Arg<BridgeEvent>());
                }
            });
    }

    [Fact]
    public async Task EmptyAndOversizedHostSets_AreRejectedBeforeRunning()
    {
        BatchDiagnosticService service = CreateService(maxBatchHosts: 2);

        Result<DiagnosticBatchResult> empty = await service.RunAsync(
            ["", " "], ScanCredentials.CurrentUser, CancellationToken.None);
        Result<DiagnosticBatchResult> oversized = await service.RunAsync(
            ["pc-01", "pc-02", "pc-03"], ScanCredentials.CurrentUser, CancellationToken.None);

        Assert.Equal(ErrorCode.InvalidRequest, empty.Error!.Code);
        Assert.Equal(ErrorCode.InvalidRequest, oversized.Error!.Code);
        Assert.Contains("at most 2 hosts", oversized.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_publishedEvents);
    }

    [Fact]
    public async Task OneHostPersistenceFailure_DoesNotAbortOtherHealthRuns()
    {
        _repository.SaveAsync(
                "PC-BAD",
                Arg.Any<DiagnosticRunResult>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("fixture persistence failure"));

        Result<DiagnosticBatchResult> result = await CreateService().RunAsync(
            [" pc-bad ", "pc-good", "PC-GOOD"],
            ScanCredentials.CurrentUser,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Hosts.Count);
        Assert.Equal(DiagnosticBatchHostStatus.Failed, result.Value.Hosts[0].Status);
        Assert.Equal(ErrorCode.InternalError, result.Value.Hosts[0].Error!.Code);
        Assert.Equal(DiagnosticBatchHostStatus.Completed, result.Value.Hosts[1].Status);
        Assert.Equal("pc-good", result.Value.Hosts[1].Run!.Results.Single().AffectedResource);

        List<DiagnosticBatchProgress> progress;
        lock (_publishedEvents)
        {
            progress = _publishedEvents
                .Select(bridgeEvent => Assert.IsType<DiagnosticBatchProgress>(bridgeEvent.Payload))
                .ToList();
        }
        Assert.All(_publishedEvents, bridgeEvent =>
        {
            Assert.Equal("diagnostics", bridgeEvent.Module);
            Assert.Equal("batchRunProgress", bridgeEvent.EventName);
        });
        Assert.Contains(progress, item => item.Host == "pc-good" && item.Status == DiagnosticBatchHostStatus.Queued);
        Assert.Contains(progress, item => item.Host == "pc-good" && item.Status == DiagnosticBatchHostStatus.Running);
        Assert.Contains(progress, item => item.Host == "pc-good" && item.Status == DiagnosticBatchHostStatus.Completed);
    }

    private BatchDiagnosticService CreateService(int maxParallelScans = 2, int maxBatchHosts = 50)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var remoteOptions = MsOptions.Create(new RemoteScanOptions
        {
            MaxParallelScans = maxParallelScans,
            MaxBatchHosts = maxBatchHosts,
        });

        var services = new ServiceCollection();
        services.AddSingleton<IDiagnostic>(_diagnostic);
        services.AddSingleton<IDiagnosticRunRepository>(_repository);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton(remoteOptions);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<DiagnosticRunService>>(
            NullLogger<DiagnosticRunService>.Instance);
        services.AddScoped<DiagnosticRunService>();
        ServiceProvider provider = services.BuildServiceProvider();

        return new BatchDiagnosticService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _eventPublisher,
            clock,
            remoteOptions,
            NullLogger<BatchDiagnosticService>.Instance);
    }
}
