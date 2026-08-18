using Wec.Core.Dhcp;
using Wec.Infrastructure.Dhcp;

namespace Wec.Infrastructure.IntegrationTests.Dhcp;

public sealed class PowerShellDhcpReaderTests
{
    [Fact]
    public void ParseReservations_ArrayOfReservations_ReadsEach()
    {
        const string json =
            """[{"ip":"172.20.20.12","mac":"00-11-22-33-44-55","name":"PK-NETPRT012"},{"ip":"172.20.20.13","mac":null,"name":null}]""";

        IReadOnlyList<DhcpReservation> reservations = PowerShellDhcpReader.ParseReservations(json);

        Assert.Equal(2, reservations.Count);
        Assert.Equal("172.20.20.12", reservations[0].IpAddress);
        Assert.Equal("00-11-22-33-44-55", reservations[0].MacAddress);
        Assert.Equal("PK-NETPRT012", reservations[0].Name);
        Assert.Equal("172.20.20.13", reservations[1].IpAddress);
        Assert.Null(reservations[1].MacAddress);
    }

    [Fact]
    public void ParseReservations_SingleObject_IsAccepted()
    {
        // Windows PowerShell collapses a one-element array to a bare object
        const string json = """{"ip":"172.20.20.12","mac":"00-11-22-33-44-55","name":"PK-NETPRT012"}""";

        DhcpReservation reservation = Assert.Single(PowerShellDhcpReader.ParseReservations(json));

        Assert.Equal("172.20.20.12", reservation.IpAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    public void ParseReservations_EmptyOrNull_ReturnsNoReservations(string json)
    {
        Assert.Empty(PowerShellDhcpReader.ParseReservations(json));
    }

    [Fact]
    public void ParseReservations_SkipsEntriesWithoutIp()
    {
        const string json = """[{"mac":"aa","name":"x"},{"ip":"172.20.20.20"}]""";

        DhcpReservation reservation = Assert.Single(PowerShellDhcpReader.ParseReservations(json));

        Assert.Equal("172.20.20.20", reservation.IpAddress);
    }
}
