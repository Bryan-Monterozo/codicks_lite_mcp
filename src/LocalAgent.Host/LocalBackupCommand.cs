using LocalAgent.Core.Audit;
using LocalAgent.Core.Files;
using Microsoft.Extensions.DependencyInjection;

namespace LocalAgent.Host;

internal static class LocalBackupCommand
{
    private const string ListFlag =
        "--local-backup-list";

    private const string ShowFlag =
        "--local-backup-show";

    private const string RestoreFlag =
        "--local-backup-restore";

    internal const string NonInteractiveRestoreEnvironmentVariable =
        "CODICKS_LITE_TEST_ALLOW_NONINTERACTIVE_BACKUP_RESTORE";

    public static bool TryRun(
        string[] args,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        if (args.Length == 0)
        {
            return false;
        }

        return args[0] switch
        {
            ListFlag =>
                RunList(services),

            ShowFlag =>
                RunShow(
                    args,
                    services),

            RestoreFlag =>
                RunRestore(
                    args,
                    services),

            _ =>
                false
        };
    }

    private static bool RunList(
        IServiceProvider services)
    {
        var manager =
            services.GetRequiredService<IUpdateBackupManagementService>();

        var backups =
            manager.List();

        if (backups.Count == 0)
        {
            Console.WriteLine(
                "No update backups found.");

            return true;
        }

        foreach (var backup in
                 backups)
        {
            Console.WriteLine(
                $"{backup.Id}\t{backup.WorkspaceId}\t{backup.RelativePath}\t{backup.SizeBytes}\t{backup.CreatedAtUtc:O}");
        }

        return true;
    }

    private static bool RunShow(
        string[] args,
        IServiceProvider services)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: codicks-lite backup-show <backupId>");

            Environment.ExitCode = 2;
            return true;
        }

        try
        {
            var manager =
                services.GetRequiredService<IUpdateBackupManagementService>();

            var preview =
                manager.PreviewRestore(
                    args[1]);

            PrintPreview(
                preview);

            return true;
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                FileNotFoundException or
                InvalidDataException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"ERROR: {exception.Message}");

            Environment.ExitCode = 1;
            return true;
        }
    }

    private static bool RunRestore(
        string[] args,
        IServiceProvider services)
    {
        var allowNonInteractive =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    NonInteractiveRestoreEnvironmentVariable),
                "1",
                StringComparison.Ordinal);

        if (Console.IsInputRedirected &&
            !allowNonInteractive)
        {
            Console.Error.WriteLine(
                "ERROR: backup-restore requires an interactive local terminal.");

            Environment.ExitCode = 1;
            return true;
        }

        if (args.Length == 3 &&
            !allowNonInteractive)
        {
            Console.Error.WriteLine(
                "ERROR: --yes is reserved for local automated acceptance tests.");

            Environment.ExitCode = 2;
            return true;
        }

        if (args.Length is < 2 or > 3 ||
            (args.Length == 3 &&
             !string.Equals(
                 args[2],
                 "--yes",
                 StringComparison.Ordinal)))
        {
            Console.Error.WriteLine(
                "Usage: codicks-lite backup-restore <backupId>");

            Environment.ExitCode = 2;
            return true;
        }

        var manager =
            services.GetRequiredService<IUpdateBackupManagementService>();

        UpdateBackupRestorePreview preview;

        try
        {
            preview =
                manager.PreviewRestore(
                    args[1]);
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                FileNotFoundException or
                InvalidDataException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"ERROR: {exception.Message}");

            Environment.ExitCode = 1;
            return true;
        }

        PrintPreview(
            preview);

        var confirmed =
            args.Length == 3;

        if (!confirmed)
        {
            Console.Write(
                "Restore this backup over the current file? [y/N] ");

            var answer =
                Console.ReadLine();

            confirmed =
                string.Equals(
                    answer,
                    "y",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    answer,
                    "yes",
                    StringComparison.OrdinalIgnoreCase);
        }

        if (!confirmed)
        {
            Console.WriteLine(
                "Restore cancelled.");

            return true;
        }

        var result =
            manager.Restore(
                preview.Backup.Id,
                preview.CurrentSha256);

        if (!result.Success ||
            result.Value is null)
        {
            Console.Error.WriteLine(
                $"ERROR: {MapError(result.Error)}: {result.Message}");

            Environment.ExitCode = 1;
            return true;
        }

        var receipt =
            result.Value;

        var auditWriter =
            services.GetRequiredService<IAuditWriter>();

        _ = auditWriter.TryWrite(
            new OperationAuditRecord(
                DateTimeOffset.UtcNow,
                "backup_restore",
                receipt.WorkspaceId,
                receipt.RelativePath,
                receipt.RelativePath,
                receipt.SafetyBackupId,
                receipt.BackupId,
                DryRun: false,
                Success: true,
                ErrorCode: null));

        Console.WriteLine();
        Console.WriteLine(
            "Backup restored.");

        Console.WriteLine(
            $"File: {receipt.RelativePath}");

        Console.WriteLine(
            $"Restored SHA-256: {receipt.RestoredSha256}");

        Console.WriteLine(
            $"Safety backup of replaced version: {receipt.SafetyBackupId}");

        return true;
    }

    private static void PrintPreview(
        UpdateBackupRestorePreview preview)
    {
        Console.WriteLine(
            $"Backup:       {preview.Backup.Id}");

        Console.WriteLine(
            $"Workspace:    {preview.Backup.WorkspaceId}");

        Console.WriteLine(
            $"File:         {preview.Backup.RelativePath}");

        Console.WriteLine(
            $"Created UTC:  {preview.Backup.CreatedAtUtc:O}");

        Console.WriteLine(
            $"Backup size:  {preview.Backup.SizeBytes} bytes");

        Console.WriteLine(
            $"Backup SHA:   {preview.Backup.OriginalSha256}");

        Console.WriteLine(
            $"Current size: {preview.CurrentSizeBytes} bytes");

        Console.WriteLine(
            $"Current SHA:  {preview.CurrentSha256}");
    }

    private static string MapError(
        FileMutationError error) =>
        error switch
        {
            FileMutationError.InvalidRequest =>
                "INVALID_REQUEST",

            FileMutationError.AccessDenied =>
                "ACCESS_DENIED",

            FileMutationError.NotFound =>
                "FILE_NOT_FOUND",

            FileMutationError.NotAFile =>
                "UNSUPPORTED_FILE_TYPE",

            FileMutationError.Conflict =>
                "CONFLICT",

            FileMutationError.RecoveryNotFound =>
                "BACKUP_NOT_FOUND",

            FileMutationError.RecoveryUnavailable =>
                "BACKUP_UNAVAILABLE",

            FileMutationError.BackupFailed =>
                "SAFETY_BACKUP_FAILED",

            FileMutationError.AtomicWriteFailed =>
                "IO_ERROR",

            FileMutationError.IoError =>
                "IO_ERROR",

            _ =>
                error.ToString()
                    .ToUpperInvariant()
        };
}
