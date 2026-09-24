using System.Text.Json;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Recovery;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.Files;

namespace LocalAgent.Infrastructure.Recovery;

public sealed class FileRecoveryStore(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver,
    WorkspaceRecoveryLocationResolver locationResolver) : IRecoveryStore
{
    private readonly string _metadataRoot = Path.Combine(
        pathResolver.Resolve(configuration.Agent.StateDirectory),
        "recovery",
        "deletions",
        "records");

    public RecoveryRecord PrepareDelete(
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string sha256,
        long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        Directory.CreateDirectory(_metadataRoot);

        var id = $"del-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var recoveryDirectory = locationResolver.Resolve(workspace);
        var quarantinePath = Path.Combine(recoveryDirectory, $"{id}.bin");
        var now = DateTimeOffset.UtcNow;

        var record = new RecoveryRecord(
            id,
            workspace.Id,
            normalizedRelativePath,
            quarantinePath,
            sha256,
            sizeBytes,
            RecoveryStatus.Prepared,
            now,
            now);

        WriteRecord(record, createNew: true);
        return record;
    }

    public RecoveryRecord? Load(string recoveryId)
    {
        if (!IsValidRecoveryId(recoveryId))
        {
            return null;
        }

        var path = GetMetadataPath(recoveryId);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<RecoveryRecord>(bytes)
            ?? throw new IOException($"Recovery metadata is invalid: {recoveryId}");
    }

    public RecoveryRecord UpdateStatus(
        string recoveryId,
        RecoveryStatus status)
    {
        var current = Load(recoveryId)
            ?? throw new FileNotFoundException(
                "Recovery metadata was not found.",
                recoveryId);

        var updated = current with
        {
            Status = status,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        WriteRecord(updated, createNew: false);
        return updated;
    }

    private void WriteRecord(RecoveryRecord record, bool createNew)
    {
        Directory.CreateDirectory(_metadataRoot);
        var destination = GetMetadataPath(record.Id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);

        if (createNew)
        {
            DurableFilePersistence.CreateNew(
                destination,
                bytes,
                unixFileMode: null);
            return;
        }

        DurableFilePersistence.ReplaceExisting(
            destination,
            bytes,
            unixFileMode: null);
    }

    private string GetMetadataPath(string recoveryId) =>
        Path.Combine(_metadataRoot, $"{recoveryId}.json");

    private static bool IsValidRecoveryId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character =>
            char.IsLetterOrDigit(character) ||
            character is '-' or '_' or '.');

}
