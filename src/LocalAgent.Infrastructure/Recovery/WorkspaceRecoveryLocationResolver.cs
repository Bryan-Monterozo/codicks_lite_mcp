using System.Security.Cryptography;
using System.Text;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Recovery;

public sealed class WorkspaceRecoveryLocationResolver(
    IFileSystemDeviceInspector deviceInspector,
    IFileSystemEntryInspector entryInspector)
{
    public string Resolve(WorkspaceDescriptor workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var workspaceRoot = Path.GetFullPath(workspace.Root);
        var workspaceDevice = deviceInspector.GetDeviceId(workspaceRoot);
        var parent = Directory.GetParent(workspaceRoot)?.FullName;

        var recoveryBase = parent is not null &&
                           Directory.Exists(parent) &&
                           string.Equals(
                               deviceInspector.GetDeviceId(parent),
                               workspaceDevice,
                               StringComparison.Ordinal)
            ? Path.Combine(parent, ".codicks-lite-recovery")
            : Path.Combine(workspaceRoot, ".codicks-lite-recovery");

        EnsurePrivateDirectory(recoveryBase);

        var recoveryDevice = deviceInspector.GetDeviceId(recoveryBase);
        if (!string.Equals(
                recoveryDevice,
                workspaceDevice,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "Recovery storage is not on the same filesystem as the workspace; delete was refused.");
        }

        var workspaceKey = CreateWorkspaceKey(workspaceRoot);
        var workspaceRecovery = Path.Combine(recoveryBase, workspaceKey);
        EnsurePrivateDirectory(workspaceRecovery);

        if (!string.Equals(
                deviceInspector.GetDeviceId(workspaceRecovery),
                workspaceDevice,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "Workspace recovery storage changed filesystem unexpectedly; delete was refused.");
        }

        return workspaceRecovery;
    }

    private void EnsurePrivateDirectory(string path)
    {
        var inspection = entryInspector.Inspect(path);

        if (inspection.Kind == FileSystemEntryKind.Missing)
        {
            Directory.CreateDirectory(path);
            inspection = entryInspector.Inspect(path);
        }

        if (inspection.Kind != FileSystemEntryKind.Directory)
        {
            throw new IOException(
                $"Recovery path is not a real directory: {path}");
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    private static string CreateWorkspaceKey(string workspaceRoot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(workspaceRoot));
        return Convert.ToHexString(bytes)[..24];
    }
}
