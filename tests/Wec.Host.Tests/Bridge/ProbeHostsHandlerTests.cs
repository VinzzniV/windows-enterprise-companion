using System.Net;
using System.Net.Sockets;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class ProbeHostsHandlerTests
{
    private readonly IPingProbe _pingProbe = Substitute.For<IPingProbe>();

    private ProbeHostsHandler CreateHandler() => new(_pingProbe);

    [Fact]
    public async Task IsPortOpenAsync_OpenPort_ReturnsTrue()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            bool open = await ProbeHostsHandler.IsPortOpenAsync(
                "127.0.0.1", port, TimeSpan.FromSeconds(2), CancellationToken.None);
            Assert.True(open);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task IsPortOpenAsync_ClosedPort_ReturnsFalse()
    {
        // Bind then immediately release a port so nothing is listening on it.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        bool open = await ProbeHostsHandler.IsPortOpenAsync(
            "127.0.0.1", port, TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.False(open);
    }

    [Fact]
    public async Task HandleAsync_DedupesHostsAndMapsPingReachability()
    {
        _pingProbe
            .SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(true, 1, "Success")));

        Result<ProbeHostsResponse> result = await CreateHandler().HandleAsync(
            new ProbeHostsRequest(["PC-1", "pc-1", "  ", ""]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        HostProbeResult host = Assert.Single(result.Value.Results);
        Assert.Equal("PC-1", host.Host);
        Assert.True(host.Reachable); // from the mocked ping
        Assert.False(host.Manageable); // the bogus name has no WinRM listener
    }

    [Fact]
    public async Task HandleAsync_NullHosts_ReturnsEmpty()
    {
        Result<ProbeHostsResponse> result = await CreateHandler().HandleAsync(
            new ProbeHostsRequest(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Results);
    }
}
