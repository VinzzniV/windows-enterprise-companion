using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Infrastructure.Microsoft365;

namespace Wec.Host.Bridge;

public sealed record GetMicrosoft365SettingsRequest;
public sealed record SaveMicrosoft365SettingsRequest(Microsoft365Configuration Settings);
public sealed record Microsoft365SettingsResult(Microsoft365Configuration Settings, bool RestartRequired);

internal sealed class GetMicrosoft365SettingsHandler(IOptions<Microsoft365Options> options)
    : IActionHandler<GetMicrosoft365SettingsRequest, Microsoft365SettingsResult>
{
    public string Module => "system";
    public string Action => "getMicrosoft365Settings";

    public Task<Result<Microsoft365SettingsResult>> HandleAsync(
        GetMicrosoft365SettingsRequest payload, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new Microsoft365SettingsResult(options.Value.Configuration, false)));
}

internal sealed class SaveMicrosoft365SettingsHandler(UserSettingsStore store)
    : IActionHandler<SaveMicrosoft365SettingsRequest, Microsoft365SettingsResult>
{
    public string Module => "system";
    public string Action => "saveMicrosoft365Settings";

    public async Task<Result<Microsoft365SettingsResult>> HandleAsync(
        SaveMicrosoft365SettingsRequest payload, CancellationToken cancellationToken)
    {
        Microsoft365Configuration? settings = payload.Settings;
        if (settings is null || !IsIdentifierOrEmpty(settings.TenantId) || !IsIdentifierOrEmpty(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.TenantId) != string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return Result.Failure<Microsoft365SettingsResult>(new Error(ErrorCode.InvalidRequest,
                "Tenant ID and Client ID must both be valid non-empty GUIDs, or both be empty to clear the defaults."));
        }

        Microsoft365Configuration normalized = settings with
        {
            TenantId = Normalize(settings.TenantId),
            ClientId = Normalize(settings.ClientId),
        };
        Result<bool> saved = await store.SaveMicrosoft365Async(normalized, cancellationToken);
        return saved.IsFailure
            ? Result.Failure<Microsoft365SettingsResult>(saved.Error!)
            : Result.Success(new Microsoft365SettingsResult(normalized, true));
    }

    private static bool IsIdentifierOrEmpty(string? value) => string.IsNullOrWhiteSpace(value)
        || Guid.TryParse(value, out Guid id) && id != Guid.Empty;

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty : Guid.Parse(value).ToString("D");
}
