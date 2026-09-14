using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wec.Host.Options;
using Wec.Infrastructure.Logging;
using Wec.Infrastructure.Persistence;

namespace Wec.Host.Runtime;

internal sealed record RuntimeInstanceProfile(
    string Name,
    string DatabasePath,
    string LogDirectory,
    string WebViewUserDataDirectory,
    bool IsIsolated)
{
    internal const string EnvironmentVariableName = "WEC_INSTANCE_PROFILE";
    internal string ObjectScope => $"{Name}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(DatabasePath)).ToUpperInvariant())))[..24].ToLowerInvariant()}";
    internal const string DefaultDatabasePath = "%LOCALAPPDATA%\\Wec\\wec.db";
    internal const string DefaultLogDirectory = "%LOCALAPPDATA%\\Wec\\logs";
    internal const string DefaultWebViewDirectory = "%LOCALAPPDATA%\\Wec\\webview2";

    private static readonly Regex ValidProfileName = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static RuntimeInstanceProfile Resolve(
        IConfiguration configuration,
        string environmentName,
        string contentRootPath,
        string localApplicationData,
        string? requestedProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);

        string? profileName = string.IsNullOrWhiteSpace(requestedProfile)
            ? null
            : requestedProfile.Trim();
        bool useDevServer = configuration.GetValue<bool>($"{FrontendOptions.SectionName}:UseDevServer");
        bool isDevelopment = string.Equals(
            environmentName,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);

        string name;
        string root;
        bool isolated;
        if (profileName is not null)
        {
            if (!ValidProfileName.IsMatch(profileName))
            {
                throw new RuntimeProfileConfigurationException(
                    $"{EnvironmentVariableName} must contain 1-64 letters, numbers, dots, underscores, or hyphens and start with a letter or number.");
            }

            name = profileName;
            root = Path.Combine(localApplicationData, "Wec", "profiles", profileName);
            isolated = true;
        }
        else if (useDevServer || isDevelopment)
        {
            string normalizedRoot = Path.GetFullPath(contentRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..12]
                .ToLowerInvariant();
            name = $"development-{hash}";
            root = Path.Combine(localApplicationData, "Wec", "development", hash);
            isolated = true;
        }
        else
        {
            name = "installed";
            root = Path.Combine(localApplicationData, "Wec");
            isolated = false;
        }

        string configuredDatabase = configuration[$"{DatabaseOptions.SectionName}:DatabasePath"] ?? string.Empty;
        string configuredLogs = configuration[$"{LoggingOptions.SectionName}:LogDirectory"] ?? string.Empty;
        string configuredWebView = configuration[$"{WebViewOptions.SectionName}:UserDataDirectory"] ?? string.Empty;

        return new RuntimeInstanceProfile(
            name,
            ResolvePath(configuredDatabase, DefaultDatabasePath, Path.Combine(root, "wec.db"), isolated),
            ResolvePath(configuredLogs, DefaultLogDirectory, Path.Combine(root, "logs"), isolated),
            ResolvePath(configuredWebView, DefaultWebViewDirectory, Path.Combine(root, "webview2"), isolated),
            isolated);
    }

    internal IReadOnlyDictionary<string, string?> ConfigurationOverrides => new Dictionary<string, string?>
    {
        [$"{DatabaseOptions.SectionName}:DatabasePath"] = DatabasePath,
        [$"{LoggingOptions.SectionName}:LogDirectory"] = LogDirectory,
        [$"{WebViewOptions.SectionName}:UserDataDirectory"] = WebViewUserDataDirectory,
    };

    private static string ResolvePath(
        string configuredPath,
        string defaultPath,
        string isolatedPath,
        bool isolated)
    {
        if (!isolated || !IsProductionDefault(configuredPath, defaultPath))
        {
            return configuredPath;
        }

        return isolatedPath;
    }

    private static bool IsProductionDefault(string configuredPath, string defaultPath) =>
        string.Equals(configuredPath, defaultPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            Environment.ExpandEnvironmentVariables(configuredPath),
            Environment.ExpandEnvironmentVariables(defaultPath),
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class RuntimeProfileConfigurationException : Exception
{
    internal RuntimeProfileConfigurationException(string message)
        : base(message)
    {
    }
}
