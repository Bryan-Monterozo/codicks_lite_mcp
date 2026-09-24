using System.Diagnostics;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WorkspacePathPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk03-policy-{Guid.NewGuid():N}");

    private readonly string _workspaceRoot;
    private readonly string _outsideRoot;

    public WorkspacePathPolicyTests()
    {
        _workspaceRoot = Path.Combine(_root, "workspace");
        _outsideRoot = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_outsideRoot);
    }

    [Fact]
    public void ValidateExisting_AllowsOrdinaryRegularFile()
    {
        File.WriteAllText(Path.Combine(_workspaceRoot, "readme.txt"), "hello");
        var policy = CreatePolicy();

        var result = policy.ValidateExisting("demo", "readme.txt", WorkspaceOperation.Read);

        Assert.True(result.Allowed, result.Message);
        Assert.Equal(FileSystemEntryKind.RegularFile, result.EntryKind);
    }

    [Theory]
    [InlineData("../outside/secret.txt")]
    [InlineData("folder/../../outside.txt")]
    public void ValidateExisting_RejectsTraversal(string path)
    {
        var policy = CreatePolicy();

        var result = policy.ValidateExisting("demo", path, WorkspaceOperation.Read);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.InvalidRelativePath, result.Error);
    }

    [Fact]
    public void ValidateExisting_RejectsAbsolutePath()
    {
        var policy = CreatePolicy();

        var result = policy.ValidateExisting("demo", "/tmp/secret.txt", WorkspaceOperation.Read);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.InvalidRelativePath, result.Error);
    }

    [Fact]
    public void ValidateExisting_RejectsDeniedPath()
    {
        File.WriteAllText(Path.Combine(_workspaceRoot, ".env"), "SECRET=value");
        var policy = CreatePolicy();

        var result = policy.ValidateExisting("demo", ".env", WorkspaceOperation.Read);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.PathDeniedByPolicy, result.Error);
    }

    [Fact]
    public void ValidateExisting_RejectsSymbolicLinkTraversal()
    {
        var outsideFile = Path.Combine(_outsideRoot, "secret.txt");
        File.WriteAllText(outsideFile, "outside");
        File.CreateSymbolicLink(Path.Combine(_workspaceRoot, "link.txt"), outsideFile);
        var policy = CreatePolicy();

        var result = policy.ValidateExisting("demo", "link.txt", WorkspaceOperation.Read);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.SymbolicLinkNotAllowed, result.Error);
    }

    [Fact]
    public void ValidateExisting_RejectsMultiplyLinkedRegularFile()
    {
        var original = Path.Combine(_workspaceRoot, "original.txt");
        var alias = Path.Combine(_workspaceRoot, "alias.txt");
        File.WriteAllText(original, "same inode");
        RunProcess("/bin/ln", original, alias);
        var policy = CreatePolicy();

        var result = policy.ValidateExisting("demo", "alias.txt", WorkspaceOperation.Read);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.HardLinkNotAllowed, result.Error);
    }

    [Fact]
    public void ValidateCreate_AllowsMissingTargetWithSafeExistingParent()
    {
        var parent = Path.Combine(_workspaceRoot, "src");
        Directory.CreateDirectory(parent);
        var policy = CreatePolicy();

        var result = policy.ValidateCreate("demo", "src/new-file.txt", WorkspaceOperation.Create);

        Assert.True(result.Allowed, result.Message);
        Assert.Equal(FileSystemEntryKind.Missing, result.EntryKind);
    }

    [Fact]
    public void ValidateCreate_RejectsSymbolicLinkInParentChain()
    {
        var outsideDirectory = Path.Combine(_outsideRoot, "target");
        Directory.CreateDirectory(outsideDirectory);
        Directory.CreateSymbolicLink(Path.Combine(_workspaceRoot, "linked"), outsideDirectory);
        var policy = CreatePolicy();

        var result = policy.ValidateCreate("demo", "linked/new.txt", WorkspaceOperation.Create);

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.SymbolicLinkNotAllowed, result.Error);
    }

    [Fact]
    public void ValidateMove_ValidatesDestinationPolicy()
    {
        File.WriteAllText(Path.Combine(_workspaceRoot, "source.txt"), "move me");
        var policy = CreatePolicy();

        var result = policy.ValidateMove("demo", "source.txt", ".env");

        Assert.False(result.Allowed);
        Assert.Equal(WorkspaceAccessError.PathDeniedByPolicy, result.Error);
    }

    [Fact]
    public void ValidateExisting_GlobalReadOnlyStillAllowsReadButDeniesUpdate()
    {
        File.WriteAllText(Path.Combine(_workspaceRoot, "file.txt"), "content");
        var policy = CreatePolicy(readOnly: true);

        var read = policy.ValidateExisting("demo", "file.txt", WorkspaceOperation.Read);
        var update = policy.ValidateExisting("demo", "file.txt", WorkspaceOperation.Update);

        Assert.True(read.Allowed, read.Message);
        Assert.False(update.Allowed);
        Assert.Equal(WorkspaceAccessError.GlobalReadOnly, update.Error);
    }

    private WorkspacePathPolicy CreatePolicy(bool readOnly = false)
    {
        var configuration = new AgentConfiguration
        {
            Agent = new AgentOptions
            {
                ReadOnly = readOnly,
                Security = new AgentSecurityOptions
                {
                    DenyGlobs = []
                },
                Workspaces = new Dictionary<string, WorkspaceOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["demo"] = new()
                    {
                        Root = _workspaceRoot,
                        Enabled = true,
                        AllowedOperations = ["read", "create", "update", "move", "delete", "restore"]
                    }
                }
            }
        };

        var pathResolver = new UserPathResolver();
        var registry = new WorkspaceRegistry(configuration, pathResolver);
        var workspaceResolver = new WorkspaceResolver(registry);
        var permissionEvaluator = new WorkspacePermissionEvaluator(configuration);
        var denyMatcher = new DenyPathMatcher(
            AgentSecurityDefaults.GetEffectiveDenyGlobs(configuration.Agent.Security.DenyGlobs));
        var inspector = new MacOsFileSystemEntryInspector();

        return new WorkspacePathPolicy(
            workspaceResolver,
            permissionEvaluator,
            denyMatcher,
            inspector);
    }

    private static void RunProcess(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        process.WaitForExit();

        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"{executable} failed: {error}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
