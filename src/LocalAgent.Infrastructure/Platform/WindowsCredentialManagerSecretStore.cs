using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Platform;

namespace LocalAgent.Infrastructure.Platform;

public sealed class WindowsCredentialManagerSecretStore :
    IPlatformSecretStore
{
    private const uint CredTypeGeneric =
        1;

    private const uint CredPersistLocalMachine =
        2;

    private const int ErrorNotFound =
        1168;

    public bool Contains(
        string serviceName) =>
        Read(
            serviceName) is not null;

    public string? Read(
        string serviceName)
    {
        ValidateServiceName(
            serviceName);

        EnsureWindows();

        if (!CredReadW(
                serviceName,
                CredTypeGeneric,
                0,
                out var credentialPointer))
        {
            var error =
                Marshal.GetLastWin32Error();

            if (error ==
                ErrorNotFound)
            {
                return null;
            }

            throw new IOException(
                $"Windows Credential Manager read failed with error {error}.");
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<Credential>(
                    credentialPointer);

            if (credential.CredentialBlob ==
                    IntPtr.Zero ||
                credential.CredentialBlobSize ==
                    0)
            {
                return string.Empty;
            }

            var byteCount =
                checked(
                    (int)
                    credential.CredentialBlobSize);

            var bytes =
                new byte[
                    byteCount];

            try
            {
                Marshal.Copy(
                    credential.CredentialBlob,
                    bytes,
                    0,
                    byteCount);

                return Encoding.Unicode.GetString(
                    bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    bytes);
            }
        }
        finally
        {
            CredFree(
                credentialPointer);
        }
    }

    public void Store(
        string serviceName,
        string secret)
    {
        ValidateServiceName(
            serviceName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            secret);

        EnsureWindows();

        var bytes =
            Encoding.Unicode.GetBytes(
                secret);

        if (bytes.Length >
            5 * 512)
        {
            CryptographicOperations.ZeroMemory(
                bytes);

            throw new ArgumentException(
                "Secret exceeds Windows Credential Manager generic-credential size limits.",
                nameof(secret));
        }

        var blob =
            Marshal.AllocHGlobal(
                bytes.Length);

        try
        {
            Marshal.Copy(
                bytes,
                0,
                blob,
                bytes.Length);

            var credential =
                new Credential
                {
                    Type =
                        CredTypeGeneric,
                    TargetName =
                        serviceName,
                    CredentialBlobSize =
                        checked(
                            (uint)
                            bytes.Length),
                    CredentialBlob =
                        blob,
                    Persist =
                        CredPersistLocalMachine,
                    UserName =
                        Environment.UserName
                };

            if (!CredWriteW(
                    ref credential,
                    0))
            {
                throw new IOException(
                    $"Windows Credential Manager write failed with error {Marshal.GetLastWin32Error()}.");
            }
        }
        finally
        {
            if (blob !=
                IntPtr.Zero)
            {
                var zeros =
                    new byte[
                        bytes.Length];

                Marshal.Copy(
                    zeros,
                    0,
                    blob,
                    zeros.Length);

                Marshal.FreeHGlobal(
                    blob);
            }

            CryptographicOperations.ZeroMemory(
                bytes);
        }
    }

    public bool Delete(
        string serviceName)
    {
        ValidateServiceName(
            serviceName);

        EnsureWindows();

        if (CredDeleteW(
                serviceName,
                CredTypeGeneric,
                0))
        {
            return true;
        }

        var error =
            Marshal.GetLastWin32Error();

        if (error ==
            ErrorNotFound)
        {
            return false;
        }

        throw new IOException(
            $"Windows Credential Manager delete failed with error {error}.");
    }

    private static void ValidateServiceName(
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serviceName);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The Windows Credential Manager secret store requires Windows.");
        }
    }

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "CredReadW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "CredWriteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(
        ref Credential credential,
        uint flags);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "CredDeleteW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(
        string target,
        uint type,
        uint flags);

    [DllImport(
        "advapi32.dll")]
    private static extern void CredFree(
        IntPtr buffer);

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}
