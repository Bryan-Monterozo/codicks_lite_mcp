using System.Text.Json;
using LocalAgent.Core.Files;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class ReviewContractSerializationTests
{
    private static readonly JsonSerializerOptions WebOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void FileDiffResult_SerializesStableReviewContract()
    {
        var result =
            new FileDiffResult(
                "src/sample.cs",
                new string('A', 64),
                new string('B', 64),
                HasChanges: true,
                Additions: 1,
                Deletions: 1,
                UnchangedLines: 2,
                UnifiedDiff:
                    "--- a/src/sample.cs\n+++ b/src/sample.cs\n@@ -1,1 +1,1 @@\n-old\n+new\n",
                Hunks:
                [
                    new FileDiffHunk(
                        1,
                        1,
                        1,
                        1,
                        [
                            new FileDiffLine(
                                FileDiffLineKind.Deletion,
                                "old",
                                1,
                                null),
                            new FileDiffLine(
                                FileDiffLineKind.Addition,
                                "new",
                                null,
                                1)
                        ])
                ],
                SourceEncoding: "utf-8",
                SourceLineEnding: "lf",
                ProposedLineEnding: "lf",
                Truncated: false,
                Warnings: [],
                ReviewState: "preview",
                ReviewSummary:
                    "File: src/sample.cs\nState: PREVIEW — not applied");

        var json =
            JsonSerializer.SerializeToElement(
                result,
                WebOptions);

        Assert.Equal(
            "src/sample.cs",
            json.GetProperty("relativePath").GetString());

        Assert.Equal(
            "preview",
            json.GetProperty("reviewState").GetString());

        Assert.Contains(
            "PREVIEW — not applied",
            json.GetProperty("reviewSummary").GetString(),
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            json.GetProperty("additions").GetInt32());

        Assert.Equal(
            1,
            json.GetProperty("deletions").GetInt32());

        Assert.False(
            json.GetProperty("truncated").GetBoolean());

        var hunks =
            json.GetProperty("hunks");

        Assert.Equal(
            1,
            hunks.GetArrayLength());

        var lines =
            hunks[0].GetProperty("lines");

        Assert.Equal(
            "delete",
            lines[0].GetProperty("kind").GetString());

        Assert.Equal(
            "add",
            lines[1].GetProperty("kind").GetString());

        Assert.True(
            json.TryGetProperty(
                "warnings",
                out _));
    }

    [Fact]
    public void FilePatchPreviewResult_SerializesStableReviewContract()
    {
        var expires =
            new DateTimeOffset(
                2026,
                9,
                24,
                12,
                0,
                0,
                TimeSpan.Zero);

        var result =
            new FilePatchPreviewResult(
                "src/sample.cs",
                new string('A', 64),
                new string('B', 64),
                new string('C', 64),
                CanApply: true,
                Additions: 2,
                Deletions: 1,
                UnifiedDiff:
                    "--- a/src/sample.cs\n+++ b/src/sample.cs\n",
                Hunks: [],
                SourceEncoding: "utf-8",
                SourceLineEnding: "lf",
                ProposedLineEnding: "lf",
                Truncated: false,
                DiffIncluded: true,
                Warnings: [],
                ReviewToken: "review-token",
                ReviewExpiresAtUtc: expires,
                ReviewState: "preview",
                ReviewSummary:
                    "File: src/sample.cs\nState: PREVIEW — not applied\nReview token expires: 2026-09-24T12:00:00.0000000+00:00");

        var json =
            JsonSerializer.SerializeToElement(
                result,
                WebOptions);

        var requiredProperties =
            new[]
            {
                "relativePath",
                "baseSha256",
                "proposedSha256",
                "patchSha256",
                "canApply",
                "additions",
                "deletions",
                "unifiedDiff",
                "hunks",
                "sourceEncoding",
                "sourceLineEnding",
                "proposedLineEnding",
                "truncated",
                "diffIncluded",
                "warnings",
                "reviewToken",
                "reviewExpiresAtUtc",
                "reviewState",
                "reviewSummary"
            };

        foreach (var propertyName in
                 requiredProperties)
        {
            Assert.True(
                json.TryGetProperty(
                    propertyName,
                    out _),
                $"Missing serialized review property '{propertyName}'.");
        }

        Assert.Equal(
            "preview",
            json.GetProperty("reviewState").GetString());

        Assert.True(
            json.GetProperty("canApply").GetBoolean());

        Assert.Equal(
            "review-token",
            json.GetProperty("reviewToken").GetString());

        Assert.Contains(
            "PREVIEW — not applied",
            json.GetProperty("reviewSummary").GetString(),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "\u001b",
            json.GetProperty("reviewSummary").GetString(),
            StringComparison.Ordinal);
    }
    [Fact]
    public void FilePatchApplyResult_SerializesAppliedStateContract()
    {
        var result =
            new FilePatchApplyResult(
                "src/sample.cs",
                123,
                new string('D', 64),
                "utf-8",
                "lf",
                "backup-123",
                ApplyState: "applied",
                ApplySummary:
                    "File: src/sample.cs\nState: APPLIED\nNew SHA-256: hash\nBackup: backup-123");

        var json =
            JsonSerializer.SerializeToElement(
                result,
                WebOptions);

        var requiredProperties =
            new[]
            {
                "relativePath",
                "sizeBytes",
                "sha256",
                "encoding",
                "lineEnding",
                "backupId",
                "applyState",
                "applySummary"
            };

        foreach (var propertyName in
                 requiredProperties)
        {
            Assert.True(
                json.TryGetProperty(
                    propertyName,
                    out _),
                $"Missing serialized apply property '{propertyName}'.");
        }

        Assert.Equal(
            "applied",
            json.GetProperty("applyState").GetString());

        Assert.Contains(
            "State: APPLIED",
            json.GetProperty("applySummary").GetString(),
            StringComparison.Ordinal);

        Assert.Contains(
            "Backup: backup-123",
            json.GetProperty("applySummary").GetString(),
            StringComparison.Ordinal);
    }

}
