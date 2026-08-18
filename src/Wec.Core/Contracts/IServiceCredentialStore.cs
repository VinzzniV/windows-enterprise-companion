using Wec.Core.Results;

namespace Wec.Core.Contracts;

public enum ServiceCredentialKind
{
    Kaspersky = 0,
    Opsi,
    Nessus,
}

public sealed record StoredServiceCredential(
    string UserName,
    string? Domain,
    string Password)
{
    public override string ToString() => "StoredServiceCredential { Protected }";
}

/// <summary>User-bound secure storage for service accounts. Admin scan credentials are out of scope.</summary>
public interface IServiceCredentialStore
{
    Result<StoredServiceCredential?> Read(ServiceCredentialKind kind);
    Result<bool> Save(ServiceCredentialKind kind, StoredServiceCredential credential);
    Result<bool> Delete(ServiceCredentialKind kind);
}
