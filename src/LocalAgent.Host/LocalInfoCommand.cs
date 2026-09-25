using LocalAgent.Core.Identity;
using LocalAgent.Core.Platform;
using LocalAgent.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace LocalAgent.Host;

public static class LocalInfoCommand
{
    private const string Flag =
        "--local-info";

    public static async Task<bool> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            args);

        ArgumentNullException.ThrowIfNull(
            services);

        if (args.Length == 0 ||
            !string.Equals(
                args[0],
                Flag,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (args.Length != 2)
        {
            Fail(
                "Usage: --local-info <version|releases|rollback|paths|doctor-local|agent-config-path>",
                2);

            return true;
        }

        var paths =
            services.GetRequiredService<
                IPlatformApplicationPaths>();

        try
        {
            switch (args[1].ToLowerInvariant())
            {
                case "version":
                    ShowVersion(
                        paths);

                    break;

                case "releases":
                    ShowReleases(
                        paths);

                    break;

                case "rollback":
                    Rollback(
                        paths);

                    break;

                case "paths":
                    ShowPaths(
                        paths);

                    break;

                case "doctor-local":
                    await DoctorLocalAsync(
                        paths,
                        services,
                        cancellationToken);

                    break;

                case "agent-config-path":
                    Console.WriteLine(
                        paths.DefaultConfigFilePath);

                    break;

                default:
                    Fail(
                        $"Unknown local info action: {args[1]}",
                        2);

                    break;
            }
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                OperationCanceledException)
        {
            Fail(
                exception.Message,
                1);
        }

        return true;
    }

    private static void ShowVersion(
        IPlatformApplicationPaths paths)
    {
        var current =
            ReadPointer(
                paths,
                "current.txt");

        if (!string.IsNullOrWhiteSpace(
                current))
        {
            var versionPath =
                Path.Combine(
                    paths.ApplicationRoot,
                    "releases",
                    current,
                    "codicks-lite-version.txt");

            if (File.Exists(
                    versionPath))
            {
                Console.WriteLine(
                    $"Codicks Lite MCP {File.ReadAllText(versionPath).Trim()}");

                Console.WriteLine(
                    $"Release: {current}");

                return;
            }
        }

        Console.WriteLine(
            $"Codicks Lite MCP {AgentIdentity.Current.Version}");
    }

    private static void ShowReleases(
        IPlatformApplicationPaths paths)
    {
        var releasesRoot =
            Path.Combine(
                paths.ApplicationRoot,
                "releases");

        var current =
            ReadPointer(
                paths,
                "current.txt");

        var previous =
            ReadPointer(
                paths,
                "previous.txt");

        Console.WriteLine(
            "Codicks Lite releases");

        Console.WriteLine();

        if (!Directory.Exists(
                releasesRoot))
        {
            Console.WriteLine(
                "No installed releases.");

            return;
        }

        foreach (var release in
                 Directory
                     .EnumerateDirectories(
                         releasesRoot)
                     .OrderBy(
                         Path.GetFileName,
                         StringComparer.OrdinalIgnoreCase))
        {
            var name =
                Path.GetFileName(
                    release);

            var marker =
                string.Equals(
                    name,
                    current,
                    StringComparison.OrdinalIgnoreCase)
                    ? "*"
                    : string.Equals(
                        name,
                        previous,
                        StringComparison.OrdinalIgnoreCase)
                        ? "-"
                        : " ";

            Console.WriteLine(
                $"{marker} {name}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "* current");
        Console.WriteLine(
            "- previous rollback target");
    }

    private static void Rollback(
        IPlatformApplicationPaths paths)
    {
        var current =
            ReadRequiredPointer(
                paths,
                "current.txt");

        var previous =
            ReadRequiredPointer(
                paths,
                "previous.txt");

        ValidateReleaseName(
            current);

        ValidateReleaseName(
            previous);

        var releasesRoot =
            Path.Combine(
                paths.ApplicationRoot,
                "releases");

        var previousPath =
            Path.Combine(
                releasesRoot,
                previous);

        if (!Directory.Exists(
                previousPath))
        {
            throw new IOException(
                $"Previous release target no longer exists: {previousPath}");
        }

        WritePointer(
            paths,
            "current.txt",
            previous);

        WritePointer(
            paths,
            "previous.txt",
            current);

        Console.WriteLine(
            "Rolled back Codicks Lite MCP.");

        Console.WriteLine(
            $"Current: {previous}");

        Console.WriteLine(
            $"Previous: {current}");

        Console.WriteLine(
            "Restart tunnel-client so it launches the selected release.");
    }

    private static void ShowPaths(
        IPlatformApplicationPaths paths)
    {
        Console.WriteLine(
            $"App root: {paths.ApplicationRoot}");

        Console.WriteLine(
            $"State root: {paths.DefaultStateDirectory}");

        Console.WriteLine(
            $"Agent config: {paths.DefaultConfigFilePath}");

        Console.WriteLine(
            $"Releases: {Path.Combine(paths.ApplicationRoot, "releases")}");

        Console.WriteLine(
            $"Current pointer: {Path.Combine(paths.ApplicationRoot, "current.txt")}");

        Console.WriteLine(
            $"Previous pointer: {Path.Combine(paths.ApplicationRoot, "previous.txt")}");
    }

    private static async Task DoctorLocalAsync(
        IPlatformApplicationPaths paths,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var failed =
            false;

        Console.WriteLine(
            "Codicks Lite local doctor");

        Console.WriteLine();

        if (Directory.Exists(
                paths.ApplicationRoot))
        {
            Console.WriteLine(
                $"[OK] app root: {paths.ApplicationRoot}");
        }
        else
        {
            Console.WriteLine(
                $"[INFO] app root not created yet: {paths.ApplicationRoot}");
        }

        if (File.Exists(
                paths.DefaultConfigFilePath))
        {
            Console.WriteLine(
                $"[OK] agent config: {paths.DefaultConfigFilePath}");
        }
        else
        {
            Console.WriteLine(
                $"[INFO] agent config not installed yet: {paths.DefaultConfigFilePath}");
        }

        try
        {
            var client =
                services.GetRequiredService<
                    ILocalSessionControlClient>();

            var response =
                await client.SendAsync(
                    new LocalSessionControlRequest(
                        "status"),
                    cancellationToken);

            if (response.Success &&
                response.Status is not null)
            {
                Console.WriteLine(
                    $"[OK] local control transport: {response.Status.Mode}");
            }
            else
            {
                Console.WriteLine(
                    "[FAIL] local control transport returned an error.");

                failed =
                    true;
            }
        }
        catch (Exception exception)
            when (exception is
                IOException or
                TimeoutException or
                PlatformNotSupportedException)
        {
            Console.WriteLine(
                $"[INFO] local control transport inactive: {exception.Message}");
        }

        if (failed)
        {
            Environment.ExitCode =
                1;
        }
    }

    private static string? ReadPointer(
        IPlatformApplicationPaths paths,
        string fileName)
    {
        var path =
            Path.Combine(
                paths.ApplicationRoot,
                fileName);

        if (!File.Exists(
                path))
        {
            return null;
        }

        var value =
            File.ReadAllText(
                    path)
                .Trim();

        return value.Length == 0
            ? null
            : value;
    }

    private static string ReadRequiredPointer(
        IPlatformApplicationPaths paths,
        string fileName) =>
        ReadPointer(
            paths,
            fileName)
        ?? throw new IOException(
            $"Release pointer is missing: {Path.Combine(paths.ApplicationRoot, fileName)}");

    private static void WritePointer(
        IPlatformApplicationPaths paths,
        string fileName,
        string releaseName)
    {
        ValidateReleaseName(
            releaseName);

        Directory.CreateDirectory(
            paths.ApplicationRoot);

        var destination =
            Path.Combine(
                paths.ApplicationRoot,
                fileName);

        var temporary =
            destination +
            $".{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(
                temporary,
                releaseName +
                Environment.NewLine);

            File.Move(
                temporary,
                destination,
                overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(
                    temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void ValidateReleaseName(
        string releaseName)
    {
        if (string.IsNullOrWhiteSpace(
                releaseName) ||
            Path.IsPathRooted(
                releaseName) ||
            releaseName.Contains(
                Path.DirectorySeparatorChar) ||
            releaseName.Contains(
                Path.AltDirectorySeparatorChar) ||
            releaseName is
                "." or
                "..")
        {
            throw new InvalidDataException(
                "Release pointer contains an invalid release name.");
        }
    }

    private static void Fail(
        string message,
        int exitCode)
    {
        Console.Error.WriteLine(
            $"ERROR: {message}");

        Environment.ExitCode =
            exitCode;
    }
}
