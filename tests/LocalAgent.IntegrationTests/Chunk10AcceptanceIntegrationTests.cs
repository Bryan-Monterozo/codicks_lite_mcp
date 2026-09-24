using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using LocalAgent.Infrastructure.Scratch;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class Chunk10AcceptanceIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk10-acceptance-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Chunk", "10")]
    public async Task Phase1AcceptanceScenario_CompletesThroughStdioMcp()
    {
        var playgroundRoot = Path.Combine(_root, "playground");
        var referenceRoot = Path.Combine(_root, "reference");
        var stateRoot = Path.Combine(_root, "state");
        var configDirectory = Path.Combine(_root, "config");
        var scratchDirectory = Path.Combine(_root, "scratch");
        var configPath = Path.Combine(
            configDirectory,
            "agent.json");

        Directory.CreateDirectory(playgroundRoot);
        Directory.CreateDirectory(referenceRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(scratchDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(referenceRoot, "reference.txt"),
            "read-only reference");

        await File.WriteAllTextAsync(
            Path.Combine(playgroundRoot, ".env"),
            "SECRET=must-not-be-readable");

        await File.WriteAllTextAsync(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "Agent": {
                "Machine": {
                  "Id": "chunk10-local-acceptance",
                  "DisplayName": "Chunk 10 Local Acceptance"
                },
                "ReadOnly": false,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Workspaces": {
                  "playground": {
                    "Root": "{{JsonEscape(playgroundRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read",
                      "create",
                      "update",
                      "move",
                      "delete",
                      "restore"
                    ]
                  },
                  "reference": {
                    "Root": "{{JsonEscape(referenceRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read"
                    ]
                  }
                }
              }
            }
            """);

        var hostAssembly = typeof(HostMarker).Assembly.Location;
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "codicks-lite-chunk10-local-acceptance",
                Command = "dotnet",
                Arguments = [hostAssembly],
                EnvironmentVariables =
                    new Dictionary<string, string?>
                    {
                        ["CODICKS_LITE_CONFIG_FILE"] = configPath,
                        [ScratchProbeService.ScratchDirectoryEnvironmentVariable] =
                            scratchDirectory
                    }
            });

        await using var client =
            await McpClient.CreateAsync(transport);

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
            Assert.Contains(
                tools,
                tool => tool.Name == expectedTool);
        }

        var workspaceList = await client.CallToolAsync(
            "workspace_list",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            workspaceList,
            "workspace_list");

        var inspect = await client.CallToolAsync(
            "workspace_inspect",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground"
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            inspect,
            "workspace_inspect");

        const string initialContent =
            "Codicks Lite Phase 1 acceptance v1";

        var create = await client.CallToolAsync(
            "file_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "acceptance.txt",
                ["content"] = initialContent,
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            create,
            "file_create");

        var read = await client.CallToolAsync(
            "file_read",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "acceptance.txt",
                ["byteOffset"] = 0
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            read,
            "file_read");

        Assert.Equal(
            initialContent,
            GetRequiredString(
                read.StructuredContent!.Value,
                "content"));

        var staleUpdate = await client.CallToolAsync(
            "file_update",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "acceptance.txt",
                ["content"] = "must-not-overwrite",
                ["expectedHash"] = new string('0', 64),
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);

        Assert.True(staleUpdate.IsError is true);
        Assert.Contains(
            "CONFLICT",
            GetResultText(staleUpdate),
            StringComparison.Ordinal);

        Assert.Equal(
            initialContent,
            await File.ReadAllTextAsync(
                Path.Combine(
                    playgroundRoot,
                    "acceptance.txt")));

        const string updatedContent =
            "Codicks Lite Phase 1 acceptance v2";

        var update = await client.CallToolAsync(
            "file_update",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "acceptance.txt",
                ["content"] = updatedContent,
                ["expectedHash"] = ComputeHash(initialContent),
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            update,
            "file_update");

        var move = await client.CallToolAsync(
            "file_move",
            new Dictionary<string, object?>
            {
                ["mutationId"] = NewMutationId("move"),
                ["workspaceId"] = "playground",
                ["sourceRelativePath"] = "acceptance.txt",
                ["destinationRelativePath"] = "acceptance-renamed.txt",
                ["expectedHash"] = ComputeHash(updatedContent)
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            move,
            "file_move");

        var delete = await client.CallToolAsync(
            "file_delete",
            new Dictionary<string, object?>
            {
                ["mutationId"] = NewMutationId("delete"),
                ["workspaceId"] = "playground",
                ["relativePath"] = "acceptance-renamed.txt"
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            delete,
            "file_delete");

        var recoveryId = GetRequiredString(
            delete.StructuredContent!.Value,
            "recoveryId");

        Assert.False(
            File.Exists(
                Path.Combine(
                    playgroundRoot,
                    "acceptance-renamed.txt")));

        var restore = await client.CallToolAsync(
            "file_restore",
            new Dictionary<string, object?>
            {
                ["mutationId"] = NewMutationId("restore"),
                ["recoveryId"] = recoveryId
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            restore,
            "file_restore");

        Assert.Equal(
            updatedContent,
            await File.ReadAllTextAsync(
                Path.Combine(
                    playgroundRoot,
                    "acceptance-renamed.txt")));

        var deniedRead = await client.CallToolAsync(
            "file_read",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = ".env",
                ["byteOffset"] = 0
            },
            cancellationToken: CancellationToken.None);

        Assert.True(deniedRead.IsError is true);
        Assert.Contains(
            "ACCESS_DENIED",
            GetResultText(deniedRead),
            StringComparison.Ordinal);

        var unauthorizedWrite = await client.CallToolAsync(
            "file_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "reference",
                ["relativePath"] = "must-not-write.txt",
                ["content"] = "blocked",
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);

        Assert.True(unauthorizedWrite.IsError is true);
        Assert.Contains(
            "ACCESS_DENIED",
            GetResultText(unauthorizedWrite),
            StringComparison.Ordinal);

        Assert.False(
            File.Exists(
                Path.Combine(
                    referenceRoot,
                    "must-not-write.txt")));
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

    private static string GetResultText(
        CallToolResult result) =>
        string.Join(
            " | ",
            result.Content
                .OfType<TextContentBlock>()
                .Select(block => block.Text));

    private static string GetRequiredString(
        JsonElement structuredContent,
        string propertyName)
    {
        foreach (var property in
                 structuredContent.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property.Value.GetString();

            Assert.False(
                string.IsNullOrWhiteSpace(value));

            return value!;
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{propertyName}'.");
    }

    private static string ComputeHash(
        string content) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(content)));

    private static string NewMutationId(
        string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}";

    private static string JsonEscape(
        string value) =>
        value.Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }
}
