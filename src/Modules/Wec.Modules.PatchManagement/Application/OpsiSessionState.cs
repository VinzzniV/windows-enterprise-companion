using Wec.Core.Opsi;

namespace Wec.Modules.PatchManagement.Application;

public sealed record OpsiSession(OpsiConnection Connection, OpsiServerInfo ServerInfo);

/// <summary>
/// Session-scoped opsi credentials (ADR 0008): set by a successful connect,
/// cleared by disconnect, gone on process exit. Never persisted, never
/// logged, never sent back over the bridge.
/// </summary>
public sealed class OpsiSessionState
{
    private OpsiSession? _current;

    public OpsiSession? Current => Volatile.Read(ref _current);

    public void Set(OpsiSession session) => Volatile.Write(ref _current, session);

    public void Clear() => Volatile.Write(ref _current, null);
}
