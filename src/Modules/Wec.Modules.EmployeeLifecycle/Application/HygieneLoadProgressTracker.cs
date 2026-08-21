using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed class HygieneLoadProgressTracker
{
    private const int SourceCount = 4;
    private readonly object _gate = new();
    private readonly string? _operationId;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly IBridgeEventPublisher _events;
    private readonly ItLifecycleOptions _options;
    private Result<AdComputerInventory>? _ad;
    private Result<KasperskyInventory>? _kaspersky;
    private Result<OpsiComputerInventory>? _opsi;
    private Result<NessusComputerInventory>? _nessus;

    public HygieneLoadProgressTracker(
        string? operationId,
        DateTimeOffset startedAtUtc,
        IBridgeEventPublisher events,
        ItLifecycleOptions options)
    {
        _operationId = string.IsNullOrWhiteSpace(operationId) ? null : operationId;
        _startedAtUtc = startedAtUtc;
        _events = events;
        _options = options;
    }

    public void Start() => Publish(HygieneLoadPhase.LoadingSources);

    public async Task<Result<AdComputerInventory>> TrackAdAsync(Task<Result<AdComputerInventory>> task)
    {
        Result<AdComputerInventory> result = await task;
        lock (_gate)
        {
            _ad = result;
            PublishLocked(HygieneLoadPhase.LoadingSources);
        }
        return result;
    }

    public async Task<Result<KasperskyInventory>> TrackKasperskyAsync(Task<Result<KasperskyInventory>> task)
    {
        Result<KasperskyInventory> result = await task;
        lock (_gate)
        {
            _kaspersky = result;
            PublishLocked(HygieneLoadPhase.LoadingSources);
        }
        return result;
    }

    public async Task<Result<OpsiComputerInventory>> TrackOpsiAsync(Task<Result<OpsiComputerInventory>> task)
    {
        Result<OpsiComputerInventory> result = await task;
        lock (_gate)
        {
            _opsi = result;
            PublishLocked(HygieneLoadPhase.LoadingSources);
        }
        return result;
    }

    public async Task<Result<NessusComputerInventory>> TrackNessusAsync(Task<Result<NessusComputerInventory>> task)
    {
        Result<NessusComputerInventory> result = await task;
        lock (_gate)
        {
            _nessus = result;
            PublishLocked(HygieneLoadPhase.LoadingSources);
        }
        return result;
    }

    public void Correlating() => Publish(HygieneLoadPhase.Correlating);

    public void Complete(HygieneSummary summary) => Publish(HygieneLoadPhase.Completed, summary);

    public void Cancel() => Publish(HygieneLoadPhase.Cancelled);

    private void Publish(HygieneLoadPhase phase, HygieneSummary? summary = null)
    {
        if (_operationId is null)
        {
            return;
        }

        lock (_gate)
        {
            PublishLocked(phase, summary);
        }
    }

    private void PublishLocked(HygieneLoadPhase phase, HygieneSummary? finalSummary = null)
    {
        if (_operationId is null)
        {
            return;
        }

        InventorySourceState adState = _ad is null
            ? LoadingState()
            : ItHygieneService.AdState(_ad);
        InventorySourceState kasperskyState = _kaspersky is null
            ? LoadingState()
            : ItHygieneService.KasperskyState(_kaspersky);
        InventorySourceState opsiState = _opsi is null
            ? LoadingState()
            : ItHygieneService.OpsiState(_opsi);
        InventorySourceState nessusState = _nessus is null
            ? LoadingState()
            : ItHygieneService.NessusState(_nessus);
        var sourceStates = new EnvironmentSourceStates(adState, kasperskyState, opsiState, nessusState);

        IReadOnlyList<HygieneDevice> partialDevices = ItHygieneService.CorrelateAndAssess(
            _ad?.IsSuccess == true ? _ad.Value.Computers : [],
            _kaspersky?.IsSuccess == true ? _kaspersky.Value.Computers : [],
            _opsi?.IsSuccess == true ? _opsi.Value.Computers : [],
            _nessus?.IsSuccess == true
                ? _nessus.Value
                : new NessusComputerInventory([], NessusInventoryAvailability.Unavailable, null),
            sourceStates,
            _startedAtUtc,
            _options);
        HygieneSummary partialSummary = finalSummary ?? ItHygieneService.Summarize(partialDevices);

        _events.Publish(new BridgeEvent(
            "employeelifecycle",
            "hygieneProgress",
            new HygieneLoadProgress(
                _operationId,
                phase,
                _startedAtUtc,
                CompletedSources(),
                SourceCount,
                partialDevices.Count,
                partialSummary,
                [
                    Progress("ACTIVE_DIRECTORY", _ad, adState, result => result.Value.Computers.Count),
                    Progress("KASPERSKY", _kaspersky, kasperskyState, result => result.Value.Computers.Count),
                    Progress("OPSI", _opsi, opsiState, result => result.Value.Computers.Count),
                    Progress("NESSUS", _nessus, nessusState, result => result.Value.Computers.Count),
                ])));
    }

    private int CompletedSources() =>
        (_ad is null ? 0 : 1)
        + (_kaspersky is null ? 0 : 1)
        + (_opsi is null ? 0 : 1)
        + (_nessus is null ? 0 : 1);

    private static HygieneSourceProgress Progress<T>(
        string source,
        Result<T>? result,
        InventorySourceState state,
        Func<Result<T>, int> count) => result is null
        ? new HygieneSourceProgress(source, HygieneSourceProgressStatus.Running)
        : new HygieneSourceProgress(
            source,
            ToProgressStatus(state.Availability),
            result.IsSuccess ? count(result) : null,
            state.Error);

    private static HygieneSourceProgressStatus ToProgressStatus(InventorySourceAvailability availability) =>
        availability switch
        {
            InventorySourceAvailability.Available => HygieneSourceProgressStatus.Available,
            InventorySourceAvailability.Partial => HygieneSourceProgressStatus.Partial,
            InventorySourceAvailability.NotConnected => HygieneSourceProgressStatus.NotConnected,
            InventorySourceAvailability.Truncated => HygieneSourceProgressStatus.Truncated,
            _ => HygieneSourceProgressStatus.Unavailable,
        };

    private static InventorySourceState LoadingState() =>
        new(InventorySourceAvailability.NotConnected, "Source is still loading.");
}
