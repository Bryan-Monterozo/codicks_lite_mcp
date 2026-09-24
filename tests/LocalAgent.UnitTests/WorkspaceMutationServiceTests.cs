using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspaceMutationServiceTests : IDisposable
{
    private readonly string _baseRoot;
    private readonly string _workspaceRoot;
    private readonly string _stateRoot;
    private readonly AgentConfiguration _configuration;

    public WorkspaceMutationServiceTests()
    {
        _baseRoot = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-chunk05-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_baseRoot, "workspace");
        _stateRoot = Path.Combine(_baseRoot, "state");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_stateRoot);

        _configuration = CreateConfiguration(
            _workspaceRoot,
            _stateRoot);
    }

    [Fact]
    public void CreateText_CreatesNewFile_AndReturnsHash()
    {
        var service = CreateService();

        var result = service.CreateText(
            "test",
            "created.txt",
            "hello\nworld");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("hello\nworld", File.ReadAllText(Path.Combine(_workspaceRoot, "created.txt")));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspaceRoot, "created.txt")))),
            result.Value.Sha256);
        Assert.False(result.Value.DryRun);
        Assert.Null(result.Value.BackupId);
    }

    [Fact]
    public void CreateText_RefusesExistingDestination()
    {
        var path = Path.Combine(_workspaceRoot, "existing.txt");
        File.WriteAllText(path, "original", new UTF8Encoding(false));

        var service = CreateService();
        var result = service.CreateText(
            "test",
            "existing.txt",
            "replacement");

        Assert.False(result.Success);
        Assert.Equal(FileMutationError.AlreadyExists, result.Error);
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void UpdateText_RejectsStaleHash_AndLeavesFileUntouched()
    {
        var path = Path.Combine(_workspaceRoot, "conflict.txt");
        File.WriteAllText(path, "version-one", new UTF8Encoding(false));

        var staleHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(path, "version-two", new UTF8Encoding(false));

        var before = File.ReadAllBytes(path);
        var service = CreateService();

        var result = service.UpdateText(
            "test",
            "conflict.txt",
            "agent-change",
            staleHash);

        Assert.False(result.Success);
        Assert.Equal(FileMutationError.Conflict, result.Error);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void UpdateText_PreservesUtf8Bom_CrLf_AndCreatesBackup()
    {
        var path = Path.Combine(_workspaceRoot, "format.txt");
        var originalText = "one\r\ntwo\r\n";
        var originalContent = Encoding.UTF8.GetBytes(originalText);
        var preamble = Encoding.UTF8.GetPreamble();
        var originalBytes = new byte[preamble.Length + originalContent.Length];

        Buffer.BlockCopy(preamble, 0, originalBytes, 0, preamble.Length);
        Buffer.BlockCopy(originalContent, 0, originalBytes, preamble.Length, originalContent.Length);
        File.WriteAllBytes(path, originalBytes);

        var expectedHash = Convert.ToHexString(SHA256.HashData(originalBytes));
        var service = CreateService();

        var result = service.UpdateText(
            "test",
            "format.txt",
            "alpha\nbeta\n",
            expectedHash);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("utf-8-bom", result.Value.Encoding);
        Assert.Equal("crlf", result.Value.LineEnding);
        Assert.NotNull(result.Value.BackupId);

        var updatedBytes = File.ReadAllBytes(path);
        Assert.True(updatedBytes.AsSpan().StartsWith(preamble));

        var updatedText = Encoding.UTF8.GetString(
            updatedBytes,
            preamble.Length,
            updatedBytes.Length - preamble.Length);
        Assert.Equal("alpha\r\nbeta\r\n", updatedText);

        var backupFiles = Directory
            .EnumerateFiles(
                Path.Combine(_stateRoot, "recovery", "updates", "test"),
                "*.bin",
                SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.Single(backupFiles);
        Assert.Equal(originalBytes, File.ReadAllBytes(backupFiles[0]));
    }

    [Fact]
    public void UpdateText_DryRun_DoesNotChangeFileOrCreateBackup()
    {
        var path = Path.Combine(_workspaceRoot, "dryrun.txt");
        File.WriteAllText(path, "before\n", new UTF8Encoding(false));

        var before = File.ReadAllBytes(path);
        var expectedHash = Convert.ToHexString(SHA256.HashData(before));
        var service = CreateService();

        var result = service.UpdateText(
            "test",
            "dryrun.txt",
            "after\n",
            expectedHash,
            dryRun: true);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.DryRun);
        Assert.Equal(before, File.ReadAllBytes(path));

        var recoveryRoot = Path.Combine(_stateRoot, "recovery", "updates");
        Assert.False(Directory.Exists(recoveryRoot));
    }

    [Fact]
    public void UpdateText_SimulatedWriterFailure_DoesNotTruncateDestination()
    {
        var path = Path.Combine(_workspaceRoot, "failure.txt");
        File.WriteAllText(path, "safe-original", new UTF8Encoding(false));

        var before = File.ReadAllBytes(path);
        var expectedHash = Convert.ToHexString(SHA256.HashData(before));

        var service = CreateService(new ThrowingAtomicFileWriter());
        var result = service.UpdateText(
            "test",
            "failure.txt",
            "replacement",
            expectedHash);

        Assert.False(result.Success);
        Assert.Equal(FileMutationError.AtomicWriteFailed, result.Error);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void CreateText_RejectsContentOverConfiguredLimit()
    {
        _configuration.Agent.Limits.MaxEditableFileBytes = 8;
        var service = CreateService();

        var result = service.CreateText(
            "test",
            "too-large.txt",
            "0123456789");

        Assert.False(result.Success);
        Assert.Equal(FileMutationError.FileTooLarge, result.Error);
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, "too-large.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseRoot))
        {
            Directory.Delete(_baseRoot, recursive: true);
        }
    }

    private WorkspaceMutationService CreateService(
        IAtomicFileWriter? atomicFileWriter = null)
    {
        IUserPathResolver pathResolver = new UserPathResolver();
        var registry = new WorkspaceRegistry(_configuration, pathResolver);
        var resolver = new WorkspaceResolver(registry);
        var permissions = new WorkspacePermissionEvaluator(_configuration);
        var denyMatcher = new DenyPathMatcher(
            AgentSecurityDefaults.GetEffectiveDenyGlobs(
                _configuration.Agent.Security.DenyGlobs));
        var inspector = new MacOsFileSystemEntryInspector();
        var policy = new WorkspacePathPolicy(
            resolver,
            permissions,
            denyMatcher,
            inspector);

        IFileHasher hasher = new Sha256FileHasher();
        IUpdateBackupStore backupStore = new UpdateBackupStore(
            _configuration,
            pathResolver);

        return new WorkspaceMutationService(
            policy,
            _configuration,
            hasher,
            atomicFileWriter ?? new AtomicFileWriter(),
            backupStore);
    }

    private static AgentConfiguration CreateConfiguration(
        string workspaceRoot,
        string stateRoot)
    {
        var configuration = new AgentConfiguration();
        configuration.Agent.StateDirectory = stateRoot;
        configuration.Agent.Limits.MaxEditableFileBytes = 1_048_576;
        configuration.Agent.Workspaces["test"] = new WorkspaceOptions
        {
            Root = workspaceRoot,
            Enabled = true,
            AllowedOperations = ["read", "create", "update", "move", "delete", "restore"]
        };

        return configuration;
    }

    private sealed class ThrowingAtomicFileWriter : IAtomicFileWriter
    {
        public void CreateNew(string destinationPath, byte[] content) =>
            throw new IOException("Simulated atomic create failure.");

        public void ReplaceExisting(
            string destinationPath,
            byte[] content,
            UnixFileMode? unixFileMode) =>
            throw new IOException("Simulated atomic replace failure.");
    }
}
