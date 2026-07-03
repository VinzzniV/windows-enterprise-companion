using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Accounts;

/// <summary>
/// Reads the local SAM password/lockout modals via NetUserModalsGet — the
/// same data `net accounts` shows. Read-only by API shape.
/// </summary>
public sealed class SamAccountPolicyReader : ILocalAccountPolicyReader
{
    private const uint TimeqForever = 0xFFFFFFFF;

    private readonly ILogger<SamAccountPolicyReader> _logger;

    public SamAccountPolicyReader(ILogger<SamAccountPolicyReader> logger)
    {
        _logger = logger;
    }

    public Result<LocalAccountPolicy> ReadAccountPolicy()
    {
        int passwordStatus = NetUserModalsGet(null, 0, out IntPtr passwordBuffer);
        if (passwordStatus != 0)
        {
            return ApiFailure(passwordStatus);
        }

        try
        {
            int lockoutStatus = NetUserModalsGet(null, 3, out IntPtr lockoutBuffer);
            if (lockoutStatus != 0)
            {
                return ApiFailure(lockoutStatus);
            }

            try
            {
                var passwordModals = Marshal.PtrToStructure<UserModalsInfo0>(passwordBuffer);
                var lockoutModals = Marshal.PtrToStructure<UserModalsInfo3>(lockoutBuffer);

                return Result.Success(new LocalAccountPolicy(
                    MinPasswordLength: (int)passwordModals.MinPasswordLength,
                    MaxPasswordAge: passwordModals.MaxPasswordAgeSeconds == TimeqForever
                        ? null
                        : TimeSpan.FromSeconds(passwordModals.MaxPasswordAgeSeconds),
                    PasswordHistoryLength: (int)passwordModals.PasswordHistoryLength,
                    LockoutThreshold: (int)lockoutModals.LockoutThreshold,
                    LockoutDuration: TimeSpan.FromSeconds(lockoutModals.LockoutDurationSeconds)));
            }
            finally
            {
                _ = NetApiBufferFree(lockoutBuffer);
            }
        }
        finally
        {
            _ = NetApiBufferFree(passwordBuffer);
        }
    }

    private Result<LocalAccountPolicy> ApiFailure(int status)
    {
        _logger.LogWarning("NetUserModalsGet failed with status {Status}", status);
        return Result.Failure<LocalAccountPolicy>(new Error(
            ErrorCode.WmiUnavailable,
            "The local account policy could not be read.")
        {
            Details = $"NetUserModalsGet returned {status}.",
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserModalsInfo0
    {
        public uint MinPasswordLength;
        public uint MaxPasswordAgeSeconds;
        public uint MinPasswordAgeSeconds;
        public uint ForceLogoffSeconds;
        public uint PasswordHistoryLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserModalsInfo3
    {
        public uint LockoutDurationSeconds;
        public uint LockoutObservationWindowSeconds;
        public uint LockoutThreshold;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserModalsGet(string? serverName, int level, out IntPtr buffer);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
