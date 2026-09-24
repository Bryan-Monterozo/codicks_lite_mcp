using System.Text;

namespace LocalAgent.Infrastructure.Scratch;

public sealed class ScratchProbeService
{
    public const string ScratchDirectoryEnvironmentVariable = "CODICKS_LITE_CHUNK01_SCRATCH_DIR";
    public const string ProbeFileName = "write-probe.txt";
    public const int MaxContentLength = 4096;

    private readonly string _scratchDirectory;

    public ScratchProbeService()
        : this(GetDefaultScratchDirectory())
    {
    }

    public ScratchProbeService(string scratchDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
        _scratchDirectory = Path.GetFullPath(scratchDirectory);
    }

    public string ScratchDirectory => _scratchDirectory;

    public async Task<ScratchProbeResult> WriteProbeAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length > MaxContentLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(content),
                $"Probe content is limited to {MaxContentLength} characters.");
        }

        Directory.CreateDirectory(_scratchDirectory);

        var destination = Path.Combine(_scratchDirectory, ProbeFileName);
        var temporary = Path.Combine(
            _scratchDirectory,
            $".{ProbeFileName}.{Guid.NewGuid():N}.tmp");

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);

        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return new ScratchProbeResult(
            Success: true,
            FileName: ProbeFileName,
            FullPath: destination,
            BytesWritten: bytes.LongLength,
            WrittenAtUtc: DateTimeOffset.UtcNow);
    }

    public async Task<string?> ReadProbeAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_scratchDirectory, ProbeFileName);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : null;
    }

    private static string GetDefaultScratchDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(ScratchDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "CodicksLiteMcp", "chunk01", "scratch")
            : Path.Combine(home, ".codicks-lite-mcp", "chunk01", "scratch");
    }
}
