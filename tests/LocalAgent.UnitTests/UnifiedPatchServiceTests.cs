using LocalAgent.Core.Files;
using LocalAgent.Infrastructure.Files;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class UnifiedPatchServiceTests
{
    private readonly UnifiedPatchService _service = new();

    [Fact]
    public void Apply_ValidReplacement_ProducesProposedContent()
    {
        var result = Apply(
            "one\ntwo\nthree",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,3 +1,3 @@",
                " one",
                "-two",
                "+TWO",
                " three"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            "one\nTWO\nthree",
            result.ProposedContent);
        Assert.Equal(
            FilePatchError.None,
            result.Error);
        Assert.Equal(
            64,
            result.PatchSha256.Length);
    }

    [Fact]
    public void Apply_InsertionAndDeletion_AreAppliedStrictly()
    {
        var inserted = Apply(
            "one\ntwo",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,1 +1,2 @@",
                " one",
                "+middle"));

        var deleted = Apply(
            "one\ntwo",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,2 +1,1 @@",
                " one",
                "-two"));

        Assert.True(inserted.Success, inserted.Message);
        Assert.Equal(
            "one\nmiddle\ntwo",
            inserted.ProposedContent);

        Assert.True(deleted.Success, deleted.Message);
        Assert.Equal(
            "one",
            deleted.ProposedContent);
    }

    [Fact]
    public void Apply_ValidMultipleHunks_AreAppliedInOrder()
    {
        var current =
            string.Join(
                '\n',
                Enumerable.Range(1, 8)
                    .Select(
                        value =>
                            $"line-{value}"));

        var result = Apply(
            current,
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -2,1 +2,1 @@",
                "-line-2",
                "+changed-2",
                "@@ -7,1 +7,1 @@",
                "-line-7",
                "+changed-7"));

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            "changed-2",
            result.ProposedContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "changed-7",
            result.ProposedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "line-2",
            result.ProposedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "line-7",
            result.ProposedContent,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@@ bad @@")]
    [InlineData("@@ -0,1 +1,1 @@")]
    [InlineData("@@ -1,1 +0,1 @@")]
    public void Apply_MalformedHunkHeader_IsRejected(
        string hunkHeader)
    {
        var result = Apply(
            "one",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                hunkHeader,
                " one"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.InvalidPatch,
            result.Error);
    }

    [Fact]
    public void Apply_HunkCountsMustMatchBody()
    {
        var result = Apply(
            "one\ntwo",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,2 +1,2 @@",
                " one"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.InvalidPatch,
            result.Error);
        Assert.Contains(
            "counts",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_OutOfRangeHunk_IsConflict()
    {
        var result = Apply(
            "one",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -99,1 +99,1 @@",
                "-one",
                "+changed"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.Conflict,
            result.Error);
    }

    [Fact]
    public void Apply_ContextMismatch_IsConflict()
    {
        var result = Apply(
            "one\ntwo",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,2 +1,2 @@",
                " WRONG",
                " two"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.Conflict,
            result.Error);
        Assert.Contains(
            "context",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_DeletionMismatch_IsConflict()
    {
        var result = Apply(
            "one\ntwo",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -2,1 +2,1 @@",
                "-WRONG",
                "+changed"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.Conflict,
            result.Error);
        Assert.Contains(
            "deletion",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_OverlappingHunks_AreRejected()
    {
        var result = Apply(
            "one\ntwo\nthree",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,2 +1,2 @@",
                " one",
                " two",
                "@@ -2,1 +2,1 @@",
                "-two",
                "+changed"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.Conflict,
            result.Error);
        Assert.Contains(
            "overlap",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WrongTargetFile_IsRejected()
    {
        var result = Apply(
            "one",
            Patch(
                "--- a/other.txt",
                "+++ b/other.txt",
                "@@ -1,1 +1,1 @@",
                "-one",
                "+changed"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.InvalidPatch,
            result.Error);
        Assert.Contains(
            "targets",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/tmp/sample.txt")]
    [InlineData("../sample.txt")]
    [InlineData("folder/../sample.txt")]
    public void Apply_AbsoluteOrTraversalPatchTarget_IsRejected(
        string headerPath)
    {
        var result = Apply(
            "one",
            Patch(
                $"--- {headerPath}",
                $"+++ {headerPath}",
                "@@ -1,1 +1,1 @@",
                "-one",
                "+changed"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.InvalidPatch,
            result.Error);
    }

    [Fact]
    public void Apply_SecondFileHeaders_AreRejected()
    {
        var result = Apply(
            "one",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,1 +1,1 @@",
                "-one",
                "+changed",
                "--- a/second.txt",
                "+++ b/second.txt"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.InvalidPatch,
            result.Error);
        Assert.Contains(
            "one file",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_GitMetadata_IsRejected()
    {
        var result = Apply(
            "one",
            Patch(
                "diff --git a/sample.txt b/sample.txt",
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,1 +1,1 @@",
                "-one",
                "+changed"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.InvalidPatch,
            result.Error);
    }

    [Fact]
    public void Apply_Unicode_IsPreserved()
    {
        var result = Apply(
            "hello 🌏\nmañana",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -2,1 +2,2 @@",
                " mañana",
                "+日本語"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            "hello 🌏\nmañana\n日本語",
            result.ProposedContent);
    }

    [Fact]
    public void Apply_CrLfInput_MatchesLogicalLines()
    {
        var result = Apply(
            "one\r\ntwo\r\n",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,2 +1,2 @@",
                " one",
                "-two",
                "+changed"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            "one\nchanged\n",
            result.ProposedContent);
    }

    [Fact]
    public void Apply_EmptyFileInsertion_IsSupported()
    {
        var result = Apply(
            string.Empty,
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -0,0 +1,1 @@",
                "+hello"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            "hello",
            result.ProposedContent);
    }

    [Fact]
    public void Apply_DeleteOnlyContent_CanProduceEmptyFile()
    {
        var result = Apply(
            "hello",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,1 +0,0 @@",
                "-hello"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            string.Empty,
            result.ProposedContent);
    }

    [Fact]
    public void Apply_HeadersOnly_IsValidNoOp()
    {
        const string current =
            "one\r\ntwo\r\n";

        var result = Apply(
            current,
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            current,
            result.ProposedContent);
    }

    [Fact]
    public void Apply_OversizedPatch_IsRejectedBeforeParsing()
    {
        var patch =
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -1,1 +1,1 @@",
                "-one",
                $"+{new string('x', 512)}");

        var result =
            _service.Apply(
                new FilePatchRequest(
                    "sample.txt",
                    "one",
                    patch,
                    MaxPatchBytes: 64));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.TooLarge,
            result.Error);
        Assert.Equal(
            64,
            result.PatchSha256.Length);
    }

    [Fact]
    public void Apply_NewLineNumberMismatch_IsConflict()
    {
        var result = Apply(
            "one\ntwo",
            Patch(
                "--- a/sample.txt",
                "+++ b/sample.txt",
                "@@ -2,1 +99,1 @@",
                "-two",
                "+changed"));

        Assert.False(result.Success);
        Assert.Equal(
            FilePatchError.Conflict,
            result.Error);
        Assert.Contains(
            "new-file line numbers",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private FilePatchApplicationResult Apply(
        string currentContent,
        string patch) =>
        _service.Apply(
            new FilePatchRequest(
                "sample.txt",
                currentContent,
                patch,
                MaxPatchBytes: 1_048_576));

    private static string Patch(
        params string[] lines) =>
        string.Join(
            '\n',
            lines);
}
