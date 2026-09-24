using System.Globalization;
using System.Text;
using LocalAgent.Core.Files;

namespace LocalAgent.Infrastructure.Files;

public static class FileReviewSummaryFormatter
{
    public const string PreviewState = "preview";

    public static string CreateDiffSummary(
        FileDiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        AppendCommon(
            builder,
            result.RelativePath,
            result.BaseSha256,
            result.ProposedSha256,
            result.Additions,
            result.Deletions,
            result.Truncated);

        builder.AppendLine(
            result.HasChanges
                ? "Changes present: yes"
                : "Changes present: no");

        return builder
            .ToString()
            .TrimEnd();
    }

    public static string CreatePatchPreviewSummary(
        FilePatchPreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        AppendCommon(
            builder,
            result.RelativePath,
            result.BaseSha256,
            result.ProposedSha256,
            result.Additions,
            result.Deletions,
            result.Truncated);

        builder.Append("Patch: ");
        builder.AppendLine(
            result.PatchSha256);

        builder.Append("Can apply: ");
        builder.AppendLine(
            result.CanApply
                ? "yes"
                : "no");

        if (result.ReviewExpiresAtUtc is not null)
        {
            builder.Append("Review token expires: ");
            builder.AppendLine(
                result.ReviewExpiresAtUtc.Value
                    .ToUniversalTime()
                    .ToString(
                        "O",
                        CultureInfo.InvariantCulture));
        }
        else
        {
            builder.AppendLine(
                "Review token expires: not issued");
        }

        return builder
            .ToString()
            .TrimEnd();
    }

    public static string CreateApplySummary(
        FileMutationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var builder = new StringBuilder();

        builder.Append("File: ");
        builder.AppendLine(
            receipt.RelativePath);

        builder.AppendLine(
            "State: APPLIED");

        builder.Append("New SHA-256: ");
        builder.AppendLine(
            receipt.Sha256);

        builder.Append("Size: ");
        builder.Append(
            receipt.SizeBytes.ToString(
                CultureInfo.InvariantCulture));
        builder.AppendLine(" bytes");

        builder.Append("Backup: ");
        builder.AppendLine(
            string.IsNullOrWhiteSpace(
                receipt.BackupId)
                ? "none"
                : receipt.BackupId);

        return builder
            .ToString()
            .TrimEnd();
    }

    private static void AppendCommon(
        StringBuilder builder,
        string relativePath,
        string baseSha256,
        string proposedSha256,
        int additions,
        int deletions,
        bool truncated)
    {
        builder.Append("File: ");
        builder.AppendLine(relativePath);

        builder.AppendLine(
            "State: PREVIEW — not applied");

        builder.Append("Base: ");
        builder.AppendLine(baseSha256);

        builder.Append("Proposed: ");
        builder.AppendLine(proposedSha256);

        builder.Append("Changes: +");
        builder.Append(additions);
        builder.Append(" -");
        builder.AppendLine(
            deletions.ToString(
                CultureInfo.InvariantCulture));

        builder.Append("Truncated: ");
        builder.AppendLine(
            truncated
                ? "yes — review output incomplete"
                : "no");
    }
}
