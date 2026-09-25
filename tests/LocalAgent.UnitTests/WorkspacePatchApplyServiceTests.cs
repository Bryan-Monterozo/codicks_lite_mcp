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

public sealed class WorkspacePatchApplyServiceTests : IDisposable
{
    private readonly string _baseRoot;
    private readonly string _workspaceRoot;
    private readonly string _otherRoot;
    private readonly string _stateRoot;
    private readonly AgentConfiguration _configuration;
    private readonly ManualTimeProvider _clock;

    public WorkspacePatchApplyServiceTests()
    {
        _baseRoot = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-v12-patch-apply-{Guid.NewGuid():N}");

        _workspaceRoot = Path.Combine(
            _baseRoot,
            "workspace");

        _otherRoot = Path.Combine(
            _baseRoot,
            "other");

        _stateRoot = Path.Combine(
            _baseRoot,
            "state");

        Directory.CreateDirectory(
            _workspaceRoot);

        Directory.CreateDirectory(
            _otherRoot);

        Directory.CreateDirectory(
            _stateRoot);

        _configuration =
            CreateConfiguration(
                _workspaceRoot,
                _otherRoot,
                _stateRoot);

        _clock =
            new ManualTimeProvider(
                new DateTimeOffset(
                    2026,
                    9,
                    24,
                    9,
                    0,
                    0,
                    TimeSpan.Zero));
    }

    [Fact]
    public void PreviewThenApply_UsesBackupAndConsumesToken()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "sample.txt",
                "one\ntwo\nthree");

        var services =
            CreateServices();

        var patch =
            ReplacementPatch(
                "sample.txt",
                "two",
                "changed",
                oldStart: 2);

        var preview =
            services.Preview.Preview(
                "test",
                "sample.txt",
                patch,
                HashFile(path));

        Assert.True(
            preview.Success,
            preview.Message);

        Assert.NotNull(
            preview.Value);

        Assert.True(
            preview.Value.CanApply);

        Assert.False(
            string.IsNullOrWhiteSpace(
                preview.Value.ReviewToken));

        Assert.NotNull(
            preview.Value.ReviewExpiresAtUtc);

        var result =
            services.Apply.Apply(
                "test",
                "sample.txt",
                patch: null,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!);

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        Assert.NotNull(
            result.Value.BackupId);

        Assert.Equal(
            "one\nchanged\nthree",
            File.ReadAllText(path));

        Assert.Equal(
            preview.Value.ProposedSha256,
            result.Value.Sha256);

        Assert.Equal(
            FilePatchReviewValidationError.ConsumedToken,
            services.Store.Validate(
                preview.Value.ReviewToken).Error);

        var backupRoot =
            Path.Combine(
                _stateRoot,
                "recovery",
                "updates",
                "test");

        Assert.True(
            Directory.Exists(
                backupRoot));

        Assert.Single(
            Directory.EnumerateFiles(
                backupRoot,
                "*.bin",
                SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ApplyWithoutPreview_IsRejected()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "sample.txt",
                "one");

        var services =
            CreateServices();

        var patch =
            ReplacementPatch(
                "sample.txt",
                "one",
                "changed",
                1);

        var missing =
            services.Apply.Apply(
                "test",
                "sample.txt",
                patch,
                HashFile(path),
                string.Empty);

        var unknown =
            services.Apply.Apply(
                "test",
                "sample.txt",
                patch,
                HashFile(path),
                "unknown-token");

        Assert.Equal(
            FileMutationError.ReviewRequired,
            missing.Error);

        Assert.Equal(
            FileMutationError.InvalidReviewToken,
            unknown.Error);

        Assert.Equal(
            "one",
            File.ReadAllText(path));
    }

    [Fact]
    public void ExpiredAndReusedTokens_AreRejected()
    {
        var expiredPath =
            WriteFile(
                _workspaceRoot,
                "expired.txt",
                "one");

        var services =
            CreateServices();

        var expiredPatch =
            ReplacementPatch(
                "expired.txt",
                "one",
                "changed",
                1);

        var expiredPreview =
            services.Preview.Preview(
                "test",
                "expired.txt",
                expiredPatch,
                HashFile(expiredPath));

        Assert.NotNull(
            expiredPreview.Value);

        _clock.Advance(
            TimeSpan.FromMinutes(11));

        var expired =
            services.Apply.Apply(
                "test",
                "expired.txt",
                expiredPatch,
                expiredPreview.Value.BaseSha256,
                expiredPreview.Value.ReviewToken!);

        Assert.Equal(
            FileMutationError.ExpiredReviewToken,
            expired.Error);

        var reusedPath =
            WriteFile(
                _workspaceRoot,
                "reused.txt",
                "one");

        var reusedPatch =
            ReplacementPatch(
                "reused.txt",
                "one",
                "changed",
                1);

        var reusedPreview =
            services.Preview.Preview(
                "test",
                "reused.txt",
                reusedPatch,
                HashFile(reusedPath));

        Assert.NotNull(
            reusedPreview.Value);

        var first =
            services.Apply.Apply(
                "test",
                "reused.txt",
                reusedPatch,
                reusedPreview.Value.BaseSha256,
                reusedPreview.Value.ReviewToken!);

        Assert.True(
            first.Success,
            first.Message);

        var second =
            services.Apply.Apply(
                "test",
                "reused.txt",
                reusedPatch,
                reusedPreview.Value.BaseSha256,
                reusedPreview.Value.ReviewToken!);

        Assert.Equal(
            FileMutationError.ConsumedReviewToken,
            second.Error);
    }

    [Fact]
    public void ModifiedPatchPathWorkspaceOrBase_AreRejected()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "sample.txt",
                "one");

        WriteFile(
            _workspaceRoot,
            "other.txt",
            "one");

        WriteFile(
            _otherRoot,
            "sample.txt",
            "one");

        var services =
            CreateServices();

        var patch =
            ReplacementPatch(
                "sample.txt",
                "one",
                "changed",
                1);

        var preview =
            services.Preview.Preview(
                "test",
                "sample.txt",
                patch,
                HashFile(path));

        Assert.NotNull(
            preview.Value);

        var modifiedPatch =
            ReplacementPatch(
                "sample.txt",
                "one",
                "different",
                1);

        Assert.Equal(
            FileMutationError.InvalidReviewToken,
            services.Apply.Apply(
                "test",
                "sample.txt",
                modifiedPatch,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!).Error);

        Assert.Equal(
            FileMutationError.InvalidReviewToken,
            services.Apply.Apply(
                "test",
                "other.txt",
                ReplacementPatch(
                    "other.txt",
                    "one",
                    "changed",
                    1),
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!).Error);

        Assert.Equal(
            FileMutationError.InvalidReviewToken,
            services.Apply.Apply(
                "other",
                "sample.txt",
                patch,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!).Error);

        Assert.Equal(
            FileMutationError.InvalidReviewToken,
            services.Apply.Apply(
                "test",
                "sample.txt",
                patch,
                new string('0', 64),
                preview.Value.ReviewToken!).Error);

        Assert.Equal(
            "one",
            File.ReadAllText(path));
    }

    [Fact]
    public void DestinationModifiedAfterPreview_ReturnsConflict()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "sample.txt",
                "one");

        var services =
            CreateServices();

        var patch =
            ReplacementPatch(
                "sample.txt",
                "one",
                "changed",
                1);

        var preview =
            services.Preview.Preview(
                "test",
                "sample.txt",
                patch,
                HashFile(path));

        Assert.NotNull(
            preview.Value);

        File.WriteAllText(
            path,
            "external-change",
            new UTF8Encoding(false));

        var result =
            services.Apply.Apply(
                "test",
                "sample.txt",
                patch,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!);

        Assert.Equal(
            FileMutationError.Conflict,
            result.Error);

        Assert.Equal(
            "external-change",
            File.ReadAllText(path));

        Assert.True(
            services.Store.Validate(
                preview.Value.ReviewToken).Valid);
    }

    [Fact]
    public void WorkspaceWithoutUpdatePermission_IsRejected()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "sample.txt",
                "one");

        var services =
            CreateServices();

        var patch =
            ReplacementPatch(
                "sample.txt",
                "one",
                "changed",
                1);

        var preview =
            services.Preview.Preview(
                "test",
                "sample.txt",
                patch,
                HashFile(path));

        Assert.NotNull(
            preview.Value);

        _configuration.Agent.Workspaces[
            "test"].AllowedOperations =
            ["read"];

        services =
            CreateServices(
                services.Store);

        var result =
            services.Apply.Apply(
                "test",
                "sample.txt",
                patch,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!);

        Assert.Equal(
            FileMutationError.AccessDenied,
            result.Error);

        Assert.Equal(
            "one",
            File.ReadAllText(path));
    }

    [Fact]
    public void AtomicWriterFailure_DoesNotChangeFileOrConsumeToken()
    {
        var path =
            WriteFile(
                _workspaceRoot,
                "failure.txt",
                "safe");

        var sharedStore =
            new MemoryFilePatchReviewStore(
                _clock);

        var previewServices =
            CreateServices(
                sharedStore);

        var patch =
            ReplacementPatch(
                "failure.txt",
                "safe",
                "changed",
                1);

        var preview =
            previewServices.Preview.Preview(
                "test",
                "failure.txt",
                patch,
                HashFile(path));

        Assert.NotNull(
            preview.Value);

        var failingServices =
            CreateServices(
                sharedStore,
                new ThrowingAtomicFileWriter());

        var result =
            failingServices.Apply.Apply(
                "test",
                "failure.txt",
                patch,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!);

        Assert.Equal(
            FileMutationError.AtomicWriteFailed,
            result.Error);

        Assert.Equal(
            "safe",
            File.ReadAllText(path));

        Assert.True(
            sharedStore.Validate(
                preview.Value.ReviewToken).Valid);
    }

    [Fact]
    public void SuccessfulApply_PreservesUnixMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path =
            WriteFile(
                _workspaceRoot,
                "mode.txt",
                "one");

        var expectedMode =
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead;

        File.SetUnixFileMode(
            path,
            expectedMode);

        var services =
            CreateServices();

        var patch =
            ReplacementPatch(
                "mode.txt",
                "one",
                "changed",
                1);

        var preview =
            services.Preview.Preview(
                "test",
                "mode.txt",
                patch,
                HashFile(path));

        Assert.NotNull(
            preview.Value);

        var result =
            services.Apply.Apply(
                "test",
                "mode.txt",
                patch,
                preview.Value.BaseSha256,
                preview.Value.ReviewToken!);

        Assert.True(
            result.Success,
            result.Message);

        Assert.Equal(
            expectedMode,
            File.GetUnixFileMode(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(
                _baseRoot))
        {
            Directory.Delete(
                _baseRoot,
                recursive: true);
        }
    }

    private PatchServices CreateServices(
        IFilePatchReviewStore? reviewStore = null,
        IAtomicFileWriter? atomicFileWriter = null)
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

        var policy =
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

        var store =
            reviewStore ??
            new MemoryFilePatchReviewStore(
                _clock);

        IUpdateBackupStore backupStore =
            new UpdateBackupStore(
                _configuration,
                pathResolver);

        IWorkspaceMutationService mutationService =
            new WorkspaceMutationService(
                policy,
                _configuration,
                hasher,
                atomicFileWriter ??
                    new AtomicFileWriter(),
                backupStore);

        var previewService =
            new WorkspacePatchPreviewService(
                policy,
                _configuration,
                hasher,
                patchService,
                diffService,
                store);

        var applyService =
            new WorkspacePatchApplyService(
                policy,
                _configuration,
                hasher,
                patchService,
                diffService,
                store,
                mutationService);

        return new PatchServices(
            previewService,
            applyService,
            store);
    }

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

    private static string ReplacementPatch(
        string relativePath,
        string oldValue,
        string newValue,
        int oldStart) =>
        string.Join(
            '\n',
            $"--- a/{relativePath}",
            $"+++ b/{relativePath}",
            $"@@ -{oldStart},1 +{oldStart},1 @@",
            $"-{oldValue}",
            $"+{newValue}");

    private static AgentConfiguration CreateConfiguration(
        string workspaceRoot,
        string otherRoot,
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
                Root = workspaceRoot,
                Enabled = true,
                AllowedOperations =
                    ["read", "update"]
            };

        configuration.Agent.Workspaces[
            "other"] =
            new WorkspaceOptions
            {
                Root = otherRoot,
                Enabled = true,
                AllowedOperations =
                    ["read", "update"]
            };

        return configuration;
    }

    private sealed record PatchServices(
        WorkspacePatchPreviewService Preview,
        WorkspacePatchApplyService Apply,
        IFilePatchReviewStore Store);

    private sealed class ThrowingAtomicFileWriter :
        IAtomicFileWriter
    {
        public void CreateNew(
            string destinationPath,
            byte[] content) =>
            throw new IOException(
                "Simulated create failure.");

        public void ReplaceExisting(
            string destinationPath,
            byte[] content,
            UnixFileMode? unixFileMode) =>
            throw new IOException(
                "Simulated replace failure.");
    }

    private sealed class ManualTimeProvider(
        DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current =
            current;

        public override DateTimeOffset GetUtcNow() =>
            _current;

        public void Advance(
            TimeSpan duration) =>
            _current =
                _current.Add(
                    duration);
    }
}
