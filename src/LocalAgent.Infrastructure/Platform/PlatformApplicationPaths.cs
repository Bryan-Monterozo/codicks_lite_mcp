using LocalAgent.Core.Platform;

namespace LocalAgent.Infrastructure.Platform;

public sealed record PlatformApplicationPaths(
    CodicksPlatformKind Platform,
    string ApplicationRoot,
    string DefaultStateDirectory,
    string DefaultConfigFilePath) : IPlatformApplicationPaths
{
    private const string ProductDirectoryName =
        "CodicksLiteMcp";

    public static PlatformApplicationPaths CreateCurrent()
    {
        var platform =
            CodicksPlatformDetector.DetectCurrent();

        return platform switch
        {
            CodicksPlatformKind.MacOS =>
                CreateMacOs(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile)),

            CodicksPlatformKind.Windows =>
                CreateWindows(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData)),

            _ =>
                throw new PlatformNotSupportedException(
                    $"Unsupported Codicks Lite platform: {platform}.")
        };
    }

    public static PlatformApplicationPaths CreateMacOs(
        string userProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            userProfile);

        var applicationRoot =
            Path.GetFullPath(
                Path.Combine(
                    userProfile,
                    "Library",
                    "Application Support",
                    ProductDirectoryName));

        return Create(
            CodicksPlatformKind.MacOS,
            applicationRoot);
    }

    public static PlatformApplicationPaths CreateWindows(
        string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            localApplicationData);

        var applicationRoot =
            Path.GetFullPath(
                Path.Combine(
                    localApplicationData,
                    ProductDirectoryName));

        return Create(
            CodicksPlatformKind.Windows,
            applicationRoot);
    }

    private static PlatformApplicationPaths Create(
        CodicksPlatformKind platform,
        string applicationRoot)
    {
        var stateDirectory =
            applicationRoot;

        var configFilePath =
            Path.Combine(
                applicationRoot,
                "config",
                "agent.json");

        return new PlatformApplicationPaths(
            platform,
            applicationRoot,
            stateDirectory,
            configFilePath);
    }
}
