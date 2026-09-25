using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Host;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LocalAgent.IntegrationTests;

public sealed class V12ReviewMcpHardeningIntegrationTests : IDisposable
{
    private readonly string _root =
        TestSessionUnlocker.CreateShortRoot("v12h");

    [Fact]
    [Trait("Chunk", "09")]
    public async Task ReviewTools_AuditStableErrorsAndRedactSensitiveInputs()
    {
        const string proposedSecret =
            "V12-PROPOSED-SECRET-61f7";

        const string patchSecret =
            "V12-PATCH-SECRET-b582";

        var fixture =
            await CreateHostFixtureAsync(
                "audit");

        await using var client =
            await McpClient.CreateAsync(
                fixture.Transport);

        await fixture.SessionUnlocker.UnlockFullAsync(
            fixture.StateRoot);

        var diff =
            await client.CallToolAsync(
                "file_diff",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "review",
                    ["relativePath"] = "sample.txt",
                    ["content"] =
                        $"one\n{proposedSecret}\n",
                    ["expectedHash"] =
                        fixture.SampleHash
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolSuccess(
            diff,
            "file_diff");

        var patch =
            ReplacementPatch(
                "sample.txt",
                "two",
                patchSecret,
                oldStart: 2);

        var preview =
            await CallPreviewAsync(
                client,
                "sample.txt",
                patch,
                fixture.SampleHash);

        AssertToolSuccess(
            preview,
            "file_patch_preview");

        var reviewToken =
            GetRequiredString(
                preview.StructuredContent!.Value,
                "reviewToken");

        var changedPatch =
            ReplacementPatch(
                "sample.txt",
                "two",
                "different-value",
                oldStart: 2);

        var changedApply =
            await CallApplyAsync(
                client,
                "sample.txt",
                changedPatch,
                fixture.SampleHash,
                reviewToken);

        AssertToolErrorContains(
            changedApply,
            "INVALID_REVIEW_TOKEN");

        var apply =
            await CallApplyAsync(
                client,
                "sample.txt",
                patch,
                fixture.SampleHash,
                reviewToken);

        AssertToolSuccess(
            apply,
            "file_patch_apply");

        var backupId =
            GetRequiredString(
                apply.StructuredContent!.Value,
                "backupId");

        var replay =
            await CallApplyAsync(
                client,
                "sample.txt",
                patch,
                fixture.SampleHash,
                reviewToken);

        AssertToolErrorContains(
            replay,
            "CONSUMED_REVIEW_TOKEN");

        var stale =
            await CallPreviewAsync(
                client,
                "largepatch.txt",
                HeaderOnlyPatch(
                    "largepatch.txt"),
                new string('0', 64));

        AssertToolErrorContains(
            stale,
            "CONFLICT");

        var malformed =
            await CallPreviewAsync(
                client,
                "largepatch.txt",
                string.Join(
                    '\n',
                    "--- a/largepatch.txt",
                    "+++ b/largepatch.txt",
                    "@@ bad @@"),
                fixture.LargePatchHash);

        AssertToolErrorContains(
            malformed,
            "INVALID_PATCH");

        var patchConflict =
            await CallPreviewAsync(
                client,
                "largepatch.txt",
                string.Join(
                    '\n',
                    "--- a/largepatch.txt",
                    "+++ b/largepatch.txt",
                    "@@ -1,1 +1,1 @@",
                    "-WRONG",
                    "+changed"),
                fixture.LargePatchHash);

        AssertToolErrorContains(
            patchConflict,
            "PATCH_CONFLICT");

        var denied =
            await client.CallToolAsync(
                "file_diff",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "review",
                    ["relativePath"] = ".env",
                    ["content"] =
                        "SECRET=changed"
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolErrorContains(
            denied,
            "ACCESS_DENIED");

        var traversal =
            await client.CallToolAsync(
                "file_diff",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "review",
                    ["relativePath"] =
                        "../outside.txt",
                    ["content"] =
                        "changed"
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolErrorContains(
            traversal,
            "INVALID_PATH");

        var unsupported =
            await client.CallToolAsync(
                "file_diff",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "review",
                    ["relativePath"] =
                        "unsupported.txt",
                    ["content"] =
                        "changed"
                },
                cancellationToken:
                    CancellationToken.None);

        AssertToolErrorContains(
            unsupported,
            "UNSUPPORTED_FILE_TYPE");

        var hugePatch =
            string.Join(
                '\n',
                "--- a/largepatch.txt",
                "+++ b/largepatch.txt",
                "@@ -1,1 +1,1 @@",
                "-base",
                $"+{new string('x', 70_000)}");

        var oversized =
            await CallPreviewAsync(
                client,
                "largepatch.txt",
                hugePatch,
                fixture.LargePatchHash);

        AssertToolErrorContains(
            oversized,
            "PATCH_TOO_LARGE");

        var currentHash =
            HashFile(
                fixture.SamplePath);

        var reviewRequired =
            await CallApplyAsync(
                client,
                "sample.txt",
                HeaderOnlyPatch(
                    "sample.txt"),
                currentHash,
                string.Empty);

        AssertToolErrorContains(
            reviewRequired,
            "REVIEW_REQUIRED");

        var invalidToken =
            await CallApplyAsync(
                client,
                "sample.txt",
                HeaderOnlyPatch(
                    "sample.txt"),
                currentHash,
                "unknown-token");

        AssertToolErrorContains(
            invalidToken,
            "INVALID_REVIEW_TOKEN");

        var auditPath =
            Path.Combine(
                fixture.StateRoot,
                "audit",
                "operations.jsonl");

        Assert.True(
            File.Exists(
                auditPath));

        var auditText =
            await File.ReadAllTextAsync(
                auditPath);

        Assert.DoesNotContain(
            proposedSecret,
            auditText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            patchSecret,
            auditText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            reviewToken,
            auditText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "different-value",
            auditText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "SECRET=changed",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "INVALID_REVIEW_TOKEN",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "CONSUMED_REVIEW_TOKEN",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "PATCH_CONFLICT",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "PATCH_TOO_LARGE",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            "UNSUPPORTED_FILE_TYPE",
            auditText,
            StringComparison.Ordinal);

        Assert.Contains(
            backupId,
            auditText,
            StringComparison.Ordinal);

        var reviewRecords =
            (await File.ReadAllLinesAsync(
                auditPath))
            .Select(
                line =>
                    JsonDocument.Parse(line))
            .Where(
                document =>
                    document.RootElement
                        .TryGetProperty(
                            "Operation",
                            out var operation) &&
                    operation.GetString() is
                        "file_diff" or
                        "file_patch_preview" or
                        "file_patch_apply")
            .ToArray();

        try
        {
            Assert.NotEmpty(
                reviewRecords);

            foreach (var document in
                     reviewRecords)
            {
                var root =
                    document.RootElement;

                Assert.False(
                    root.TryGetProperty(
                        "Content",
                        out _));

                Assert.False(
                    root.TryGetProperty(
                        "Patch",
                        out _));

                Assert.False(
                    root.TryGetProperty(
                        "ReviewToken",
                        out _));

                Assert.False(
                    root.TryGetProperty(
                        "UnifiedDiff",
                        out _));

                Assert.False(
                    root.TryGetProperty(
                        "Hunks",
                        out _));
            }

            Assert.Contains(
                reviewRecords,
                document =>
                    document.RootElement
                        .GetProperty("Operation")
                        .GetString() ==
                            "file_diff" &&
                    document.RootElement
                        .GetProperty("Preview")
                        .GetBoolean());

            Assert.Contains(
                reviewRecords,
                document =>
                    document.RootElement
                        .GetProperty("Operation")
                        .GetString() ==
                            "file_patch_apply" &&
                    !document.RootElement
                        .GetProperty("Preview")
                        .GetBoolean() &&
                    document.RootElement
                        .GetProperty("Success")
                        .GetBoolean() &&
                    document.RootElement
                        .GetProperty("BackupId")
                        .GetString() ==
                            backupId);
        }
        finally
        {
            foreach (var document in
                     reviewRecords)
            {
                document.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Chunk", "09")]
    public async Task ReviewTools_AuditLockedAndReadOnlySessionDenials()
    {
        const string secretPatchValue =
            "LOCKED-PATCH-SECRET-935d";

        const string secretToken =
            "LOCKED-REVIEW-TOKEN-4b16";

        var lockedFixture =
            await CreateHostFixtureAsync(
                "locked");

        await using (var lockedClient =
                     await McpClient.CreateAsync(
                         lockedFixture.Transport))
        {
            var lockedDiff =
                await lockedClient.CallToolAsync(
                    "file_diff",
                    new Dictionary<string, object?>
                    {
                        ["workspaceId"] = "review",
                        ["relativePath"] = "sample.txt",
                        ["content"] =
                            "LOCKED-PROPOSED-SECRET"
                    },
                    cancellationToken:
                        CancellationToken.None);

            AssertToolErrorContains(
                lockedDiff,
                "SESSION_LOCKED");

            var lockedApply =
                await CallApplyAsync(
                    lockedClient,
                    "sample.txt",
                    ReplacementPatch(
                        "sample.txt",
                        "two",
                        secretPatchValue,
                        oldStart: 2),
                    lockedFixture.SampleHash,
                    secretToken);

            AssertToolErrorContains(
                lockedApply,
                "SESSION_LOCKED");
        }

        var lockedAuditPath =
            Path.Combine(
                lockedFixture.StateRoot,
                "audit",
                "operations.jsonl");

        var lockedAudit =
            await File.ReadAllTextAsync(
                lockedAuditPath);

        Assert.Contains(
            "SESSION_LOCKED",
            lockedAudit,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "LOCKED-PROPOSED-SECRET",
            lockedAudit,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            secretPatchValue,
            lockedAudit,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            secretToken,
            lockedAudit,
            StringComparison.Ordinal);

        var readOnlyFixture =
            await CreateHostFixtureAsync(
                "readonly");

        await using (var readOnlyClient =
                     await McpClient.CreateAsync(
                         readOnlyFixture.Transport))
        {
            await readOnlyFixture.SessionUnlocker
                .UnlockReadOnlyAsync(
                    readOnlyFixture.StateRoot);

            var readOnlyApply =
                await CallApplyAsync(
                    readOnlyClient,
                    "sample.txt",
                    ReplacementPatch(
                        "sample.txt",
                        "two",
                        secretPatchValue,
                        oldStart: 2),
                    readOnlyFixture.SampleHash,
                    secretToken);

            AssertToolErrorContains(
                readOnlyApply,
                "SESSION_READ_ONLY");
        }

        var readOnlyAudit =
            await File.ReadAllTextAsync(
                Path.Combine(
                    readOnlyFixture.StateRoot,
                    "audit",
                    "operations.jsonl"));

        Assert.Contains(
            "SESSION_READ_ONLY",
            readOnlyAudit,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            secretPatchValue,
            readOnlyAudit,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            secretToken,
            readOnlyAudit,
            StringComparison.Ordinal);
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
            stateRoot);

        Directory.CreateDirectory(
            configDirectory);

        var samplePath =
            Path.Combine(
                workspaceRoot,
                "sample.txt");

        var largePatchPath =
            Path.Combine(
                workspaceRoot,
                "largepatch.txt");

        await File.WriteAllTextAsync(
            samplePath,
            "one\ntwo\n",
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            largePatchPath,
            "base",
            new UTF8Encoding(false));

        await File.WriteAllTextAsync(
            Path.Combine(
                workspaceRoot,
                ".env"),
            "SECRET=denied",
            new UTF8Encoding(false));

        await File.WriteAllBytesAsync(
            Path.Combine(
                workspaceRoot,
                "unsupported.txt"),
            [0xFF, 0xFE, 0x00, 0x41]);

        var configuration =
            new
            {
                SchemaVersion = 1,
                Agent = new
                {
                    Machine = new
                    {
                        Id =
                            $"v12-review-hardening-{suffix}",
                        DisplayName =
                            $"V12 Review Hardening {suffix}"
                    },
                    ReadOnly = false,
                    StateDirectory =
                        stateRoot,
                    Limits = new
                    {
                        MaxEditableFileBytes =
                            1024,
                        MaxReadResponseBytes =
                            512,
                        MaxDirectoryEntries =
                            100,
                        MaxSearchResults =
                            100,
                        MaxTraversalDepth =
                            12,
                        OperationTimeoutSeconds =
                            15,
                        MaxConcurrentMutations =
                            1
                    },
                    Workspaces =
                        new Dictionary<string, object>
                        {
                            ["review"] =
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
                        $"codicks-v12-review-hardening-{suffix}",
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

        return new HostFixture(
            samplePath,
            HashFile(samplePath),
            HashFile(largePatchPath),
            stateRoot,
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
                ["workspaceId"] = "review",
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
        string relativePath,
        string patch,
        string expectedHash,
        string reviewToken) =>
        client.CallToolAsync(
            "file_patch_apply",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = "review",
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
        string toolName)
    {
        Assert.False(
            result.IsError is true,
            $"{toolName} failed: {GetResultText(result)}");

        Assert.True(
            result.StructuredContent.HasValue,
            $"{toolName} did not return structuredContent.");
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
        foreach (var property in
                 content.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value =
                property.Value.GetString();

            Assert.False(
                string.IsNullOrWhiteSpace(
                    value));

            return value!;
        }

        throw new Xunit.Sdk.XunitException(
            $"Structured content did not contain '{propertyName}'.");
    }

    private static string HeaderOnlyPatch(
        string relativePath) =>
        string.Join(
            '\n',
            $"--- a/{relativePath}",
            $"+++ b/{relativePath}");

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
        try
        {
            if (Directory.Exists(
                    _root))
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

    private sealed record HostFixture(
        string SamplePath,
        string SampleHash,
        string LargePatchHash,
        string StateRoot,
        TestSessionUnlocker SessionUnlocker,
        StdioClientTransport Transport);
}
