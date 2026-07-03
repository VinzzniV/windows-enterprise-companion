namespace Wec.Core.Opsi;

/// <summary>
/// Connection to one opsi service (opsiconfd JSON-RPC, ADR 0008). The
/// password is session-scoped in-memory state owned by the Patch Management
/// module — it is never persisted and never logged.
/// <see cref="TrustServerCertificate"/> disables chain validation for this
/// connection only (opsi's default CA is self-signed).
/// </summary>
public sealed record OpsiConnection(
    Uri ServiceUrl,
    string UserName,
    string Password,
    bool TrustServerCertificate,
    TimeSpan RequestTimeout)
{
    // The generated record ToString would print the password; keep it out of
    // every log/exception path
    public override string ToString() =>
        $"OpsiConnection {{ ServiceUrl = {ServiceUrl}, UserName = {UserName}, "
        + $"TrustServerCertificate = {TrustServerCertificate} }}";
}
