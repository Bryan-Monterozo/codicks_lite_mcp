using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspaceQueryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly AgentConfiguration _configuration;
    private readonly WorkspaceQueryService _service;

    public WorkspaceQueryServiceTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-chunk04-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _configuration = CreateConfiguration(_root);
        _service = CreateService(_configuration);
    }

    [Fact]
    public void InspectWorkspace_ReturnsConfiguredWorkspace()
    {
        var result = _service.InspectWorkspace("test");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("test", result.Value.Id);
        Assert.Equal(_root, result.Value.Root);
        Assert.Equal(FileSystemEntryKind.Directory, result.Value.RootEntry.Kind);
        Assert.Contains(WorkspaceOperation.Read, result.Value.AllowedOperations);
    }

    [Fact]
    public void ListDirectory_ExcludesDeniedEntries_AndPaginates()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "A", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "b.txt"), "B", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "c.txt"), "C", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, ".env"), "SECRET=1", Encoding.UTF8);

        var first = _service.ListDirectory("test", string.Empty, offset: 0, limit: 2);

        Assert.True(first.Success, first.Message);
        Assert.NotNull(first.Value);
        Assert.Equal(2, first.Value.Entries.Count);
        Assert.DoesNotContain(first.Value.Entries, entry => entry.RelativePath == ".env");
        Assert.True(first.Value.IsPartial);
        Assert.Equal(2, first.Value.NextOffset);

        var second = _service.ListDirectory(
            "test",
            string.Empty,
            offset: first.Value.NextOffset!.Value,
            limit: 2);

        Assert.True(second.Success, second.Message);
        Assert.NotNull(second.Value);
        Assert.Single(second.Value.Entries);
        Assert.Equal("c.txt", second.Value.Entries[0].RelativePath);
        Assert.False(second.Value.IsPartial);
        Assert.Null(second.Value.NextOffset);
    }

    [Fact]
    public void ReadText_ReturnsHash_AndSafeContinuation()
    {
        var path = Path.Combine(_root, "large.txt");
        var content = string.Concat(Enumerable.Repeat("Hello-é-世界|", 40));
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _configuration.Agent.Limits.MaxReadResponseBytes = 48;
        var service = CreateService(_configuration);

        var first = service.ReadText("test", "large.txt");

        Assert.True(first.Success, first.Message);
        Assert.NotNull(first.Value);
        Assert.True(first.Value.IsPartial);
        Assert.NotNull(first.Value.NextByteOffset);
        Assert.True(Encoding.UTF8.GetByteCount(first.Value.Content) <= 48);

        var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(expectedHash, first.Value.Sha256);

        var second = service.ReadText(
            "test",
            "large.txt",
            first.Value.NextByteOffset!.Value);

        Assert.True(second.Success, second.Message);
        Assert.NotNull(second.Value);
        Assert.Equal(first.Value.NextByteOffset, second.Value.ByteOffset);
    }

    [Fact]
    public void Search_ReturnsLineNumbers_AndHonorsSearchExclusions()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "build"));

        File.WriteAllLines(
            Path.Combine(_root, "src", "main.cs"),
            ["first", "needle is here", "third"],
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(_root, "build", "generated.cs"),
            "needle should not appear",
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(_root, ".env"),
            "needle=secret",
            Encoding.UTF8);

        var result = _service.Search("test", "needle");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Matches);
        Assert.Equal("src/main.cs", result.Value.Matches[0].RelativePath);
        Assert.Equal(2, result.Value.Matches[0].LineNumber);
        Assert.Contains("needle", result.Value.Matches[0].Excerpt);
    }

    [Fact]
    public void ReadText_RejectsNonUtf8BinaryContent()
    {
        File.WriteAllBytes(
            Path.Combine(_root, "binary.bin"),
            [0x00, 0x01, 0x02, 0xFF, 0x10]);

        var result = _service.ReadText("test", "binary.bin");

        Assert.False(result.Success);
        Assert.Equal(FileQueryError.UnsupportedTextEncoding, result.Error);
    }

    [Fact]
    public void Stat_ReturnsRegularFileMetadata()
    {
        File.WriteAllText(Path.Combine(_root, "info.txt"), "metadata", Encoding.UTF8);

        var result = _service.Stat("test", "info.txt");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("info.txt", result.Value.RelativePath);
        Assert.Equal(FileSystemEntryKind.RegularFile, result.Value.Kind);
        Assert.True(result.Value.SizeBytes > 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static AgentConfiguration CreateConfiguration(string root)
    {
        var configuration = new AgentConfiguration();
        configuration.Agent.Workspaces["test"] = new WorkspaceOptions
        {
            Root = root,
            Enabled = true,
            AllowedOperations = ["read", "create", "update", "move", "delete", "restore"]
        };

        configuration.Agent.Search.ExcludeGlobs = ["**/build/**"];
        configuration.Agent.Limits.MaxDirectoryEntries = 2;
        configuration.Agent.Limits.MaxSearchResults = 20;
        configuration.Agent.Limits.MaxTraversalDepth = 8;
        configuration.Agent.Limits.MaxReadResponseBytes = 4_096;
        configuration.Agent.Limits.OperationTimeoutSeconds = 5;

        return configuration;
    }

    private static WorkspaceQueryService CreateService(AgentConfiguration configuration)
    {
        IUserPathResolver pathResolver = new UserPathResolver();
        var registry = new WorkspaceRegistry(configuration, pathResolver);
        var resolver = new WorkspaceResolver(registry);
        var permissions = new WorkspacePermissionEvaluator(configuration);
        var denyMatcher = new DenyPathMatcher(
            AgentSecurityDefaults.GetEffectiveDenyGlobs(configuration.Agent.Security.DenyGlobs));
        var inspector = new MacOsFileSystemEntryInspector();
        var policy = new WorkspacePathPolicy(
            resolver,
            permissions,
            denyMatcher,
            inspector);

        return new WorkspaceQueryService(policy, configuration);
    }
}
