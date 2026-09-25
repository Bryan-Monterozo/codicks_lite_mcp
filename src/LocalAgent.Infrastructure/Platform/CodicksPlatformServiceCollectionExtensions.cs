using LocalAgent.Core.Platform;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace LocalAgent.Infrastructure.Platform;

public static class CodicksPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddCodicksPlatformServices(
        this IServiceCollection services,
        IPlatformApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(
            services);

        ArgumentNullException.ThrowIfNull(
            applicationPaths);

        services.AddSingleton<IPlatformApplicationPaths>(
            applicationPaths);

        services.AddSingleton<LocalSessionControlProcessor>();

        switch (applicationPaths.Platform)
        {
            case CodicksPlatformKind.MacOS:
                services.AddSingleton<
                    IFileSystemEntryInspector,
                    MacOsFileSystemEntryInspector>();

                services.AddSingleton<
                    IFileSystemDeviceInspector,
                    MacOsFileSystemDeviceInspector>();

                services.AddSingleton<
                    ILocalSessionControlClient,
                    MacOsLocalSessionControlClient>();

                services.AddSingleton<
                    IPlatformSecretStore,
                    MacOsKeychainSecretStore>();

                services.AddHostedService<
                    UnixControlSocketServer>();

                break;

            case CodicksPlatformKind.Windows:
                services.AddSingleton<
                    IFileSystemEntryInspector,
                    WindowsFileSystemEntryInspector>();

                services.AddSingleton<
                    IFileSystemDeviceInspector,
                    WindowsFileSystemDeviceInspector>();

                services.AddSingleton<
                    ILocalSessionControlClient,
                    WindowsLocalSessionControlClient>();

                services.AddSingleton<
                    IPlatformSecretStore,
                    WindowsCredentialManagerSecretStore>();

                services.AddHostedService<
                    WindowsNamedPipeControlServer>();

                break;

            default:
                throw new PlatformNotSupportedException(
                    $"Unsupported Codicks Lite platform: {applicationPaths.Platform}.");
        }

        return services;
    }
}
