using Wec.Core.Network;
using Wec.Infrastructure.Network;

namespace Wec.Infrastructure.IntegrationTests.Network;

public sealed class NmapScannerTests
{
    // Trimmed real nmap -oX output: DOCTYPE + stylesheet PI, an up host with a
    // reverse-DNS name, an on-link MAC vendor, one open and one closed port, plus
    // a down host that must be reported as not up.
    private const string SampleXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE nmaprun>
        <?xml-stylesheet href="file:///usr/share/nmap/nmap.xsl" type="text/xsl"?>
        <nmaprun scanner="nmap" args="nmap -oX -">
          <host>
            <status state="up" reason="arp-response"/>
            <address addr="172.20.20.12" addrtype="ipv4"/>
            <address addr="00:11:22:33:44:55" addrtype="mac" vendor="Canon Inc."/>
            <hostnames><hostname name="pk-netprt012.kauth.local" type="PTR"/></hostnames>
            <ports>
              <port protocol="tcp" portid="9100"><state state="open"/><service name="jetdirect"/></port>
              <port protocol="tcp" portid="80"><state state="closed"/><service name="http"/></port>
            </ports>
          </host>
          <host>
            <status state="down" reason="no-response"/>
            <address addr="172.20.20.13" addrtype="ipv4"/>
          </host>
        </nmaprun>
        """;

    [Fact]
    public void ParseNmapXml_ReadsUpHost_WithNameVendorAndOpenPort()
    {
        IReadOnlyList<ScannedHost> hosts = NmapScanner.ParseNmapXml(SampleXml);

        ScannedHost up = Assert.Single(hosts, host => host.IpAddress == "172.20.20.12");
        Assert.True(up.IsUp);
        Assert.Equal("pk-netprt012.kauth.local", up.Hostname);
        Assert.Equal("Canon Inc.", up.MacVendor);
        ScannedPort port = Assert.Single(up.OpenPorts);
        Assert.Equal(9100, port.Port);
        Assert.Equal("jetdirect", port.Service);
    }

    [Fact]
    public void ParseNmapXml_ReportsDownHost_AsNotUp()
    {
        IReadOnlyList<ScannedHost> hosts = NmapScanner.ParseNmapXml(SampleXml);

        ScannedHost down = Assert.Single(hosts, host => host.IpAddress == "172.20.20.13");
        Assert.False(down.IsUp);
        Assert.Empty(down.OpenPorts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseNmapXml_EmptyInput_ReturnsNoHosts(string xml) =>
        Assert.Empty(NmapScanner.ParseNmapXml(xml));
}
