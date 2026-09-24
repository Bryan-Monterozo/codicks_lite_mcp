namespace LocalAgent.Infrastructure.Files;

internal static class DurableFilePersistence
{
    public static void CreateNew(
        string destinationPath,
        byte[] content,
        UnixFileMode? unixFileMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);

        var tempPath = CreateSiblingTempPath(destinationPath);

        try
        {
            WriteTempFile(tempPath, content, unixFileMode);

            // File.Move without overwrite is the final no-clobber guarantee.
            File.Move(tempPath, destinationPath);
        }
        finally
        {
            DeleteTempIfPresent(tempPath);
        }
    }

    public static void ReplaceExisting(
        string destinationPath,
        byte[] content,
        UnixFileMode? unixFileMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);

        var tempPath = CreateSiblingTempPath(destinationPath);

        try
        {
            WriteTempFile(tempPath, content, unixFileMode);

            // Same-directory temp + rename keeps replacement atomic on the
            // destination filesystem.
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            DeleteTempIfPresent(tempPath);
        }
    }

    private static string CreateSiblingTempPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException(
                "Destination parent directory could not be determined.");
        }

        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(
            directory,
            $".{fileName}.codicks-lite-{Guid.NewGuid():N}.tmp");
    }

    private static void WriteTempFile(
        string tempPath,
        byte[] content,
        UnixFileMode? unixFileMode)
    {
        using (var stream = new FileStream(
                   tempPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 81_920,
                   FileOptions.WriteThrough))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        if (unixFileMode.HasValue && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tempPath, unixFileMode.Value);
        }
    }

    private static void DeleteTempIfPresent(string tempPath)
    {
        if (!File.Exists(tempPath))
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup only.
        }
    }
}
