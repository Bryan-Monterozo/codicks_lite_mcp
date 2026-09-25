using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Audit;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Recovery;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.Audit;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WindowsStateStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-win-state-{Guid.NewGuid():N}");

    private readonly string _workspaceRoot;
    private readonly string _stateRoot;

    public WindowsStateStorageTests()
    {
        _workspaceRoot =
            Path.Combine(
                _root,
                "workspace");

        _stateRoot =
            Path.Combine(
                _root,
                "localappdata",
                "CodicksLiteMcp");

        Directory.CreateDirectory(
            _workspaceRoot);

        Directory.CreateDirectory(
            _stateRoot);
    }

    [Fact]
    public void UpdateBackupRestore_UsesConfiguredStateRoot_AndRestoresExactBytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var configuration =
            CreateConfiguration(
                "read",
                "update");

        var path =
            Path.Combine(
                _workspaceRoot,
                "sample.txt");

        var original =
            Encoding.UTF8.GetBytes(
                "original\r\n");

        File.WriteAllBytes(
            path,
            original);

        var services =
            CreateMutationServices(
                configuration);

        var update =
            services.Mutation.UpdateText(
                "windows",
                "sample.txt",
                "changed\n",
                HashBytes(
                    original));

        Assert.True(
            update.Success,
            update.Message);

        Assert.NotNull(
            update.Value);

        Assert.False(
            string.IsNullOrWhiteSpace(
                update.Value.BackupId));

        var backupDirectory =
            Path.Combine(
                _stateRoot,
                "recovery",
                "updates",
                "windows");

        var selectedPayload =
            Path.Combine(
                backupDirectory,
                $"{update.Value.BackupId}.bin");

        var selectedMetadata =
            Path.Combine(
                backupDirectory,
                $"{update.Value.BackupId}.json");

        Assert.Equal(
            original,
            File.ReadAllBytes(
                selectedPayload));

        Assert.True(
            File.Exists(
                selectedMetadata));

        var changed =
            File.ReadAllBytes(
                path);

        var preview =
            services.BackupManager.PreviewRestore(
                update.Value.BackupId!);

        Assert.Equal(
            HashBytes(
                changed),
            preview.CurrentSha256);

        var restore =
            services.BackupManager.Restore(
                update.Value.BackupId!,
                preview.CurrentSha256);

        Assert.True(
            restore.Success,
            restore.Message);

        Assert.NotNull(
            restore.Value);

        Assert.Equal(
            original,
            File.ReadAllBytes(
                path));

        var safetyPayload =
            Path.Combine(
                backupDirectory,
                $"{restore.Value.SafetyBackupId}.bin");

        Assert.Equal(
            changed,
            File.ReadAllBytes(
                safetyPayload));

        Assert.Empty(
            Directory.EnumerateFiles(
                _workspaceRoot,
                "*.tmp",
                SearchOption.AllDirectories));
    }

    [Fact]
    public void PatchApply_UsesSameUpdateBackupStore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var configuration =
            CreateConfiguration(
                "read",
                "update");

        var path =
            Path.Combine(
                _workspaceRoot,
                "patch.txt");

        File.WriteAllText(
            path,
            "one\ntwo",
            new UTF8Encoding(false));

        var pathPolicy =
            CreatePathPolicy(
                configuration);

        IFileHasher hasher =
            new Sha256FileHasher();

        IUpdateBackupStore backupStore =
            new UpdateBackupStore(
                configuration,
                new UserPathResolver());

        var mutation =
            new WorkspaceMutationService(
                pathPolicy,
                configuration,
                hasher,
                new AtomicFileWriter(),
                backupStore);

        var reviewStore =
            new MemoryFilePatchReviewStore(
                TimeProvider.System);

        var patchService =
            new UnifiedPatchService();

        var diffService =
            new FileDiffService();

        var previewService =
            new WorkspacePatchPreviewService(
                pathPolicy,
                configuration,
                hasher,
                patchService,
                diffService,
                reviewStore);

        var applyService =
            new WorkspacePatchApplyService(
                pathPolicy,
                configuration,
                hasher,
                patchService,
                diffService,
                reviewStore,
                mutation);

        var patch =
            string.Join(
                '\n',
                "--- a/patch.txt",
                "+++ b/patch.txt",
                "@@ -2,1 +2,1 @@",
                "-two",
                "+patched");

        var preview =
            previewService.Preview(
                "windows",
                "patch.txt",
                patch,
                HashBytes(
                    File.ReadAllBytes(
                        path)));

        Assert.True(
            preview.Success,
            preview.Message);

        Assert.NotNull(
            preview.Value);

        var apply =
            applyService.Apply(
                "windows",
                "patch.txt",
                patch: null,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!);

        Assert.True(
            apply.Success,
            apply.Message);

        Assert.NotNull(
            apply.Value);

        Assert.False(
            string.IsNullOrWhiteSpace(
                apply.Value.BackupId));

        Assert.Equal(
            "one\npatched",
            File.ReadAllText(
                path));

        Assert.True(
            File.Exists(
                Path.Combine(
                    _stateRoot,
                    "recovery",
                    "updates",
                    "windows",
                    $"{apply.Value.BackupId}.bin")));
    }

    [Fact]
    public void DeleteRecovery_StoresMetadataInStateRoot_AndPayloadOnWorkspaceVolume()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var configuration =
            CreateConfiguration(
                "read",
                "delete",
                "restore");

        var path =
            Path.Combine(
                _workspaceRoot,
                "delete-me.txt");

        var original =
            Encoding.UTF8.GetBytes(
                "recover me");

        File.WriteAllBytes(
            path,
            original);

        var pathResolver =
            new UserPathResolver();

        var registry =
            new WorkspaceRegistry(
                configuration,
                pathResolver);

        var resolver =
            new WorkspaceResolver(
                registry);

        var entryInspector =
            new WindowsFileSystemEntryInspector();

        var deviceInspector =
            new WindowsFileSystemDeviceInspector();

        var lifecycle =
            new WorkspaceLifecycleService(
                CreatePathPolicy(
                    configuration),
                resolver,
                new Sha256FileHasher(),
                new FileRecoveryStore(
                    configuration,
                    pathResolver,
                    new WorkspaceRecoveryLocationResolver(
                        deviceInspector,
                        entryInspector)),
                new JsonMutationReceiptStore(
                    configuration,
                    pathResolver));

        var result =
            lifecycle.DeleteFile(
                $"delete-{Guid.NewGuid():N}",
                "windows",
                "delete-me.txt");

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        var recoveryId =
            result.Value.RecoveryId!;

        var metadataPath =
            Path.Combine(
                _stateRoot,
                "recovery",
                "deletions",
                "records",
                $"{recoveryId}.json");

        Assert.True(
            File.Exists(
                metadataPath));

        var recoveryStore =
            new FileRecoveryStore(
                configuration,
                pathResolver,
                new WorkspaceRecoveryLocationResolver(
                    deviceInspector,
                    entryInspector));

        var record =
            recoveryStore.Load(
                recoveryId);

        Assert.NotNull(
            record);

        Assert.False(
            File.Exists(
                path));

        Assert.True(
            File.Exists(
                record.QuarantinePath));

        Assert.Equal(
            original,
            File.ReadAllBytes(
                record.QuarantinePath));

        Assert.Equal(
            deviceInspector.GetDeviceId(
                _workspaceRoot),
            deviceInspector.GetDeviceId(
                Path.GetDirectoryName(
                    record.QuarantinePath)!));

        var restore =
            lifecycle.RestoreFile(
                $"restore-{Guid.NewGuid():N}",
                recoveryId);

        Assert.True(
            restore.Success,
            restore.Message);

        Assert.Equal(
            original,
            File.ReadAllBytes(
                path));

        Assert.True(
            Directory.Exists(
                Path.Combine(
                    _stateRoot,
                    "mutation-receipts")));
    }

    [Fact]
    public void AuditWriter_WritesUnderConfiguredStateRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var configuration =
            CreateConfiguration(
                "read");

        var writer =
            new JsonLinesAuditWriter(
                configuration,
                new UserPathResolver());

        Assert.True(
            writer.TryWrite(
                new OperationAuditRecord(
                    DateTimeOffset.UtcNow,
                    "windows-state-test",
                    "windows",
                    "sample.txt",
                    "sample.txt",
                    MutationId: null,
                    RecoveryId: null,
                    DryRun: false,
                    Success: true,
                    ErrorCode: null)));

        Assert.True(
            File.Exists(
                Path.Combine(
                    _stateRoot,
                    "audit",
                    "operations.jsonl")));
    }

    private MutationServices CreateMutationServices(
        AgentConfiguration configuration)
    {
        var pathResolver =
            new UserPathResolver();

        var pathPolicy =
            CreatePathPolicy(
                configuration);

        IFileHasher hasher =
            new Sha256FileHasher();

        IUpdateBackupStore backupStore =
            new UpdateBackupStore(
                configuration,
                pathResolver);

        var mutation =
            new WorkspaceMutationService(
                pathPolicy,
                configuration,
                hasher,
                new AtomicFileWriter(),
                backupStore);

        var manager =
            new UpdateBackupManagementService(
                configuration,
                pathResolver,
                pathPolicy,
                hasher,
                new AtomicFileWriter(),
                backupStore);

        return new MutationServices(
            mutation,
            manager);
    }

    private WorkspacePathPolicy CreatePathPolicy(
        AgentConfiguration configuration)
    {
        var pathResolver =
            new UserPathResolver();

        var registry =
            new WorkspaceRegistry(
                configuration,
                pathResolver);

        return new WorkspacePathPolicy(
            new WorkspaceResolver(
                registry),
            new WorkspacePermissionEvaluator(
                configuration),
            new DenyPathMatcher(
                AgentSecurityDefaults
                    .GetEffectiveDenyGlobs(
                        configuration.Agent.Security.DenyGlobs)),
            new WindowsFileSystemEntryInspector());
    }

    private AgentConfiguration CreateConfiguration(
        params string[] allowedOperations)
    {
        var configuration =
            new AgentConfiguration();

        configuration.Agent.StateDirectory =
            _stateRoot;

        configuration.Agent.Security.DenyGlobs =
            [];

        configuration.Agent.Workspaces[
            "windows"] =
            new WorkspaceOptions
            {
                Root =
                    _workspaceRoot,
                Enabled =
                    true,
                AllowedOperations =
                    allowedOperations.ToList()
            };

        return configuration;
    }

    private static string HashBytes(
        byte[] bytes) =>
        Convert.ToHexString(
            SHA256.HashData(
                bytes));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(
                    _root))
            {
                Directory.Delete(
                    _root,
                    recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record MutationServices(
        WorkspaceMutationService Mutation,
        UpdateBackupManagementService BackupManager);
}
