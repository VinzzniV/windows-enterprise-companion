using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Dhcp;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.PrintManagement;
using Wec.Modules.PrintManagement.Application;

namespace Wec.Modules.PrintManagement.Tests.Application;

public sealed class DhcpCheckServiceTests
{
    private readonly IDhcpReader _reader = Substitute.For<IDhcpReader>();

    private DhcpCheckService CreateService() =>
        new(_reader, Options.Create(new PrintManagementOptions()));

    [Fact]
    public async Task CheckAsync_ReturnsOnlyReservationsForRequestedIps()
    {
        _reader.GetReservationsAsync("dhcp01", Arg.Any<ScanCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DhcpReservation>>(
            [
                new("172.20.20.12", "00-11-22", "PK-NETPRT012"),
                new("172.20.20.99", "aa-bb-cc", "Some-Other-Host"),
            ]));

        Result<DhcpCheckResult> result = await CreateService().CheckAsync(
            "dhcp01", ScanCredentials.CurrentUser, ["172.20.20.12", "172.20.20.13"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        DhcpReservationInfo reserved = Assert.Single(result.Value.Reserved);
        Assert.Equal("172.20.20.12", reserved.Ip);
        Assert.Equal("PK-NETPRT012", reserved.Name);
    }

    [Fact]
    public async Task CheckAsync_MatchesIpCaseInsensitively()
    {
        _reader.GetReservationsAsync(Arg.Any<string>(), Arg.Any<ScanCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DhcpReservation>>([new("10.0.0.1", null, null)]));

        Result<DhcpCheckResult> result = await CreateService().CheckAsync(
            "dhcp01", ScanCredentials.CurrentUser, ["10.0.0.1"], CancellationToken.None);

        Assert.Single(result.Value.Reserved);
    }

    [Fact]
    public async Task CheckAsync_PropagatesReaderFailure()
    {
        _reader.GetReservationsAsync(Arg.Any<string>(), Arg.Any<ScanCredentials>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DhcpReservation>>(
                new Error(ErrorCode.RemoteCommandFailed, "module missing")));

        Result<DhcpCheckResult> result = await CreateService().CheckAsync(
            "dhcp01", ScanCredentials.CurrentUser, ["10.0.0.1"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.RemoteCommandFailed, result.Error!.Code);
    }
}
