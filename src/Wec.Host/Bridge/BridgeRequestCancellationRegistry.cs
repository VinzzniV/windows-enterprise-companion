using System.Collections.Concurrent;

namespace Wec.Host.Bridge;

internal sealed class BridgeRequestCancellationRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new(StringComparer.Ordinal);

    public bool TryRegister(string requestId, out CancellationTokenSource cancellation)
    {
        var candidate = new CancellationTokenSource();
        if (_requests.TryAdd(requestId, candidate))
        {
            cancellation = candidate;
            return true;
        }

        candidate.Dispose();
        cancellation = null!;
        return false;
    }

    public bool Cancel(string requestId)
    {
        if (!_requests.TryGetValue(requestId, out CancellationTokenSource? cancellation))
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Complete(string requestId)
    {
        if (_requests.TryRemove(requestId, out CancellationTokenSource? cancellation))
        {
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        foreach ((string requestId, _) in _requests)
        {
            Complete(requestId);
        }
    }
}
