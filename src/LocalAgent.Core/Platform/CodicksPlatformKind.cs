namespace LocalAgent.Core.Platform;

public enum CodicksPlatformKind
{
    MacOS,
    Windows
}

public static class CodicksPlatformDetector
{
    public static CodicksPlatformKind DetectCurrent()
    {
        if (OperatingSystem.IsMacOS())
        {
            return CodicksPlatformKind.MacOS;
        }

        if (OperatingSystem.IsWindows())
        {
            return CodicksPlatformKind.Windows;
        }

        throw new PlatformNotSupportedException(
            "Codicks Lite currently supports macOS and Windows only.");
    }
}
