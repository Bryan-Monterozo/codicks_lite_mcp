using System.ComponentModel;
using System.Diagnostics;
using LocalAgent.Core.Platform;

namespace LocalAgent.Infrastructure.Platform;

public sealed class MacOsKeychainSecretStore :
    IPlatformSecretStore
{
    private const string SecurityExecutable =
        "/usr/bin/security";

    public bool Contains(
        string serviceName) =>
        Read(
            serviceName) is not null;

    public string? Read(
        string serviceName)
    {
        ValidateServiceName(
            serviceName);

        EnsureMacOs();

        var result =
            RunSecurity(
                [
                    "find-generic-password",
                    "-a",
                    Environment.UserName,
                    "-s",
                    serviceName,
                    "-w"
                ]);

        return result.ExitCode == 0
            ? result.StandardOutput.TrimEnd(
                '\r',
                '\n')
            : null;
    }

    public void Store(
        string serviceName,
        string secret)
    {
        ValidateServiceName(
            serviceName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            secret);

        EnsureMacOs();

        var result =
            RunSecurity(
                [
                    "add-generic-password",
                    "-U",
                    "-a",
                    Environment.UserName,
                    "-s",
                    serviceName,
                    "-w",
                    secret
                ]);

        if (result.ExitCode != 0)
        {
            throw new IOException(
                "macOS Keychain rejected the secret-store update.");
        }
    }

    public bool Delete(
        string serviceName)
    {
        ValidateServiceName(
            serviceName);

        EnsureMacOs();

        var result =
            RunSecurity(
                [
                    "delete-generic-password",
                    "-a",
                    Environment.UserName,
                    "-s",
                    serviceName
                ]);

        return result.ExitCode == 0;
    }

    private static void ValidateServiceName(
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serviceName);
    }

    private static void EnsureMacOs()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "The macOS Keychain secret store requires macOS.");
        }
    }

    private static ProcessResult RunSecurity(
        IReadOnlyList<string> arguments)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    SecurityExecutable,
                RedirectStandardOutput =
                    true,
                RedirectStandardError =
                    true,
                UseShellExecute =
                    false,
                CreateNoWindow =
                    true
            };

        foreach (var argument in
                 arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        try
        {
            using var process =
                Process.Start(
                    startInfo)
                ?? throw new IOException(
                    "Unable to start macOS Keychain command.");

            var standardOutput =
                process.StandardOutput.ReadToEnd();

            var standardError =
                process.StandardError.ReadToEnd();

            process.WaitForExit();

            return new ProcessResult(
                process.ExitCode,
                standardOutput,
                standardError);
        }
        catch (Win32Exception exception)
        {
            throw new IOException(
                "Unable to start macOS Keychain command.",
                exception);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
