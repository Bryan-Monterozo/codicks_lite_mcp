using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class FilePatchPreviewMcpIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("p12");

    [Fact]
    public async Task FilePatchPreview_FullSession_ValidatesAndReviewsWithoutMutation()
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
                tool.Name == "file_patch_preview");

        var missingExpectedHash =
            await client.CallToolAsync(
                "file_patch_preview",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "patch",
                    ["relativePath"] = "sample.txt",
                    ["patch"] =
                        Patch(
                            "--- a/sample.txt",
                            "+++ b/sample.txt")
                },
                cancellationToken:
                    CancellationToken.None);

        Assert.True(
            missingExpectedHash.IsError is true);

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
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt",
                    "@@ -1,3 +1,3 @@",
                    " one",
                    "-two",
                    "+changed",
                    " three"),
                expectedHash);

        AssertToolSuccess(
            preview,
            "file_patch_preview");

        var content =
            preview.StructuredContent!.Value;

        Assert.True(
            GetRequiredBoolean(
                content,
                "canApply"));

        Assert.Equal(
            expectedHash,
            GetRequiredString(
                content,
                "baseSha256"));

        Assert.Equal(
            64,
            GetRequiredString(
                content,
                "patchSha256")
                .Length);

        Assert.Equal(
            1,
            GetRequiredInt32(
                content,
                "additions"));

        Assert.Equal(
            1,
            GetRequiredInt32(
                content,
                "deletions"));

        var unifiedDiff =
            GetRequiredString(
                content,
                "unifiedDiff");

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
                content,
                "hunks");

        Assert.True(
            hunks.GetArrayLength() > 0);

        Assert.Equal(
            "preview",
            GetRequiredString(
                content,
                "reviewState"));

        var reviewSummary =
            GetRequiredString(
                content,
                "reviewSummary");

        Assert.Contains(
            "State: PREVIEW — not applied",
            reviewSummary,
            StringComparison.Ordinal);

        Assert.Contains(
            "Patch:",
            reviewSummary,
            StringComparison.Ordinal);

        Assert.Contains(
            "Can apply: yes",
            reviewSummary,
            StringComparison.Ordinal);

        Assert.Contains(
            "Review token expires:",
            reviewSummary,
            StringComparison.Ordinal);

        Assert.False(
            string.IsNullOrWhiteSpace(
                GetRequiredString(
                    content,
                    "reviewToken")));

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
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt"),
                new string('0', 64));

        AssertToolErrorContains(
            stale,
            "CONFLICT");

        var malformed =
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt",
                    "@@ bad @@"),
                expectedHash);

        AssertToolErrorContains(
            malformed,
            "INVALID_PATCH");

        var contextConflict =
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt",
                    "@@ -1,2 +1,2 @@",
                    " WRONG",
                    " two"),
                expectedHash);

        AssertToolErrorContains(
            contextConflict,
            "PATCH_CONFLICT");

        var wrongTarget =
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/other.txt",
                    "+++ b/other.txt",
                    "@@ -1,1 +1,1 @@",
                    "-one",
                    "+changed"),
                expectedHash);

        AssertToolErrorContains(
            wrongTarget,
            "INVALID_PATCH");

        var traversal =
            await client.CallToolAsync(
                "file_patch_preview",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "patch",
                    ["relativePath"] = "../outside.txt",
                    ["patch"] =
                        Patch(
                            "--- a/outside.txt",
                            "+++ b/outside.txt"),
                    ["expectedHash"] =
                        expectedHash
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolErrorContains(
            traversal,
            "INVALID_PATH");

        var noOp =
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt"),
                expectedHash);

        AssertToolSuccess(
            noOp,
            "no-op patch preview");

        Assert.False(
            GetRequiredBoolean(
                noOp.StructuredContent!.Value,
                "canApply"));

        Assert.Equal(
            string.Empty,
            GetRequiredString(
                noOp.StructuredContent.Value,
                "unifiedDiff"));

        Assert.Contains(
            "Can apply: no",
            GetRequiredString(
                noOp.StructuredContent.Value,
                "reviewSummary"),
            StringComparison.Ordinal);

        Assert.Contains(
            "Review token expires: not issued",
            GetRequiredString(
                noOp.StructuredContent.Value,
                "reviewSummary"),
            StringComparison.Ordinal);

        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(
                samplePath));
    }

    [Fact]
    public async Task FilePatchPreview_LockedSession_IsRejected()
    {
        var fixture =
            await CreateHostFixtureAsync(
                "locked");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        var result =
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt"),
                fixture.SampleHash);

        AssertToolErrorContains(
            result,
            "SESSION_LOCKED");
    }

    [Fact]
    public async Task FilePatchPreview_ReadOnlySession_IsAllowed()
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
            await CallPreviewAsync(
                client,
                "sample.txt",
                Patch(
                    "--- a/sample.txt",
                    "+++ b/sample.txt",
                    "@@ -2,1 +2,1 @@",
                    "-two",
                    "+readonly-preview"),
                fixture.SampleHash);

        AssertToolSuccess(
            result,
            "READ_ONLY file_patch_preview");

        Assert.True(
            GetRequiredBoolean(
                result.StructuredContent!.Value,
                "canApply"));
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
            stateRoot);

        Directory.CreateDirectory(
            configDirectory);

        var samplePath = Path.Combine(
            workspaceRoot,
            "sample.txt");

        await File.WriteAllTextAsync(
            samplePath,
            "one\ntwo\nthree",
            new UTF8Encoding(false));

        var sampleHash =
            Convert.ToHexString(
                SHA256.HashData(
                    await File.ReadAllBytesAsync(
                        samplePath)));

        var configuration =
            new
            {
                SchemaVersion = 1,
                Agent = new
                {
                    Machine = new
                    {
                        Id =
                            $"file-patch-preview-{suffix}",
                        DisplayName =
                            $"File Patch Preview {suffix}"
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
                        $"codicks-file-patch-preview-{suffix}",
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
            sampleHash,
            sessionUnlocker,
            transport);
    }

    private static ValueTask<CallToolResult> CallPreviewAsync(
        McpClient client,
        string relativePath,
        string patch,
        string expectedHash) =>
        client.CallToolAsync(
            "file_patch_preview",
            new Dictionary<string, object?>
            {
                ["workspaceId"] =
                    "patch",
                ["relativePath"] =
                    relativePath,
                ["patch"] =
                    patch,
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

        Assert.NotNull(value);

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

    private static string Patch(
        params string[] lines) =>
        string.Join(
            '\n',
            lines);

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
        string SampleHash,
        TestSessionUnlocker SessionUnlocker,
        StdioClientTransport Transport);
}
