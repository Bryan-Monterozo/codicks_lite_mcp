using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Files;

public sealed class WorkspaceQueryService(
    IWorkspacePathPolicy pathPolicy,
    AgentConfiguration configuration) : IWorkspaceQueryService
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly SearchExcludeMatcher _searchExcludeMatcher =
        new(configuration.Agent.Search.ExcludeGlobs);

    private readonly int _maxDirectoryEntries = configuration.Agent.Limits.MaxDirectoryEntries;
    private readonly int _maxSearchResults = configuration.Agent.Limits.MaxSearchResults;
    private readonly int _maxTraversalDepth = configuration.Agent.Limits.MaxTraversalDepth;
    private readonly int _operationTimeoutSeconds = configuration.Agent.Limits.OperationTimeoutSeconds;
    private readonly int _maxReadResponseBytes = checked((int)Math.Min(
        configuration.Agent.Limits.MaxReadResponseBytes,
        int.MaxValue));

    public QueryResult<WorkspaceInspection> InspectWorkspace(string workspaceId)
    {
        var access = pathPolicy.ValidateExisting(
            workspaceId,
            string.Empty,
            WorkspaceOperation.Read,
            allowWorkspaceRoot: true);

        if (!access.Allowed || access.Workspace is null || access.FullPath is null)
        {
            return FromAccessFailure<WorkspaceInspection>(access);
        }

        var metadata = CreateMetadata(
            access.Workspace,
            string.Empty,
            access.FullPath,
            access.EntryKind);

        var value = new WorkspaceInspection(
            access.Workspace.Id,
            access.Workspace.Root,
            access.Workspace.Enabled,
            access.Workspace.AllowedOperations.ToArray(),
            metadata);

        return QueryResult.Ok(value);
    }

    public QueryResult<FileMetadata> Stat(string workspaceId, string relativePath)
    {
        var access = pathPolicy.ValidateExisting(
            workspaceId,
            relativePath,
            WorkspaceOperation.Read,
            allowWorkspaceRoot: true);

        if (!access.Allowed || access.Workspace is null || access.FullPath is null)
        {
            return FromAccessFailure<FileMetadata>(access);
        }

        try
        {
            var metadata = CreateMetadata(
                access.Workspace,
                access.NormalizedRelativePath ?? string.Empty,
                access.FullPath,
                access.EntryKind);

            return QueryResult.Ok(metadata);
        }
        catch (IOException exception)
        {
            return IoFailure<FileMetadata>(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return IoFailure<FileMetadata>(exception);
        }
    }

    public QueryResult<DirectoryPage> ListDirectory(
        string workspaceId,
        string relativePath,
        int offset = 0,
        int? limit = null)
    {
        if (offset < 0)
        {
            return QueryResult.Fail<DirectoryPage>(
                FileQueryError.InvalidRequest,
                "Directory offset cannot be negative.");
        }

        var effectiveLimit = limit ?? _maxDirectoryEntries;
        if (effectiveLimit <= 0)
        {
            return QueryResult.Fail<DirectoryPage>(
                FileQueryError.InvalidRequest,
                "Directory page limit must be greater than zero.");
        }

        effectiveLimit = Math.Min(effectiveLimit, _maxDirectoryEntries);

        var access = pathPolicy.ValidateExisting(
            workspaceId,
            relativePath,
            WorkspaceOperation.Read,
            allowWorkspaceRoot: true);

        if (!access.Allowed || access.Workspace is null || access.FullPath is null)
        {
            return FromAccessFailure<DirectoryPage>(access);
        }

        if (access.EntryKind != FileSystemEntryKind.Directory)
        {
            return QueryResult.Fail<DirectoryPage>(
                FileQueryError.NotADirectory,
                "The requested path is not a directory.");
        }

        try
        {
            var visibleEntries = new List<FileMetadata>();

            foreach (var childFullPath in Directory
                         .EnumerateFileSystemEntries(access.FullPath)
                         .OrderBy(path => Path.GetFileName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var childRelativePath = NormalizeRelativePath(
                    Path.GetRelativePath(access.Workspace.Root, childFullPath));

                var childAccess = pathPolicy.ValidateExisting(
                    workspaceId,
                    childRelativePath,
                    WorkspaceOperation.Read,
                    allowWorkspaceRoot: false);

                if (!childAccess.Allowed || childAccess.FullPath is null)
                {
                    continue;
                }

                visibleEntries.Add(CreateMetadata(
                    access.Workspace,
                    childRelativePath,
                    childAccess.FullPath,
                    childAccess.EntryKind));
            }

            if (offset > visibleEntries.Count)
            {
                return QueryResult.Fail<DirectoryPage>(
                    FileQueryError.InvalidRequest,
                    "Directory offset is beyond the available visible entries.");
            }

            var entries = visibleEntries
                .Skip(offset)
                .Take(effectiveLimit)
                .ToArray();

            var nextOffset = offset + entries.Length < visibleEntries.Count
                ? offset + entries.Length
                : (int?)null;

            return QueryResult.Ok(new DirectoryPage(
                access.NormalizedRelativePath ?? string.Empty,
                entries,
                offset,
                nextOffset,
                nextOffset.HasValue));
        }
        catch (IOException exception)
        {
            return IoFailure<DirectoryPage>(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return IoFailure<DirectoryPage>(exception);
        }
    }

    public QueryResult<TextReadPage> ReadText(
        string workspaceId,
        string relativePath,
        long byteOffset = 0,
        int? maxBytes = null)
    {
        if (byteOffset < 0)
        {
            return QueryResult.Fail<TextReadPage>(
                FileQueryError.InvalidRequest,
                "Byte offset cannot be negative.");
        }

        var effectiveMaxBytes = maxBytes ?? _maxReadResponseBytes;
        if (effectiveMaxBytes <= 0)
        {
            return QueryResult.Fail<TextReadPage>(
                FileQueryError.InvalidRequest,
                "Read size must be greater than zero.");
        }

        effectiveMaxBytes = Math.Min(effectiveMaxBytes, _maxReadResponseBytes);

        var access = pathPolicy.ValidateExisting(
            workspaceId,
            relativePath,
            WorkspaceOperation.Read,
            allowWorkspaceRoot: false);

        if (!access.Allowed || access.FullPath is null)
        {
            return FromAccessFailure<TextReadPage>(access);
        }

        if (access.EntryKind != FileSystemEntryKind.RegularFile)
        {
            return QueryResult.Fail<TextReadPage>(
                FileQueryError.NotAFile,
                "The requested path is not a regular file.");
        }

        try
        {
            using var stream = new FileStream(
                access.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                FileOptions.SequentialScan);

            var fileSize = stream.Length;

            using var sha256 = SHA256.Create();
            var hash = Convert.ToHexString(sha256.ComputeHash(stream));

            stream.Position = 0;
            var encodingResult = DetectUtf8Encoding(stream);
            if (!encodingResult.Supported)
            {
                return QueryResult.Fail<TextReadPage>(
                    FileQueryError.UnsupportedTextEncoding,
                    encodingResult.Message);
            }

            var contentStart = encodingResult.PreambleLength;
            var actualOffset = byteOffset == 0 ? contentStart : byteOffset;

            if (actualOffset < contentStart || actualOffset > fileSize)
            {
                return QueryResult.Fail<TextReadPage>(
                    FileQueryError.InvalidRequest,
                    "Byte offset is outside the UTF-8 text content range.");
            }

            if (!IsUtf8Boundary(stream, actualOffset, contentStart, fileSize))
            {
                return QueryResult.Fail<TextReadPage>(
                    FileQueryError.InvalidRequest,
                    "Byte offset must point to the beginning of a UTF-8 character. Use NextByteOffset returned by a previous read.");
            }

            stream.Position = actualOffset;

            var bytesToRead = (int)Math.Min(
                Math.Max(0, fileSize - actualOffset),
                effectiveMaxBytes + 3L);

            var buffer = new byte[bytesToRead];
            var bytesRead = ReadFully(stream, buffer);
            var candidateLength = Math.Min(bytesRead, effectiveMaxBytes);

            if (!TryDecodeUtf8Prefix(buffer, candidateLength, out var content, out var safeLength))
            {
                return QueryResult.Fail<TextReadPage>(
                    FileQueryError.UnsupportedTextEncoding,
                    "The file is not valid UTF-8 text or contains binary NUL bytes in the requested window.");
            }

            var nextOffsetValue = actualOffset + safeLength;
            var isPartial = nextOffsetValue < fileSize;

            return QueryResult.Ok(new TextReadPage(
                access.NormalizedRelativePath ?? relativePath,
                content,
                fileSize,
                encodingResult.Name,
                hash,
                actualOffset,
                isPartial ? nextOffsetValue : null,
                isPartial));
        }
        catch (IOException exception)
        {
            return IoFailure<TextReadPage>(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return IoFailure<TextReadPage>(exception);
        }
    }

    public QueryResult<SearchPage> Search(
        string workspaceId,
        string query,
        string relativePath = "",
        bool caseSensitive = false,
        int? maxResults = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return QueryResult.Fail<SearchPage>(
                FileQueryError.InvalidRequest,
                "Search query is required.");
        }

        var effectiveMaxResults = maxResults ?? _maxSearchResults;
        if (effectiveMaxResults <= 0)
        {
            return QueryResult.Fail<SearchPage>(
                FileQueryError.InvalidRequest,
                "Search result limit must be greater than zero.");
        }

        effectiveMaxResults = Math.Min(effectiveMaxResults, _maxSearchResults);

        var rootAccess = pathPolicy.ValidateExisting(
            workspaceId,
            relativePath,
            WorkspaceOperation.Read,
            allowWorkspaceRoot: true);

        if (!rootAccess.Allowed || rootAccess.Workspace is null || rootAccess.FullPath is null)
        {
            return FromAccessFailure<SearchPage>(rootAccess);
        }

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var matches = new List<SearchMatch>();
        var stopwatch = Stopwatch.StartNew();
        var responseBytes = 0;
        var truncatedByDepth = false;
        string? stopReason = null;

        try
        {
            if (rootAccess.EntryKind == FileSystemEntryKind.RegularFile)
            {
                SearchFile(
                    workspaceId,
                    rootAccess.NormalizedRelativePath ?? relativePath,
                    rootAccess.FullPath,
                    query,
                    comparison,
                    effectiveMaxResults,
                    stopwatch,
                    matches,
                    ref responseBytes,
                    ref stopReason);
            }
            else if (rootAccess.EntryKind == FileSystemEntryKind.Directory)
            {
                var stack = new Stack<SearchDirectory>();
                stack.Push(new SearchDirectory(
                    rootAccess.FullPath,
                    0));

                while (stack.Count > 0 && stopReason is null)
                {
                    if (HasTimedOut(stopwatch))
                    {
                        stopReason = "operation-timeout";
                        break;
                    }

                    var current = stack.Pop();
                    var children = Directory
                        .EnumerateFileSystemEntries(current.FullPath)
                        .OrderByDescending(path => Path.GetFileName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    foreach (var childFullPath in children)
                    {
                        if (HasTimedOut(stopwatch))
                        {
                            stopReason = "operation-timeout";
                            break;
                        }

                        var childRelativePath = NormalizeRelativePath(
                            Path.GetRelativePath(rootAccess.Workspace.Root, childFullPath));

                        var childAccess = pathPolicy.ValidateExisting(
                            workspaceId,
                            childRelativePath,
                            WorkspaceOperation.Read,
                            allowWorkspaceRoot: false);

                        if (!childAccess.Allowed || childAccess.FullPath is null)
                        {
                            continue;
                        }

                        if (_searchExcludeMatcher.IsExcluded(childRelativePath))
                        {
                            continue;
                        }

                        if (childAccess.EntryKind == FileSystemEntryKind.Directory)
                        {
                            if (current.Depth >= _maxTraversalDepth)
                            {
                                truncatedByDepth = true;
                                continue;
                            }

                            stack.Push(new SearchDirectory(
                                childAccess.FullPath,
                                current.Depth + 1));
                            continue;
                        }

                        if (childAccess.EntryKind != FileSystemEntryKind.RegularFile)
                        {
                            continue;
                        }

                        SearchFile(
                            workspaceId,
                            childRelativePath,
                            childAccess.FullPath,
                            query,
                            comparison,
                            effectiveMaxResults,
                            stopwatch,
                            matches,
                            ref responseBytes,
                            ref stopReason);

                        if (stopReason is not null)
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                return QueryResult.Fail<SearchPage>(
                    FileQueryError.InvalidRequest,
                    "Search root must be a regular file or directory.");
            }

            if (stopReason is null && truncatedByDepth)
            {
                stopReason = "max-traversal-depth";
            }

            return QueryResult.Ok(new SearchPage(
                query,
                rootAccess.NormalizedRelativePath ?? string.Empty,
                matches.ToArray(),
                stopReason is not null,
                stopReason));
        }
        catch (IOException exception)
        {
            return IoFailure<SearchPage>(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return IoFailure<SearchPage>(exception);
        }
    }

    private void SearchFile(
        string workspaceId,
        string relativePath,
        string fullPath,
        string query,
        StringComparison comparison,
        int maxResults,
        Stopwatch stopwatch,
        List<SearchMatch> matches,
        ref int responseBytes,
        ref string? stopReason)
    {
        if (stopReason is not null || _searchExcludeMatcher.IsExcluded(relativePath))
        {
            return;
        }

        var revalidated = pathPolicy.ValidateExisting(
            workspaceId,
            relativePath,
            WorkspaceOperation.Read,
            allowWorkspaceRoot: false);

        if (!revalidated.Allowed ||
            revalidated.FullPath is null ||
            revalidated.EntryKind != FileSystemEntryKind.RegularFile)
        {
            return;
        }

        if (!IsLikelyUtf8TextFile(revalidated.FullPath))
        {
            return;
        }

        var matchesBeforeFile = matches.Count;

        try
        {
            using var stream = new FileStream(
                revalidated.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                FileOptions.SequentialScan);
            using var reader = new StreamReader(
                stream,
                StrictUtf8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4_096,
                leaveOpen: false);

            var lineNumber = 0;
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;

                if (HasTimedOut(stopwatch))
                {
                    stopReason = "operation-timeout";
                    return;
                }

                if (line.Contains('\0'))
                {
                    if (matches.Count > matchesBeforeFile)
                    {
                        matches.RemoveRange(matchesBeforeFile, matches.Count - matchesBeforeFile);
                    }
                    return;
                }

                var matchIndex = line.IndexOf(query, comparison);
                if (matchIndex < 0)
                {
                    continue;
                }

                var excerpt = CreateExcerpt(line, matchIndex, query.Length);
                var estimatedBytes = Encoding.UTF8.GetByteCount(relativePath) +
                                     Encoding.UTF8.GetByteCount(excerpt) +
                                     32;

                if (responseBytes + estimatedBytes > _maxReadResponseBytes)
                {
                    stopReason = "response-byte-limit";
                    return;
                }

                matches.Add(new SearchMatch(relativePath, lineNumber, excerpt));
                responseBytes += estimatedBytes;

                if (matches.Count >= maxResults)
                {
                    stopReason = "max-search-results";
                    return;
                }
            }
        }
        catch (DecoderFallbackException)
        {
            if (matches.Count > matchesBeforeFile)
            {
                matches.RemoveRange(matchesBeforeFile, matches.Count - matchesBeforeFile);
            }
        }
        catch (IOException)
        {
            if (matches.Count > matchesBeforeFile)
            {
                matches.RemoveRange(matchesBeforeFile, matches.Count - matchesBeforeFile);
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (matches.Count > matchesBeforeFile)
            {
                matches.RemoveRange(matchesBeforeFile, matches.Count - matchesBeforeFile);
            }
        }
    }

    private bool HasTimedOut(Stopwatch stopwatch) =>
        stopwatch.Elapsed >= TimeSpan.FromSeconds(_operationTimeoutSeconds);

    private static FileMetadata CreateMetadata(
        WorkspaceDescriptor workspace,
        string relativePath,
        string fullPath,
        FileSystemEntryKind kind)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var name = normalized.Length == 0
            ? Path.GetFileName(workspace.Root.TrimEnd(Path.DirectorySeparatorChar))
            : Path.GetFileName(normalized);

        if (kind == FileSystemEntryKind.RegularFile)
        {
            var info = new FileInfo(fullPath);
            return new FileMetadata(
                normalized,
                name,
                kind,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc));
        }

        var directoryInfo = new DirectoryInfo(fullPath);
        return new FileMetadata(
            normalized,
            name,
            kind,
            null,
            new DateTimeOffset(directoryInfo.LastWriteTimeUtc));
    }

    private static QueryResult<T> FromAccessFailure<T>(PathPolicyResult result)
        where T : class
    {
        var error = result.Error switch
        {
            WorkspaceAccessError.PathNotFound => FileQueryError.NotFound,
            WorkspaceAccessError.InvalidRelativePath => FileQueryError.InvalidRequest,
            WorkspaceAccessError.WorkspaceRootNotAllowed => FileQueryError.InvalidRequest,
            WorkspaceAccessError.InspectionFailed => FileQueryError.IoError,
            _ => FileQueryError.AccessDenied
        };

        return QueryResult.Fail<T>(error, result.Message, result.Error);
    }

    private static QueryResult<T> IoFailure<T>(Exception exception)
        where T : class =>
        QueryResult.Fail<T>(
            FileQueryError.IoError,
            $"Filesystem query failed: {exception.Message}");

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static Utf8EncodingResult DetectUtf8Encoding(FileStream stream)
    {
        stream.Position = 0;
        Span<byte> prefix = stackalloc byte[3];
        var bytesRead = stream.Read(prefix);

        if (bytesRead >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
        {
            return Utf8EncodingResult.Unsupported("UTF-16 LE is not supported in Chunk 04; use UTF-8 text.");
        }

        if (bytesRead >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
        {
            return Utf8EncodingResult.Unsupported("UTF-16 BE is not supported in Chunk 04; use UTF-8 text.");
        }

        if (bytesRead >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            return Utf8EncodingResult.SupportedEncoding("utf-8-bom", 3);
        }

        return Utf8EncodingResult.SupportedEncoding("utf-8", 0);
    }

    private static bool IsUtf8Boundary(
        FileStream stream,
        long offset,
        int contentStart,
        long fileSize)
    {
        if (offset <= contentStart || offset >= fileSize)
        {
            return true;
        }

        stream.Position = offset;
        var value = stream.ReadByte();
        return value < 0 || (value & 0xC0) != 0x80;
    }

    private static int ReadFully(FileStream stream, byte[] buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool TryDecodeUtf8Prefix(
        byte[] buffer,
        int candidateLength,
        out string content,
        out int safeLength)
    {
        content = string.Empty;
        safeLength = 0;

        for (var trim = 0; trim <= 3 && candidateLength - trim >= 0; trim++)
        {
            var length = candidateLength - trim;

            if (Array.IndexOf(buffer, (byte)0, 0, length) >= 0)
            {
                return false;
            }

            try
            {
                content = StrictUtf8.GetString(buffer, 0, length);
                safeLength = length;
                return true;
            }
            catch (DecoderFallbackException)
            {
                // A bounded read may stop in the middle of a multi-byte UTF-8 character.
            }
        }

        return false;
    }

    private static bool IsLikelyUtf8TextFile(string fullPath)
    {
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4_096,
            FileOptions.SequentialScan);

        var encoding = DetectUtf8Encoding(stream);
        if (!encoding.Supported)
        {
            return false;
        }

        stream.Position = encoding.PreambleLength;
        var sampleSize = (int)Math.Min(Math.Max(0, stream.Length - stream.Position), 4_099L);
        var buffer = new byte[sampleSize];
        var bytesRead = ReadFully(stream, buffer);
        var candidateLength = Math.Min(bytesRead, 4_096);

        return TryDecodeUtf8Prefix(buffer, candidateLength, out _, out _);
    }

    private static string CreateExcerpt(string line, int matchIndex, int queryLength)
    {
        const int maxExcerptCharacters = 240;
        const int contextCharacters = 100;

        if (line.Length <= maxExcerptCharacters)
        {
            return line;
        }

        var start = Math.Max(0, matchIndex - contextCharacters);
        var requiredEnd = Math.Min(line.Length, matchIndex + queryLength + contextCharacters);
        var length = Math.Min(maxExcerptCharacters, requiredEnd - start);

        if (start + length < requiredEnd)
        {
            start = Math.Max(0, requiredEnd - maxExcerptCharacters);
            length = Math.Min(maxExcerptCharacters, line.Length - start);
        }

        var excerpt = line.Substring(start, length);
        return start > 0 ? $"…{excerpt}" : excerpt;
    }

    private sealed record SearchDirectory(string FullPath, int Depth);

    private sealed record Utf8EncodingResult(
        bool Supported,
        string Name,
        int PreambleLength,
        string Message)
    {
        public static Utf8EncodingResult SupportedEncoding(string name, int preambleLength) =>
            new(true, name, preambleLength, string.Empty);

        public static Utf8EncodingResult Unsupported(string message) =>
            new(false, string.Empty, 0, message);
    }
}
