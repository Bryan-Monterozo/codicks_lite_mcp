using LocalAgent.Core.Platform;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Platform;
using LocalAgent.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class PlatformBoundaryTests
{
    [Fact]
    public void MacOsPaths_PreserveExistingApplicationLayout()
    {
        var userProfile =
            Path.Combine(
                Path.GetTempPath(),
                "codicks-platform-macos-user");

        var paths =
            PlatformApplicationPaths.CreateMacOs(
                userProfile);

        var expectedRoot =
            Path.GetFullPath(
                Path.Combine(
                    userProfile,
                    "Library",
                    "Application Support",
                    "CodicksLiteMcp"));

        Assert.Equal(
            CodicksPlatformKind.MacOS,
            paths.Platform);

        Assert.Equal(
            expectedRoot,
            paths.ApplicationRoot);

        Assert.Equal(
            expectedRoot,
            paths.DefaultStateDirectory);

        Assert.Equal(
            Path.Combine(
                expectedRoot,
                "config",
                "agent.json"),
            paths.DefaultConfigFilePath);
    }

    [Fact]
    public void WindowsPaths_UseLocalApplicationDataLayout()
    {
        var localApplicationData =
            Path.Combine(
                Path.GetTempPath(),
                "codicks-platform-windows-localappdata");

        var paths =
            PlatformApplicationPaths.CreateWindows(
                localApplicationData);

        var expectedRoot =
            Path.GetFullPath(
                Path.Combine(
                    localApplicationData,
                    "CodicksLiteMcp"));

        Assert.Equal(
            CodicksPlatformKind.Windows,
            paths.Platform);

        Assert.Equal(
            expectedRoot,
            paths.ApplicationRoot);

        Assert.Equal(
            expectedRoot,
            paths.DefaultStateDirectory);

        Assert.Equal(
            Path.Combine(
                expectedRoot,
                "config",
                "agent.json"),
            paths.DefaultConfigFilePath);
    }

    [Fact]
    public void MacOsRegistration_UsesExistingPlatformImplementations()
    {
        var services =
            new ServiceCollection();

        var paths =
            PlatformApplicationPaths.CreateMacOs(
                Path.Combine(
                    Path.GetTempPath(),
                    "codicks-platform-registration-macos"));

        services.AddCodicksPlatformServices(
            paths);

        using var provider =
            services.BuildServiceProvider();

        Assert.IsType<MacOsFileSystemEntryInspector>(
            provider.GetRequiredService<
                IFileSystemEntryInspector>());

        Assert.IsType<MacOsFileSystemDeviceInspector>(
            provider.GetRequiredService<
                IFileSystemDeviceInspector>());

        Assert.Same(
            paths,
            provider.GetRequiredService<
                IPlatformApplicationPaths>());

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(ILocalSessionControlClient) &&
                descriptor.ImplementationType ==
                    typeof(MacOsLocalSessionControlClient));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(IPlatformSecretStore) &&
                descriptor.ImplementationType ==
                    typeof(MacOsKeychainSecretStore));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(LocalSessionControlProcessor));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(IHostedService) &&
                descriptor.ImplementationType ==
                    typeof(UnixControlSocketServer));

        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(IHostedService) &&
                descriptor.ImplementationType ==
                    typeof(WindowsNamedPipeControlServer));
    }

    [Fact]
    public void WindowsRegistration_UsesWindowsFilesystemAdapters()
    {
        var services =
            new ServiceCollection();

        var paths =
            PlatformApplicationPaths.CreateWindows(
                Path.Combine(
                    Path.GetTempPath(),
                    "codicks-platform-registration-windows"));

        services.AddCodicksPlatformServices(
            paths);

        using var provider =
            services.BuildServiceProvider();

        var entryInspector =
            provider.GetRequiredService<
                IFileSystemEntryInspector>();

        var deviceInspector =
            provider.GetRequiredService<
                IFileSystemDeviceInspector>();

        Assert.IsType<
            WindowsFileSystemEntryInspector>(
            entryInspector);

        Assert.IsType<
            WindowsFileSystemDeviceInspector>(
            deviceInspector);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(ILocalSessionControlClient) &&
                descriptor.ImplementationType ==
                    typeof(WindowsLocalSessionControlClient));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(IPlatformSecretStore) &&
                descriptor.ImplementationType ==
                    typeof(WindowsCredentialManagerSecretStore));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(LocalSessionControlProcessor));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(IHostedService) &&
                descriptor.ImplementationType ==
                    typeof(WindowsNamedPipeControlServer));

        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType ==
                    typeof(IHostedService) &&
                descriptor.ImplementationType ==
                    typeof(UnixControlSocketServer));
    }
}
