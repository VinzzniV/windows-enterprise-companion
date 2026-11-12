using Wec.Core.Opsi;

namespace Wec.Modules.PatchManagement.Application;

public sealed record OpsiSession(OpsiConnection Connection, OpsiServerInfo ServerInfo)
{
    public Guid SessionId { get; } = Guid.NewGuid();
}

/// <summary>
/// Live opsi session: set by a successful connect, cleared by disconnect and
/// gone on process exit. An optional persisted copy is owned separately by
/// Windows Credential Manager; passwords are never logged or returned.
/// </summary>
public sealed class OpsiSessionState
{
    private OpsiSession? _current;

    public OpsiSession? Current => Volatile.Read(ref _current);

    public void Set(OpsiSession session) => Volatile.Write(ref _current, session);

    public void Clear() => Volatile.Write(ref _current, null);
}
