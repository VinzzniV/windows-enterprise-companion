using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Tests.Application;

public class SecurityScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 15, 0, 0, TimeSpan.Zero);

    private readonly ISecurityScanRepository _repository = Substitute.For<ISecurityScanRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private SecurityScanService CreateService(params ISecurityCheck[] checks)
    {
        _clock.UtcNow.Returns(Now);
        _repository
            .SaveScanAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<ScanStatus>(),
                Arg.Any<IReadOnlyList<SecurityFinding>>(),
                Arg.Any<CancellationToken>())
            .Returns(42L);
        return new SecurityScanService(checks, _repository, _clock, NullLogger<SecurityScanService>.Instance);
    }

    private static ISecurityCheck CheckReturning(params SecurityFinding[] findings)
    {
        var check = Substitute.For<ISecurityCheck>();
        check.CheckId.Returns("TEST-CHECK");
        check.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityFinding>>(findings));
        return check;
    }

    private static SecurityFinding Finding(string findingId) => new(
        findingId,
        "title",
        "description",
        FindingSeverity.High,
        FindingCategory.Firewall,
        "resource",
        new Dictionary<string, string>(),
        "recommendation",
        RequiredPrivilege: null,
        CapturedAtUtc: Now);

    [Fact]
    public async Task RunScan_AggregatesFindingsFromAllChecksAndPersistsThem()
    {
        SecurityScanService service = CreateService(
            CheckReturning(Finding("A")),
            CheckReturning(Finding("B"), Finding("C")));

        Result<SecurityScanResult> result = await service.RunScanAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42L, result.Value.ScanId);
        Assert.Equal(ScanStatus.Completed, result.Value.Status);
        Assert.Equal(new[] { "A", "B", "C" }, result.Value.Findings.Select(finding => finding.FindingId));
        await _repository.Received(1).SaveScanAsync(
            Now,
            Now,
            ScanStatus.Completed,
            Arg.Is<IReadOnlyList<SecurityFinding>>(findings => findings.Count == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CrashingCheck_DoesNotHideOtherResults_AndDegradesScanStatus()
    {
        var crashingCheck = Substitute.For<ISecurityCheck>();
        crashingCheck.CheckId.Returns("CRASHING-CHECK");
        crashingCheck.EvaluateAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bug"));

        SecurityScanService service = CreateService(crashingCheck, CheckReturning(Finding("SURVIVOR")));

        Result<SecurityScanResult> result = await service.RunScanAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScanStatus.CompletedWithErrors, result.Value.Status);
        Assert.Equal("SURVIVOR", Assert.Single(result.Value.Findings).FindingId);
    }

    [Fact]
    public async Task GetLatestScan_ReturnsNullScanWhenNothingWasPersisted()
    {
        _repository.GetLatestScanAsync(Arg.Any<CancellationToken>()).Returns((SecurityScanResult?)null);
        SecurityScanService service = CreateService();

        Result<LatestScanResult> result = await service.GetLatestScanAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Scan);
    }
}
