using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class V122ReleaseAcceptanceIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("r122");

    [Fact]
    public async Task V122_BackwardCompatibleAndReviewedWorkflow_CompletesThroughStdio()
    {
        var workspaceRoot =
            Path.Combine(
                _root,
                "workspace");

        var stateRoot =
            Path.Combine(
                _root,
                "state");

        var configDirectory =
            Path.Combine(
                _root,
                "config");

        var configPath =
            Path.Combine(
                configDirectory,
                "agent.json");

        Directory.CreateDirectory(
            workspaceRoot);

        Directory.CreateDirectory(
            stateRoot);

        Directory.CreateDirectory(
            configDirectory);

        var configuration =
            new
            {
                SchemaVersion = 1,
                Agent = new
                {
                    Machine = new
                    {
                        Id =
                            "v122-release-acceptance",
                        DisplayName =
                            "V122 Release Acceptance"
                    },
                    ReadOnly = false,
                    StateDirectory =
                        stateRoot,
                    Workspaces =
                        new Dictionary<string, object>
                        {
                            ["release"] =
                                new
                                {
                                    Root =
                                        workspaceRoot,
                                    Enabled = true,
                                    AllowedOperations =
                                        new[]
                                        {
                                            "read",
                                            "create",
                                            "update",
                                            "move",
                                            "delete",
                                            "restore"
                                        }
                                }
                        }
                }
            };

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(
                configuration));

        var sessionUnlocker =
            new TestSessionUnlocker();

        var transport =
            new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name =
                        "codicks-v122-release-acceptance",
                    Command = "dotnet",
                    Arguments =
                    [
                        typeof(HostMarker)
                            .Assembly
                            .Location
                    ],
                    EnvironmentVariables =
                        new Dictionary<string, string?>
                        {
                            ["CODICKS_LITE_CONFIG_FILE"] =
                                configPath
                        },
                    StandardErrorLines =
                        sessionUnlocker.StandardErrorLines
                });

        await using var client =
            await McpClient.CreateAsync(
                transport);

        var serverInfo =
            await client.CallToolAsync(
                "server_info",
                new Dictionary<string, object?>(),
                cancellationToken:
                    CancellationToken.None);

        Assert.False(
            serverInfo.IsError is true,
            $"server_info failed: {GetResultText(serverInfo)}");

        Assert.Contains(
            "1.2.2",
            GetResultText(serverInfo),
            StringComparison.Ordinal);

        await sessionUnlocker.UnlockFullAsync(
            stateRoot);

        var tools =
            await client.ListToolsAsync();

        var requiredTools =
            new[]
            {
                "file_read",
                "file_create",
                "file_update",
                "file_move",
                "file_delete",
                "file_restore",
                "file_diff",
                "file_patch_preview",
                "file_patch_apply",
                "process_exec"
            };

        foreach (var toolName in
                 requiredTools)
        {
            Assert.Contains(
                tools,
                tool =>
                    tool.Name == toolName);
        }

        Assert.DoesNotContain(
            tools,
            tool =>
                tool.Name.Contains(
                    "backup",
                    StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            tools,
            tool =>
                string.Equals(
                    tool.Name,
                    "shell_exec",
                    StringComparison.Ordinal));

        var create =
            await client.CallToolAsync(
                "file_create",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] =
                        "release",
                    ["relativePath"] =
                        "sample.txt",
                    ["content"] =
                        "one\ntwo\n",
                    ["dryRun"] =
                        false
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            create,
            "file_create");

        var firstHash =
            GetRequiredString(
                create.StructuredContent!.Value,
                "sha256");

        var update =
            await client.CallToolAsync(
                "file_update",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] =
                        "release",
                    ["relativePath"] =
                        "sample.txt",
                    ["content"] =
                        "one\ntwo-updated\n",
                    ["expectedHash"] =
                        firstHash,
                    ["dryRun"] =
                        false
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            update,
            "file_update");

        var updatedHash =
            GetRequiredString(
                update.StructuredContent!.Value,
                "sha256");

        var diff =
            await client.CallToolAsync(
                "file_diff",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] =
                        "release",
                    ["relativePath"] =
                        "sample.txt",
                    ["content"] =
                        "one\npatched\n",
                    ["expectedHash"] =
                        updatedHash
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            diff,
            "file_diff");

        Assert.Equal(
            "preview",
            GetRequiredString(
                diff.StructuredContent!.Value,
                "reviewState"));

        Assert.Equal(
            "one\ntwo-updated\n",
            await File.ReadAllTextAsync(
                Path.Combine(
                    workspaceRoot,
                    "sample.txt")));

        var patch =
            string.Join(
                '\n',
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -2,1 +2,1 @@",
                "-two-updated",
                "+patched");

        var preview =
            await client.CallToolAsync(
                "file_patch_preview",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] =
                        "release",
                    ["relativePath"] =
                        "sample.txt",
                    ["patch"] =
                        patch,
                    ["expectedHash"] =
                        updatedHash
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            preview,
            "file_patch_preview");

        Assert.True(
            GetRequiredBoolean(
                preview.StructuredContent!.Value,
                "canApply"));

        Assert.False(
            GetRequiredBoolean(
                preview.StructuredContent.Value,
                "diffIncluded"));

        Assert.Equal(
            string.Empty,
            GetRequiredStringAllowEmpty(
                preview.StructuredContent.Value,
                "unifiedDiff"));

        Assert.Equal(
            0,
            GetRequiredProperty(
                preview.StructuredContent.Value,
                "hunks")
                .GetArrayLength());

        var reviewToken =
            GetRequiredString(
                preview.StructuredContent.Value,
                "reviewToken");

        var proposedHash =
            GetRequiredString(
                preview.StructuredContent.Value,
                "proposedSha256");

        var apply =
            await client.CallToolAsync(
                "file_patch_apply",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] =
                        "release",
                    ["relativePath"] =
                        "sample.txt",
                    ["expectedHash"] =
                        updatedHash,
                    ["reviewToken"] =
                        reviewToken
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            apply,
            "file_patch_apply");

        Assert.Equal(
            "applied",
            GetRequiredString(
                apply.StructuredContent!.Value,
                "applyState"));

        Assert.Equal(
            proposedHash,
            GetRequiredString(
                apply.StructuredContent.Value,
                "sha256"));

        Assert.False(
            string.IsNullOrWhiteSpace(
                GetRequiredString(
                    apply.StructuredContent.Value,
                    "backupId")));

        Assert.Equal(
            "one\npatched\n",
            await File.ReadAllTextAsync(
                Path.Combine(
                    workspaceRoot,
                    "sample.txt")));

        var delete =
            await client.CallToolAsync(
                "file_delete",
                new Dictionary<string, object?>
                {
                    ["mutationId"] =
                        $"release-delete-{Guid.NewGuid():N}",
                    ["workspaceId"] =
                        "release",
                    ["relativePath"] =
                        "sample.txt"
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            delete,
            "file_delete");

        var recoveryId =
            GetRequiredString(
                delete.StructuredContent!.Value,
                "recoveryId");

        var restore =
            await client.CallToolAsync(
                "file_restore",
                new Dictionary<string, object?>
                {
                    ["mutationId"] =
                        $"release-restore-{Guid.NewGuid():N}",
                    ["recoveryId"] =
                        recoveryId
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            restore,
            "file_restore");

        Assert.Equal(
            "one\npatched\n",
            await File.ReadAllTextAsync(
                Path.Combine(
                    workspaceRoot,
                    "sample.txt")));
    }

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
        var property =
            GetRequiredProperty(
                content,
                propertyName);

        var value =
            property.GetString();

        Assert.False(
            string.IsNullOrWhiteSpace(
                value));

        return value!;
    }

    private static string GetRequiredStringAllowEmpty(
        JsonElement content,
        string propertyName)
    {
        var property =
            GetRequiredProperty(
                content,
                propertyName);

        var value =
            property.GetString();

        Assert.NotNull(value);

        return value;
    }

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
}
