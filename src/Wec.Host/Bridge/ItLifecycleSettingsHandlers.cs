using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle;

namespace Wec.Host.Bridge;

public sealed record KasperskySettingsValue(
    string Server,
    int Port,
    int RequestTimeoutSeconds,
    string TrustedCertificateThumbprint,
    IReadOnlyList<string> ExcludedAdministrationGroups);

public sealed record ItLifecycleSettingsValue(
    int InventoryLimit,
    int StaleWarningDays,
    int StaleCriticalDays,
    string TargetAgentVersion,
    string TargetKesVersion,
    KasperskySettingsValue Kaspersky);

public sealed record GetItLifecycleSettingsRequest;

public sealed record SaveItLifecycleSettingsRequest(ItLifecycleSettingsValue Settings);

public sealed record ItLifecycleSettingsResult(
    ItLifecycleSettingsValue Settings,
    bool RestartRequired);

internal sealed class GetItLifecycleSettingsHandler
    : IActionHandler<GetItLifecycleSettingsRequest, ItLifecycleSettingsResult>
{
    private readonly IOptions<ItLifecycleOptions> _options;

    public GetItLifecycleSettingsHandler(IOptions<ItLifecycleOptions> options)
    {
        _options = options;
    }

    public string Module => "system";

    public string Action => "getItLifecycleSettings";

    public Task<Result<ItLifecycleSettingsResult>> HandleAsync(
        GetItLifecycleSettingsRequest payload,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new ItLifecycleSettingsResult(
            Map(_options.Value),
            RestartRequired: false)));

    internal static ItLifecycleSettingsValue Map(ItLifecycleOptions options) => new(
        options.InventoryLimit,
        options.StaleWarningDays,
        options.StaleCriticalDays,
        options.TargetAgentVersion,
        options.TargetKesVersion,
        new KasperskySettingsValue(
            options.Kaspersky.Server,
            options.Kaspersky.Port,
            (int)options.Kaspersky.RequestTimeout.TotalSeconds,
            options.Kaspersky.TrustedCertificateThumbprint,
            options.Kaspersky.ExcludedAdministrationGroups ?? []));
}

internal sealed class SaveItLifecycleSettingsHandler
    : IActionHandler<SaveItLifecycleSettingsRequest, ItLifecycleSettingsResult>
{
    private readonly UserSettingsStore _store;

    public SaveItLifecycleSettingsHandler(UserSettingsStore store)
    {
        _store = store;
    }

    public string Module => "system";

    public string Action => "saveItLifecycleSettings";

    public async Task<Result<ItLifecycleSettingsResult>> HandleAsync(
        SaveItLifecycleSettingsRequest payload,
        CancellationToken cancellationToken)
    {
        Result<ItLifecycleOptions> validated = Validate(payload.Settings);
        if (validated.IsFailure)
        {
            return Result.Failure<ItLifecycleSettingsResult>(validated.Error!);
        }

        Result<bool> saved = await _store.SaveItLifecycleAsync(validated.Value, cancellationToken);
        return saved.IsFailure
            ? Result.Failure<ItLifecycleSettingsResult>(saved.Error!)
            : Result.Success(new ItLifecycleSettingsResult(
                GetItLifecycleSettingsHandler.Map(validated.Value),
                RestartRequired: true));
    }

    private static Result<ItLifecycleOptions> Validate(ItLifecycleSettingsValue input)
    {
        if (input.InventoryLimit is < 1 or > 100_000)
        {
            return Invalid("Inventory limit must be between 1 and 100000.");
        }

        if (input.StaleWarningDays is < 1 or > 3650
            || input.StaleCriticalDays is < 1 or > 3650
            || input.StaleCriticalDays <= input.StaleWarningDays)
        {
            return Invalid("The critical stale threshold must be greater than the warning threshold.");
        }

        if (input.Kaspersky.Port is < 1 or > 65_535)
        {
            return Invalid("The Kaspersky OpenAPI port must be between 1 and 65535.");
        }

        if (input.Kaspersky.RequestTimeoutSeconds is < 1 or > 600)
        {
            return Invalid("The Kaspersky request timeout must be between 1 and 600 seconds.");
        }

        string thumbprint = NormalizeThumbprint(input.Kaspersky.TrustedCertificateThumbprint);
        if (thumbprint.Length is not 0 and not 40 and not 64)
        {
            return Invalid("The certificate thumbprint must be empty, SHA-1 (40 hex characters), or SHA-256 (64 hex characters).");
        }

        var options = new ItLifecycleOptions
        {
            InventoryLimit = input.InventoryLimit,
            StaleWarningDays = input.StaleWarningDays,
            StaleCriticalDays = input.StaleCriticalDays,
            TargetAgentVersion = input.TargetAgentVersion.Trim(),
            TargetKesVersion = input.TargetKesVersion.Trim(),
            Kaspersky = new KasperskyOptions
            {
                Server = input.Kaspersky.Server.Trim(),
                Port = input.Kaspersky.Port,
                RequestTimeout = TimeSpan.FromSeconds(input.Kaspersky.RequestTimeoutSeconds),
                TrustedCertificateThumbprint = thumbprint,
                ExcludedAdministrationGroups = (input.Kaspersky.ExcludedAdministrationGroups ?? [])
                    .Where(group => !string.IsNullOrWhiteSpace(group))
                    .Select(group => group.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            },
        };

        var validationResults = new List<ValidationResult>();
        return Validator.TryValidateObject(options, new ValidationContext(options), validationResults, true)
            ? Result.Success(options)
            : Invalid(string.Join(" ", validationResults.Select(result => result.ErrorMessage)));
    }

    private static string NormalizeThumbprint(string value) =>
        string.Concat(value.Where(Uri.IsHexDigit)).ToUpperInvariant();

    private static Result<ItLifecycleOptions> Invalid(string message) =>
        Result.Failure<ItLifecycleOptions>(new Error(ErrorCode.InvalidRequest, message));
}

/// <summary>
/// Small merge-preserving store for UI-editable settings. New settings pages
/// can add a section writer here without inventing another persistence path.
/// </summary>
internal sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public UserSettingsStore(string path)
    {
        _path = path;
    }

    public async Task<Result<bool>> SaveItLifecycleAsync(
        ItLifecycleOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            JsonObject root = await ReadRootAsync(cancellationToken);
            JsonObject wec = GetOrCreateObject(root, "Wec");
            wec["ItLifecycle"] = JsonSerializer.SerializeToNode(options, JsonOptions);

            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    root.ToJsonString(JsonOptions),
                    cancellationToken);
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return Result.Success(true);
        }
        catch (JsonException exception)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.FileWriteFailed,
                "The existing user settings file contains invalid JSON and was not overwritten.")
            {
                Details = exception.Message,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.FileWriteFailed,
                "The user settings file could not be saved.")
            {
                Details = exception.Message,
            });
        }
    }

    public async Task<Result<bool>> SaveOpsiAsync(
        OpsiSettingsValue settings,
        CancellationToken cancellationToken)
    {
        try
        {
            JsonObject root = await ReadRootAsync(cancellationToken);
            JsonObject wec = GetOrCreateObject(root, "Wec");
            JsonObject patch = GetOrCreateObject(wec, "PatchManagement");
            patch["OpsiServer"] = settings.Server;
            patch["DefaultServicePort"] = settings.Port;
            patch["OpsiRequestTimeout"] = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds).ToString("c");
            patch["DefaultDepotFilter"] = settings.DefaultDepotFilter;
            patch["TrustServerCertificate"] = settings.TrustServerCertificate;
            await WriteRootAsync(root, cancellationToken);
            return Result.Success(true);
        }
        catch (JsonException exception)
        {
            return InvalidSettingsFile(exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SettingsWriteFailure(exception);
        }
    }

    private async Task WriteRootAsync(JsonObject root, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(JsonOptions), cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static Result<bool> InvalidSettingsFile(JsonException exception) =>
        Result.Failure<bool>(new Error(
            ErrorCode.FileWriteFailed,
            "The existing user settings file contains invalid JSON and was not overwritten.")
        {
            Details = exception.Message,
        });

    private static Result<bool> SettingsWriteFailure(Exception exception) =>
        Result.Failure<bool>(new Error(ErrorCode.FileWriteFailed, "The user settings file could not be saved.")
        {
            Details = exception.Message,
        });

    private async Task<JsonObject> ReadRootAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new JsonObject();
        }

        string json = await File.ReadAllTextAsync(_path, cancellationToken);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("The root value must be a JSON object.");
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }
}
