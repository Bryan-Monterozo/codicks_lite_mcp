using System.Text.Json;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;

namespace LocalAgent.Infrastructure.Recovery;

public sealed class UpdateBackupStore(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver) : IUpdateBackupStore
{
    private readonly string _recoveryRoot = Path.Combine(
        pathResolver.Resolve(configuration.Agent.StateDirectory),
        "recovery",
        "updates");

    public UpdateBackupReceipt Save(
        string workspaceId,
        string relativePath,
        byte[] originalBytes,
        string originalSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalSha256);

        var safeWorkspaceId = SanitizeSegment(workspaceId);
        var id = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var directory = Path.Combine(_recoveryRoot, safeWorkspaceId);
        Directory.CreateDirectory(directory);

        var contentPath = Path.Combine(directory, $"{id}.bin");
        var metadataPath = Path.Combine(directory, $"{id}.json");

        WriteDurableFile(contentPath, originalBytes);

        try
        {
            var metadata = new BackupMetadata(
                id,
                workspaceId,
                relativePath,
                originalSha256,
                originalBytes.LongLength,
                DateTimeOffset.UtcNow);

            var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
            WriteDurableFile(metadataPath, metadataBytes);
        }
        catch (IOException)
        {
            TryDelete(contentPath);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(contentPath);
            throw;
        }
        catch (NotSupportedException)
        {
            TryDelete(contentPath);
            throw;
        }

        return new UpdateBackupReceipt(id, contentPath);
    }

    private static void WriteDurableFile(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.WriteThrough);

        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static string SanitizeSegment(string value)
    {
        var characters = value
            .Select(character =>
                char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.'
                    ? character
                    : '_')
            .ToArray();

        return new string(characters);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort rollback of an incomplete backup record.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort rollback of an incomplete backup record.
        }
    }

    private sealed record BackupMetadata(
        string Id,
        string WorkspaceId,
        string RelativePath,
        string OriginalSha256,
        long SizeBytes,
        DateTimeOffset CreatedAtUtc);
}
