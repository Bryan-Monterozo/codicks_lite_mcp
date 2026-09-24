using LocalAgent.Core.Files;
using LocalAgent.Infrastructure.Files;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class FileDiffServiceTests
{
    private readonly FileDiffService _service = new();

    [Fact]
    public void NoChange_ReturnsNoDiff()
    {
        var result = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            "one\ntwo",
            "one\ntwo"));

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.Additions);
        Assert.Equal(0, result.Deletions);
        Assert.Empty(result.Hunks);
        Assert.Empty(result.UnifiedDiff);
        Assert.Equal(result.BaseSha256, result.ProposedSha256);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Insertion_IsReported()
    {
        var result = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            "one\nthree",
            "one\ntwo\nthree"));

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.Additions);
        Assert.Equal(0, result.Deletions);
        Assert.Contains("+two", result.UnifiedDiff);
    }

    [Fact]
    public void Deletion_IsReported()
    {
        var result = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            "one\ntwo\nthree",
            "one\nthree"));

        Assert.True(result.HasChanges);
        Assert.Equal(0, result.Additions);
        Assert.Equal(1, result.Deletions);
        Assert.Contains("-two", result.UnifiedDiff);
    }

    [Fact]
    public void Replacement_IsReportedAsDeleteAndAdd()
    {
        var result = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            "one\nold\nthree",
            "one\nnew\nthree"));

        Assert.Equal(1, result.Additions);
        Assert.Equal(1, result.Deletions);
        Assert.Contains("-old", result.UnifiedDiff);
        Assert.Contains("+new", result.UnifiedDiff);
    }

    [Fact]
    public void SeparatedChanges_CreateSeparatedHunks()
    {
        var current = string.Join(
            '\n',
            Enumerable.Range(1, 10).Select(value => $"line-{value}"));

        var proposed = current
            .Replace("line-2", "changed-2", StringComparison.Ordinal)
            .Replace("line-9", "changed-9", StringComparison.Ordinal);

        var result = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            current,
            proposed,
            ContextLines: 1));

        Assert.Equal(2, result.Hunks.Count);
        Assert.Equal(2, result.Additions);
        Assert.Equal(2, result.Deletions);
    }

    [Fact]
    public void BeginningAndEndInsertions_AreReported()
    {
        var result = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            "middle",
            "start\nmiddle\nend"));

        Assert.Equal(2, result.Additions);
        Assert.Equal(0, result.Deletions);
        Assert.Contains("+start", result.UnifiedDiff);
        Assert.Contains("+end", result.UnifiedDiff);
    }

    [Fact]
    public void EmptyContent_IsHandled()
    {
        var added = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            string.Empty,
            "hello"));

        var removed = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            "hello",
            string.Empty));

        Assert.Equal(1, added.Additions);
        Assert.Equal(0, added.Deletions);
        Assert.Equal(0, removed.Additions);
        Assert.Equal(1, removed.Deletions);
    }

    [Fact]
    public void LineEndingOnlyChange_IsExplicitlyReported()
    {
        var result = _service.CreateDiff(new FileDiffRequest(
            "sample.txt",
            "one\r\ntwo\r\n",
            "one\ntwo\n"));

        Assert.True(result.HasChanges);
        Assert.Equal("crlf", result.SourceLineEnding);
        Assert.Equal("lf", result.ProposedLineEnding);
        Assert.NotEmpty(result.Hunks);
        Assert.True(result.Additions > 0);
        Assert.True(result.Deletions > 0);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains(
                "Line endings change",
                StringComparison.Ordinal));
    }

    [Fact]
    public void UnicodeAndRepeatedCalls_AreDeterministic()
    {
        var request = new FileDiffRequest(
            "unicode.txt",
            "hello 🌏\nmañana",
            "hello 🌏\nmañana!\n日本語");

        var first = _service.CreateDiff(request);
        var second = _service.CreateDiff(request);

        Assert.Contains("+mañana!", first.UnifiedDiff);
        Assert.Contains("+日本語", first.UnifiedDiff);
        Assert.Equal(first.BaseSha256, second.BaseSha256);
        Assert.Equal(first.ProposedSha256, second.ProposedSha256);
        Assert.Equal(first.UnifiedDiff, second.UnifiedDiff);
        Assert.Equal(first.Additions, second.Additions);
        Assert.Equal(first.Deletions, second.Deletions);
    }

    [Fact]
    public void LargeDiff_RemainsBoundedAndMarksTruncation()
    {
        var current = string.Join(
            '\n',
            Enumerable.Range(0, 1600).Select(value => $"old-{value}"));

        var proposed = string.Join(
            '\n',
            Enumerable.Range(0, 1600).Select(value => $"new-{value}"));

        var result = _service.CreateDiff(new FileDiffRequest(
            "large.txt",
            current,
            proposed,
            ContextLines: 1,
            MaxUnifiedDiffChars: 512,
            MaxStructuredLines: 25));

        Assert.True(result.HasChanges);
        Assert.True(result.Truncated);
        Assert.True(result.UnifiedDiff.Length <= 512);
        Assert.True(result.Hunks.Sum(hunk => hunk.Lines.Count) <= 25);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains(
                "Detailed comparison limit exceeded",
                StringComparison.Ordinal));
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains(
                "truncated",
                StringComparison.OrdinalIgnoreCase));
    }
}
