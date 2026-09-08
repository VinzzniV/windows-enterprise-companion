using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Handlers;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class KasperskyCertificateHandlerTests
{
    [Fact]
    public async Task RejectsInvalidPortWithoutOpeningAConnection()
    {
        var handler = new GetKasperskyCertificateHandler();

        Result<KasperskyCertificateResult> result = await handler.HandleAsync(
            new GetKasperskyCertificateRequest("ksc.example.test", 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.ServiceUnavailable, result.Error!.Code);
        Assert.Equal("The Kaspersky certificate could not be retrieved.", result.Error.Message);
        Assert.Contains("between 1 and 65535", result.Error.Details, StringComparison.Ordinal);
    }
}
