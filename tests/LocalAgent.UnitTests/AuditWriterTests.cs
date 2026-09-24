using System.Text.Json;
using LocalAgent.Core.Audit;
using LocalAgent.Core.Configuration;
using LocalAgent.Infrastructure.Audit;
using LocalAgent.Infrastructure.Paths;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class AuditWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"codicks-lite-chunk08-audit-{Guid.NewGuid():N}");

    [Fact]
    public void TryWrite_WritesMetadataOnly_UnderStateDirectory()
    {
        Directory.CreateDirectory(_root);

        var configuration = new AgentConfiguration
        {
            Agent =
            {
                StateDirectory = _root
            }
        };

        var writer = new JsonLinesAuditWriter(
            configuration,
            new UserPathResolver());

        var record = new OperationAuditRecord(
            DateTimeOffset.UtcNow,
            "file_update",
            "test",
            "src/example.txt",
            "src/example.txt",
            MutationId: null,
            RecoveryId: null,
            DryRun: false,
            Success: true,
            ErrorCode: null);

        Assert.True(writer.TryWrite(record));

        var auditPath = Path.Combine(
            _root,
            "audit",
            "operations.jsonl");

        Assert.True(File.Exists(auditPath));

        var line = Assert.Single(File.ReadAllLines(auditPath));
        using var json = JsonDocument.Parse(line);

        Assert.Equal(
            "file_update",
            json.RootElement.GetProperty("Operation").GetString());
        Assert.Equal(
            "src/example.txt",
            json.RootElement.GetProperty("SourceRelativePath").GetString());
        Assert.False(
            json.RootElement.TryGetProperty(
                "Content",
                out _));
    }


    [Fact]
    public void TryWrite_ProcessExecution_WritesMetadataOnly()
    {
        Directory.CreateDirectory(_root);

        var configuration = new AgentConfiguration
        {
            Agent =
            {
                StateDirectory = _root
            }
        };

        var writer = new JsonLinesAuditWriter(
            configuration,
            new UserPathResolver());

        var record = new ProcessExecutionAuditRecord(
            TimestampUtc: DateTimeOffset.UtcNow,
            Operation: "process_exec",
            WorkspaceId: "test",
            RelativeWorkingDirectory: "src",
            Executable: "dotnet",
            ArgumentCount: 3,
            ExecutionMode: "Host",
            DurationMilliseconds: 250,
            ExitCode: 1,
            TimedOut: false,
            Cancelled: false,
            StandardOutputBytes: 128,
            StandardErrorBytes: 64,
            StandardOutputTruncated: true,
            StandardErrorTruncated: false,
            Success: true,
            ErrorCode: null);

        Assert.True(writer.TryWrite(record));

        var auditPath = Path.Combine(
            _root,
            "audit",
            "operations.jsonl");

        var line = Assert.Single(
            File.ReadAllLines(auditPath));

        using var json =
            JsonDocument.Parse(line);

        Assert.Equal(
            "process_exec",
            json.RootElement
                .GetProperty("Operation")
                .GetString());

        Assert.Equal(
            "dotnet",
            json.RootElement
                .GetProperty("Executable")
                .GetString());

        Assert.Equal(
            3,
            json.RootElement
                .GetProperty("ArgumentCount")
                .GetInt32());

        Assert.Equal(
            128,
            json.RootElement
                .GetProperty("StandardOutputBytes")
                .GetInt32());

        Assert.True(
            json.RootElement
                .GetProperty("StandardOutputTruncated")
                .GetBoolean());

        Assert.False(
            json.RootElement.TryGetProperty(
                "Arguments",
                out _));

        Assert.False(
            json.RootElement.TryGetProperty(
                "StandardOutput",
                out _));

        Assert.False(
            json.RootElement.TryGetProperty(
                "StandardError",
                out _));

        Assert.False(
            json.RootElement.TryGetProperty(
                "Environment",
                out _));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
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
}
