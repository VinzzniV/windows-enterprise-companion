using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PatchManagement;

namespace Wec.Host.Bridge;

public sealed record OpsiSettingsValue(
    string Server,
    int Port,
    int RequestTimeoutSeconds,
    string DefaultDepotFilter,
    bool TrustServerCertificate);

public sealed record GetOpsiSettingsRequest;
public sealed record SaveOpsiSettingsRequest(OpsiSettingsValue Settings);
public sealed record OpsiSettingsResult(OpsiSettingsValue Settings, bool RestartRequired);

internal sealed class GetOpsiSettingsHandler
    : IActionHandler<GetOpsiSettingsRequest, OpsiSettingsResult>
{
    private readonly IOptions<PatchManagementOptions> _options;

    public GetOpsiSettingsHandler(IOptions<PatchManagementOptions> options) => _options = options;

    public string Module => "system";
    public string Action => "getOpsiSettings";

    public Task<Result<OpsiSettingsResult>> HandleAsync(
        GetOpsiSettingsRequest payload,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new OpsiSettingsResult(Map(_options.Value), false)));

    internal static OpsiSettingsValue Map(PatchManagementOptions options) => new(
        options.OpsiServer,
        options.DefaultServicePort,
        (int)options.OpsiRequestTimeout.TotalSeconds,
        options.DefaultDepotFilter,
        options.TrustServerCertificate);
}

internal sealed class SaveOpsiSettingsHandler
    : IActionHandler<SaveOpsiSettingsRequest, OpsiSettingsResult>
{
    private readonly UserSettingsStore _store;

    public SaveOpsiSettingsHandler(UserSettingsStore store) => _store = store;

    public string Module => "system";
    public string Action => "saveOpsiSettings";

    public async Task<Result<OpsiSettingsResult>> HandleAsync(
        SaveOpsiSettingsRequest payload,
        CancellationToken cancellationToken)
    {
        OpsiSettingsValue value = payload.Settings with
        {
            Server = payload.Settings.Server.Trim(),
            DefaultDepotFilter = payload.Settings.DefaultDepotFilter.Trim(),
        };
        if (value.Port is < 1 or > 65_535)
        {
            return Invalid("The opsi port must be between 1 and 65535.");
        }
        if (value.RequestTimeoutSeconds is < 1 or > 600)
        {
            return Invalid("The opsi request timeout must be between 1 and 600 seconds.");
        }

        Result<bool> saved = await _store.SaveOpsiAsync(value, cancellationToken);
        return saved.IsFailure
            ? Result.Failure<OpsiSettingsResult>(saved.Error!)
            : Result.Success(new OpsiSettingsResult(value, true));
    }

    private static Result<OpsiSettingsResult> Invalid(string message) =>
        Result.Failure<OpsiSettingsResult>(new Error(ErrorCode.InvalidRequest, message));
}
