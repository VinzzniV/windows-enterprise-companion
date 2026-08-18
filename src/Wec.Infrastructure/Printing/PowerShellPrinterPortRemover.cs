using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wec.Core.Printing;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Infrastructure.Printing;

/// <summary>
/// Removes printer ports by running <c>Remove-PrinterPort</c> on the print server
/// over PowerShell remoting (WinRM), mirroring <c>PowerShellDhcpReader</c>: with
/// explicit credentials it runs as the admin the server requires, without
/// elevating the app. Credentials and the port names travel over stdin (the names
/// as a JSON line, bound to <c>-Name</c> server-side), so nothing user-supplied
/// ever reaches a command line. The server refuses a port still bound to a queue,
/// which is the real safety net behind the "unused" classification.
/// </summary>
public sealed partial class PowerShellPrinterPortRemover : IPrinterPortRemover
{
    private readonly ILogger<PowerShellPrinterPortRemover> _logger;

    public PowerShellPrinterPortRemover(ILogger<PowerShellPrinterPortRemover> logger)
    {
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<PortRemovalResult>>> RemovePortsAsync(
        string printServer, IReadOnlyList<string> portNames, ScanCredentials credentials,
        CancellationToken cancellationToken)
    {
        bool explicitCredentials = credentials.Mode == CredentialMode.Explicit;
        string server = printServer.Trim();
        if (server.Length == 0 || !HostPattern().IsMatch(server))
        {
            return Result.Failure<IReadOnlyList<PortRemovalResult>>(new Error(
                ErrorCode.InvalidRequest, $"'{printServer}' is not a valid print server name."));
        }

        if (portNames.Count == 0)
        {
            return Result.Success<IReadOnlyList<PortRemovalResult>>([]);
        }

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardInput = true,
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
                string userName = credentials.Domain is { Length: > 0 } domain
                    ? $"{domain}\\{credentials.UserName}"
                    : credentials.UserName!;
                await process.StandardInput.WriteLineAsync(userName);
                await process.StandardInput.WriteLineAsync(credentials.Password);
            }

            // Port names as one JSON line, never on the command line; bound to -Name server-side.
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(portNames));
            process.StandardInput.Close();

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            string output = await standardOutput;
            string error = await standardError;
            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Port removal against {Server} exited {Code}: {Error}", server, process.ExitCode, error);
                return Result.Failure<IReadOnlyList<PortRemovalResult>>(new Error(
                    ErrorCode.RemoteCommandFailed, $"Removing printer ports on '{server}' failed.")
                {
                    Details = Truncate(error),
                });
            }

            return Result.Success(ParseResults(output));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Result.Failure<IReadOnlyList<PortRemovalResult>>(new Error(
                ErrorCode.ConnectionTimeout, $"Removing printer ports on '{server}' timed out or was cancelled."));
        }
        catch (Win32Exception exception)
        {
            return Result.Failure<IReadOnlyList<PortRemovalResult>>(new Error(
                ErrorCode.ServiceUnavailable, "powershell.exe could not be started for the port removal.")
            {
                Details = exception.Message,
            });
        }
        catch (JsonException exception)
        {
            return Result.Failure<IReadOnlyList<PortRemovalResult>>(new Error(
                ErrorCode.RemoteCommandFailed, "The port removal returned output that could not be parsed.")
            {
                Details = exception.Message,
            });
        }
        catch (IOException exception)
        {
            TryKill(process);
            return Result.Failure<IReadOnlyList<PortRemovalResult>>(new Error(
                ErrorCode.RemoteCommandFailed, "The port removal process could not be driven.")
            {
                Details = exception.Message,
            });
        }
    }

    // Run Remove-PrinterPort on the server (the PrintManagement module lives there,
    // not necessarily on the admin workstation). Each port is attempted on its own
    // so one failure — e.g. a port that became used again — does not abort the rest.
    private static string BuildCommand(string server, bool withCredential)
    {
        string credentialSetup = withCredential
            ? "$u=[Console]::In.ReadLine(); $p=[Console]::In.ReadLine(); "
              + "$ss=[System.Security.SecureString]::new(); foreach($c in $p.ToCharArray()){ $ss.AppendChar($c) }; "
              + "$cred=[System.Management.Automation.PSCredential]::new($u,$ss); "
            : string.Empty;
        string credentialArgument = withCredential ? "-Credential $cred " : string.Empty;

        // Windows PowerShell 5.1 does NOT enumerate a JSON array through the pipeline,
        // so @([Console]::In.ReadLine() | ConvertFrom-Json) collapses all port names
        // into ONE element (the whole array). That made the loop run once with $n =
        // the entire array, Remove-PrinterPort delete everything at once, and the
        // result object carry a name array the C# side then dropped (reported 0/0).
        // Assigning first, then wrapping, enumerates correctly on 5.1 and 7+.
        return "$ErrorActionPreference='Stop'; try { "
            + credentialSetup
            + "$parsed = [Console]::In.ReadLine() | ConvertFrom-Json; $names = @($parsed); "
            + "$so = New-PSSessionOption -ProxyAccessType NoProxyServer -OpenTimeout 15000; "
            + $"Invoke-Command -ComputerName '{server}' {credentialArgument}-SessionOption $so -ScriptBlock {{ "
            + "param($portNames) foreach($n in $portNames){ "
            + "try { Remove-PrinterPort -Name $n -ErrorAction Stop; "
            + "[pscustomobject]@{ name=$n; ok=$true; error=$null } } "
            + "catch { [pscustomobject]@{ name=$n; ok=$false; error=$_.Exception.Message } } } "
            + "} -ArgumentList (,$names) | "
            + "Select-Object name,ok,error | ConvertTo-Json -Compress -Depth 4 "
            + "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
    }

    /// <summary>
    /// Parses the per-port JSON. <c>ConvertTo-Json</c> emits a bare object for a
    /// single result and nothing for none, so object, array and empty are accepted.
    /// </summary>
    internal static IReadOnlyList<PortRemovalResult> ParseResults(string json)
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

        var results = new List<PortRemovalResult>();
        foreach (JsonElement element in elements)
        {
            if (ReadString(element, "name") is { Length: > 0 } name)
            {
                bool removed = element.TryGetProperty("ok", out JsonElement ok)
                    && ok.ValueKind is JsonValueKind.True;
                results.Add(new PortRemovalResult(name, removed, removed ? null : ReadString(element, "error")));
            }
        }

        return results;
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
