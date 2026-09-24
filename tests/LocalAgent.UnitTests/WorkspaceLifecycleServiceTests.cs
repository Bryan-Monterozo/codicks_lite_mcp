using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Recovery;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspaceLifecycleServiceTests : IDisposable
{
    private readonly string _baseRoot;
    private readonly string _workspaceRoot;
    private readonly string _stateRoot;
    private readonly AgentConfiguration _configuration;
    private readonly UserPathResolver _pathResolver;
    private readonly Sha256FileHasher _hasher;
    private readonly FileRecoveryStore _recoveryStore;
    private readonly JsonMutationReceiptStore _receiptStore;
    private readonly WorkspaceLifecycleService _service;

    public WorkspaceLifecycleServiceTests()
    {
        _baseRoot = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-chunk06-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_baseRoot, "workspace");
        _stateRoot = Path.Combine(_baseRoot, "state");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_stateRoot);

        _configuration = CreateConfiguration(
            _workspaceRoot,
            _stateRoot);
        _pathResolver = new UserPathResolver();
        _hasher = new Sha256FileHasher();

        var registry = new WorkspaceRegistry(_configuration, _pathResolver);
        var resolver = new WorkspaceResolver(registry);
        var permissions = new WorkspacePermissionEvaluator(_configuration);
        var denyMatcher = new DenyPathMatcher(
            AgentSecurityDefaults.GetEffectiveDenyGlobs(
                _configuration.Agent.Security.DenyGlobs));
        var entryInspector = new MacOsFileSystemEntryInspector();
        var pathPolicy = new WorkspacePathPolicy(
            resolver,
            permissions,
            denyMatcher,
            entryInspector);
        var deviceInspector = new MacOsFileSystemDeviceInspector();
        var locationResolver = new WorkspaceRecoveryLocationResolver(
            deviceInspector,
            entryInspector);

        _recoveryStore = new FileRecoveryStore(
            _configuration,
            _pathResolver,
            locationResolver);
        _receiptStore = new JsonMutationReceiptStore(
            _configuration,
            _pathResolver);

        _service = new WorkspaceLifecycleService(
            pathPolicy,
            resolver,
            _hasher,
            _recoveryStore,
            _receiptStore);
    }

    [Fact]
    public void CreateDirectory_CreatesNestedDirectories_OneValidatedComponentAtATime()
    {
        var result = _service.CreateDirectory(
            NewMutationId(),
            "test",
            "one/two/three");

        Assert.True(result.Success, result.Message);
        Assert.True(Directory.Exists(Path.Combine(_workspaceRoot, "one", "two", "three")));
    }

    [Fact]
    public void CreateDirectory_RejectsTraversal_WithoutCreatingAnything()
    {
        var result = _service.CreateDirectory(
            NewMutationId(),
            "test",
            "safe/../escape");

        Assert.False(result.Success);
        Assert.False(Directory.Exists(Path.Combine(_workspaceRoot, "safe")));
    }

    [Fact]
    public void MoveFile_RequiresExpectedHash_AndRefusesDestinationOverwrite()
    {
        var source = Path.Combine(_workspaceRoot, "source.txt");
        var destination = Path.Combine(_workspaceRoot, "destination.txt");
        File.WriteAllText(source, "source", new UTF8Encoding(false));
        File.WriteAllText(destination, "destination", new UTF8Encoding(false));

        var sourceHash = _hasher.ComputeFile(source);
        var result = _service.MoveFile(
            NewMutationId(),
            "test",
            "source.txt",
            "destination.txt",
            sourceHash);

        Assert.False(result.Success);
        Assert.Equal(FileMutationError.AlreadyExists, result.Error);
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("destination", File.ReadAllText(destination));
    }

    [Fact]
    public void MoveFile_MovesByteForByte_AndReplaysSameMutationId()
    {
        var source = Path.Combine(_workspaceRoot, "move-me.txt");
        var destination = Path.Combine(_workspaceRoot, "moved.txt");
        var bytes = Encoding.UTF8.GetBytes("move-content");
        File.WriteAllBytes(source, bytes);

        var hash = _hasher.ComputeFile(source);
        var mutationId = NewMutationId();

        var first = _service.MoveFile(
            mutationId,
            "test",
            "move-me.txt",
            "moved.txt",
            hash);
        var second = _service.MoveFile(
            mutationId,
            "test",
            "move-me.txt",
            "moved.txt",
            hash);

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.NotNull(second.Value);
        Assert.True(second.Value.Replayed);
        Assert.False(File.Exists(source));
        Assert.Equal(bytes, File.ReadAllBytes(destination));
    }

    [Fact]
    public void MoveFile_RejectsMutationIdReuseWithDifferentArguments()
    {
        var firstPath = Path.Combine(_workspaceRoot, "first.txt");
        var secondPath = Path.Combine(_workspaceRoot, "second.txt");
        File.WriteAllText(firstPath, "first", new UTF8Encoding(false));
        File.WriteAllText(secondPath, "second", new UTF8Encoding(false));

        var mutationId = NewMutationId();
        var firstHash = _hasher.ComputeFile(firstPath);

        var first = _service.MoveFile(
            mutationId,
            "test",
            "first.txt",
            "first-moved.txt",
            firstHash);
        var second = _service.MoveFile(
            mutationId,
            "test",
            "second.txt",
            "second-moved.txt",
            _hasher.ComputeFile(secondPath));

        Assert.True(first.Success, first.Message);
        Assert.False(second.Success);
        Assert.Equal(FileMutationError.MutationIdConflict, second.Error);
        Assert.True(File.Exists(secondPath));
    }

    [Fact]
    public void DeleteAndRestore_RoundTripsFileByteForByte()
    {
        var path = Path.Combine(_workspaceRoot, "recover-me.txt");
        var bytes = Encoding.UTF8.GetBytes("recoverable-content\n");
        File.WriteAllBytes(path, bytes);

        var deleteResult = _service.DeleteFile(
            NewMutationId(),
            "test",
            "recover-me.txt");

        Assert.True(deleteResult.Success, deleteResult.Message);
        Assert.NotNull(deleteResult.Value);
        Assert.False(File.Exists(path));
        Assert.False(string.IsNullOrWhiteSpace(deleteResult.Value.RecoveryId));

        var recovery = _recoveryStore.Load(deleteResult.Value.RecoveryId!);
        Assert.NotNull(recovery);
        Assert.Equal(RecoveryStatus.Quarantined, recovery.Status);
        Assert.True(File.Exists(recovery.QuarantinePath));
        Assert.Equal(bytes, File.ReadAllBytes(recovery.QuarantinePath));

        var restoreResult = _service.RestoreFile(
            NewMutationId(),
            deleteResult.Value.RecoveryId!);

        Assert.True(restoreResult.Success, restoreResult.Message);
        Assert.True(File.Exists(path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(recovery.QuarantinePath));

        recovery = _recoveryStore.Load(deleteResult.Value.RecoveryId!);
        Assert.NotNull(recovery);
        Assert.Equal(RecoveryStatus.Restored, recovery.Status);
    }

    [Fact]
    public void Restore_RefusesToOverwriteCurrentDestination()
    {
        var path = Path.Combine(_workspaceRoot, "collision.txt");
        File.WriteAllText(path, "original", new UTF8Encoding(false));

        var delete = _service.DeleteFile(
            NewMutationId(),
            "test",
            "collision.txt");

        Assert.True(delete.Success, delete.Message);
        Assert.NotNull(delete.Value);

        File.WriteAllText(path, "new-owner", new UTF8Encoding(false));

        var restore = _service.RestoreFile(
            NewMutationId(),
            delete.Value.RecoveryId!);

        Assert.False(restore.Success);
        Assert.Equal(FileMutationError.AlreadyExists, restore.Error);
        Assert.Equal("new-owner", File.ReadAllText(path));
    }

    [Fact]
    public void MandatoryDenyRules_ProtectRecoverySidecar()
    {
        var effective = AgentSecurityDefaults.GetEffectiveDenyGlobs([]);

        Assert.Contains("**/.codicks-lite-recovery", effective);
        Assert.Contains("**/.codicks-lite-recovery/**", effective);
    }

    [Fact]
    public void RecoveryLocation_RejectsUnexpectedDeviceMismatch()
    {
        var inspector = new MacOsFileSystemEntryInspector();
        var resolver = new WorkspaceRecoveryLocationResolver(
            new MismatchedDeviceInspector(_workspaceRoot),
            inspector);
        var workspace = new WorkspaceDescriptor(
            "test",
            _workspaceRoot,
            true,
            [WorkspaceOperation.Delete, WorkspaceOperation.Restore]);

        Assert.Throws<IOException>(() => resolver.Resolve(workspace));
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseRoot))
        {
            Directory.Delete(_baseRoot, recursive: true);
        }
    }

    private static string NewMutationId() =>
        $"mut-{Guid.NewGuid():N}";

    private static AgentConfiguration CreateConfiguration(
        string workspaceRoot,
        string stateRoot)
    {
        var configuration = new AgentConfiguration();
        configuration.Agent.StateDirectory = stateRoot;
        configuration.Agent.Workspaces["test"] = new WorkspaceOptions
        {
            Root = workspaceRoot,
            Enabled = true,
            AllowedOperations = ["read", "create", "update", "move", "delete", "restore"]
        };

        return configuration;
    }

    private sealed class MismatchedDeviceInspector(string workspaceRoot)
        : IFileSystemDeviceInspector
    {
        public string GetDeviceId(string existingPath)
        {
            var fullPath = Path.GetFullPath(existingPath);
            return string.Equals(
                    fullPath,
                    Path.GetFullPath(workspaceRoot),
                    StringComparison.Ordinal)
                ? "workspace-device"
                : "other-device";
        }
    }
}
