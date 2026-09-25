using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Core.Configuration;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class LocalBackupCliIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("bcli");

    [Fact]
    public async Task BackupCli_ListShowRestore_WorksOutsideMcp()
    {
        var workspaceRoot =
            Path.Combine(
                _root,
                "workspace");

        var stateRoot =
            Path.Combine(
                _root,
                "state");

        var configDirectory =
            Path.Combine(
                _root,
                "config");

        var configPath =
            Path.Combine(
                configDirectory,
                "agent.json");

        Directory.CreateDirectory(
            workspaceRoot);

        Directory.CreateDirectory(
            stateRoot);

        Directory.CreateDirectory(
            configDirectory);

        var filePath =
            Path.Combine(
                workspaceRoot,
                "sample.txt");

        var originalBytes =
            Encoding.UTF8.GetBytes(
                "original\n");

        await File.WriteAllBytesAsync(
            filePath,
            originalBytes);

        var configuration =
            new AgentConfiguration();

        configuration.Agent.StateDirectory =
            stateRoot;

        configuration.Agent.Workspaces[
            "backup"] =
            new WorkspaceOptions
            {
                Root =
                    workspaceRoot,
                Enabled = true,
                AllowedOperations =
                    ["read", "update"]
            };

        var backupStore =
            new UpdateBackupStore(
                configuration,
                new UserPathResolver());

        var selected =
            backupStore.Save(
                "backup",
                "sample.txt",
                originalBytes,
                HashBytes(originalBytes));

        await File.WriteAllTextAsync(
            filePath,
            "changed\n",
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(
                new
                {
                    SchemaVersion = 1,
                    Agent = new
                    {
                        StateDirectory =
                            stateRoot,
                        Workspaces =
                            new Dictionary<string, object>
                            {
                                ["backup"] =
                                    new
                                    {
                                        Root =
                                            workspaceRoot,
                                        Enabled = true,
                                        AllowedOperations =
                                            new[]
                                            {
                                                "read",
                                                "update"
                                            }
                                    }
                            }
                    }
                }));

        var list =
            await RunHostAsync(
                configPath,
                "--local-backup-list");

        Assert.Equal(
            0,
            list.ExitCode);

        Assert.Contains(
            selected.Id,
            list.StandardOutput,
            StringComparison.Ordinal);

        var show =
            await RunHostAsync(
                configPath,
                "--local-backup-show",
                selected.Id);

        Assert.Equal(
            0,
            show.ExitCode);

        Assert.Contains(
            "File:         sample.txt",
            show.StandardOutput,
            StringComparison.Ordinal);

        Assert.Contains(
            "Backup SHA:",
            show.StandardOutput,
            StringComparison.Ordinal);

        Assert.Contains(
            "Current SHA:",
            show.StandardOutput,
            StringComparison.Ordinal);

        var blockedRestore =
            await RunHostAsync(
                configPath,
                "--local-backup-restore",
                selected.Id);

        Assert.Equal(
            1,
            blockedRestore.ExitCode);

        Assert.Contains(
            "interactive local terminal",
            blockedRestore.StandardError,
            StringComparison.Ordinal);

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                "changed\n"),
            await File.ReadAllBytesAsync(
                filePath));

        var restore =
            await RunHostAsync(
                configPath,
                "--local-backup-restore",
                selected.Id,
                "--yes");

        Assert.Equal(
            0,
            restore.ExitCode);

        Assert.Contains(
            "Backup restored.",
            restore.StandardOutput,
            StringComparison.Ordinal);

        Assert.Contains(
            "Safety backup of replaced version:",
            restore.StandardOutput,
            StringComparison.Ordinal);

        Assert.Equal(
            originalBytes,
            await File.ReadAllBytesAsync(
                filePath));

        var auditPath =
            Path.Combine(
                stateRoot,
                "audit",
                "operations.jsonl");

        Assert.True(
            File.Exists(
                auditPath));

        Assert.Contains(
            File.ReadAllLines(
                auditPath),
            line =>
                line.Contains(
                    "\"Operation\":\"backup_restore\"",
                    StringComparison.Ordinal));

        var listAfter =
            await RunHostAsync(
                configPath,
                "--local-backup-list");

        Assert.Equal(
            0,
            listAfter.ExitCode);

        Assert.True(
            listAfter.StandardOutput
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Length >= 2);
    }

    private static async Task<ProcessResult> RunHostAsync(
        string configPath,
        params string[] arguments)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

        startInfo.ArgumentList.Add(
            typeof(LocalAgent.Host.HostMarker)
                .Assembly
                .Location);

        foreach (var argument in
                 arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        startInfo.Environment[
            "CODICKS_LITE_CONFIG_FILE"] =
            configPath;

        if (arguments.Contains(
                "--yes",
                StringComparer.Ordinal))
        {
            startInfo.Environment[
                "CODICKS_LITE_TEST_ALLOW_NONINTERACTIVE_BACKUP_RESTORE"] =
                "1";
        }

        using var process =
            Process.Start(
                startInfo);

        Assert.NotNull(
            process);

        var standardOutput =
            await process.StandardOutput.ReadToEndAsync();

        var standardError =
            await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private static string HashBytes(
        byte[] bytes) =>
        Convert.ToHexString(
            SHA256.HashData(
                bytes));

    public void Dispose()
    {
        if (Directory.Exists(
                _root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
