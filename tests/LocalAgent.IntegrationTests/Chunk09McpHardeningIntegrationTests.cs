using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using LocalAgent.Infrastructure.Scratch;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class Chunk09McpHardeningIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk09-mcp-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Chunk", "09")]
    public async Task McpSurface_EnforcesErrorsBoundsPrivacyAndCleanStdio()
    {
        var workspaceRoot = Path.Combine(_root, "workspace");
        var stateRoot = Path.Combine(_root, "state");
        var configDirectory = Path.Combine(_root, "config");
        var scratchDirectory = Path.Combine(_root, "scratch");
        var configPath = Path.Combine(
            configDirectory,
            "agent.json");

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
                  "Id": "chunk09-integration",
                  "DisplayName": "Chunk 09 Integration"
                },
                "ReadOnly": false,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Limits": {
                  "MaxEditableFileBytes": 1048576,
                  "MaxReadResponseBytes": 32,
                  "MaxDirectoryEntries": 20,
                  "MaxSearchResults": 20,
                  "MaxTraversalDepth": 8,
                  "OperationTimeoutSeconds": 10,
                  "MaxConcurrentMutations": 1
                },
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
        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "codicks-lite-chunk09-hardening",
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

        Assert.DoesNotContain(
            tools,
            tool => string.Equals(
                tool.Name,
                "file_permanent_delete",
                StringComparison.Ordinal));

        var serverInfo = await client.CallToolAsync(
            "server_info",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);
        Assert.False(serverInfo.IsError is true);

        const string secretContent =
            "TOP-SECRET-CHUNK09-82f73aef\n" +
            "abcdefghijklmnopqrstuvwxyz0123456789" +
            "abcdefghijklmnopqrstuvwxyz0123456789";

        var create = await client.CallToolAsync(
            "file_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "safe.txt",
                ["content"] = secretContent,
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            create,
            "file_create");

        var boundedRead = await client.CallToolAsync(
            "file_read",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "safe.txt",
                ["byteOffset"] = 0,
                ["maxBytes"] = 4096
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            boundedRead,
            "file_read");

        var readContent = GetRequiredString(
            boundedRead.StructuredContent!.Value,
            "content");

        Assert.True(
            Encoding.UTF8.GetByteCount(readContent) <= 32,
            "file_read exceeded configured MaxReadResponseBytes.");

        Assert.True(
            GetRequiredBoolean(
                boundedRead.StructuredContent!.Value,
                "isPartial"));

        var deniedCreate = await client.CallToolAsync(
            "file_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = ".env",
                ["content"] = "PASSWORD=should-never-write",
                ["dryRun"] = false
            },
            cancellationToken: CancellationToken.None);

        Assert.True(deniedCreate.IsError is true);
        Assert.Contains(
            "ACCESS_DENIED",
            GetResultText(deniedCreate),
            StringComparison.Ordinal);

        var initialHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(secretContent)));

        var staleUpdate = await client.CallToolAsync(
            "file_update",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["relativePath"] = "safe.txt",
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
            secretContent,
            await File.ReadAllTextAsync(
                Path.Combine(
                    workspaceRoot,
                    "safe.txt")));

        const string deniedSecret =
            "DENIED-SECRET-CHUNK09-a108";
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, ".env"),
            $"TOKEN={deniedSecret}");

        var search = await client.CallToolAsync(
            "file_search",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "playground",
                ["query"] = deniedSecret,
                ["relativePath"] = "",
                ["caseSensitive"] = true
            },
            cancellationToken: CancellationToken.None);

        AssertToolSuccess(
            search,
            "file_search");

        var searchContent =
            search.StructuredContent!.Value;

        // SearchPage intentionally echoes the caller-provided Query field.
        // Privacy here means a denied file contributes no match/excerpt.
        Assert.Equal(
            deniedSecret,
            GetRequiredString(
                searchContent,
                "query"));

        var searchMatches =
            GetRequiredProperty(
                searchContent,
                "matches");

        Assert.Equal(
            JsonValueKind.Array,
            searchMatches.ValueKind);
        Assert.Equal(
            0,
            searchMatches.GetArrayLength());

        var auditPath = Path.Combine(
            stateRoot,
            "audit",
            "operations.jsonl");

        Assert.True(
            File.Exists(auditPath),
            "Mutation audit log was not created.");

        var auditText =
            await File.ReadAllTextAsync(auditPath);

        Assert.DoesNotContain(
            secretContent,
            auditText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TOP-SECRET-CHUNK09",
            auditText,
            StringComparison.Ordinal);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
            {
                _ = await client.CallToolAsync(
                    "file_read",
                    new Dictionary<string, object?>
                    {
                        ["workspaceId"] = "playground",
                        ["relativePath"] = "safe.txt",
                        ["byteOffset"] = 0
                    },
                    cancellationToken: cancelled.Token);
            });

        Assert.Equal(
            secretContent,
            await File.ReadAllTextAsync(
                Path.Combine(
                    workspaceRoot,
                    "safe.txt")));

        Assert.Equal(
            initialHash,
            GetRequiredString(
                create.StructuredContent!.Value,
                "sha256"));
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

    private static JsonElement GetRequiredProperty(
        JsonElement structuredContent,
        string propertyName)
    {
        foreach (var property in
                 structuredContent.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{propertyName}'.");
    }

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
            Assert.False(string.IsNullOrWhiteSpace(value));
            return value!;
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{propertyName}'.");
    }

    private static bool GetRequiredBoolean(
        JsonElement structuredContent,
        string propertyName)
    {
        foreach (var property in
                 structuredContent.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.GetBoolean();
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{propertyName}'.");
    }

    private static string JsonEscape(string value) =>
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
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(
                    _root,
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
}
