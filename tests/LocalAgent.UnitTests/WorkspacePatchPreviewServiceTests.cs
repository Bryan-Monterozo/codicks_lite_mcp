using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspacePatchPreviewServiceTests : IDisposable
{
    private readonly string _baseRoot;
    private readonly string _workspaceRoot;
    private readonly string _stateRoot;
    private readonly AgentConfiguration _configuration;

    public WorkspacePatchPreviewServiceTests()
    {
        _baseRoot = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-v12-patch-preview-{Guid.NewGuid():N}");

        _workspaceRoot = Path.Combine(
            _baseRoot,
            "workspace");

        _stateRoot = Path.Combine(
            _baseRoot,
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
    public void Preview_ValidPatch_ReturnsCanonicalDiffWithoutMutation()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "sample.txt");

        File.WriteAllText(
            path,
            "one\ntwo\nthree",
            new UTF8Encoding(false));

        var before =
            File.ReadAllBytes(path);

        var hash =
            Convert.ToHexString(
                SHA256.HashData(before));

        var result =
            CreateService().Preview(
                "test",
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt",
                    "@@ -1,3 +1,3 @@",
                    " one",
                    "-two",
                    "+changed",
                    " three"),
                hash);

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        Assert.True(
            result.Value.CanApply);

        Assert.Equal(
            hash,
            result.Value.BaseSha256);

        Assert.Equal(
            64,
            result.Value.PatchSha256.Length);

        Assert.Equal(
            1,
            result.Value.Additions);

        Assert.Equal(
            1,
            result.Value.Deletions);

        Assert.Contains(
            "-two",
            result.Value.UnifiedDiff,
            StringComparison.Ordinal);

        Assert.Contains(
            "+changed",
            result.Value.UnifiedDiff,
            StringComparison.Ordinal);

        Assert.Equal(
            before,
            File.ReadAllBytes(path));

        Assert.False(
            Directory.Exists(
                Path.Combine(
                    _stateRoot,
                    "recovery")));
    }

    [Fact]
    public void Preview_StaleHash_ReturnsConflict()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "stale.txt");

        File.WriteAllText(
            path,
            "current",
            new UTF8Encoding(false));

        var result =
            CreateService().Preview(
                "test",
                "stale.txt",
                Patch(
                    "--- a/stale.txt",
                    "+++ b/stale.txt"),
                new string('0', 64));

        Assert.False(result.Success);
        Assert.Equal(
            FileQueryError.Conflict,
            result.Error);
    }

    [Fact]
    public void Preview_MalformedHash_IsRejected()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "hash.txt");

        File.WriteAllText(
            path,
            "current",
            new UTF8Encoding(false));

        var result =
            CreateService().Preview(
                "test",
                "hash.txt",
                Patch(
                    "--- a/hash.txt",
                    "+++ b/hash.txt"),
                "bad-hash");

        Assert.False(result.Success);
        Assert.Equal(
            FileQueryError.InvalidRequest,
            result.Error);
    }

    [Fact]
    public void Preview_ContextMismatch_MapsToPatchConflict()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "context.txt");

        File.WriteAllText(
            path,
            "one\ntwo",
            new UTF8Encoding(false));

        var hash =
            HashFile(path);

        var result =
            CreateService().Preview(
                "test",
                "context.txt",
                Patch(
                    "--- a/context.txt",
                    "+++ b/context.txt",
                    "@@ -1,2 +1,2 @@",
                    " WRONG",
                    " two"),
                hash);

        Assert.False(result.Success);
        Assert.Equal(
            FileQueryError.PatchConflict,
            result.Error);
    }

    [Fact]
    public void Preview_WrongPatchTarget_MapsToInvalidPatch()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "target.txt");

        File.WriteAllText(
            path,
            "one",
            new UTF8Encoding(false));

        var result =
            CreateService().Preview(
                "test",
                "target.txt",
                Patch(
                    "--- a/other.txt",
                    "+++ b/other.txt",
                    "@@ -1,1 +1,1 @@",
                    "-one",
                    "+two"),
                HashFile(path));

        Assert.False(result.Success);
        Assert.Equal(
            FileQueryError.InvalidPatch,
            result.Error);
    }

    [Fact]
    public void Preview_HeadersOnlyNoOp_ReturnsCanApplyFalse()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "noop.txt");

        File.WriteAllText(
            path,
            "same\n",
            new UTF8Encoding(false));

        var result =
            CreateService().Preview(
                "test",
                "noop.txt",
                Patch(
                    "--- a/noop.txt",
                    "+++ b/noop.txt"),
                HashFile(path));

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        Assert.False(
            result.Value.CanApply);

        Assert.Empty(
            result.Value.UnifiedDiff);

        Assert.Contains(
            result.Value.Warnings,
            warning =>
                warning.Contains(
                    "no content changes",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preview_PreservesBomAndCrLfInProposedHash()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "format.txt");

        var originalContent =
            Encoding.UTF8.GetBytes(
                "one\r\ntwo\r\n");

        var preamble =
            Encoding.UTF8.GetPreamble();

        var originalBytes =
            new byte[
                preamble.Length +
                originalContent.Length];

        Buffer.BlockCopy(
            preamble,
            0,
            originalBytes,
            0,
            preamble.Length);

        Buffer.BlockCopy(
            originalContent,
            0,
            originalBytes,
            preamble.Length,
            originalContent.Length);

        File.WriteAllBytes(
            path,
            originalBytes);

        var result =
            CreateService().Preview(
                "test",
                "format.txt",
                Patch(
                    "--- a/format.txt",
                    "+++ b/format.txt",
                    "@@ -1,2 +1,2 @@",
                    " one",
                    "-two",
                    "+changed"),
                HashFile(path));

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        Assert.Equal(
            "utf-8-bom",
            result.Value.SourceEncoding);

        Assert.Equal(
            "crlf",
            result.Value.SourceLineEnding);

        Assert.Equal(
            "crlf",
            result.Value.ProposedLineEnding);

        var proposedContent =
            Encoding.UTF8.GetBytes(
                "one\r\nchanged\r\n");

        var expectedBytes =
            new byte[
                preamble.Length +
                proposedContent.Length];

        Buffer.BlockCopy(
            preamble,
            0,
            expectedBytes,
            0,
            preamble.Length);

        Buffer.BlockCopy(
            proposedContent,
            0,
            expectedBytes,
            preamble.Length,
            proposedContent.Length);

        Assert.Equal(
            Convert.ToHexString(
                SHA256.HashData(
                    expectedBytes)),
            result.Value.ProposedSha256);
    }

    [Fact]
    public void Preview_DeniedPath_IsRejected()
    {
        var path = Path.Combine(
            _workspaceRoot,
            ".env");

        File.WriteAllText(
            path,
            "SECRET=value",
            new UTF8Encoding(false));

        var result =
            CreateService().Preview(
                "test",
                ".env",
                Patch(
                    "--- a/.env",
                    "+++ b/.env"),
                HashFile(path));

        Assert.False(result.Success);
        Assert.Equal(
            FileQueryError.AccessDenied,
            result.Error);
    }

    [Fact]
    public void Preview_OversizedPatch_MapsToPatchTooLarge()
    {
        _configuration.Agent.Limits.MaxEditableFileBytes = 8;

        var path = Path.Combine(
            _workspaceRoot,
            "small.txt");

        File.WriteAllText(
            path,
            "one",
            new UTF8Encoding(false));

        var patch =
            Patch(
                "--- a/small.txt",
                "+++ b/small.txt",
                "@@ -1,1 +1,1 @@",
                "-one",
                $"+{new string('x', 70_000)}");

        var result =
            CreateService().Preview(
                "test",
                "small.txt",
                patch,
                HashFile(path));

        Assert.False(result.Success);
        Assert.Equal(
            FileQueryError.PatchTooLarge,
            result.Error);
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

    private WorkspacePatchPreviewService CreateService()
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

        return new WorkspacePatchPreviewService(
            policy,
            _configuration,
            hasher,
            patchService,
            diffService,
            new MemoryFilePatchReviewStore(
                TimeProvider.System));
    }

    private static string HashFile(
        string path) =>
        Convert.ToHexString(
            SHA256.HashData(
                File.ReadAllBytes(
                    path)));

    private static string Patch(
        params string[] lines) =>
        string.Join(
            '\n',
            lines);

    private static AgentConfiguration CreateConfiguration(
        string workspaceRoot,
        string stateRoot)
    {
        var configuration =
            new AgentConfiguration();

        configuration.Agent.StateDirectory =
            stateRoot;

        configuration.Agent.Limits.MaxEditableFileBytes =
            1_048_576;

        configuration.Agent.Workspaces[
            "test"] =
            new WorkspaceOptions
            {
                Root = workspaceRoot,
                Enabled = true,
                AllowedOperations =
                    ["read", "update"]
            };

        return configuration;
    }
}
