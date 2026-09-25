using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LocalAgent.Infrastructure.FileSystem;

internal static class WindowsFileSystemNative
{
    internal const uint InvalidFileAttributes =
        0xFFFFFFFF;

    internal const uint FileAttributeDirectory =
        0x00000010;

    internal const uint FileAttributeDevice =
        0x00000040;

    internal const uint FileAttributeReparsePoint =
        0x00000400;

    private const uint FileReadAttributes =
        0x00000080;

    private const uint FileShareRead =
        0x00000001;

    private const uint FileShareWrite =
        0x00000002;

    private const uint FileShareDelete =
        0x00000004;

    private const uint OpenExisting =
        3;

    private const uint FileFlagBackupSemantics =
        0x02000000;

    private const uint FileFlagOpenReparsePoint =
        0x00200000;

    internal const int ErrorFileNotFound =
        2;

    internal const int ErrorPathNotFound =
        3;

    internal static uint GetAttributes(
        string path)
    {
        return GetFileAttributesW(
            path);
    }

    internal static SafeFileHandle OpenMetadataHandle(
        string path)
    {
        return CreateFileW(
            path,
            FileReadAttributes,
            FileShareRead |
            FileShareWrite |
            FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics |
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
    }

    internal static bool TryGetFileInformation(
        SafeFileHandle handle,
        out ByHandleFileInformation information)
    {
        return GetFileInformationByHandle(
            handle,
            out information);
    }

    internal static bool TryGetVolumePath(
        string path,
        out string volumePath)
    {
        var buffer =
            new char[512];

        if (!GetVolumePathNameW(
                path,
                buffer,
                buffer.Length))
        {
            volumePath =
                string.Empty;

            return false;
        }

        volumePath =
            ReadNullTerminated(
                buffer);

        return volumePath.Length > 0;
    }

    internal static bool TryGetVolumeGuid(
        string volumeMountPoint,
        out string volumeGuid)
    {
        var buffer =
            new char[64];

        if (!GetVolumeNameForVolumeMountPointW(
                volumeMountPoint,
                buffer,
                buffer.Length))
        {
            volumeGuid =
                string.Empty;

            return false;
        }

        volumeGuid =
            ReadNullTerminated(
                buffer);

        return volumeGuid.Length > 0;
    }

    private static string ReadNullTerminated(
        char[] buffer)
    {
        var length =
            Array.IndexOf(
                buffer,
                '\0');

        if (length < 0)
        {
            length =
                buffer.Length;
        }

        return new string(
            buffer,
            0,
            length);
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "GetFileAttributesW")]
    private static extern uint GetFileAttributesW(
        string lpFileName);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "GetVolumePathNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(
        string lpszFileName,
        [Out] char[] lpszVolumePathName,
        int cchBufferLength);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string lpszVolumeMountPoint,
        [Out] char[] lpszVolumeName,
        int cchBufferLength);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }
}
