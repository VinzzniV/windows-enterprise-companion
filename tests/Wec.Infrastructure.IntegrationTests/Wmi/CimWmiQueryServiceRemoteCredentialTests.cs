using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Infrastructure.Wmi;

namespace Wec.Infrastructure.IntegrationTests.Wmi;

public sealed class CimWmiQueryServiceRemoteCredentialTests
{
    /// <summary>
    /// Regression: the explicit-credential SecureString was disposed before the
    /// WSMan session authenticated (lazy connect on first operation). The bug
    /// class surfaces as an unhandled ObjectDisposedException instead of a
    /// mapped Result failure — this test drives the full remote path with
    /// explicit credentials against an unreachable loopback endpoint and
    /// asserts the failure stays inside the typed error model.
    /// </summary>
    [Fact]
    public async Task RemoteQueryWithExplicitCredentials_FailsWithMappedErrorNotException()
    {
        var service = new CimWmiQueryService(NullLogger<CimWmiQueryService>.Instance);

        Result<IReadOnlyList<WmiInstance>> result = await service.QueryAsync(
            ScanTarget.Remote("127.0.0.2"),
            ScanCredentials.Explicit("wec-nonexistent-user", "WEC-TEST", "not-a-real-password"),
            new ConnectionOptions { Timeout = TimeSpan.FromSeconds(5) },
            "root/cimv2",
            "SELECT Caption FROM Win32_OperatingSystem",
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Error!.Code,
            new[]
            {
                ErrorCode.AuthenticationFailed,
                ErrorCode.AccessDenied,
                ErrorCode.ConnectionTimeout,
                ErrorCode.WinRmUnavailable,
            });
    }
}
