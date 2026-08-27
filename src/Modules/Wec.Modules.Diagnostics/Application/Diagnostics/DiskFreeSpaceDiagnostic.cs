using System.Globalization;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

/// <summary>
/// Free space of fixed drives via Win32_LogicalDisk — one code path that
/// works locally and against remote targets.
/// </summary>
internal sealed class DiskFreeSpaceDiagnostic : IDiagnostic
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public DiskFreeSpaceDiagnostic(
        IWmiQueryService wmiQueryService,
        IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => DeviceHealthDiagnosticIds.DiskFreeSpace;

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        // DriveType 3 = local fixed disk
        Result<IReadOnlyList<WmiInstance>> disks = await _wmiQueryService.QueryAsync(
            context,
            CimV2Namespace,
            "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3",
            cancellationToken);
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (disks.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Disk free space could not be read",
                new Dictionary<string, string>
                {
                    ["errorCode"] = disks.Error!.Code.ToString(),
                    ["errorMessage"] = disks.Error.Message,
                },
                ["Rerun the diagnostics; if it keeps failing check WMI and the storage stack."],
                capturedAtUtc)];
        }

        var evidence = new Dictionary<string, string>();
        var lowDrives = new List<string>();
        foreach (WmiInstance disk in disks.Value)
        {
            string name = disk.GetString("DeviceID") ?? "?";
            long totalBytes = disk.GetInteger("Size") ?? 0;
            long freeBytes = disk.GetInteger("FreeSpace") ?? 0;
            double freePercent = totalBytes > 0 ? freeBytes * 100.0 / totalBytes : 0;
            evidence[name] = string.Create(
                CultureInfo.InvariantCulture,
                $"{freeBytes / 1_073_741_824.0:F1} GB free of {totalBytes / 1_073_741_824.0:F1} GB ({freePercent:F0} %)");
            if (freePercent < _options.MinimumFreeDiskSpacePercent)
            {
                lowDrives.Add(name);
            }
        }

        if (lowDrives.Count > 0)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                $"Low disk space on {string.Join(", ", lowDrives)}",
                evidence,
                [
                    "Free up space (Storage Sense, temporary files, old user profiles).",
                    "Windows Update and applications fail unpredictably on full system drives.",
                ],
                capturedAtUtc)];
        }

        return [BuildResult(
            DiagnosticStatus.Pass,
            "All fixed drives have sufficient free space",
            evidence,
            [],
            capturedAtUtc)];
    }

    private DiagnosticResult BuildResult(
        DiagnosticStatus status,
        string title,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps,
        DateTimeOffset capturedAtUtc) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.System,
        "Fixed drives",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
