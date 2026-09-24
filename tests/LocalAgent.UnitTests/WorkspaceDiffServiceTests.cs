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

public sealed class WorkspaceDiffServiceTests : IDisposable
{
    private readonly string _baseRoot;
    private readonly string _workspaceRoot;
    private readonly string _stateRoot;
    private readonly AgentConfiguration _configuration;

    public WorkspaceDiffServiceTests()
    {
        _baseRoot = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-v12-diff-{Guid.NewGuid():N}");

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
    public void DiffText_ReturnsCanonicalDiff_WithoutChangingFile()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "sample.txt");

        File.WriteAllText(
            path,
            "one\ntwo\n",
            new UTF8Encoding(false));

        var before =
            File.ReadAllBytes(path);

        var expectedHash =
            Convert.ToHexString(
                SHA256.HashData(before));

        var result =
            CreateService().DiffText(
                "test",
                "sample.txt",
                "one\nchanged\n",
                expectedHash);

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        Assert.True(
            result.Value.HasChanges);

        Assert.Equal(
            expectedHash,
            result.Value.BaseSha256);

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
    }

    [Fact]
    public void DiffText_StaleExpectedHash_ReturnsConflict()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "conflict.txt");

        File.WriteAllText(
            path,
            "current",
            new UTF8Encoding(false));

        var result =
            CreateService().DiffText(
                "test",
                "conflict.txt",
                "proposed",
                new string('0', 64));

        Assert.False(
            result.Success);

        Assert.Equal(
            FileQueryError.Conflict,
            result.Error);
    }

    [Fact]
    public void DiffText_InvalidExpectedHash_IsRejected()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "hash.txt");

        File.WriteAllText(
            path,
            "current",
            new UTF8Encoding(false));

        var result =
            CreateService().DiffText(
                "test",
                "hash.txt",
                "proposed",
                "not-a-hash");

        Assert.False(
            result.Success);

        Assert.Equal(
            FileQueryError.InvalidRequest,
            result.Error);
    }

    [Fact]
    public void DiffText_NoChange_ReturnsNoChanges()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "same.txt");

        File.WriteAllText(
            path,
            "same\n",
            new UTF8Encoding(false));

        var result =
            CreateService().DiffText(
                "test",
                "same.txt",
                "same\n");

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        Assert.False(
            result.Value.HasChanges);

        Assert.Empty(
            result.Value.UnifiedDiff);
    }

    [Fact]
    public void DiffText_PreservesUtf8BomAndExistingLineEndings()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "format.txt");

        var content =
            Encoding.UTF8.GetBytes(
                "one\r\ntwo\r\n");

        var preamble =
            Encoding.UTF8.GetPreamble();

        var bytes =
            new byte[
                preamble.Length +
                content.Length];

        Buffer.BlockCopy(
            preamble,
            0,
            bytes,
            0,
            preamble.Length);

        Buffer.BlockCopy(
            content,
            0,
            bytes,
            preamble.Length,
            content.Length);

        File.WriteAllBytes(
            path,
            bytes);

        var result =
            CreateService().DiffText(
                "test",
                "format.txt",
                "alpha\nbeta\n");

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

        Assert.Contains(
            result.Value.Warnings,
            warning =>
                warning.Contains(
                    "normalized",
                    StringComparison.OrdinalIgnoreCase));

        var expectedProposed =
            new byte[
                preamble.Length +
                Encoding.UTF8.GetByteCount(
                    "alpha\r\nbeta\r\n")];

        Buffer.BlockCopy(
            preamble,
            0,
            expectedProposed,
            0,
            preamble.Length);

        var proposedContent =
            Encoding.UTF8.GetBytes(
                "alpha\r\nbeta\r\n");

        Buffer.BlockCopy(
            proposedContent,
            0,
            expectedProposed,
            preamble.Length,
            proposedContent.Length);

        Assert.Equal(
            Convert.ToHexString(
                SHA256.HashData(
                    expectedProposed)),
            result.Value.ProposedSha256);
    }

    [Fact]
    public void DiffText_DeniedPath_IsRejected()
    {
        File.WriteAllText(
            Path.Combine(
                _workspaceRoot,
                ".env"),
            "SECRET=value",
            new UTF8Encoding(false));

        var result =
            CreateService().DiffText(
                "test",
                ".env",
                "SECRET=changed");

        Assert.False(
            result.Success);

        Assert.Equal(
            FileQueryError.AccessDenied,
            result.Error);
    }

    [Fact]
    public void DiffText_MissingFileAndDirectory_AreRejected()
    {
        Directory.CreateDirectory(
            Path.Combine(
                _workspaceRoot,
                "folder"));

        var service =
            CreateService();

        var missing =
            service.DiffText(
                "test",
                "missing.txt",
                "content");

        var directory =
            service.DiffText(
                "test",
                "folder",
                "content");

        Assert.Equal(
            FileQueryError.NotFound,
            missing.Error);

        Assert.Equal(
            FileQueryError.NotAFile,
            directory.Error);
    }

    [Fact]
    public void DiffText_DisabledWorkspace_IsRejected()
    {
        _configuration.Agent.Workspaces[
            "test"].Enabled = false;

        var result =
            CreateService().DiffText(
                "test",
                "sample.txt",
                "content");

        Assert.False(
            result.Success);

        Assert.Equal(
            FileQueryError.AccessDenied,
            result.Error);
    }

    [Fact]
    public void DiffText_ExistingOrProposedOversize_IsRejected()
    {
        _configuration.Agent.Limits.MaxEditableFileBytes = 8;

        var existingPath = Path.Combine(
            _workspaceRoot,
            "large.txt");

        File.WriteAllText(
            existingPath,
            "0123456789",
            new UTF8Encoding(false));

        var service =
            CreateService();

        var existing =
            service.DiffText(
                "test",
                "large.txt",
                "tiny");

        Assert.Equal(
            FileQueryError.FileTooLarge,
            existing.Error);

        File.WriteAllText(
            existingPath,
            "tiny",
            new UTF8Encoding(false));

        service =
            CreateService();

        var proposed =
            service.DiffText(
                "test",
                "large.txt",
                "0123456789");

        Assert.Equal(
            FileQueryError.FileTooLarge,
            proposed.Error);
    }

    [Fact]
    public void DiffText_LargeDiff_IsBoundedAndMarkedTruncated()
    {
        var path = Path.Combine(
            _workspaceRoot,
            "many-lines.txt");

        var current =
            string.Join(
                '\n',
                Enumerable.Range(
                    0,
                    2_500)
                    .Select(
                        value =>
                            $"old-{value:D4}-{new string('x', 24)}"));

        var proposed =
            string.Join(
                '\n',
                Enumerable.Range(
                    0,
                    2_500)
                    .Select(
                        value =>
                            $"new-{value:D4}-{new string('y', 24)}"));

        File.WriteAllText(
            path,
            current,
            new UTF8Encoding(false));

        var result =
            CreateService().DiffText(
                "test",
                "many-lines.txt",
                proposed);

        Assert.True(
            result.Success,
            result.Message);

        Assert.NotNull(
            result.Value);

        Assert.True(
            result.Value.Truncated);

        Assert.True(
            result.Value.UnifiedDiff.Length <=
            65_536);

        Assert.True(
            result.Value.Hunks.Sum(
                hunk =>
                    hunk.Lines.Count) <=
            2_000);
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

    private WorkspaceDiffService CreateService()
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

        IFileDiffService diffService =
            new FileDiffService();

        return new WorkspaceDiffService(
            policy,
            _configuration,
            hasher,
            diffService);
    }

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
