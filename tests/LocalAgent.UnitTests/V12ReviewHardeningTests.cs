using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Recovery;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class V12ReviewHardeningTests : IDisposable
{
    private readonly string _baseRoot;
    private readonly string _workspaceRoot;
    private readonly string _outsideRoot;
    private readonly string _stateRoot;
    private readonly AgentConfiguration _configuration;

    public V12ReviewHardeningTests()
    {
        _baseRoot = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-v12-hardening-{Guid.NewGuid():N}");

        _workspaceRoot = Path.Combine(
            _baseRoot,
            "workspace");

        _outsideRoot = Path.Combine(
            _baseRoot,
            "outside");

        _stateRoot = Path.Combine(
            _baseRoot,
            "state");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_outsideRoot);
        Directory.CreateDirectory(_stateRoot);

        _configuration =
            CreateConfiguration();
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void ReviewPaths_RejectTraversalDeniedPathAndSymlinkEscape()
    {
        var samplePath =
            WriteFile(
                _workspaceRoot,
                "sample.txt",
                "one");

        var envPath =
            WriteFile(
                _workspaceRoot,
                ".env",
                "SECRET=value");

        var outsidePath =
            WriteFile(
                _outsideRoot,
                "outside.txt",
                "outside");

        var linkPath =
            Path.Combine(
                _workspaceRoot,
                "outside-link.txt");

        File.CreateSymbolicLink(
            linkPath,
            outsidePath);

        var services =
            CreateServices();

        var traversal =
            services.Preview.Preview(
                "test",
                "../outside/outside.txt",
                HeaderOnlyPatch(
                    "outside/outside.txt"),
                HashFile(samplePath));

        var denied =
            services.Preview.Preview(
                "test",
                ".env",
                HeaderOnlyPatch(".env"),
                HashFile(envPath));

        var symlink =
            services.Preview.Preview(
                "test",
                "outside-link.txt",
                HeaderOnlyPatch(
                    "outside-link.txt"),
                HashFile(outsidePath));

        Assert.False(traversal.Success);
        Assert.Equal(
            FileQueryError.InvalidRequest,
            traversal.Error);

        Assert.False(denied.Success);
        Assert.Equal(
            FileQueryError.AccessDenied,
            denied.Error);

        Assert.False(symlink.Success);
        Assert.Equal(
            FileQueryError.AccessDenied,
            symlink.Error);

        Assert.Equal(
            "outside",
            File.ReadAllText(
                outsidePath));
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void ReviewInputs_RejectMalformedHashAndOversizedPatch()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "sample.txt",
                "one");

        var services =
            CreateServices();

        var malformedHash =
            services.Preview.Preview(
                "test",
                "sample.txt",
                HeaderOnlyPatch(
                    "sample.txt"),
                "not-a-hash");

        Assert.False(malformedHash.Success);
        Assert.Equal(
            FileQueryError.InvalidRequest,
            malformedHash.Error);

        _configuration.Agent.Limits.MaxEditableFileBytes =
            8;

        services =
            CreateServices();

        var hugePatch =
            string.Join(
                '\n',
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,1 +1,1 @@",
                "-one",
                $"+{new string('x', 70_000)}");

        var oversized =
            services.Preview.Preview(
                "test",
                "sample.txt",
                hugePatch,
                HashFile(path));

        Assert.False(oversized.Success);
        Assert.Equal(
            FileQueryError.PatchTooLarge,
            oversized.Error);
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void WorkspacePermissionChange_AfterPreviewDeniesApply()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "permission.txt",
                "one");

        var sharedStore =
            new MemoryFilePatchReviewStore(
                TimeProvider.System);

        var services =
            CreateServices(
                sharedStore);

        var patch =
            ReplacementPatch(
                "permission.txt",
                "one",
                "changed");

        var preview =
            services.Preview.Preview(
                "test",
                "permission.txt",
                patch,
                HashFile(path));

        Assert.True(
            preview.Success,
            preview.Message);

        Assert.NotNull(
            preview.Value);

        _configuration.Agent.Workspaces[
            "test"].AllowedOperations =
            ["read"];

        services =
            CreateServices(
                sharedStore);

        var apply =
            services.Apply.Apply(
                "test",
                "permission.txt",
                patch,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!);

        Assert.False(apply.Success);
        Assert.Equal(
            FileMutationError.AccessDenied,
            apply.Error);

        Assert.Equal(
            "one",
            File.ReadAllText(path));
    }

    [Fact]
    [Trait("Chunk", "09")]
    public async Task ConcurrentReviewedApplies_FromSameBase_ExactlyOneCommits()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "race.txt",
                "base");

        var services =
            CreateServices();

        var patch =
            ReplacementPatch(
                "race.txt",
                "base",
                "changed");

        var baseHash =
            HashFile(path);

        var firstPreview =
            services.Preview.Preview(
                "test",
                "race.txt",
                patch,
                baseHash);

        var secondPreview =
            services.Preview.Preview(
                "test",
                "race.txt",
                patch,
                baseHash);

        Assert.NotNull(firstPreview.Value);
        Assert.NotNull(secondPreview.Value);

        using var gate =
            new ManualResetEventSlim(
                initialState: false);

        var firstTask =
            Task.Run(
                () =>
                {
                    gate.Wait();

                    return services.Apply.Apply(
                        "test",
                        "race.txt",
                        patch,
                        baseHash,
                        firstPreview.Value.ReviewToken!);
                });

        var secondTask =
            Task.Run(
                () =>
                {
                    gate.Wait();

                    return services.Apply.Apply(
                        "test",
                        "race.txt",
                        patch,
                        baseHash,
                        secondPreview.Value.ReviewToken!);
                });

        gate.Set();

        var results =
            await Task.WhenAll(
                firstTask,
                secondTask);

        Assert.Equal(
            1,
            results.Count(
                result =>
                    result.Success));

        Assert.Equal(
            1,
            results.Count(
                result =>
                    !result.Success &&
                    result.Error ==
                        FileMutationError.Conflict));

        Assert.Equal(
            "changed",
            File.ReadAllText(path));

        var successfulIndex =
            Array.FindIndex(
                results,
                result =>
                    result.Success);

        var successfulToken =
            successfulIndex == 0
                ? firstPreview.Value.ReviewToken!
                : secondPreview.Value.ReviewToken!;

        var replay =
            services.Apply.Apply(
                "test",
                "race.txt",
                patch,
                baseHash,
                successfulToken);

        Assert.False(replay.Success);
        Assert.Equal(
            FileMutationError.ConsumedReviewToken,
            replay.Error);
    }

    [Fact]
    [Trait("Chunk", "09")]
    public void DisabledWorkspace_RejectsReviewOperations()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "disabled.txt",
                "one");

        _configuration.Agent.Workspaces[
            "test"].Enabled =
            false;

        var services =
            CreateServices();

        var preview =
            services.Preview.Preview(
                "test",
                "disabled.txt",
                HeaderOnlyPatch(
                    "disabled.txt"),
                HashFile(path));

        Assert.False(preview.Success);
        Assert.Equal(
            FileQueryError.AccessDenied,
            preview.Error);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(
                    _baseRoot))
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

    private ReviewServices CreateServices(
        IFilePatchReviewStore? sharedStore = null)
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

        IUnifiedPatchService patchService =
            new UnifiedPatchService();

        IFileDiffService diffService =
            new FileDiffService();

        var reviewStore =
            sharedStore ??
            new MemoryFilePatchReviewStore(
                TimeProvider.System);

        IUpdateBackupStore backupStore =
            new UpdateBackupStore(
                _configuration,
                pathResolver);

        IWorkspaceMutationService mutationService =
            new WorkspaceMutationService(
                pathPolicy,
                _configuration,
                hasher,
                new AtomicFileWriter(),
                backupStore);

        var preview =
            new WorkspacePatchPreviewService(
                pathPolicy,
                _configuration,
                hasher,
                patchService,
                diffService,
                reviewStore);

        var apply =
            new WorkspacePatchApplyService(
                pathPolicy,
                _configuration,
                hasher,
                patchService,
                diffService,
                reviewStore,
                mutationService);

        return new ReviewServices(
            preview,
            apply);
    }

    private AgentConfiguration CreateConfiguration()
    {
        var configuration =
            new AgentConfiguration();

        configuration.Agent.StateDirectory =
            _stateRoot;

        configuration.Agent.Limits.MaxEditableFileBytes =
            1_048_576;

        configuration.Agent.Workspaces[
            "test"] =
            new WorkspaceOptions
            {
                Root =
                    _workspaceRoot,
                Enabled = true,
                AllowedOperations =
                    ["read", "update"]
            };

        return configuration;
    }

    private static string HeaderOnlyPatch(
        string relativePath) =>
        string.Join(
            '\n',
            $"--- a/{relativePath}",
            $"+++ b/{relativePath}");

    private static string ReplacementPatch(
        string relativePath,
        string oldValue,
        string newValue) =>
        string.Join(
            '\n',
            $"--- a/{relativePath}",
            $"+++ b/{relativePath}",
            "@@ -1,1 +1,1 @@",
            $"-{oldValue}",
            $"+{newValue}");

    private static string WriteFile(
        string root,
        string relativePath,
        string content)
    {
        var path =
            Path.Combine(
                root,
                relativePath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(false));

        return path;
    }

    private static string HashFile(
        string path) =>
        Convert.ToHexString(
            SHA256.HashData(
                File.ReadAllBytes(
                    path)));

    private sealed record ReviewServices(
        WorkspacePatchPreviewService Preview,
        WorkspacePatchApplyService Apply);
}
