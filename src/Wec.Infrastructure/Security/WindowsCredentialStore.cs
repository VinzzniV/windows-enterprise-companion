using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Infrastructure.Security;

/// <summary>Stores generic credentials in the current Windows user's Credential Manager vault.</summary>
public sealed class WindowsCredentialStore : IServiceCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxCredentialBlobBytes = 2560;
    private const string TargetPrefix = "WindowsEnterpriseCompanion/";

    public Result<StoredServiceCredential?> Read(ServiceCredentialKind kind)
    {
        if (!CredRead(Target(kind), CredentialTypeGeneric, 0, out nint credentialPointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? Result.Success<StoredServiceCredential?>(null)
                : Failure<StoredServiceCredential?>("The saved service credential could not be read.", error);
        }

        try
        {
            NativeCredential native = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            string identity = native.UserName ?? string.Empty;
            string blob = native.CredentialBlob == nint.Zero || native.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(native.CredentialBlob, checked((int)native.CredentialBlobSize / 2)) ?? string.Empty;
            if (kind == ServiceCredentialKind.Nessus)
            {
                NessusCredentialPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<NessusCredentialPayload>(blob);
                }
                catch (JsonException)
                {
                    payload = null;
                }
                return payload is null
                    ? Result.Failure<StoredServiceCredential?>(new Error(ErrorCode.InvalidRequest, "The saved Nessus API keys are invalid."))
                    : Result.Success<StoredServiceCredential?>(new StoredServiceCredential(payload.AccessKey, null, payload.SecretKey));
            }
            (string userName, string? domain) = SplitIdentity(identity);
            return Result.Success<StoredServiceCredential?>(new StoredServiceCredential(userName, domain, blob));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Result<bool> Save(ServiceCredentialKind kind, StoredServiceCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.UserName) || string.IsNullOrEmpty(credential.Password))
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                "A user name and password are required before the service credential can be saved."));
        }

        string protectedPayload = kind == ServiceCredentialKind.Nessus
            ? JsonSerializer.Serialize(new NessusCredentialPayload(credential.UserName, credential.Password))
            : credential.Password;
        byte[] passwordBytes = Encoding.Unicode.GetBytes(protectedPayload);
        if (passwordBytes.Length > MaxCredentialBlobBytes)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                "The password is too long for Windows Credential Manager."));
        }

        nint passwordPointer = Marshal.AllocCoTaskMem(passwordBytes.Length);
        try
        {
            Marshal.Copy(passwordBytes, 0, passwordPointer, passwordBytes.Length);
            var native = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target(kind),
                CredentialBlobSize = checked((uint)passwordBytes.Length),
                CredentialBlob = passwordPointer,
                Persist = CredentialPersistLocalMachine,
                // Nessus keeps both API keys inside the protected credential blob.
                UserName = kind == ServiceCredentialKind.Nessus ? "Nessus API keys" : JoinIdentity(credential),
            };
            return CredWrite(ref native, 0)
                ? Result.Success(true)
                : Failure<bool>("The service credential could not be saved in Windows Credential Manager.", Marshal.GetLastWin32Error());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            Marshal.Copy(passwordBytes, 0, passwordPointer, passwordBytes.Length);
            Marshal.FreeCoTaskMem(passwordPointer);
        }
    }

    public Result<bool> Delete(ServiceCredentialKind kind)
    {
        if (CredDelete(Target(kind), CredentialTypeGeneric, 0))
        {
            return Result.Success(true);
        }

        int error = Marshal.GetLastWin32Error();
        return error == ErrorNotFound
            ? Result.Success(false)
            : Failure<bool>("The saved service credential could not be removed.", error);
    }

    internal static string JoinIdentity(StoredServiceCredential credential) =>
        string.IsNullOrWhiteSpace(credential.Domain)
            ? credential.UserName.Trim()
            : $"{credential.Domain.Trim()}\\{credential.UserName.Trim()}";

    internal static (string UserName, string? Domain) SplitIdentity(string identity)
    {
        int separator = identity.IndexOf('\\');
        return separator > 0 && separator < identity.Length - 1
            ? (identity[(separator + 1)..], identity[..separator])
            : (identity, null);
    }

    private static string Target(ServiceCredentialKind kind) => $"{TargetPrefix}{kind}";

    private sealed record NessusCredentialPayload(string AccessKey, string SecretKey);

    private static Result<T> Failure<T>(string message, int nativeError) =>
        Result.Failure<T>(new Error(ErrorCode.FileWriteFailed, message)
        {
            Details = new Win32Exception(nativeError).Message,
        });

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        public nint Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}
