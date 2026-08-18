using Wec.Core.Printing;
using Wec.Infrastructure.Printing;

namespace Wec.Infrastructure.IntegrationTests.Printing;

public sealed class PowerShellPrinterPortRemoverTests
{
    [Fact]
    public void ParseResults_ArrayWithSuccessAndFailure_ReadsEach()
    {
        const string json =
            """[{"name":"IP_10.1.1.99","ok":true,"error":null},{"name":"IP_10.1.1.98","ok":false,"error":"port in use"}]""";

        IReadOnlyList<PortRemovalResult> results = PowerShellPrinterPortRemover.ParseResults(json);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Removed);
        Assert.Null(results[0].Error);
        Assert.False(results[1].Removed);
        Assert.Equal("port in use", results[1].Error);
    }

    [Fact]
    public void ParseResults_SingleObject_IsAccepted()
    {
        // Windows PowerShell collapses a one-element array to a bare object
        const string json = """{"name":"IP_10.1.1.99","ok":true,"error":null}""";

        PortRemovalResult result = Assert.Single(PowerShellPrinterPortRemover.ParseResults(json));

        Assert.Equal("IP_10.1.1.99", result.Name);
        Assert.True(result.Removed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    public void ParseResults_EmptyOrNull_ReturnsNothing(string json)
    {
        Assert.Empty(PowerShellPrinterPortRemover.ParseResults(json));
    }
}
