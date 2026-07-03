using System.IO;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Storage;

public sealed class SystemDriveInfoProvider : IDriveInfoProvider
{
    private readonly ILogger<SystemDriveInfoProvider> _logger;

    public SystemDriveInfoProvider(ILogger<SystemDriveInfoProvider> logger)
    {
        _logger = logger;
    }

    public Result<IReadOnlyList<DriveSpaceInfo>> GetFixedDrives()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => new DriveSpaceInfo(drive.Name, drive.TotalSize, drive.AvailableFreeSpace))
                .ToList();
            return Result.Success<IReadOnlyList<DriveSpaceInfo>>(drives);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Reading drive information failed");
            return Result.Failure<IReadOnlyList<DriveSpaceInfo>>(new Error(
                ErrorCode.InternalError,
                "The drive information could not be read.")
            {
                Details = exception.Message,
            });
        }
    }
}
