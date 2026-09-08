using NSubstitute;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Handlers;
using Wec.Modules.Diagnostics.Persistence;

namespace Wec.Modules.Diagnostics.Tests.Handlers;

public sealed class GetLatestDiagnosticsHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithUnreadableStoredRun_ReturnsTypedFailure()
    {
        IDiagnosticRunRepository repository = Substitute.For<IDiagnosticRunRepository>();
        repository.GetLatestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Wec.Modules.Diagnostics.Domain.DiagnosticRunResult?>>(
                _ => throw new InvalidDataException("Damaged payload."));
        var handler = new GetLatestDiagnosticsHandler(repository);

        Result<LatestDiagnosticRunResult> result = await handler.HandleAsync(
            new GetLatestDiagnosticsRequest(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.StoredDataUnreadable, result.Error!.Code);
    }
}
