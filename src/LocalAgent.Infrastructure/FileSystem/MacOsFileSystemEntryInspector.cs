using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.FileSystem;

public sealed class MacOsFileSystemEntryInspector : IFileSystemEntryInspector
{
    private const string StatPath = "/usr/bin/stat";
    private const string StatFormat = "%HT|%l";

    public FileSystemEntryInspection Inspect(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (!OperatingSystem.IsMacOS())
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.Unknown,
                0,
                "The current filesystem inspector requires macOS.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = StatPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(StatFormat);
            startInfo.ArgumentList.Add(fullPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new FileSystemEntryInspection(
                    FileSystemEntryKind.Unknown,
                    0,
                    "Unable to start /usr/bin/stat.");
            }

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return new FileSystemEntryInspection(
                    FileSystemEntryKind.Missing,
                    0,
                    string.IsNullOrWhiteSpace(standardError)
                        ? "Path does not exist or cannot be inspected."
                        : standardError.Trim());
            }

            return ParseStatOutput(standardOutput);
        }
        catch (Win32Exception exception)
        {
            return InspectionFailure(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return InspectionFailure(exception.Message);
        }
        catch (IOException exception)
        {
            return InspectionFailure(exception.Message);
        }
    }

    private static FileSystemEntryInspection ParseStatOutput(string output)
    {
        var value = output.Trim();
        var separatorIndex = value.LastIndexOf('|');

        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.Unknown,
                0,
                $"Unexpected stat output: '{value}'.");
        }

        var typeText = value[..separatorIndex].Trim();
        var linkText = value[(separatorIndex + 1)..].Trim();

        if (!long.TryParse(linkText, NumberStyles.None, CultureInfo.InvariantCulture, out var hardLinkCount))
        {
            return new FileSystemEntryInspection(
                FileSystemEntryKind.Unknown,
                0,
                $"Unable to parse hard-link count from stat output: '{value}'.");
        }

        var kind = typeText switch
        {
            "Regular File" => FileSystemEntryKind.RegularFile,
            "Directory" => FileSystemEntryKind.Directory,
            "Symbolic Link" => FileSystemEntryKind.SymbolicLink,
            _ => FileSystemEntryKind.Other
        };

        return new FileSystemEntryInspection(kind, hardLinkCount, typeText);
    }

    private static FileSystemEntryInspection InspectionFailure(string message) =>
        new(FileSystemEntryKind.Unknown, 0, message);
}
