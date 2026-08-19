using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using Wec.Host.Runtime;

namespace Wec.Host.Tests.Runtime;

public sealed class StartupFailurePresentationTests
{
    [Fact]
    public void ConfigurationFailure_ExplainsValidatedSetting()
    {
        var exception = new OptionsValidationException(
            "Wec:Frontend",
            typeof(object),
            ["DevServerUrl is invalid."]);

        StartupFailurePresentation presentation = StartupFailurePresentation.From(exception, StartupPhase.Host);

        Assert.Equal("Configuration is invalid", presentation.Title);
        Assert.Contains("DevServerUrl is invalid", presentation.Message);
    }

    [Theory]
    [MemberData(nameof(SanitizedHostFailures))]
    public void HostFailure_DoesNotExposeExceptionDetails(Exception exception, string expectedTitle)
    {
        StartupFailurePresentation presentation = StartupFailurePresentation.From(exception, StartupPhase.Host);

        Assert.Equal(expectedTitle, presentation.Title);
        Assert.DoesNotContain("sensitive-detail", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("application log", presentation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingWebViewRuntime_HasSpecificRemediation()
    {
        StartupFailurePresentation presentation = StartupFailurePresentation.From(
            new WebView2RuntimeNotFoundException("sensitive-detail"),
            StartupPhase.WebView);

        Assert.Equal("WebView2 runtime is missing", presentation.Title);
        Assert.Contains("Evergreen Runtime", presentation.Message);
        Assert.DoesNotContain("sensitive-detail", presentation.Message);
    }

    [Fact]
    public void OtherWebViewFailure_ExplainsProfileRemediationWithoutLeakingDetails()
    {
        StartupFailurePresentation presentation = StartupFailurePresentation.From(
            new InvalidOperationException("sensitive-detail"),
            StartupPhase.WebView);

        Assert.Equal("Browser profile could not be initialized", presentation.Title);
        Assert.Contains("data directory", presentation.Message);
        Assert.DoesNotContain("sensitive-detail", presentation.Message);
    }

    public static TheoryData<Exception, string> SanitizedHostFailures => new()
    {
        { new UnauthorizedAccessException("sensitive-detail"), "Application data could not be initialized" },
        { new IOException("sensitive-detail"), "Application data could not be initialized" },
        { new DbUpdateException("sensitive-detail"), "Database could not be initialized" },
        { new InvalidOperationException("sensitive-detail"), "Application could not be started" },
    };
}
