using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

public sealed record GetKasperskyCertificateRequest(
    string Server,
    int Port,
    int RequestTimeoutSeconds = 30);

public sealed record KasperskyCertificateResult(
    string Sha256Fingerprint,
    string Subject,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidToUtc);

internal sealed class GetKasperskyCertificateHandler
    : IActionHandler<GetKasperskyCertificateRequest, KasperskyCertificateResult>
{
    public string Module => "employeelifecycle";

    public string Action => "getKasperskyCertificate";

    public async Task<Result<KasperskyCertificateResult>> HandleAsync(
        GetKasperskyCertificateRequest payload,
        CancellationToken cancellationToken)
    {
        try
        {
            KasperskyCertificateInfo certificate = await KasperskySecurityCenterClient.GetCertificateAsync(
                payload.Server,
                payload.Port,
                payload.RequestTimeoutSeconds,
                cancellationToken);
            return Result.Success(new KasperskyCertificateResult(
                certificate.Sha256Fingerprint,
                certificate.Subject,
                certificate.ValidFromUtc,
                certificate.ValidToUtc));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<KasperskyCertificateResult>(new Error(
                ErrorCode.ServiceUnavailable,
                "The Kaspersky certificate could not be retrieved.")
            {
                Details = exception.Message,
            });
        }
    }
}
