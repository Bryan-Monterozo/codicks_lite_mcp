using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LocalAgent.Core.Files;

namespace LocalAgent.Infrastructure.Files;

public sealed class UnifiedPatchService : IUnifiedPatchService
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    private static readonly Regex HunkHeaderPattern =
        new(
            "^@@ -(?<oldStart>\\d+)(?:,(?<oldCount>\\d+))? \\+(?<newStart>\\d+)(?:,(?<newCount>\\d+))? @@(?: .*)?$",
            RegexOptions.CultureInvariant |
            RegexOptions.Compiled);

    public FilePatchApplicationResult Apply(
        FilePatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(
                request.RelativePath))
        {
            return Invalid(
                "relativePath is required.");
        }

        if (request.CurrentContent is null)
        {
            return Invalid(
                "CurrentContent is required.");
        }

        if (request.Patch is null)
        {
            return Invalid(
                "patch is required.");
        }

        if (request.MaxPatchBytes <= 0)
        {
            return Invalid(
                "MaxPatchBytes must be greater than zero.");
        }

        byte[] patchBytes;

        try
        {
            patchBytes =
                StrictUtf8.GetBytes(
                    request.Patch);
        }
        catch (EncoderFallbackException)
        {
            return Invalid(
                "Patch cannot be encoded as strict UTF-8.");
        }

        var patchHash =
            Convert.ToHexString(
                SHA256.HashData(
                    patchBytes));

        if (patchBytes.Length >
            request.MaxPatchBytes)
        {
            return FilePatchApplicationResult.Fail(
                FilePatchError.TooLarge,
                $"Patch exceeds the configured preview limit of {request.MaxPatchBytes} bytes.",
                patchHash);
        }

        if (request.Patch.Contains('\0'))
        {
            return Invalid(
                "Patch contains a NUL character.",
                patchHash);
        }

        var lines =
            SplitPatchLines(
                request.Patch);

        if (lines.Count < 2)
        {
            return Invalid(
                "Patch must begin with --- and +++ file headers.",
                patchHash);
        }

        var expectedPath =
            NormalizeTargetPath(
                request.RelativePath);

        var oldHeader =
            ValidateHeader(
                lines[0],
                "--- ",
                "a/",
                expectedPath);

        if (oldHeader is not null)
        {
            return Invalid(
                oldHeader,
                patchHash);
        }

        var newHeader =
            ValidateHeader(
                lines[1],
                "+++ ",
                "b/",
                expectedPath);

        if (newHeader is not null)
        {
            return Invalid(
                newHeader,
                patchHash);
        }

        var hunks =
            new List<ParsedHunk>();

        var index = 2;

        while (index < lines.Count)
        {
            var headerLine =
                lines[index];

            if (headerLine.StartsWith(
                    "--- ",
                    StringComparison.Ordinal) ||
                headerLine.StartsWith(
                    "+++ ",
                    StringComparison.Ordinal))
            {
                return Invalid(
                    "A patch may target only one file.",
                    patchHash);
            }

            var header =
                ParseHunkHeader(
                    headerLine);

            if (!header.Success ||
                header.Value is null)
            {
                return Invalid(
                    header.Message,
                    patchHash);
            }

            index++;

            var patchLines =
                new List<ParsedPatchLine>();

            while (index < lines.Count &&
                   !lines[index].StartsWith(
                       "@@ ",
                       StringComparison.Ordinal) &&
                   !lines[index].StartsWith(
                       "@@ -",
                       StringComparison.Ordinal))
            {
                var line =
                    lines[index];

                if (line.StartsWith(
                        "--- ",
                        StringComparison.Ordinal) ||
                    line.StartsWith(
                        "+++ ",
                        StringComparison.Ordinal))
                {
                    return Invalid(
                        "A patch may target only one file.",
                        patchHash);
                }

                if (line.Length == 0)
                {
                    return Invalid(
                        "Every hunk line must begin with space, '+', or '-'.",
                        patchHash);
                }

                var kind =
                    line[0] switch
                    {
                        ' ' =>
                            ParsedPatchLineKind.Context,

                        '+' =>
                            ParsedPatchLineKind.Addition,

                        '-' =>
                            ParsedPatchLineKind.Deletion,

                        _ =>
                            ParsedPatchLineKind.Invalid
                    };

                if (kind ==
                    ParsedPatchLineKind.Invalid)
                {
                    return Invalid(
                        $"Unsupported patch line prefix '{line[0]}'.",
                        patchHash);
                }

                patchLines.Add(
                    new ParsedPatchLine(
                        kind,
                        line[1..]));

                index++;
            }

            if (patchLines.Count == 0)
            {
                return Invalid(
                    "Patch hunks cannot be empty.",
                    patchHash);
            }

            var oldCount =
                patchLines.Count(
                    line =>
                        line.Kind is
                            ParsedPatchLineKind.Context or
                            ParsedPatchLineKind.Deletion);

            var newCount =
                patchLines.Count(
                    line =>
                        line.Kind is
                            ParsedPatchLineKind.Context or
                            ParsedPatchLineKind.Addition);

            if (oldCount !=
                    header.Value.OldCount ||
                newCount !=
                    header.Value.NewCount)
            {
                return Invalid(
                    "Hunk line counts do not match the hunk header.",
                    patchHash);
            }

            hunks.Add(
                new ParsedHunk(
                    header.Value.OldStart,
                    header.Value.OldCount,
                    header.Value.NewStart,
                    header.Value.NewCount,
                    patchLines));
        }

        if (hunks.Count == 0)
        {
            return FilePatchApplicationResult.Ok(
                request.CurrentContent,
                patchHash);
        }

        var currentLines =
            SplitContentLines(
                request.CurrentContent);

        var proposedLines =
            new List<string>(
                currentLines.Length +
                hunks.Sum(
                    hunk =>
                        hunk.NewCount -
                        hunk.OldCount));

        var oldCursor = 0;

        foreach (var hunk in hunks)
        {
            var oldStartIndex =
                hunk.OldCount == 0
                    ? hunk.OldStart
                    : hunk.OldStart - 1;

            if (oldStartIndex < oldCursor)
            {
                return Conflict(
                    "Patch hunks overlap or are out of order.",
                    patchHash);
            }

            if (oldStartIndex < 0 ||
                oldStartIndex >
                    currentLines.Length)
            {
                return Conflict(
                    "Patch hunk starts outside the current file.",
                    patchHash);
            }

            while (oldCursor <
                   oldStartIndex)
            {
                proposedLines.Add(
                    currentLines[oldCursor]);

                oldCursor++;
            }

            var expectedNewStart =
                hunk.NewCount == 0
                    ? proposedLines.Count
                    : proposedLines.Count + 1;

            if (hunk.NewStart !=
                expectedNewStart)
            {
                return Conflict(
                    "Patch new-file line numbers do not match the exact application position.",
                    patchHash);
            }

            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case ParsedPatchLineKind.Context:
                        if (!TryMatchCurrentLine(
                                currentLines,
                                oldCursor,
                                line.Text))
                        {
                            return Conflict(
                                "Patch context does not match the current file.",
                                patchHash);
                        }

                        proposedLines.Add(
                            line.Text);

                        oldCursor++;
                        break;

                    case ParsedPatchLineKind.Deletion:
                        if (!TryMatchCurrentLine(
                                currentLines,
                                oldCursor,
                                line.Text))
                        {
                            return Conflict(
                                "Patch deletion does not match the current file.",
                                patchHash);
                        }

                        oldCursor++;
                        break;

                    case ParsedPatchLineKind.Addition:
                        proposedLines.Add(
                            line.Text);
                        break;

                    default:
                        return Invalid(
                            "Patch contains an unsupported hunk line.",
                            patchHash);
                }
            }
        }

        while (oldCursor <
               currentLines.Length)
        {
            proposedLines.Add(
                currentLines[oldCursor]);

            oldCursor++;
        }

        return FilePatchApplicationResult.Ok(
            string.Join(
                '\n',
                proposedLines),
            patchHash);
    }

    private static HeaderParseResult ParseHunkHeader(
        string line)
    {
        var match =
            HunkHeaderPattern.Match(
                line);

        if (!match.Success)
        {
            return HeaderParseResult.Fail(
                $"Malformed hunk header: {line}");
        }

        if (!int.TryParse(
                match.Groups["oldStart"].Value,
                out var oldStart) ||
            !int.TryParse(
                match.Groups["newStart"].Value,
                out var newStart))
        {
            return HeaderParseResult.Fail(
                "Hunk line numbers are outside the supported integer range.");
        }

        var oldCount =
            ParseOptionalCount(
                match.Groups["oldCount"],
                out var oldCountValid);

        var newCount =
            ParseOptionalCount(
                match.Groups["newCount"],
                out var newCountValid);

        if (!oldCountValid ||
            !newCountValid)
        {
            return HeaderParseResult.Fail(
                "Hunk line counts are outside the supported integer range.");
        }

        if (oldCount > 0 &&
            oldStart == 0)
        {
            return HeaderParseResult.Fail(
                "A non-empty old hunk range must start at line 1 or later.");
        }

        if (newCount > 0 &&
            newStart == 0)
        {
            return HeaderParseResult.Fail(
                "A non-empty new hunk range must start at line 1 or later.");
        }

        return HeaderParseResult.Ok(
            new ParsedHunkHeader(
                oldStart,
                oldCount,
                newStart,
                newCount));
    }

    private static int ParseOptionalCount(
        Group group,
        out bool valid)
    {
        if (!group.Success)
        {
            valid = true;
            return 1;
        }

        valid =
            int.TryParse(
                group.Value,
                out var value);

        return value;
    }

    private static string? ValidateHeader(
        string line,
        string marker,
        string optionalPrefix,
        string expectedPath)
    {
        if (!line.StartsWith(
                marker,
                StringComparison.Ordinal))
        {
            return
                $"Patch must contain a '{marker.TrimEnd()}' file header.";
        }

        var rawPath =
            line[marker.Length..];

        if (string.IsNullOrWhiteSpace(
                rawPath))
        {
            return
                "Patch file header path cannot be empty.";
        }

        if (rawPath.Contains('\t') ||
            rawPath.Contains('\0'))
        {
            return
                "Patch file headers cannot contain timestamps or NUL characters.";
        }

        if (rawPath.Contains('\\'))
        {
            return
                "Patch file header paths must use '/' separators.";
        }

        var candidate =
            rawPath.StartsWith(
                optionalPrefix,
                StringComparison.Ordinal)
                ? rawPath[
                    optionalPrefix.Length..]
                : rawPath;

        if (Path.IsPathRooted(
                candidate) ||
            candidate.StartsWith('/'))
        {
            return
                "Absolute patch paths are not allowed.";
        }

        var segments =
            candidate.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 ||
            segments.Any(
                segment =>
                    segment is "." or ".."))
        {
            return
                "Patch file header contains an invalid or traversal path.";
        }

        var normalized =
            string.Join(
                '/',
                segments);

        if (!string.Equals(
                normalized,
                expectedPath,
                StringComparison.Ordinal))
        {
            return
                $"Patch targets '{normalized}', but the requested file is '{expectedPath}'.";
        }

        return null;
    }

    private static bool TryMatchCurrentLine(
        string[] currentLines,
        int index,
        string expected) =>
        index >= 0 &&
        index < currentLines.Length &&
        string.Equals(
            currentLines[index],
            expected,
            StringComparison.Ordinal);

    private static string NormalizeTargetPath(
        string relativePath) =>
        relativePath.Replace(
            '\\',
            '/');

    private static string[] SplitContentLines(
        string content)
    {
        if (content.Length == 0)
        {
            return [];
        }

        return content
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n')
            .Split('\n');
    }

    private static List<string> SplitPatchLines(
        string patch)
    {
        var normalized =
            patch
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        var lines =
            normalized.Split(
                '\n')
                .ToList();

        if (lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(
                lines.Count - 1);
        }

        return lines;
    }

    private static FilePatchApplicationResult Invalid(
        string message,
        string patchHash = "") =>
        FilePatchApplicationResult.Fail(
            FilePatchError.InvalidPatch,
            message,
            patchHash);

    private static FilePatchApplicationResult Conflict(
        string message,
        string patchHash) =>
        FilePatchApplicationResult.Fail(
            FilePatchError.Conflict,
            message,
            patchHash);

    private enum ParsedPatchLineKind
    {
        Invalid,
        Context,
        Addition,
        Deletion
    }

    private sealed record ParsedPatchLine(
        ParsedPatchLineKind Kind,
        string Text);

    private sealed record ParsedHunk(
        int OldStart,
        int OldCount,
        int NewStart,
        int NewCount,
        IReadOnlyList<ParsedPatchLine> Lines);

    private sealed record ParsedHunkHeader(
        int OldStart,
        int OldCount,
        int NewStart,
        int NewCount);

    private sealed record HeaderParseResult(
        bool Success,
        ParsedHunkHeader? Value,
        string Message)
    {
        public static HeaderParseResult Ok(
            ParsedHunkHeader value) =>
            new(
                true,
                value,
                string.Empty);

        public static HeaderParseResult Fail(
            string message) =>
            new(
                false,
                null,
                message);
    }
}
