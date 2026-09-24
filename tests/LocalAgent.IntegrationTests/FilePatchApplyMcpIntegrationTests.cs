using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class FilePatchApplyMcpIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("pa12");

    [Fact]
    public async Task FilePatchApply_FullSession_AppliesReviewedPatchAndConsumesToken()
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
                tool.Name == "file_patch_apply");

        var patch =
            ReplacementPatch(
                "sample.txt",
                "two",
                "changed",
                oldStart: 2);

        var preview =
            await CallPreviewAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash);

        AssertToolSuccess(
            preview,
            "file_patch_preview");

        var previewContent =
            preview.StructuredContent!.Value;

        Assert.True(
            GetRequiredBoolean(
                previewContent,
                "canApply"));

        var reviewToken =
            GetRequiredString(
                previewContent,
                "reviewToken");

        Assert.False(
            string.IsNullOrWhiteSpace(
                reviewToken));

        Assert.True(
            GetRequiredProperty(
                previewContent,
                "reviewExpiresAtUtc")
                .ValueKind ==
            JsonValueKind.String);

        var apply =
            await CallApplyAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash,
                reviewToken);

        AssertToolSuccess(
            apply,
            "file_patch_apply");

        var receipt =
            apply.StructuredContent!.Value;

        Assert.Equal(
            GetRequiredString(
                previewContent,
                "proposedSha256"),
            GetRequiredString(
                receipt,
                "sha256"));

        Assert.False(
            string.IsNullOrWhiteSpace(
                GetRequiredString(
                    receipt,
                    "backupId")));

        Assert.Equal(
            "applied",
            GetRequiredString(
                receipt,
                "applyState"));

        var applySummary =
            GetRequiredString(
                receipt,
                "applySummary");

        Assert.Contains(
            "State: APPLIED",
            applySummary,
            StringComparison.Ordinal);

        Assert.Contains(
            "Backup:",
            applySummary,
            StringComparison.Ordinal);

        Assert.Equal(
            "one\nchanged\nthree",
            await File.ReadAllTextAsync(
                fixture.SamplePath));

        var backupRoot =
            Path.Combine(
                fixture.StateRoot,
                "recovery",
                "updates",
                "patch");

        Assert.True(
            Directory.Exists(
                backupRoot));

        Assert.Single(
            Directory.EnumerateFiles(
                backupRoot,
                "*.bin",
                SearchOption.TopDirectoryOnly));

        var replay =
            await CallApplyAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash,
                reviewToken);

        AssertToolErrorContains(
            replay,
            "CONSUMED_REVIEW_TOKEN");
    }

    [Fact]
    public async Task FilePatchApply_WithoutPreview_IsRejected()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "unreviewed");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockFullAsync(
            fixture.StateRoot);

        var patch =
            ReplacementPatch(
                "sample.txt",
                "two",
                "changed",
                oldStart: 2);

        var missing =
            await CallApplyAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash,
                string.Empty);

        AssertToolErrorContains(
            missing,
            "REVIEW_REQUIRED");

        var unknown =
            await CallApplyAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash,
                "unknown-review-token");

        AssertToolErrorContains(
            unknown,
            "INVALID_REVIEW_TOKEN");

        Assert.Equal(
            "one\ntwo\nthree",
            await File.ReadAllTextAsync(
                fixture.SamplePath));
    }

    [Fact]
    public async Task FilePatchApply_ModifiedFileAfterPreview_ReturnsConflict()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "conflict");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockFullAsync(
            fixture.StateRoot);

        var patch =
            ReplacementPatch(
                "sample.txt",
                "two",
                "changed",
                oldStart: 2);

        var preview =
            await CallPreviewAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash);

        AssertToolSuccess(
            preview,
            "preview before conflict");

        var reviewToken =
            GetRequiredString(
                preview.StructuredContent!.Value,
                "reviewToken");

        await File.WriteAllTextAsync(
            fixture.SamplePath,
            "external-change",
            new UTF8Encoding(false));

        var apply =
            await CallApplyAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash,
                reviewToken);

        AssertToolErrorContains(
            apply,
            "CONFLICT");

        Assert.Equal(
            "external-change",
            await File.ReadAllTextAsync(
                fixture.SamplePath));
    }

    [Fact]
    public async Task FilePatchApply_WorkspaceWithoutUpdatePermission_IsRejected()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "no-update");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockFullAsync(
            fixture.StateRoot);

        var patch =
            ReplacementPatch(
                "readonly.txt",
                "before",
                "after",
                oldStart: 1);

        var preview =
            await CallPreviewAsync(
                client,
                "readonly",
                "readonly.txt",
                patch,
                fixture.ReadOnlyHash);

        AssertToolSuccess(
            preview,
            "read-only workspace preview");

        var apply =
            await CallApplyAsync(
                client,
                "readonly",
                "readonly.txt",
                patch,
                fixture.ReadOnlyHash,
                GetRequiredString(
                    preview.StructuredContent!.Value,
                    "reviewToken"));

        AssertToolErrorContains(
            apply,
            "ACCESS_DENIED");

        Assert.Equal(
            "before",
            await File.ReadAllTextAsync(
                fixture.ReadOnlyPath));
    }

    [Fact]
    public async Task FilePatchApply_LockedSession_IsRejected()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "locked");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        var patch =
            ReplacementPatch(
                "sample.txt",
                "two",
                "changed",
                oldStart: 2);

        var result =
            await CallApplyAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash,
                "token");

        AssertToolErrorContains(
            result,
            "SESSION_LOCKED");
    }

    [Fact]
    public async Task FilePatchApply_ReadOnlySession_IsRejectedAfterPreview()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "readonly-session");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockReadOnlyAsync(
            fixture.StateRoot);

        var patch =
            ReplacementPatch(
                "sample.txt",
                "two",
                "changed",
                oldStart: 2);

        var preview =
            await CallPreviewAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash);

        AssertToolSuccess(
            preview,
            "READ_ONLY preview");

        var result =
            await CallApplyAsync(
                client,
                "patch",
                "sample.txt",
                patch,
                fixture.SampleHash,
                GetRequiredString(
                    preview.StructuredContent!.Value,
                    "reviewToken"));

        AssertToolErrorContains(
            result,
            "SESSION_READ_ONLY");

        Assert.Equal(
            "one\ntwo\nthree",
            await File.ReadAllTextAsync(
                fixture.SamplePath));
    }

    private async Task<HostFixture> CreateHostFixtureAsync(
        string suffix)
    {
        var fixtureRoot =
            Path.Combine(
                _root,
                suffix);

        var workspaceRoot =
            Path.Combine(
                fixtureRoot,
                "workspace");

        var readOnlyRoot =
            Path.Combine(
                fixtureRoot,
                "readonly");

        var stateRoot =
            Path.Combine(
                fixtureRoot,
                "state");

        var configDirectory =
            Path.Combine(
                fixtureRoot,
                "config");

        var configPath =
            Path.Combine(
                configDirectory,
                "agent.json");

        Directory.CreateDirectory(
            workspaceRoot);

        Directory.CreateDirectory(
            readOnlyRoot);

        Directory.CreateDirectory(
            stateRoot);

        Directory.CreateDirectory(
            configDirectory);

        var samplePath =
            Path.Combine(
                workspaceRoot,
                "sample.txt");

        var readOnlyPath =
            Path.Combine(
                readOnlyRoot,
                "readonly.txt");

        await File.WriteAllTextAsync(
            samplePath,
            "one\ntwo\nthree",
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            readOnlyPath,
            "before",
            new UTF8Encoding(false));

        var sampleHash =
            HashFile(
                samplePath);

        var readOnlyHash =
            HashFile(
                readOnlyPath);

        var configuration =
            new
            {
                SchemaVersion = 1,
                Agent = new
                {
                    Machine = new
                    {
                        Id =
                            $"file-patch-apply-{suffix}",
                        DisplayName =
                            $"File Patch Apply {suffix}"
                    },
                    ReadOnly = false,
                    StateDirectory =
                        stateRoot,
                    Workspaces =
                        new Dictionary<string, object>
                        {
                            ["patch"] =
                                new
                                {
                                    Root =
                                        workspaceRoot,
                                    Enabled = true,
                                    AllowedOperations =
                                        new[]
                                        {
                                            "read",
                                            "update"
                                        }
                                },
                            ["readonly"] =
                                new
                                {
                                    Root =
                                        readOnlyRoot,
                                    Enabled = true,
                                    AllowedOperations =
                                        new[]
                                        {
                                            "read"
                                        }
                                }
                        }
                }
            };

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(
                configuration));

        var hostAssembly =
            typeof(HostMarker).Assembly.Location;

        var sessionUnlocker =
            new TestSessionUnlocker();

        var transport =
            new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name =
                        $"codicks-file-patch-apply-{suffix}",
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
            samplePath,
            sampleHash,
            readOnlyPath,
            readOnlyHash,
            stateRoot,
            sessionUnlocker,
            transport);
    }

    private static ValueTask<CallToolResult> CallPreviewAsync(
        McpClient client,
        string workspaceId,
        string relativePath,
        string patch,
        string expectedHash) =>
        client.CallToolAsync(
            "file_patch_preview",
            new Dictionary<string, object?>
            {
                ["workspaceId"] =
                    workspaceId,
                ["relativePath"] =
                    relativePath,
                ["patch"] =
                    patch,
                ["expectedHash"] =
                    expectedHash
            },
            cancellationToken:
                CancellationToken.None);

    private static ValueTask<CallToolResult> CallApplyAsync(
        McpClient client,
        string workspaceId,
        string relativePath,
        string patch,
        string expectedHash,
        string reviewToken) =>
        client.CallToolAsync(
            "file_patch_apply",
            new Dictionary<string, object?>
            {
                ["workspaceId"] =
                    workspaceId,
                ["relativePath"] =
                    relativePath,
                ["patch"] =
                    patch,
                ["expectedHash"] =
                    expectedHash,
                ["reviewToken"] =
                    reviewToken
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

    private static string HashFile(
        string path) =>
        Convert.ToHexString(
            SHA256.HashData(
                File.ReadAllBytes(
                    path)));

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
        string SamplePath,
        string SampleHash,
        string ReadOnlyPath,
        string ReadOnlyHash,
        string StateRoot,
        TestSessionUnlocker SessionUnlocker,
        StdioClientTransport Transport);
}
