using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class UpdateBackupManagementServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspaceRoot;
    private readonly string _stateRoot;
    private readonly AgentConfiguration _configuration;

    public UpdateBackupManagementServiceTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-backup-management-{Guid.NewGuid():N}");

        _workspaceRoot =
            Path.Combine(
                _root,
                "workspace");

        _stateRoot =
            Path.Combine(
                _root,
                "state");

        Directory.CreateDirectory(
            _workspaceRoot);

        Directory.CreateDirectory(
            _stateRoot);

        _configuration =
            CreateConfiguration(
                _workspaceRoot,
                _stateRoot);
    }

    [Fact]
    public void ListShowRestore_RestoresExactBytes_AndCreatesSafetyBackup()
    {
        var path =
            Path.Combine(
                _workspaceRoot,
                "sample.txt");

        var preamble =
            Encoding.UTF8.GetPreamble();

        var originalText =
            Encoding.UTF8.GetBytes(
                "one\r\ntwo\r\n");

        var originalBytes =
            new byte[
                preamble.Length +
                originalText.Length];

        Buffer.BlockCopy(
            preamble,
            0,
            originalBytes,
            0,
            preamble.Length);

        Buffer.BlockCopy(
            originalText,
            0,
            originalBytes,
            preamble.Length,
            originalText.Length);

        File.WriteAllBytes(
            path,
            originalBytes);

        var services =
            CreateServices();

        var originalHash =
            HashBytes(
                originalBytes);

        var selected =
            services.BackupStore.Save(
                "test",
                "sample.txt",
                originalBytes,
                originalHash);

        var changedBytes =
            Encoding.UTF8.GetBytes(
                "changed\n");

        File.WriteAllBytes(
            path,
            changedBytes);

        var list =
            services.Manager.List();

        Assert.Contains(
            list,
            item =>
                item.Id ==
                selected.Id);

        var shown =
            services.Manager.GetBackup(
                selected.Id);

        Assert.Equal(
            "test",
            shown.WorkspaceId);

        Assert.Equal(
            "sample.txt",
            shown.RelativePath);

        Assert.Equal(
            originalHash,
            shown.OriginalSha256);

        var preview =
            services.Manager.PreviewRestore(
                selected.Id);

        Assert.Equal(
            HashBytes(
                changedBytes),
            preview.CurrentSha256);

        var restore =
            services.Manager.Restore(
                selected.Id,
                preview.CurrentSha256);

        Assert.True(
            restore.Success,
            restore.Message);

        Assert.NotNull(
            restore.Value);

        Assert.Equal(
            originalBytes,
            File.ReadAllBytes(
                path));

        Assert.Equal(
            originalHash,
            restore.Value.RestoredSha256);

        Assert.NotEqual(
            selected.Id,
            restore.Value.SafetyBackupId);

        var safetyPayload =
            Path.Combine(
                _stateRoot,
                "recovery",
                "updates",
                "test",
                $"{restore.Value.SafetyBackupId}.bin");

        Assert.Equal(
            changedBytes,
            File.ReadAllBytes(
                safetyPayload));
    }

    [Fact]
    public void Restore_StaleConfirmationHash_ReturnsConflict()
    {
        var path =
            Path.Combine(
                _workspaceRoot,
                "conflict.txt");

        var original =
            Encoding.UTF8.GetBytes(
                "original");

        File.WriteAllBytes(
            path,
            original);

        var services =
            CreateServices();

        var selected =
            services.BackupStore.Save(
                "test",
                "conflict.txt",
                original,
                HashBytes(original));

        File.WriteAllText(
            path,
            "changed",
            new UTF8Encoding(false));

        var before =
            File.ReadAllBytes(
                path);

        var result =
            services.Manager.Restore(
                selected.Id,
                new string(
                    '0',
                    64));

        Assert.False(
            result.Success);

        Assert.Equal(
            FileMutationError.Conflict,
            result.Error);

        Assert.Equal(
            before,
            File.ReadAllBytes(
                path));
    }

    [Fact]
    public void Restore_RequiresCurrentUpdatePermission()
    {
        var path =
            Path.Combine(
                _workspaceRoot,
                "permission.txt");

        var original =
            Encoding.UTF8.GetBytes(
                "original");

        File.WriteAllBytes(
            path,
            original);

        var services =
            CreateServices();

        var selected =
            services.BackupStore.Save(
                "test",
                "permission.txt",
                original,
                HashBytes(original));

        File.WriteAllText(
            path,
            "changed",
            new UTF8Encoding(false));

        _configuration.Agent.Workspaces[
            "test"].AllowedOperations =
            ["read"];

        services =
            CreateServices();

        var result =
            services.Manager.Restore(
                selected.Id,
                HashBytes(
                    File.ReadAllBytes(
                        path)));

        Assert.False(
            result.Success);

        Assert.Equal(
            FileMutationError.AccessDenied,
            result.Error);

        Assert.Equal(
            "changed",
            File.ReadAllText(
                path));
    }

    [Fact]
    public void Restore_PreservesCurrentUnixMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path =
            Path.Combine(
                _workspaceRoot,
                "mode.txt");

        var original =
            Encoding.UTF8.GetBytes(
                "original");

        File.WriteAllBytes(
            path,
            original);

        var services =
            CreateServices();

        var selected =
            services.BackupStore.Save(
                "test",
                "mode.txt",
                original,
                HashBytes(original));

        File.WriteAllText(
            path,
            "changed",
            new UTF8Encoding(false));

        var expectedMode =
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute;

        File.SetUnixFileMode(
            path,
            expectedMode);

        var preview =
            services.Manager.PreviewRestore(
                selected.Id);

        var result =
            services.Manager.Restore(
                selected.Id,
                preview.CurrentSha256);

        Assert.True(
            result.Success,
            result.Message);

        Assert.Equal(
            expectedMode,
            File.GetUnixFileMode(
                path));
    }

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

    private Services CreateServices()
    {
        IUserPathResolver pathResolver =
            new UserPathResolver();

        var registry =
            new WorkspaceRegistry(
                _configuration,
                pathResolver);

        var resolver =
            new WorkspaceResolver(
                registry);

        var permissions =
            new WorkspacePermissionEvaluator(
                _configuration);

        var denyMatcher =
            new DenyPathMatcher(
                AgentSecurityDefaults.GetEffectiveDenyGlobs(
                    _configuration.Agent.Security.DenyGlobs));

        var inspector =
            new MacOsFileSystemEntryInspector();

        var pathPolicy =
            new WorkspacePathPolicy(
                resolver,
                permissions,
                denyMatcher,
                inspector);

        IFileHasher hasher =
            new Sha256FileHasher();

        IAtomicFileWriter writer =
            new AtomicFileWriter();

        IUpdateBackupStore backupStore =
            new UpdateBackupStore(
                _configuration,
                pathResolver);

        var manager =
            new UpdateBackupManagementService(
                _configuration,
                pathResolver,
                pathPolicy,
                hasher,
                writer,
                backupStore);

        return new Services(
            manager,
            backupStore);
    }

    private static AgentConfiguration CreateConfiguration(
        string workspaceRoot,
        string stateRoot)
    {
        var configuration =
            new AgentConfiguration();

        configuration.Agent.StateDirectory =
            stateRoot;

        configuration.Agent.Workspaces[
            "test"] =
            new WorkspaceOptions
            {
                Root =
                    workspaceRoot,
                Enabled = true,
                AllowedOperations =
                    ["read", "update"]
            };

        return configuration;
    }

    private static string HashBytes(
        byte[] bytes) =>
        Convert.ToHexString(
            SHA256.HashData(
                bytes));

    private sealed record Services(
        UpdateBackupManagementService Manager,
        IUpdateBackupStore BackupStore);
}
