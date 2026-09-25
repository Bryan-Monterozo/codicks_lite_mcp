using System.Text.Json;
using LocalAgent.Core.Audit;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Paths;

namespace LocalAgent.Infrastructure.Audit;

public sealed class JsonLinesAuditWriter(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver) : IAuditWriter
{
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(
        pathResolver.Resolve(configuration.Agent.StateDirectory),
        "audit",
        "operations.jsonl");

    public bool TryWrite(OperationAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return TryWriteCore(record);
    }

    public bool TryWrite(ProcessExecutionAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return TryWriteCore(record);
    }

    public bool TryWrite(FileReviewAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return TryWriteCore(record);
    }

    private bool TryWriteCore<T>(T record)
        where T : class
    {
        try
        {
            lock (_sync)
            {
                var directory = Path.GetDirectoryName(_path);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return false;
                }

                Directory.CreateDirectory(directory);

                using var stream = new FileStream(
                    _path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 16_384,
                    FileOptions.WriteThrough);

                JsonSerializer.Serialize(
                    stream,
                    record);

                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
