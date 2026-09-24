using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class FileDiffMcpIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("d12");

    [Fact]
    public async Task FileDiff_FullSession_ProvidesReadOnlyReviewSurface()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "full");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockFullAsync(
            fixture.StateRoot);

        var tools =
            await client.ListToolsAsync();

        Assert.Contains(
            tools,
            tool =>
                tool.Name == "file_diff");

        var missingContent =
            await client.CallToolAsync(
                "file_diff",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "diff",
                    ["relativePath"] = "sample.txt"
                },
                cancellationToken:
                    CancellationToken.None);

        Assert.True(
            missingContent.IsError is true);

        var samplePath = Path.Combine(
            fixture.WorkspaceRoot,
            "sample.txt");

        var before =
            await File.ReadAllBytesAsync(
                samplePath);

        var expectedHash =
            Convert.ToHexString(
                SHA256.HashData(
                    before));

        var preview =
            await CallFileDiffAsync(
                client,
                "diff",
                "sample.txt",
                "one\nchanged\n",
                expectedHash);

        AssertToolSuccess(
            preview,
            "file_diff");

        var previewContent =
            preview.StructuredContent!.Value;

        Assert.True(
            GetRequiredBoolean(
                previewContent,
                "hasChanges"));

        Assert.Equal(
            expectedHash,
            GetRequiredString(
                previewContent,
                "baseSha256"));

        Assert.Equal(
            1,
            GetRequiredInt32(
                previewContent,
                "additions"));

        Assert.Equal(
            1,
            GetRequiredInt32(
                previewContent,
                "deletions"));

        var unifiedDiff =
            GetRequiredString(
                previewContent,
                "unifiedDiff");

        Assert.Contains(
            "--- a/sample.txt",
            unifiedDiff,
            StringComparison.Ordinal);

        Assert.Contains(
            "-two",
            unifiedDiff,
            StringComparison.Ordinal);

        Assert.Contains(
            "+changed",
            unifiedDiff,
            StringComparison.Ordinal);

        var hunks =
            GetRequiredProperty(
                previewContent,
                "hunks");

        Assert.True(
            hunks.GetArrayLength() > 0);

        Assert.Equal(
            "preview",
            GetRequiredString(
                previewContent,
                "reviewState"));

        Assert.Contains(
            "State: PREVIEW — not applied",
            GetRequiredString(
                previewContent,
                "reviewSummary"),
            StringComparison.Ordinal);

        Assert.Contains(
            "Changes: +1 -1",
            GetRequiredString(
                previewContent,
                "reviewSummary"),
            StringComparison.Ordinal);

        var serializedHunks =
            hunks.GetRawText();

        Assert.Contains(
            "\"kind\":\"delete\"",
            serializedHunks,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"kind\":\"add\"",
            serializedHunks,
            StringComparison.Ordinal);

        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(
                samplePath));

        Assert.False(
            Directory.Exists(
                Path.Combine(
                    fixture.StateRoot,
                    "recovery")));

        var stale =
            await CallFileDiffAsync(
                client,
                "diff",
                "sample.txt",
                "replacement",
                new string(
                    '0',
                    64));

        AssertToolErrorContains(
            stale,
            "CONFLICT");

        var malformedHash =
            await CallFileDiffAsync(
                client,
                "diff",
                "sample.txt",
                "replacement",
                "bad-hash");

        AssertToolErrorContains(
            malformedHash,
            "INVALID_REQUEST");

        var noChange =
            await CallFileDiffAsync(
                client,
                "diff",
                "same.txt",
                "same\n");

        AssertToolSuccess(
            noChange,
            "no-change file_diff");

        Assert.False(
            GetRequiredBoolean(
                noChange.StructuredContent!.Value,
                "hasChanges"));

        Assert.Equal(
            string.Empty,
            GetRequiredString(
                noChange.StructuredContent.Value,
                "unifiedDiff"));

        var denied =
            await CallFileDiffAsync(
                client,
                "diff",
                ".env",
                "SECRET=changed");

        AssertToolErrorContains(
            denied,
            "ACCESS_DENIED");

        var traversal =
            await CallFileDiffAsync(
                client,
                "diff",
                "../outside.txt",
                "changed");

        AssertToolErrorContains(
            traversal,
            "INVALID_PATH");

        var missing =
            await CallFileDiffAsync(
                client,
                "diff",
                "missing.txt",
                "content");

        AssertToolErrorContains(
            missing,
            "FILE_NOT_FOUND");

        var directory =
            await CallFileDiffAsync(
                client,
                "diff",
                "folder",
                "content");

        AssertToolErrorContains(
            directory,
            "UNSUPPORTED_FILE_TYPE");

        var disabled =
            await CallFileDiffAsync(
                client,
                "disabled",
                "sample.txt",
                "content");

        AssertToolErrorContains(
            disabled,
            "ACCESS_DENIED");

        var largeProposed =
            string.Join(
                '\n',
                Enumerable.Range(
                    0,
                    2_500)
                    .Select(
                        value =>
                            $"new-{value:D4}-{new string('y', 24)}"));

        var large =
            await CallFileDiffAsync(
                client,
                "diff",
                "large.txt",
                largeProposed);

        AssertToolSuccess(
            large,
            "large file_diff");

        Assert.True(
            GetRequiredBoolean(
                large.StructuredContent!.Value,
                "truncated"));

        Assert.True(
            GetRequiredString(
                large.StructuredContent.Value,
                "unifiedDiff")
                .Length <=
            65_536);

        Assert.Contains(
            "Truncated: yes — review output incomplete",
            GetRequiredString(
                large.StructuredContent.Value,
                "reviewSummary"),
            StringComparison.Ordinal);

        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(
                samplePath));
    }

    [Fact]
    public async Task FileDiff_LockedSession_IsRejected()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "locked");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        var result =
            await CallFileDiffAsync(
                client,
                "diff",
                "sample.txt",
                "changed");

        AssertToolErrorContains(
            result,
            "SESSION_LOCKED");
    }

    [Fact]
    public async Task FileDiff_ReadOnlySession_IsAllowed()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "readonly");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockReadOnlyAsync(
            fixture.StateRoot);

        var result =
            await CallFileDiffAsync(
                client,
                "diff",
                "sample.txt",
                "one\nreadonly-preview\n");

        AssertToolSuccess(
            result,
            "READ_ONLY file_diff");

        Assert.True(
            GetRequiredBoolean(
                result.StructuredContent!.Value,
                "hasChanges"));
    }

    private async Task<HostFixture> CreateHostFixtureAsync(
        string suffix)
    {
        var fixtureRoot = Path.Combine(
            _root,
            suffix);

        var workspaceRoot = Path.Combine(
            fixtureRoot,
            "workspace");

        var disabledRoot = Path.Combine(
            fixtureRoot,
            "disabled");

        var stateRoot = Path.Combine(
            fixtureRoot,
            "state");

        var configDirectory = Path.Combine(
            fixtureRoot,
            "config");

        var configPath = Path.Combine(
            configDirectory,
            "agent.json");

        Directory.CreateDirectory(
            workspaceRoot);

        Directory.CreateDirectory(
            disabledRoot);

        Directory.CreateDirectory(
            stateRoot);

        Directory.CreateDirectory(
            configDirectory);

        Directory.CreateDirectory(
            Path.Combine(
                workspaceRoot,
                "folder"));

        await File.WriteAllTextAsync(
            Path.Combine(
                workspaceRoot,
                "sample.txt"),
            "one\ntwo\n",
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            Path.Combine(
                workspaceRoot,
                "same.txt"),
            "same\n",
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            Path.Combine(
                workspaceRoot,
                ".env"),
            "SECRET=value",
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            Path.Combine(
                disabledRoot,
                "sample.txt"),
            "disabled",
            new UTF8Encoding(false));

        var largeCurrent =
            string.Join(
                '\n',
                Enumerable.Range(
                    0,
                    2_500)
                    .Select(
                        value =>
                            $"old-{value:D4}-{new string('x', 24)}"));

        await File.WriteAllTextAsync(
            Path.Combine(
                workspaceRoot,
                "large.txt"),
            largeCurrent,
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            configPath,
            $$"""
            {
              "SchemaVersion": 1,
              "Agent": {
                "Machine": {
                  "Id": "file-diff-{{suffix}}",
                  "DisplayName": "File Diff {{suffix}}"
                },
                "ReadOnly": false,
                "StateDirectory": "{{JsonEscape(stateRoot)}}",
                "Limits": {
                  "MaxEditableFileBytes": 1048576,
                  "MaxReadResponseBytes": 65536,
                  "MaxDirectoryEntries": 200,
                  "MaxSearchResults": 100,
                  "MaxTraversalDepth": 12,
                  "OperationTimeoutSeconds": 15,
                  "MaxConcurrentMutations": 1
                },
                "Workspaces": {
                  "diff": {
                    "Root": "{{JsonEscape(workspaceRoot)}}",
                    "Enabled": true,
                    "AllowedOperations": [
                      "read"
                    ]
                  },
                  "disabled": {
                    "Root": "{{JsonEscape(disabledRoot)}}",
                    "Enabled": false,
                    "AllowedOperations": [
                      "read"
                    ]
                  }
                }
              }
            }
            """);

        var hostAssembly =
            typeof(HostMarker).Assembly.Location;

        var sessionUnlocker =
            new TestSessionUnlocker();

        var transport =
            new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name =
                        $"codicks-file-diff-{suffix}",
                    Command = "dotnet",
                    Arguments = [hostAssembly],
                    EnvironmentVariables =
                        new Dictionary<string, string?>
                        {
                            ["CODICKS_LITE_CONFIG_FILE"] =
                                configPath
                        },
                    StandardErrorLines =
                        sessionUnlocker.StandardErrorLines
                });

        return new HostFixture(
            workspaceRoot,
            stateRoot,
            sessionUnlocker,
            transport);
    }

    private static ValueTask<CallToolResult> CallFileDiffAsync(
        McpClient client,
        string workspaceId,
        string relativePath,
        string content,
        string? expectedHash = null) =>
        client.CallToolAsync(
            "file_diff",
            new Dictionary<string, object?>
            {
                ["workspaceId"] =
                    workspaceId,
                ["relativePath"] =
                    relativePath,
                ["content"] =
                    content,
                ["expectedHash"] =
                    expectedHash
            },
            cancellationToken:
                CancellationToken.None);

    private static void AssertToolSuccess(
        CallToolResult result,
        string operation)
    {
        Assert.False(
            result.IsError is true,
            $"{operation} failed: {GetResultText(result)}");

        Assert.True(
            result.StructuredContent.HasValue,
            $"{operation} did not return structuredContent.");
    }

    private static void AssertToolErrorContains(
        CallToolResult result,
        string expectedCode)
    {
        Assert.True(
            result.IsError is true);

        Assert.Contains(
            expectedCode,
            GetResultText(result),
            StringComparison.Ordinal);
    }

    private static string GetResultText(
        CallToolResult result) =>
        string.Join(
            " | ",
            result.Content
                .OfType<TextContentBlock>()
                .Select(
                    block =>
                        block.Text));

    private static string GetRequiredString(
        JsonElement content,
        string propertyName)
    {
        var value =
            GetRequiredProperty(
                content,
                propertyName)
                .GetString();

        Assert.NotNull(
            value);

        return value;
    }

    private static int GetRequiredInt32(
        JsonElement content,
        string propertyName) =>
        GetRequiredProperty(
            content,
            propertyName)
            .GetInt32();

    private static bool GetRequiredBoolean(
        JsonElement content,
        string propertyName) =>
        GetRequiredProperty(
            content,
            propertyName)
            .GetBoolean();

    private static JsonElement GetRequiredProperty(
        JsonElement content,
        string propertyName)
    {
        foreach (var property in
                 content.EnumerateObject())
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
        if (Directory.Exists(
                _root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }

    private sealed record HostFixture(
        string WorkspaceRoot,
        string StateRoot,
        TestSessionUnlocker SessionUnlocker,
        StdioClientTransport Transport);
}
