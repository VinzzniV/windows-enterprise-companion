using System.Data.Common;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;

namespace Wec.Host.Runtime;

internal enum StartupPhase
{
    Host,
    WebView,
}

internal sealed record StartupFailurePresentation(string Title, string Message)
{
    internal static StartupFailurePresentation From(Exception exception, StartupPhase phase)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OptionsValidationException or RuntimeProfileConfigurationException)
        {
            return new(
                "Configuration is invalid",
                $"The application configuration is invalid:{Environment.NewLine}{exception.Message}");
        }

        if (exception is WebView2RuntimeNotFoundException)
        {
            return new(
                "WebView2 runtime is missing",
                "Install the Microsoft Edge WebView2 Evergreen Runtime and restart the application.");
        }

        if (phase == StartupPhase.WebView)
        {
            return new(
                "Browser profile could not be initialized",
                "The embedded browser or its data directory could not be initialized. Close other application instances, check access to the configured WebView data directory, and review the application log.");
        }

        if (exception is DbException or DbUpdateException)
        {
            return new(
                "Database could not be initialized",
                "The application database could not be opened or migrated. Check access to the configured database directory, preserve a backup, and review the application log.");
        }

        if (exception is UnauthorizedAccessException or IOException)
        {
            return new(
                "Application data could not be initialized",
                "A configured data or log directory could not be accessed. Check the configured paths and permissions, then review the application log.");
        }

        if (exception is HostAbortedException)
        {
            return new(
                "Application startup was interrupted",
                "The application host could not finish starting. Review the application log and try again.");
        }

        return new(
            "Application could not be started",
            "An unexpected startup error occurred. Review the application log for details.");
    }
}
