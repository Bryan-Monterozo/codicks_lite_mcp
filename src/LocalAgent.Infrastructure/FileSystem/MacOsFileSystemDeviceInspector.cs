using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.FileSystem;

public sealed class MacOsFileSystemDeviceInspector : IFileSystemDeviceInspector
{
    private const string StatPath = "/usr/bin/stat";

    public string GetDeviceId(string existingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingPath);

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "The current filesystem device inspector requires macOS.");
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
            startInfo.ArgumentList.Add("%d");
            startInfo.ArgumentList.Add(existingPath);

            using var process = Process.Start(startInfo)
                ?? throw new IOException("Unable to start /usr/bin/stat.");

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new IOException(
                    string.IsNullOrWhiteSpace(standardError)
                        ? $"Unable to inspect filesystem device for: {existingPath}"
                        : standardError.Trim());
            }

            var value = standardOutput.Trim();
            if (!ulong.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                throw new IOException(
                    $"Unexpected device id returned by /usr/bin/stat: '{value}'.");
            }

            return value;
        }
        catch (Win32Exception exception)
        {
            throw new IOException("Unable to inspect filesystem device.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new IOException("Unable to inspect filesystem device.", exception);
        }
    }
}
