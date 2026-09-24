using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Files;

namespace LocalAgent.Infrastructure.Files;

public sealed class FileDiffService : IFileDiffService
{
    private const long MaxLcsCells = 2_000_000;

    public FileDiffResult CreateDiff(FileDiffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RelativePath))
        {
            throw new ArgumentException("RelativePath is required.", nameof(request));
        }

        if (request.ContextLines < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "ContextLines cannot be negative.");
        }

        if (request.MaxUnifiedDiffChars < 256)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxUnifiedDiffChars must be at least 256.");
        }

        if (request.MaxStructuredLines < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxStructuredLines must be at least 1.");
        }

        var baseHash = ComputeSha256(request.CurrentContent);
        var proposedHash = ComputeSha256(request.ProposedContent);
        var sourceLineEnding = DetectLineEnding(request.CurrentContent);
        var proposedLineEnding = DetectLineEnding(request.ProposedContent);

        if (string.Equals(
                request.CurrentContent,
                request.ProposedContent,
                StringComparison.Ordinal))
        {
            return new FileDiffResult(
                request.RelativePath,
                baseHash,
                proposedHash,
                HasChanges: false,
                Additions: 0,
                Deletions: 0,
                UnchangedLines: SplitLines(request.CurrentContent).Length,
                UnifiedDiff: string.Empty,
                Hunks: Array.Empty<FileDiffHunk>(),
                SourceEncoding: "utf-8",
                sourceLineEnding,
                proposedLineEnding,
                Truncated: false,
                Warnings: Array.Empty<string>());
        }

        var oldLines = SplitLines(request.CurrentContent);
        var newLines = SplitLines(request.ProposedContent);

        var steps = BuildSteps(oldLines, newLines, out var simplified);

        // A line-based comparison normalizes newline delimiters. If the only
        // difference is newline style, show it as a full replacement so the
        // review still makes the byte-level change explicit.
        if (steps.Count > 0 &&
            steps.All(step => step.Kind == FileDiffLineKind.Context))
        {
            steps = BuildWholeReplacement(oldLines, newLines);
        }

        var warnings = new List<string>();

        if (!string.Equals(
                sourceLineEnding,
                proposedLineEnding,
                StringComparison.Ordinal))
        {
            warnings.Add(
                $"Line endings change from {sourceLineEnding} to {proposedLineEnding}.");
        }

        if (simplified)
        {
            warnings.Add(
                "Detailed comparison limit exceeded; the changed middle region is represented as a deterministic replacement.");
        }

        var fullHunks = BuildHunks(steps, request.ContextLines);
        var limitedHunks = LimitStructuredHunks(
            fullHunks,
            request.MaxStructuredLines,
            out var structuredTruncated);

        var unifiedDiff = BuildUnifiedDiff(
            request.RelativePath,
            fullHunks,
            request.MaxUnifiedDiffChars,
            out var unifiedTruncated);

        var truncated = structuredTruncated || unifiedTruncated;
        if (truncated)
        {
            warnings.Add("Diff output was truncated to configured response limits.");
        }

        return new FileDiffResult(
            request.RelativePath,
            baseHash,
            proposedHash,
            HasChanges: true,
            Additions: steps.Count(step => step.Kind == FileDiffLineKind.Addition),
            Deletions: steps.Count(step => step.Kind == FileDiffLineKind.Deletion),
            UnchangedLines: steps.Count(step => step.Kind == FileDiffLineKind.Context),
            unifiedDiff,
            limitedHunks,
            SourceEncoding: "utf-8",
            sourceLineEnding,
            proposedLineEnding,
            truncated,
            warnings);
    }

    private static List<DiffStep> BuildSteps(
        string[] oldLines,
        string[] newLines,
        out bool simplified)
    {
        var cells = (long)(oldLines.Length + 1) * (newLines.Length + 1);
        if (cells > MaxLcsCells)
        {
            simplified = true;
            return BuildSimplifiedSteps(oldLines, newLines);
        }

        simplified = false;
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];

        for (var oldIndex = oldLines.Length - 1; oldIndex >= 0; oldIndex--)
        {
            for (var newIndex = newLines.Length - 1; newIndex >= 0; newIndex--)
            {
                lcs[oldIndex, newIndex] =
                    string.Equals(
                        oldLines[oldIndex],
                        newLines[newIndex],
                        StringComparison.Ordinal)
                        ? lcs[oldIndex + 1, newIndex + 1] + 1
                        : Math.Max(
                            lcs[oldIndex + 1, newIndex],
                            lcs[oldIndex, newIndex + 1]);
            }
        }

        var steps = new List<DiffStep>();
        var oldPosition = 0;
        var newPosition = 0;

        while (oldPosition < oldLines.Length &&
               newPosition < newLines.Length)
        {
            if (string.Equals(
                    oldLines[oldPosition],
                    newLines[newPosition],
                    StringComparison.Ordinal))
            {
                steps.Add(new DiffStep(
                    FileDiffLineKind.Context,
                    oldLines[oldPosition]));
                oldPosition++;
                newPosition++;
                continue;
            }

            if (lcs[oldPosition + 1, newPosition] >=
                lcs[oldPosition, newPosition + 1])
            {
                steps.Add(new DiffStep(
                    FileDiffLineKind.Deletion,
                    oldLines[oldPosition]));
                oldPosition++;
            }
            else
            {
                steps.Add(new DiffStep(
                    FileDiffLineKind.Addition,
                    newLines[newPosition]));
                newPosition++;
            }
        }

        while (oldPosition < oldLines.Length)
        {
            steps.Add(new DiffStep(
                FileDiffLineKind.Deletion,
                oldLines[oldPosition++]));
        }

        while (newPosition < newLines.Length)
        {
            steps.Add(new DiffStep(
                FileDiffLineKind.Addition,
                newLines[newPosition++]));
        }

        return steps;
    }

    private static List<DiffStep> BuildSimplifiedSteps(
        string[] oldLines,
        string[] newLines)
    {
        var prefix = 0;
        while (prefix < oldLines.Length &&
               prefix < newLines.Length &&
               string.Equals(
                   oldLines[prefix],
                   newLines[prefix],
                   StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix &&
               suffix < newLines.Length - prefix &&
               string.Equals(
                   oldLines[oldLines.Length - 1 - suffix],
                   newLines[newLines.Length - 1 - suffix],
                   StringComparison.Ordinal))
        {
            suffix++;
        }

        var steps = new List<DiffStep>(
            oldLines.Length + newLines.Length);

        for (var index = 0; index < prefix; index++)
        {
            steps.Add(new DiffStep(
                FileDiffLineKind.Context,
                oldLines[index]));
        }

        for (var index = prefix;
             index < oldLines.Length - suffix;
             index++)
        {
            steps.Add(new DiffStep(
                FileDiffLineKind.Deletion,
                oldLines[index]));
        }

        for (var index = prefix;
             index < newLines.Length - suffix;
             index++)
        {
            steps.Add(new DiffStep(
                FileDiffLineKind.Addition,
                newLines[index]));
        }

        for (var index = oldLines.Length - suffix;
             index < oldLines.Length;
             index++)
        {
            steps.Add(new DiffStep(
                FileDiffLineKind.Context,
                oldLines[index]));
        }

        return steps;
    }

    private static List<DiffStep> BuildWholeReplacement(
        string[] oldLines,
        string[] newLines)
    {
        var steps = new List<DiffStep>(
            oldLines.Length + newLines.Length);

        foreach (var line in oldLines)
        {
            steps.Add(new DiffStep(FileDiffLineKind.Deletion, line));
        }

        foreach (var line in newLines)
        {
            steps.Add(new DiffStep(FileDiffLineKind.Addition, line));
        }

        return steps;
    }

    private static IReadOnlyList<FileDiffHunk> BuildHunks(
        IReadOnlyList<DiffStep> steps,
        int contextLines)
    {
        var changeIndexes = Enumerable.Range(0, steps.Count)
            .Where(index => steps[index].Kind != FileDiffLineKind.Context)
            .ToArray();

        if (changeIndexes.Length == 0)
        {
            return Array.Empty<FileDiffHunk>();
        }

        var ranges = new List<(int Start, int End)>();

        foreach (var changeIndex in changeIndexes)
        {
            var start = Math.Max(0, changeIndex - contextLines);
            var end = Math.Min(steps.Count - 1, changeIndex + contextLines);

            if (ranges.Count == 0 || start > ranges[^1].End + 1)
            {
                ranges.Add((start, end));
            }
            else
            {
                var previous = ranges[^1];
                ranges[^1] = (
                    previous.Start,
                    Math.Max(previous.End, end));
            }
        }

        var oldPrefix = new int[steps.Count + 1];
        var newPrefix = new int[steps.Count + 1];

        for (var index = 0; index < steps.Count; index++)
        {
            oldPrefix[index + 1] =
                oldPrefix[index] +
                (steps[index].Kind == FileDiffLineKind.Addition ? 0 : 1);

            newPrefix[index + 1] =
                newPrefix[index] +
                (steps[index].Kind == FileDiffLineKind.Deletion ? 0 : 1);
        }

        var hunks = new List<FileDiffHunk>(ranges.Count);

        foreach (var range in ranges)
        {
            var oldCount =
                oldPrefix[range.End + 1] - oldPrefix[range.Start];
            var newCount =
                newPrefix[range.End + 1] - newPrefix[range.Start];

            var oldStart = oldCount == 0
                ? oldPrefix[range.Start]
                : oldPrefix[range.Start] + 1;

            var newStart = newCount == 0
                ? newPrefix[range.Start]
                : newPrefix[range.Start] + 1;

            var lines = new List<FileDiffLine>(
                range.End - range.Start + 1);

            for (var index = range.Start;
                 index <= range.End;
                 index++)
            {
                var step = steps[index];

                lines.Add(step.Kind switch
                {
                    FileDiffLineKind.Addition => new FileDiffLine(
                        step.Kind,
                        step.Text,
                        OldLineNumber: null,
                        NewLineNumber: newPrefix[index] + 1),

                    FileDiffLineKind.Deletion => new FileDiffLine(
                        step.Kind,
                        step.Text,
                        OldLineNumber: oldPrefix[index] + 1,
                        NewLineNumber: null),

                    _ => new FileDiffLine(
                        step.Kind,
                        step.Text,
                        OldLineNumber: oldPrefix[index] + 1,
                        NewLineNumber: newPrefix[index] + 1)
                });
            }

            hunks.Add(new FileDiffHunk(
                oldStart,
                oldCount,
                newStart,
                newCount,
                lines));
        }

        return hunks;
    }

    private static IReadOnlyList<FileDiffHunk> LimitStructuredHunks(
        IReadOnlyList<FileDiffHunk> hunks,
        int maxLines,
        out bool truncated)
    {
        var totalLines = hunks.Sum(hunk => hunk.Lines.Count);
        if (totalLines <= maxLines)
        {
            truncated = false;
            return hunks;
        }

        truncated = true;
        var remaining = maxLines;
        var result = new List<FileDiffHunk>();

        foreach (var hunk in hunks)
        {
            if (remaining == 0)
            {
                break;
            }

            if (hunk.Lines.Count <= remaining)
            {
                result.Add(hunk);
                remaining -= hunk.Lines.Count;
                continue;
            }

            result.Add(hunk with
            {
                Lines = hunk.Lines.Take(remaining).ToArray()
            });
            remaining = 0;
        }

        return result;
    }

    private static string BuildUnifiedDiff(
        string relativePath,
        IReadOnlyList<FileDiffHunk> hunks,
        int maxChars,
        out bool truncated)
    {
        const string truncationMarker = "... diff truncated ...\n";
        var payloadLimit = maxChars - truncationMarker.Length;
        var builder = new StringBuilder();
        var normalizedPath = relativePath.Replace('\\', '/');

        truncated =
            !TryAppend(
                builder,
                $"--- a/{normalizedPath}\n",
                payloadLimit) ||
            !TryAppend(
                builder,
                $"+++ b/{normalizedPath}\n",
                payloadLimit);

        if (!truncated)
        {
            foreach (var hunk in hunks)
            {
                if (!TryAppend(
                        builder,
                        $"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@\n",
                        payloadLimit))
                {
                    truncated = true;
                    break;
                }

                foreach (var line in hunk.Lines)
                {
                    var prefix = line.Kind switch
                    {
                        FileDiffLineKind.Addition => '+',
                        FileDiffLineKind.Deletion => '-',
                        _ => ' '
                    };

                    if (!TryAppend(
                            builder,
                            $"{prefix}{line.Text}\n",
                            payloadLimit))
                    {
                        truncated = true;
                        break;
                    }
                }

                if (truncated)
                {
                    break;
                }
            }
        }

        if (truncated)
        {
            builder.Append(truncationMarker);
        }

        return builder.ToString();
    }

    private static bool TryAppend(
        StringBuilder builder,
        string value,
        int limit)
    {
        if (builder.Length + value.Length > limit)
        {
            return false;
        }

        builder.Append(value);
        return true;
    }

    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static string DetectLineEnding(string text)
    {
        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var withoutCrLf = text.Replace(
            "\r\n",
            string.Empty,
            StringComparison.Ordinal);
        var hasLf = withoutCrLf.Contains('\n');
        var hasCr = withoutCrLf.Contains('\r');

        var kinds =
            (hasCrLf ? 1 : 0) +
            (hasLf ? 1 : 0) +
            (hasCr ? 1 : 0);

        if (kinds > 1)
        {
            return "mixed";
        }

        if (hasCrLf)
        {
            return "crlf";
        }

        if (hasLf)
        {
            return "lf";
        }

        if (hasCr)
        {
            return "cr";
        }

        return "none";
    }

    private static string ComputeSha256(string content) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private sealed record DiffStep(
        FileDiffLineKind Kind,
        string Text);
}
