using System.Globalization;
using System.Runtime.InteropServices;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.FileSystem;

public sealed class WindowsFileSystemDeviceInspector :
    IFileSystemDeviceInspector
{
    public string GetDeviceId(
        string existingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            existingPath);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The Windows filesystem device inspector requires Windows.");
        }

        var attributes =
            WindowsFileSystemNative.GetAttributes(
                existingPath);

        if (attributes ==
            WindowsFileSystemNative.InvalidFileAttributes)
        {
            var error =
                Marshal.GetLastWin32Error();

            throw new IOException(
                $"Unable to inspect Windows filesystem device; GetFileAttributesW failed with Windows error {error}.");
        }

        if ((attributes &
             WindowsFileSystemNative.FileAttributeReparsePoint) != 0)
        {
            throw new IOException(
                "Filesystem device inspection does not follow Windows reparse points.");
        }

        using var handle =
            WindowsFileSystemNative.OpenMetadataHandle(
                existingPath);

        if (handle.IsInvalid)
        {
            throw new IOException(
                $"Unable to inspect Windows filesystem device; CreateFileW failed with Windows error {Marshal.GetLastWin32Error()}.");
        }

        if (!WindowsFileSystemNative.TryGetFileInformation(
                handle,
                out var information))
        {
            throw new IOException(
                $"Unable to inspect Windows filesystem device; GetFileInformationByHandle failed with Windows error {Marshal.GetLastWin32Error()}.");
        }

        if (WindowsFileSystemNative.TryGetVolumePath(
                existingPath,
                out var volumePath))
        {
            if (WindowsFileSystemNative.TryGetVolumeGuid(
                    EnsureTrailingSeparator(
                        volumePath),
                    out var volumeGuid))
            {
                return
                    "WINGUID-" +
                    volumeGuid.ToUpperInvariant();
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"WINVOL-{information.VolumeSerialNumber:X8}-{volumePath.ToUpperInvariant()}");
        }

        var root =
            Path.GetPathRoot(
                Path.GetFullPath(
                    existingPath))
            ?? string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"WINVOL-{information.VolumeSerialNumber:X8}-{root.ToUpperInvariant()}");
    }

    private static string EnsureTrailingSeparator(
        string path)
    {
        return path.EndsWith(
                Path.DirectorySeparatorChar)
            ? path
            : path +
              Path.DirectorySeparatorChar;
    }
}
