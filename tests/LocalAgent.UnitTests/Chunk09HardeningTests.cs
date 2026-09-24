using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Recovery;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class Chunk09HardeningTests : IDisposable
{
    private readonly string _baseRoot;
    private readonly string _workspaceRoot;
    private readonly string _outsideRoot;
    private readonly string _stateRoot;

    public Chunk09HardeningTests()
    {
        _baseRoot = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-chunk09-{Guid.NewGuid():N}");
        _workspaceRoot = Path.Combine(_baseRoot, "workspace");
        _outsideRoot = Path.Combine(_baseRoot, "outside");
        _stateRoot = Path.Combine(_baseRoot, "state");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_outsideRoot);
        Directory.CreateDirectory(_stateRoot);
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void ConfigurationValidator_RejectsInvalidLimitsAndMissingWorkspaceRoot()
    {
        var configuration = CreateConfiguration();
        configuration.Agent.Limits.MaxEditableFileBytes = 0;
        configuration.Agent.Limits.MaxReadResponseBytes = 1_024;
        configuration.Agent.Workspaces["test"].Root =
            Path.Combine(_baseRoot, "missing-workspace");

        var configDirectory = Path.Combine(_baseRoot, "config");
        Directory.CreateDirectory(configDirectory);
        var configFile = Path.Combine(configDirectory, "agent.json");

        var validator = new AgentConfigurationValidator(
            new UserPathResolver());

        var errors = validator.Validate(
            configuration,
            configFile);

        Assert.Contains(
            errors,
            error => error.Contains(
                "MaxEditableFileBytes",
                StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains(
                "root does not exist",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void UnsafeCreatePaths_AreDeniedWithoutOutsideSideEffects()
    {
        var configuration = CreateConfiguration();
        var service = CreateMutationService(configuration);

        var traversal = service.CreateText(
            "test",
            "../outside/owned.txt",
            "should-never-write");

        var absolute = service.CreateText(
            "test",
            Path.Combine(_outsideRoot, "absolute-owned.txt"),
            "should-never-write");

        var deniedSecret = service.CreateText(
            "test",
            ".env",
            "SECRET=should-never-write");

        Assert.False(traversal.Success);
        Assert.False(absolute.Success);
        Assert.False(deniedSecret.Success);
        Assert.False(File.Exists(Path.Combine(_outsideRoot, "owned.txt")));
        Assert.False(File.Exists(Path.Combine(_outsideRoot, "absolute-owned.txt")));
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, ".env")));
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void PermissionMatrix_DeniesDisabledWorkspaceAndGlobalReadOnlyMutation()
    {
        var disabledConfiguration = CreateConfiguration();
        disabledConfiguration.Agent.Workspaces["test"].Enabled = false;

        var query = CreateQueryService(disabledConfiguration);
        var inspect = query.InspectWorkspace("test");

        Assert.False(inspect.Success);
        Assert.Equal(
            WorkspaceAccessError.WorkspaceDisabled,
            inspect.AccessError);

        var readOnlyConfiguration = CreateConfiguration();
        readOnlyConfiguration.Agent.ReadOnly = true;

        var mutation = CreateMutationService(readOnlyConfiguration);
        var create = mutation.CreateText(
            "test",
            "blocked.txt",
            "blocked");

        Assert.False(create.Success);
        Assert.Equal(
            WorkspaceAccessError.GlobalReadOnly,
            create.AccessError);
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, "blocked.txt")));
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void TextFidelity_PreservesLfAndTrailingNewline_AndReadsEmptyFile()
    {
        var path = Path.Combine(_workspaceRoot, "lf.txt");
        var original = Encoding.UTF8.GetBytes("one\ntwo\n");
        File.WriteAllBytes(path, original);

        var configuration = CreateConfiguration();
        var mutation = CreateMutationService(configuration);
        var query = CreateQueryService(configuration);

        var expectedHash = Convert.ToHexString(
            SHA256.HashData(original));

        var update = mutation.UpdateText(
            "test",
            "lf.txt",
            "alpha\nbeta\n",
            expectedHash);

        Assert.True(update.Success, update.Message);
        Assert.NotNull(update.Value);
        Assert.Equal("lf", update.Value.LineEnding);
        Assert.Equal(
            Encoding.UTF8.GetBytes("alpha\nbeta\n"),
            File.ReadAllBytes(path));

        var emptyPath = Path.Combine(_workspaceRoot, "empty.txt");
        File.WriteAllBytes(emptyPath, []);

        var emptyRead = query.ReadText(
            "test",
            "empty.txt");

        Assert.True(emptyRead.Success, emptyRead.Message);
        Assert.NotNull(emptyRead.Value);
        Assert.Equal(string.Empty, emptyRead.Value.Content);
        Assert.False(emptyRead.Value.IsPartial);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())),
            emptyRead.Value.Sha256);
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void UnicodeWorkspacePaths_RoundTripThroughCreateAndRead()
    {
        var configuration = CreateConfiguration();
        var lifecycle = CreateLifecycleService(configuration);
        var mutation = CreateMutationService(configuration);
        var query = CreateQueryService(configuration);

        var directory = lifecycle.CreateDirectory(
            NewMutationId("unicode-dir"),
            "test",
            "日本語");

        Assert.True(directory.Success, directory.Message);

        const string relativePath = "日本語/mañana-文件.txt";
        const string content = "héllo 世界\n";

        var create = mutation.CreateText(
            "test",
            relativePath,
            content);

        Assert.True(create.Success, create.Message);

        var read = query.ReadText(
            "test",
            relativePath);

        Assert.True(read.Success, read.Message);
        Assert.NotNull(read.Value);
        Assert.Equal(content, read.Value.Content);
    }

    [Fact]
    [Trait("Chunk", "09")]
    public async Task ConcurrentUpdates_FromSameHash_ExactlyOneCommits()
    {
        var path = Path.Combine(_workspaceRoot, "concurrent-update.txt");
        File.WriteAllText(
            path,
            "base",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var configuration = CreateConfiguration();
        var service = CreateMutationService(configuration);
        var expectedHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(path)));

        using var gate = new ManualResetEventSlim(initialState: false);

        var firstTask = Task.Run(() =>
        {
            gate.Wait();
            return service.UpdateText(
                "test",
                "concurrent-update.txt",
                "first",
                expectedHash);
        });

        var secondTask = Task.Run(() =>
        {
            gate.Wait();
            return service.UpdateText(
                "test",
                "concurrent-update.txt",
                "second",
                expectedHash);
        });

        gate.Set();

        var results = await Task.WhenAll(
            firstTask,
            secondTask);

        Assert.Equal(
            1,
            results.Count(result => result.Success));
        Assert.Equal(
            1,
            results.Count(result =>
                !result.Success &&
                result.Error == FileMutationError.Conflict));

        var finalContent = File.ReadAllText(path);
        Assert.True(
            finalContent is "first" or "second");
    }

    [Fact]
    [Trait("Chunk", "09")]
    public async Task ConcurrentCreates_ToSameDestination_ExactlyOneCommits()
    {
        var configuration = CreateConfiguration();
        var service = CreateMutationService(configuration);

        using var gate = new ManualResetEventSlim(initialState: false);

        var firstTask = Task.Run(() =>
        {
            gate.Wait();
            return service.CreateText(
                "test",
                "race-create.txt",
                "first");
        });

        var secondTask = Task.Run(() =>
        {
            gate.Wait();
            return service.CreateText(
                "test",
                "race-create.txt",
                "second");
        });

        gate.Set();

        var results = await Task.WhenAll(
            firstTask,
            secondTask);

        Assert.Equal(
            1,
            results.Count(result => result.Success));
        Assert.Equal(
            1,
            results.Count(result =>
                !result.Success &&
                result.Error == FileMutationError.AlreadyExists));

        var finalContent = File.ReadAllText(
            Path.Combine(_workspaceRoot, "race-create.txt"));
        Assert.True(
            finalContent is "first" or "second");
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void ExternalFileChange_BetweenReadAndUpdate_IsRejected()
    {
        var path = Path.Combine(_workspaceRoot, "external-change.txt");
        File.WriteAllText(
            path,
            "version-one",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var expectedHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(path)));

        File.WriteAllText(
            path,
            "external-version",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var configuration = CreateConfiguration();
        var service = CreateMutationService(configuration);

        var result = service.UpdateText(
            "test",
            "external-change.txt",
            "agent-version",
            expectedHash);

        Assert.False(result.Success);
        Assert.Equal(
            FileMutationError.Conflict,
            result.Error);
        Assert.Equal(
            "external-version",
            File.ReadAllText(path));
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void CompletedMutationReceipt_ReplaysAcrossServiceRestart()
    {
        var path = Path.Combine(_workspaceRoot, "restart-replay.txt");
        File.WriteAllText(
            path,
            "restart-safe",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var configuration = CreateConfiguration();
        var mutationId = NewMutationId("delete-restart");

        var firstService = CreateLifecycleService(configuration);
        var first = firstService.DeleteFile(
            mutationId,
            "test",
            "restart-replay.txt");

        Assert.True(first.Success, first.Message);
        Assert.NotNull(first.Value);
        Assert.False(File.Exists(path));

        var secondService = CreateLifecycleService(configuration);
        var replay = secondService.DeleteFile(
            mutationId,
            "test",
            "restart-replay.txt");

        Assert.True(replay.Success, replay.Message);
        Assert.NotNull(replay.Value);
        Assert.True(replay.Value.Replayed);
        Assert.Equal(
            first.Value.RecoveryId,
            replay.Value.RecoveryId);
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void WorkspaceRoot_DeleteIsRejected_AndRootRemains()
    {
        var configuration = CreateConfiguration();
        var service = CreateLifecycleService(configuration);

        var result = service.DeleteFile(
            NewMutationId("root-delete"),
            "test",
            string.Empty);

        Assert.False(result.Success);
        Assert.True(Directory.Exists(_workspaceRoot));
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void Search_DoesNotReturnDeniedCredentialFile()
    {
        const string secret = "chunk09-denied-secret-7d824";

        File.WriteAllText(
            Path.Combine(_workspaceRoot, ".env"),
            $"PASSWORD={secret}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Directory.CreateDirectory(
            Path.Combine(_workspaceRoot, "src"));
        File.WriteAllText(
            Path.Combine(_workspaceRoot, "src", "visible.txt"),
            $"public {secret}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var configuration = CreateConfiguration();
        var query = CreateQueryService(configuration);

        var result = query.Search(
            "test",
            secret);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Matches);
        Assert.Equal(
            "src/visible.txt",
            result.Value.Matches[0].RelativePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_baseRoot))
            {
                Directory.Delete(
                    _baseRoot,
                    recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
        }
    }

    private AgentConfiguration CreateConfiguration(
        bool readOnly = false,
        bool enabled = true,
        IReadOnlyList<string>? allowedOperations = null)
    {
        var configuration = new AgentConfiguration();
        configuration.Agent.ReadOnly = readOnly;
        configuration.Agent.StateDirectory = _stateRoot;
        configuration.Agent.Search.ExcludeGlobs =
        [
            "**/build/**",
            "**/obj/**"
        ];
        configuration.Agent.Limits.MaxEditableFileBytes = 1_048_576;
        configuration.Agent.Limits.MaxReadResponseBytes = 4_096;
        configuration.Agent.Limits.MaxDirectoryEntries = 100;
        configuration.Agent.Limits.MaxSearchResults = 100;
        configuration.Agent.Limits.MaxTraversalDepth = 16;
        configuration.Agent.Limits.OperationTimeoutSeconds = 10;
        configuration.Agent.Workspaces["test"] = new WorkspaceOptions
        {
            Root = _workspaceRoot,
            Enabled = enabled,
            AllowedOperations = allowedOperations?.ToList() ??
            [
                "read",
                "create",
                "update",
                "move",
                "delete",
                "restore"
            ]
        };

        return configuration;
    }

    private static WorkspacePathPolicy CreatePathPolicy(
        AgentConfiguration configuration)
    {
        IUserPathResolver pathResolver = new UserPathResolver();
        var registry = new WorkspaceRegistry(
            configuration,
            pathResolver);
        var resolver = new WorkspaceResolver(registry);
        var permissions = new WorkspacePermissionEvaluator(
            configuration);
        var denyMatcher = new DenyPathMatcher(
            AgentSecurityDefaults.GetEffectiveDenyGlobs(
                configuration.Agent.Security.DenyGlobs));
        var inspector = new MacOsFileSystemEntryInspector();

        return new WorkspacePathPolicy(
            resolver,
            permissions,
            denyMatcher,
            inspector);
    }

    private static WorkspaceQueryService CreateQueryService(
        AgentConfiguration configuration) =>
        new(
            CreatePathPolicy(configuration),
            configuration);

    private static WorkspaceMutationService CreateMutationService(
        AgentConfiguration configuration)
    {
        IUserPathResolver pathResolver = new UserPathResolver();

        return new WorkspaceMutationService(
            CreatePathPolicy(configuration),
            configuration,
            new Sha256FileHasher(),
            new AtomicFileWriter(),
            new UpdateBackupStore(
                configuration,
                pathResolver));
    }

    private static WorkspaceLifecycleService CreateLifecycleService(
        AgentConfiguration configuration)
    {
        IUserPathResolver pathResolver = new UserPathResolver();
        var registry = new WorkspaceRegistry(
            configuration,
            pathResolver);
        var resolver = new WorkspaceResolver(registry);
        var entryInspector = new MacOsFileSystemEntryInspector();
        var locationResolver = new WorkspaceRecoveryLocationResolver(
            new MacOsFileSystemDeviceInspector(),
            entryInspector);

        return new WorkspaceLifecycleService(
            CreatePathPolicy(configuration),
            resolver,
            new Sha256FileHasher(),
            new FileRecoveryStore(
                configuration,
                pathResolver,
                locationResolver),
            new JsonMutationReceiptStore(
                configuration,
                pathResolver));
    }

    private static string NewMutationId(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}";
}
