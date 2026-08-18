using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wec.Core.Dhcp;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Infrastructure.Dhcp;

/// <summary>
/// Reads DHCP reservations by running the <c>DhcpServer</c> cmdlets on the DHCP
/// server over PowerShell remoting (WinRM). With explicit credentials it runs as
/// that account (the admin the app already holds), which is what a DHCP server
/// on a domain controller requires; otherwise it runs as the signed-in user.
/// The password is handed to the child process over stdin, never on the command
/// line. When remoting is blocked or rights are missing, the query fails cleanly
/// instead of throwing. No writes, ever.
/// </summary>
public sealed partial class PowerShellDhcpReader : IDhcpReader
{
    private readonly ILogger<PowerShellDhcpReader> _logger;

    public PowerShellDhcpReader(ILogger<PowerShellDhcpReader> logger)
    {
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<DhcpReservation>>> GetReservationsAsync(
        string dhcpServer, ScanCredentials credentials, CancellationToken cancellationToken)
    {
        bool explicitCredentials = credentials.Mode == CredentialMode.Explicit;
        string server = dhcpServer.Trim();
        // The server name is interpolated into a PowerShell command, so it must
        // not carry anything but a host name or IP (no quotes, no separators).
        if (server.Length == 0 || !HostPattern().IsMatch(server))
        {
            return Result.Failure<IReadOnlyList<DhcpReservation>>(new Error(
                ErrorCode.InvalidRequest, $"'{dhcpServer}' is not a valid DHCP server name."));
        }

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardInput = explicitCredentials,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(BuildCommand(server, explicitCredentials));

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            if (explicitCredentials)
            {
                // Username + password over stdin, so they never reach the command line.
                string userName = credentials.Domain is { Length: > 0 } domain
                    ? $"{domain}\\{credentials.UserName}"
                    : credentials.UserName!;
                await process.StandardInput.WriteLineAsync(userName);
                await process.StandardInput.WriteLineAsync(credentials.Password);
                process.StandardInput.Close();
            }

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            string output = await standardOutput;
            string error = await standardError;
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("DHCP query against {Server} exited {Code}: {Error}", server, process.ExitCode, error);
                return Result.Failure<IReadOnlyList<DhcpReservation>>(new Error(
                    ErrorCode.RemoteCommandFailed,
                    $"Reading DHCP reservations from '{server}' failed.")
                {
                    Details = Truncate(error),
                });
            }

            return Result.Success(ParseReservations(output));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Result.Failure<IReadOnlyList<DhcpReservation>>(new Error(
                ErrorCode.ConnectionTimeout, $"The DHCP query against '{server}' timed out or was cancelled."));
        }
        catch (Win32Exception exception)
        {
            return Result.Failure<IReadOnlyList<DhcpReservation>>(new Error(
                ErrorCode.ServiceUnavailable, "powershell.exe could not be started for the DHCP query.")
            {
                Details = exception.Message,
            });
        }
        catch (JsonException exception)
        {
            return Result.Failure<IReadOnlyList<DhcpReservation>>(new Error(
                ErrorCode.RemoteCommandFailed, "The DHCP query returned output that could not be parsed.")
            {
                Details = exception.Message,
            });
        }
        catch (IOException exception)
        {
            TryKill(process);
            return Result.Failure<IReadOnlyList<DhcpReservation>>(new Error(
                ErrorCode.RemoteCommandFailed, "The DHCP query process could not be driven.")
            {
                Details = exception.Message,
            });
        }
    }

    // The DhcpServer module ships on the DHCP server, rarely on an admin
    // workstation, so run the cmdlets on the server over PowerShell remoting
    // (WinRM) rather than relying on local RSAT. Invoke-Command tags each object
    // with PSComputerName etc.; the trailing Select-Object strips those again.
    private static string BuildCommand(string server, bool withCredential)
    {
        // Username + password arrive on stdin, so nothing secret is on the command line.
        // The credential is built via .NET constructors rather than
        // ConvertTo-SecureString, because the Microsoft.PowerShell.Security module
        // fails to load in some locked-down domain contexts; [Type]::new() needs no module.
        string credentialSetup = withCredential
            ? "$u=[Console]::In.ReadLine(); $p=[Console]::In.ReadLine(); "
              + "$ss=[System.Security.SecureString]::new(); foreach($c in $p.ToCharArray()){ $ss.AppendChar($c) }; "
              + "$cred=[System.Management.Automation.PSCredential]::new($u,$ss); "
            : string.Empty;
        string credentialArgument = withCredential ? "-Credential $cred " : string.Empty;

        return "$ErrorActionPreference='Stop'; try { "
            + credentialSetup
            // A WinHTTP proxy intercepting the WS-Man request is a common cause of a
            // WinRM HTTP 400; bypass it explicitly for this domain-internal call.
            + "$so = New-PSSessionOption -ProxyAccessType NoProxyServer -OpenTimeout 15000; "
            + $"Invoke-Command -ComputerName '{server}' {credentialArgument}-SessionOption $so -ScriptBlock {{ "
            + "Get-DhcpServerv4Scope | Get-DhcpServerv4Reservation | "
            + "Select-Object @{n='ip';e={$_.IPAddress.IPAddressToString}},@{n='mac';e={[string]$_.ClientId}},@{n='name';e={[string]$_.Name}} "
            + "} | Select-Object ip,mac,name | ConvertTo-Json -Compress -Depth 4 "
            + "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
    }

    /// <summary>
    /// Parses the reservation JSON. Windows PowerShell's <c>ConvertTo-Json</c>
    /// emits a bare object for a single reservation and nothing for none, so
    /// object, array and empty are all accepted.
    /// </summary>
    internal static IReadOnlyList<DhcpReservation> ParseReservations(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var elements = new List<JsonElement>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            elements.AddRange(root.EnumerateArray());
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            elements.Add(root);
        }

        var reservations = new List<DhcpReservation>();
        foreach (JsonElement element in elements)
        {
            if (ReadString(element, "ip") is { Length: > 0 } ip)
            {
                reservations.Add(new DhcpReservation(ip, ReadString(element, "mac"), ReadString(element, "name")));
            }
        }

        return reservations;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex HostPattern();
}
