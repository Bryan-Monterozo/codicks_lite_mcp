using System.Runtime.InteropServices;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.FileSystem;

public sealed class WindowsFileSystemEntryInspector :
    IFileSystemEntryInspector
{
    public FileSystemEntryInspection Inspect(
        string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            fullPath);

        if (!OperatingSystem.IsWindows())
        {
            return InspectionFailure(
                "The Windows filesystem inspector requires Windows.");
        }

        if (IsReservedDosDevicePath(
                fullPath))
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.Other,
                0,
                "Reserved Windows DOS device names are not supported.");
        }

        if (HasAlternateDataStreamSyntax(
                fullPath))
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.Other,
                0,
                "Windows alternate data streams are not supported.");
        }

        if (HasAmbiguousTrailingComponent(
                fullPath))
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.Other,
                0,
                "Windows path components ending in a dot or space are not supported.");
        }

        var attributes =
            WindowsFileSystemNative.GetAttributes(
                fullPath);

        if (attributes ==
            WindowsFileSystemNative.InvalidFileAttributes)
        {
            var error =
                Marshal.GetLastWin32Error();

            if (error is
                WindowsFileSystemNative.ErrorFileNotFound or
                WindowsFileSystemNative.ErrorPathNotFound)
            {
                return new FileSystemEntryInspection(
                    FileSystemEntryKind.Missing,
                    0,
                    "Path does not exist.");
            }

            return InspectionFailure(
                $"GetFileAttributesW failed with Windows error {error}.");
        }

        if ((attributes &
             WindowsFileSystemNative.FileAttributeReparsePoint) != 0)
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.SymbolicLink,
                1,
                "Windows reparse point.");
        }

        if ((attributes &
             WindowsFileSystemNative.FileAttributeDevice) != 0)
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.Other,
                0,
                "Windows device entry.");
        }

        using var handle =
            WindowsFileSystemNative.OpenMetadataHandle(
                fullPath);

        if (handle.IsInvalid)
        {
            return InspectionFailure(
                $"CreateFileW metadata open failed with Windows error {Marshal.GetLastWin32Error()}.");
        }

        if (!WindowsFileSystemNative.TryGetFileInformation(
                handle,
                out var information))
        {
            return InspectionFailure(
                $"GetFileInformationByHandle failed with Windows error {Marshal.GetLastWin32Error()}.");
        }

        var kind =
            (attributes &
             WindowsFileSystemNative.FileAttributeDirectory) != 0
                ? FileSystemEntryKind.Directory
                : FileSystemEntryKind.RegularFile;

        return new FileSystemEntryInspection(
            kind,
            information.NumberOfLinks,
            kind == FileSystemEntryKind.Directory
                ? "Directory"
                : "Regular File");
    }

    private static FileSystemEntryInspection InspectionFailure(
        string message) =>
        new(
            FileSystemEntryKind.Unknown,
            0,
            message);

    private static bool HasAlternateDataStreamSyntax(
        string fullPath)
    {
        var root =
            Path.GetPathRoot(
                fullPath);

        var remainder =
            string.IsNullOrEmpty(
                root)
                ? fullPath
                : fullPath[root.Length..];

        return remainder.Contains(
            ':');
    }

    private static bool HasAmbiguousTrailingComponent(
        string fullPath)
    {
        var root =
            Path.GetPathRoot(
                fullPath);

        var remainder =
            string.IsNullOrEmpty(
                root)
                ? fullPath
                : fullPath[root.Length..];

        return remainder
            .Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(
                component =>
                    component.EndsWith(
                        '.')
                    ||
                    component.EndsWith(
                        ' '));
    }

    private static bool IsReservedDosDevicePath(
        string fullPath)
    {
        var trimmedPath =
            Path.TrimEndingDirectorySeparator(
                fullPath);

        var name =
            Path.GetFileName(
                trimmedPath)
                .TrimEnd(
                    ' ',
                    '.');

        if (name.Length == 0)
        {
            return false;
        }

        var dotIndex =
            name.IndexOf(
                '.');

        var stem =
            (dotIndex >= 0
                ? name[..dotIndex]
                : name)
            .TrimEnd(
                ' ',
                '.')
            .ToUpperInvariant();

        return stem is
                   "CON" or
                   "PRN" or
                   "AUX" or
                   "NUL" or
                   "CLOCK$" ||
               IsNumberedDevice(
                   stem,
                   "COM") ||
               IsNumberedDevice(
                   stem,
                   "LPT");
    }

    private static bool IsNumberedDevice(
        string stem,
        string prefix)
    {
        return stem.Length ==
                   prefix.Length + 1 &&
               stem.StartsWith(
                   prefix,
                   StringComparison.Ordinal) &&
               stem[^1] is >= '1' and <= '9';
    }
}
