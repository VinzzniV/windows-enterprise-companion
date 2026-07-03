using Wec.Core.Results;

namespace Wec.Core.Abstractions;

public sealed record DriveSpaceInfo(
    string Name,
    long TotalBytes,
    long AvailableFreeBytes);

/// <summary>Fixed (non-removable) drives of the local machine.</summary>
public interface IDriveInfoProvider
{
    Result<IReadOnlyList<DriveSpaceInfo>> GetFixedDrives();
}
