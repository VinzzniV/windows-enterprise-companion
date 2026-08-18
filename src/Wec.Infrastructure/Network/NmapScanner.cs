using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Wec.Core.Network;
using Wec.Core.Results;

namespace Wec.Infrastructure.Network;

/// <summary>
/// Runs nmap as a child process and parses its XML output. A connect scan
/// (<c>-sT</c>) is used so the app needs no elevation (ADR 0002); host discovery
/// (<c>-sn</c>) is used when no ports are requested. The target is validated and
/// passed as separate process arguments — never through a shell — so it cannot
/// inject nmap options or commands. nmap resolves reverse DNS itself and, for
/// on-link hosts, reports the MAC vendor; both come back in the XML for free.
/// </summary>
public sealed partial class NmapScanner : INetworkScanner
{
    private static readonly string[] DefaultInstallPaths =
    [
        @"C:\Program Files (x86)\Nmap\nmap.exe",
        @"C:\Program Files\Nmap\nmap.exe",
    ];

    private readonly ILogger<NmapScanner> _logger;

    public NmapScanner(ILogger<NmapScanner> logger)
    {
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ScannedHost>>> ScanAsync(
        string? nmapPath, string target, IReadOnlyList<int> ports, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>> targets = ValidateTarget(target);
        if (targets.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScannedHost>>(targets.Error!);
        }

        Result<string> executable = ResolveExecutable(nmapPath);
        if (executable.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ScannedHost>>(executable.Error!);
        }

        var startInfo = new ProcessStartInfo(executable.Value)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in BuildArguments(ports, targets.Value))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            string output = await standardOutput;
            string error = await standardError;
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("nmap scan of {Target} exited {Code}: {Error}", target, process.ExitCode, error);
                return Result.Failure<IReadOnlyList<ScannedHost>>(new Error(
                    ErrorCode.RemoteCommandFailed, $"The network scan of '{target}' failed.")
                {
                    Details = Truncate(error),
                });
            }

            return Result.Success(ParseNmapXml(output));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Result.Failure<IReadOnlyList<ScannedHost>>(new Error(
                ErrorCode.ConnectionTimeout, $"The network scan of '{target}' timed out or was cancelled."));
        }
        catch (Win32Exception exception)
        {
            return Result.Failure<IReadOnlyList<ScannedHost>>(new Error(
                ErrorCode.ServiceUnavailable,
                $"nmap could not be started ({executable.Value}). Install it or set Wec:NetworkScan:NmapPath.")
            {
                Details = exception.Message,
            });
        }
        catch (XmlException exception)
        {
            return Result.Failure<IReadOnlyList<ScannedHost>>(new Error(
                ErrorCode.RemoteCommandFailed, "The network scan returned output that could not be parsed.")
            {
                Details = exception.Message,
            });
        }
        catch (IOException exception)
        {
            TryKill(process);
            return Result.Failure<IReadOnlyList<ScannedHost>>(new Error(
                ErrorCode.RemoteCommandFailed, "The network scan process could not be driven.")
            {
                Details = exception.Message,
            });
        }
    }

    private static Result<string> ResolveExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return File.Exists(configuredPath)
                ? Result.Success(configuredPath)
                : Result.Failure<string>(new Error(
                    ErrorCode.ServiceUnavailable, $"nmap.exe was not found at the configured path '{configuredPath}'."));
        }

        foreach (string candidate in DefaultInstallPaths)
        {
            if (File.Exists(candidate))
            {
                return Result.Success(candidate);
            }
        }

        // Fall back to PATH resolution by the OS when nmap is not at a default location.
        return Result.Success("nmap.exe");
    }

    // -oX - streams XML to stdout; -T4 is safe on a LAN. An empty port list means
    // discovery only (-sn); otherwise a no-elevation connect scan of the given ports.
    private static IEnumerable<string> BuildArguments(IReadOnlyList<int> ports, IReadOnlyList<string> targets)
    {
        yield return "-oX";
        yield return "-";
        yield return "-T4";
        if (ports.Count == 0)
        {
            yield return "-sn";
        }
        else
        {
            yield return "-sT";
            yield return "-p";
            yield return string.Join(",", ports);
        }

        foreach (string target in targets)
        {
            yield return target;
        }
    }

    // A target token becomes its own nmap argument, so it must look like an IP,
    // CIDR, octet range, host name or list — never a flag. Rejecting a leading '-'
    // and stray characters stops the token from being read as an nmap option.
    private static Result<IReadOnlyList<string>> ValidateTarget(string target)
    {
        string[] tokens = target.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error(
                ErrorCode.InvalidRequest, "A scan target (e.g. 172.20.20.0/24) is required."));
        }

        if (tokens.Length > 16)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error(
                ErrorCode.InvalidRequest, "Too many scan targets at once (max 16)."));
        }

        foreach (string token in tokens)
        {
            if (!TargetPattern().IsMatch(token))
            {
                return Result.Failure<IReadOnlyList<string>>(new Error(
                    ErrorCode.InvalidRequest, $"'{token}' is not a valid scan target."));
            }

            int slash = token.IndexOf('/');
            if (slash >= 0
                && int.TryParse(token[(slash + 1)..], out int prefix)
                && prefix < 16)
            {
                return Result.Failure<IReadOnlyList<string>>(new Error(
                    ErrorCode.InvalidRequest, $"The subnet '{token}' is too large to scan (minimum /16)."));
            }
        }

        return Result.Success<IReadOnlyList<string>>(tokens);
    }

    /// <summary>
    /// Parses nmap's <c>-oX</c> output into hosts. DTD processing is disabled so
    /// the <c>&lt;!DOCTYPE nmaprun&gt;</c> declaration is ignored and no external
    /// entity is ever fetched (XXE-safe).
    /// </summary>
    internal static IReadOnlyList<ScannedHost> ParseNmapXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
        };
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        XDocument document = XDocument.Load(reader);

        var hosts = new List<ScannedHost>();
        foreach (XElement host in document.Descendants("host"))
        {
            string? ip = host.Elements("address")
                .FirstOrDefault(address => (string?)address.Attribute("addrtype") == "ipv4")
                ?.Attribute("addr")?.Value;
            if (ip is null)
            {
                continue;
            }

            bool isUp = string.Equals(
                host.Element("status")?.Attribute("state")?.Value, "up", StringComparison.OrdinalIgnoreCase);

            XElement? mac = host.Elements("address")
                .FirstOrDefault(address => (string?)address.Attribute("addrtype") == "mac");
            string? hostname = host.Element("hostnames")?.Elements("hostname")
                .FirstOrDefault()?.Attribute("name")?.Value;

            var openPorts = new List<ScannedPort>();
            foreach (XElement port in host.Element("ports")?.Elements("port") ?? [])
            {
                if (port.Element("state")?.Attribute("state")?.Value == "open"
                    && int.TryParse(port.Attribute("portid")?.Value, out int portId))
                {
                    openPorts.Add(new ScannedPort(portId, port.Element("service")?.Attribute("name")?.Value));
                }
            }

            hosts.Add(new ScannedHost(
                ip, isUp, hostname, mac?.Attribute("addr")?.Value, mac?.Attribute("vendor")?.Value, openPorts));
        }

        return hosts;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone — nothing to kill
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value.Trim() : value[..500].Trim();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.,-]*(/\d{1,2})?$")]
    private static partial Regex TargetPattern();
}
