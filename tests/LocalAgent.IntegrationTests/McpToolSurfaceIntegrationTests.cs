using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using LocalAgent.Infrastructure.Scratch;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class McpToolSurfaceIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("c07");

    [Fact]
    public async Task Chunk07_AdvertisesAndExecutesCompleteToolSurface()
    {
        var workspaceRoot = Path.Combine(_root, "workspace");
        var stateRoot = Path.Combine(_root, "state");
        var configDirectory = Path.Combine(_root, "config");
        var scratchDirectory = Path.Combine(_root, "scratch");
        var configPath = Path.Combine(configDirectory, "agent.json");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(scratchDirectory);

        await File.WriteAllTextAsync(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "Agent": {
                "Machine": {
                  "Id": "chunk07-integration",
                  "DisplayName": "Chunk 07 Integration"
                },
                "ReadOnly": false,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Workspaces": {
                  "playground": {
                    "Root": "{{JsonEscape(workspaceRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read",
                      "create",
                      "update",
                      "move",
                      "delete",
                      "restore"
                    ]
                  }
                }
              }
            }
            """);

        var hostAssembly = typeof(HostMarker).Assembly.Location;
        var sessionUnlocker = new TestSessionUnlocker();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "codicks-lite-chunk07-tools-integration",
            Command = "dotnet",
            Arguments = [hostAssembly],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["CODICKS_LITE_CONFIG_FILE"] = configPath,
                [ScratchProbeService.ScratchDirectoryEnvironmentVariable] = scratchDirectory
            },
            StandardErrorLines = sessionUnlocker.StandardErrorLines
        });

        await using var client = await McpClient.CreateAsync(transport);
        await sessionUnlocker.UnlockFullAsync(stateRoot);

        var tools = await client.ListToolsAsync();
        var expectedTools = new[]
        {
            "workspace_list",
            "workspace_inspect",
            "file_list",
            "file_stat",
            "file_read",
            "file_search",
            "file_create",
            "directory_create",
            "file_update",
            "file_move",
            "file_delete",
            "file_restore"
        };

        foreach (var expectedTool in expectedTools)
        {
            Assert.Contains(tools, tool => tool.Name == expectedTool);
        }

        var workspaceList = await client.CallToolAsync(
            "workspace_list",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(workspaceList, "workspace_list");

        var workspaceInspect = await client.CallToolAsync(
            "workspace_inspect",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground"
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(workspaceInspect, "workspace_inspect");

        var directoryCreate = await client.CallToolAsync(
            "directory_create",
            new Dictionary<string, object?>
            {
                ["mutationId"] = NewMutationId("dir"),
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs"
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(directoryCreate, "directory_create");

        const string initialContent = "hello Codicks Lite";
        var fileCreate = await client.CallToolAsync(
            "file_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs/alpha.txt",
                ["content"] = initialContent,
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileCreate, "file_create");

        var fileStat = await client.CallToolAsync(
            "file_stat",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs/alpha.txt"
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileStat, "file_stat");

        var fileList = await client.CallToolAsync(
            "file_list",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs",
                ["offset"] = 0
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileList, "file_list");

        var fileRead = await client.CallToolAsync(
            "file_read",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs/alpha.txt",
                ["byteOffset"] = 0
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileRead, "file_read");

        var fileSearch = await client.CallToolAsync(
            "file_search",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["query"] = "Codicks Lite",
                ["relativePath"] = "docs",
                ["caseSensitive"] = true
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileSearch, "file_search");

        var initialHash = ComputeHash(initialContent);
        var conflictUpdate = await client.CallToolAsync(
            "file_update",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs/alpha.txt",
                ["content"] = "should not write",
                ["expectedHash"] = new string('0', 64),
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);

        Assert.True(conflictUpdate.IsError is true);
        Assert.Contains(
            "CONFLICT",
            GetResultText(conflictUpdate),
            StringComparison.Ordinal);

        const string updatedContent = "hello Codicks Lite updated";
        var fileUpdate = await client.CallToolAsync(
            "file_update",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs/alpha.txt",
                ["content"] = updatedContent,
                ["expectedHash"] = initialHash,
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileUpdate, "file_update");

        var updatedHash = ComputeHash(updatedContent);
        var fileMove = await client.CallToolAsync(
            "file_move",
            new Dictionary<string, object?>
            {
                ["mutationId"] = NewMutationId("move"),
                ["workspaceId"] = "playground",
                ["sourceRelativePath"] = "docs/alpha.txt",
                ["destinationRelativePath"] = "docs/beta.txt",
                ["expectedHash"] = updatedHash
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileMove, "file_move");

        var fileDelete = await client.CallToolAsync(
            "file_delete",
            new Dictionary<string, object?>
            {
                ["mutationId"] = NewMutationId("delete"),
                ["workspaceId"] = "playground",
                ["relativePath"] = "docs/beta.txt"
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileDelete, "file_delete");

        var recoveryId = GetRequiredString(
            fileDelete.StructuredContent!.Value,
            "recoveryId");

        Assert.False(File.Exists(Path.Combine(workspaceRoot, "docs", "beta.txt")));

        var fileRestore = await client.CallToolAsync(
            "file_restore",
            new Dictionary<string, object?>
            {
                ["mutationId"] = NewMutationId("restore"),
                ["recoveryId"] = recoveryId
            },
            cancellationToken: CancellationToken.None);
        AssertToolSuccess(fileRestore, "file_restore");

        Assert.Equal(
            updatedContent,
            await File.ReadAllTextAsync(
                Path.Combine(workspaceRoot, "docs", "beta.txt")));
    }

    private static void AssertToolSuccess(
        CallToolResult result,
        string toolName)
    {
        Assert.False(
            result.IsError is true,
            $"{toolName} failed: {GetResultText(result)}");

        Assert.True(
            result.StructuredContent.HasValue,
            $"{toolName} did not return structuredContent.");
    }

    private static string GetResultText(CallToolResult result) =>
        string.Join(
            " | ",
            result.Content
                .OfType<TextContentBlock>()
                .Select(block => block.Text));

    private static string GetRequiredString(
        JsonElement structuredContent,
        string propertyName)
    {
        foreach (var property in structuredContent.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                var value = property.Value.GetString();
                Assert.False(string.IsNullOrWhiteSpace(value));
                return value!;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{propertyName}'.");
    }

    private static string ComputeHash(string content) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(content)));

    private static string NewMutationId(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}";

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
