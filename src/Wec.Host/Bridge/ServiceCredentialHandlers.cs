using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

public sealed record ServiceCredentialStatus(bool Saved, string? UserName, string? Domain);
public sealed record ServiceCredentialStatuses(
    ServiceCredentialStatus Kaspersky,
    ServiceCredentialStatus Opsi);
public sealed record GetServiceCredentialStatusesRequest;
public sealed record SaveServiceCredentialRequest(
    ServiceCredentialKind Kind,
    string UserName,
    string? Domain,
    string Password);
public sealed record DeleteServiceCredentialRequest(ServiceCredentialKind Kind);

internal sealed class GetServiceCredentialStatusesHandler
    : IActionHandler<GetServiceCredentialStatusesRequest, ServiceCredentialStatuses>
{
    private readonly IServiceCredentialStore _store;

    public GetServiceCredentialStatusesHandler(IServiceCredentialStore store) => _store = store;

    public string Module => "system";
    public string Action => "getServiceCredentialStatuses";

    public Task<Result<ServiceCredentialStatuses>> HandleAsync(
        GetServiceCredentialStatusesRequest payload,
        CancellationToken cancellationToken)
    {
        Result<StoredServiceCredential?> kaspersky = _store.Read(ServiceCredentialKind.Kaspersky);
        if (kaspersky.IsFailure)
        {
            return Task.FromResult(Result.Failure<ServiceCredentialStatuses>(kaspersky.Error!));
        }
        Result<StoredServiceCredential?> opsi = _store.Read(ServiceCredentialKind.Opsi);
        if (opsi.IsFailure)
        {
            return Task.FromResult(Result.Failure<ServiceCredentialStatuses>(opsi.Error!));
        }
        return Task.FromResult(Result.Success(new ServiceCredentialStatuses(
            Status(kaspersky.Value),
            Status(opsi.Value))));
    }

    private static ServiceCredentialStatus Status(StoredServiceCredential? credential) =>
        credential is null
            ? new ServiceCredentialStatus(false, null, null)
            : new ServiceCredentialStatus(true, credential.UserName, credential.Domain);
}

internal sealed class SaveServiceCredentialHandler
    : IActionHandler<SaveServiceCredentialRequest, ServiceCredentialStatus>
{
    private readonly IServiceCredentialStore _store;

    public SaveServiceCredentialHandler(IServiceCredentialStore store) => _store = store;

    public string Module => "system";
    public string Action => "saveServiceCredential";

    public Task<Result<ServiceCredentialStatus>> HandleAsync(
        SaveServiceCredentialRequest payload,
        CancellationToken cancellationToken)
    {
        var credential = new StoredServiceCredential(
            payload.UserName.Trim(),
            string.IsNullOrWhiteSpace(payload.Domain) ? null : payload.Domain.Trim(),
            payload.Password);
        Result<bool> saved = _store.Save(payload.Kind, credential);
        return Task.FromResult(saved.IsFailure
            ? Result.Failure<ServiceCredentialStatus>(saved.Error!)
            : Result.Success(new ServiceCredentialStatus(true, credential.UserName, credential.Domain)));
    }
}

internal sealed class DeleteServiceCredentialHandler
    : IActionHandler<DeleteServiceCredentialRequest, ServiceCredentialStatus>
{
    private readonly IServiceCredentialStore _store;

    public DeleteServiceCredentialHandler(IServiceCredentialStore store) => _store = store;

    public string Module => "system";
    public string Action => "deleteServiceCredential";

    public Task<Result<ServiceCredentialStatus>> HandleAsync(
        DeleteServiceCredentialRequest payload,
        CancellationToken cancellationToken)
    {
        Result<bool> deleted = _store.Delete(payload.Kind);
        return Task.FromResult(deleted.IsFailure
            ? Result.Failure<ServiceCredentialStatus>(deleted.Error!)
            : Result.Success(new ServiceCredentialStatus(false, null, null)));
    }
}
