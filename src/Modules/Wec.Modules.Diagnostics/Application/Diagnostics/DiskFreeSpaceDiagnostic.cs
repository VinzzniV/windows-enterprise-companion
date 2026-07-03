using System.Globalization;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class DiskFreeSpaceDiagnostic : IDiagnostic
{
    private readonly IDriveInfoProvider _driveInfoProvider;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public DiskFreeSpaceDiagnostic(
        IDriveInfoProvider driveInfoProvider,
        IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _driveInfoProvider = driveInfoProvider;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-DISKSPACE";

    public Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DriveSpaceInfo>> drives = _driveInfoProvider.GetFixedDrives();
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (drives.IsFailure)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                DiagnosticStatus.NotRun,
                "Disk free space could not be read",
                new Dictionary<string, string>
                {
                    ["errorCode"] = drives.Error!.Code.ToString(),
                    ["errorMessage"] = drives.Error.Message,
                },
                ["Rerun the diagnostics; if it keeps failing check the storage stack."],
                capturedAtUtc)]);
        }

        var evidence = new Dictionary<string, string>();
        var lowDrives = new List<string>();
        foreach (DriveSpaceInfo drive in drives.Value)
        {
            double freePercent = drive.TotalBytes > 0
                ? drive.AvailableFreeBytes * 100.0 / drive.TotalBytes
                : 0;
            evidence[drive.Name] = string.Create(
                CultureInfo.InvariantCulture,
                $"{drive.AvailableFreeBytes / 1_073_741_824.0:F1} GB free of {drive.TotalBytes / 1_073_741_824.0:F1} GB ({freePercent:F0} %)");
            if (freePercent < _options.MinimumFreeDiskSpacePercent)
            {
                lowDrives.Add(drive.Name);
            }
        }

        if (lowDrives.Count > 0)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                DiagnosticStatus.Warning,
                $"Low disk space on {string.Join(", ", lowDrives)}",
                evidence,
                [
                    "Free up space (Storage Sense, temporary files, old user profiles).",
                    "Windows Update and applications fail unpredictably on full system drives.",
                ],
                capturedAtUtc)]);
        }

        return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
            DiagnosticStatus.Pass,
            "All fixed drives have sufficient free space",
            evidence,
            [],
            capturedAtUtc)]);
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
