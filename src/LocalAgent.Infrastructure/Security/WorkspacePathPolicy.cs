using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;

namespace LocalAgent.Infrastructure.Security;

public sealed class WorkspacePathPolicy(
    IWorkspaceResolver workspaceResolver,
    IWorkspacePermissionEvaluator permissionEvaluator,
    IDenyPathMatcher denyPathMatcher,
    IFileSystemEntryInspector entryInspector) : IWorkspacePathPolicy
{
    public PathPolicyResult ValidateExisting(
        string workspaceId,
        string relativePath,
        WorkspaceOperation operation,
        bool allowWorkspaceRoot = false)
    {
        var access = BeginAccess(workspaceId, operation);
        if (!access.Allowed)
        {
            return access;
        }

        var workspace = access.Workspace!;
        var path = ResolveRelativePath(workspace, relativePath, allowWorkspaceRoot);
        if (!path.Allowed)
        {
            return path;
        }

        if (IsDenied(path.NormalizedRelativePath!, out var denied))
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.PathDeniedByPolicy,
                $"Path is denied by security policy '{denied}'.",
                workspace,
                path.NormalizedRelativePath,
                path.FullPath);
        }

        return InspectExistingChain(
            workspace,
            path.NormalizedRelativePath!,
            path.FullPath!,
            allowWorkspaceRoot);
    }

    public PathPolicyResult ValidateCreate(
        string workspaceId,
        string relativePath,
        WorkspaceOperation operation)
    {
        var access = BeginAccess(workspaceId, operation);
        if (!access.Allowed)
        {
            return access;
        }

        var workspace = access.Workspace!;
        var path = ResolveRelativePath(workspace, relativePath, allowWorkspaceRoot: false);
        if (!path.Allowed)
        {
            return path;
        }

        if (IsDenied(path.NormalizedRelativePath!, out var denied))
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.PathDeniedByPolicy,
                $"Path is denied by security policy '{denied}'.",
                workspace,
                path.NormalizedRelativePath,
                path.FullPath);
        }

        return InspectCreateChain(
            workspace,
            path.NormalizedRelativePath!,
            path.FullPath!);
    }

    public MovePathPolicyResult ValidateMove(
        string workspaceId,
        string sourceRelativePath,
        string destinationRelativePath)
    {
        var source = ValidateExisting(
            workspaceId,
            sourceRelativePath,
            WorkspaceOperation.Move,
            allowWorkspaceRoot: false);

        if (!source.Allowed)
        {
            return MovePathPolicyResult.Deny(
                source.Error,
                $"Move source rejected: {source.Message}",
                source.FullPath,
                sourceKind: source.EntryKind);
        }

        var destination = ValidateCreate(
            workspaceId,
            destinationRelativePath,
            WorkspaceOperation.Move);

        if (!destination.Allowed)
        {
            return MovePathPolicyResult.Deny(
                destination.Error,
                $"Move destination rejected: {destination.Message}",
                source.FullPath,
                destination.FullPath,
                source.EntryKind);
        }

        if (source.EntryKind == FileSystemEntryKind.Directory &&
            IsSameOrDescendant(destination.FullPath!, source.FullPath!))
        {
            return MovePathPolicyResult.Deny(
                WorkspaceAccessError.InvalidRelativePath,
                "A directory cannot be moved into itself or one of its descendants.",
                source.FullPath,
                destination.FullPath,
                source.EntryKind);
        }

        return MovePathPolicyResult.Allow(
            source.FullPath!,
            destination.FullPath!,
            source.EntryKind);
    }

    private PathPolicyResult BeginAccess(string workspaceId, WorkspaceOperation operation)
    {
        var resolution = workspaceResolver.Resolve(workspaceId);
        if (!resolution.Resolved || resolution.Workspace is null)
        {
            return PathPolicyResult.Deny(resolution.Error, resolution.Message);
        }

        var permission = permissionEvaluator.Evaluate(resolution.Workspace, operation);
        if (!permission.Allowed)
        {
            return PathPolicyResult.Deny(
                permission.Error,
                permission.Message,
                resolution.Workspace);
        }

        return PathPolicyResult.Allow(
            resolution.Workspace,
            string.Empty,
            resolution.Workspace.Root,
            FileSystemEntryKind.Directory);
    }

    private static PathPolicyResult ResolveRelativePath(
        WorkspaceDescriptor workspace,
        string relativePath,
        bool allowWorkspaceRoot)
    {
        if (relativePath is null)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.InvalidRelativePath,
                "Relative path is required.",
                workspace);
        }

        var trimmed = relativePath.Trim();

        if (Path.IsPathRooted(trimmed))
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.InvalidRelativePath,
                "Absolute/rooted paths are not allowed. Use a workspace-relative path.",
                workspace);
        }

        var segments = trimmed
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();

        if (segments.Any(segment => segment == ".."))
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.InvalidRelativePath,
                "Parent-directory traversal ('..') is not allowed.",
                workspace);
        }

        var normalizedRelativePath = string.Join(Path.DirectorySeparatorChar, segments);

        if (normalizedRelativePath.Length == 0 && !allowWorkspaceRoot)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.WorkspaceRootNotAllowed,
                "This operation cannot target the workspace root.",
                workspace,
                normalizedRelativePath,
                workspace.Root);
        }

        try
        {
            var root = Path.GetFullPath(workspace.Root);
            var fullPath = normalizedRelativePath.Length == 0
                ? root
                : Path.GetFullPath(Path.Combine(root, normalizedRelativePath));

            if (!IsWithinWorkspace(fullPath, root, allowWorkspaceRoot))
            {
                return PathPolicyResult.Deny(
                    WorkspaceAccessError.PathOutsideWorkspace,
                    "Resolved path is outside the approved workspace.",
                    workspace,
                    normalizedRelativePath,
                    fullPath);
            }

            return PathPolicyResult.Allow(
                workspace,
                normalizedRelativePath,
                fullPath,
                FileSystemEntryKind.Unknown);
        }
        catch (ArgumentException exception)
        {
            return InvalidPath(workspace, normalizedRelativePath, exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return InvalidPath(workspace, normalizedRelativePath, exception.Message);
        }
        catch (PathTooLongException exception)
        {
            return InvalidPath(workspace, normalizedRelativePath, exception.Message);
        }
    }

    private PathPolicyResult InspectExistingChain(
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string fullPath,
        bool allowWorkspaceRoot)
    {
        var rootInspection = entryInspector.Inspect(workspace.Root);
        var rootCheck = ValidateInspection(
            workspace,
            string.Empty,
            workspace.Root,
            rootInspection,
            requireDirectory: true);

        if (!rootCheck.Allowed)
        {
            return rootCheck;
        }

        if (normalizedRelativePath.Length == 0)
        {
            return allowWorkspaceRoot
                ? PathPolicyResult.Allow(workspace, string.Empty, workspace.Root, FileSystemEntryKind.Directory)
                : PathPolicyResult.Deny(
                    WorkspaceAccessError.WorkspaceRootNotAllowed,
                    "This operation cannot target the workspace root.",
                    workspace,
                    string.Empty,
                    workspace.Root,
                    FileSystemEntryKind.Directory);
        }

        var segments = normalizedRelativePath.Split(Path.DirectorySeparatorChar);
        var currentPath = workspace.Root;
        var finalKind = FileSystemEntryKind.Unknown;

        for (var index = 0; index < segments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);
            var inspection = entryInspector.Inspect(currentPath);
            var isFinal = index == segments.Length - 1;

            if (inspection.Kind == FileSystemEntryKind.Missing)
            {
                return PathPolicyResult.Deny(
                    WorkspaceAccessError.PathNotFound,
                    $"Path does not exist: {normalizedRelativePath}",
                    workspace,
                    normalizedRelativePath,
                    fullPath,
                    inspection.Kind);
            }

            var check = ValidateInspection(
                workspace,
                normalizedRelativePath,
                currentPath,
                inspection,
                requireDirectory: !isFinal);

            if (!check.Allowed)
            {
                return check with { FullPath = fullPath };
            }

            if (isFinal)
            {
                finalKind = inspection.Kind;
            }
        }

        return PathPolicyResult.Allow(
            workspace,
            normalizedRelativePath,
            fullPath,
            finalKind);
    }

    private PathPolicyResult InspectCreateChain(
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string fullPath)
    {
        var rootInspection = entryInspector.Inspect(workspace.Root);
        var rootCheck = ValidateInspection(
            workspace,
            string.Empty,
            workspace.Root,
            rootInspection,
            requireDirectory: true);

        if (!rootCheck.Allowed)
        {
            return rootCheck;
        }

        var segments = normalizedRelativePath.Split(Path.DirectorySeparatorChar);
        var currentPath = workspace.Root;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);
            var inspection = entryInspector.Inspect(currentPath);

            if (inspection.Kind == FileSystemEntryKind.Missing)
            {
                return PathPolicyResult.Deny(
                    WorkspaceAccessError.ParentNotFound,
                    $"Parent path does not exist: {Path.GetRelativePath(workspace.Root, currentPath)}",
                    workspace,
                    normalizedRelativePath,
                    fullPath,
                    inspection.Kind);
            }

            var check = ValidateInspection(
                workspace,
                normalizedRelativePath,
                currentPath,
                inspection,
                requireDirectory: true);

            if (!check.Allowed)
            {
                return check with { FullPath = fullPath };
            }
        }

        var targetInspection = entryInspector.Inspect(fullPath);
        if (targetInspection.Kind == FileSystemEntryKind.SymbolicLink)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.SymbolicLinkNotAllowed,
                "Symbolic links are not allowed in approved workspace paths.",
                workspace,
                normalizedRelativePath,
                fullPath,
                targetInspection.Kind);
        }

        if (targetInspection.Kind == FileSystemEntryKind.Unknown)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.InspectionFailed,
                targetInspection.Detail ?? "Unable to inspect target path.",
                workspace,
                normalizedRelativePath,
                fullPath,
                targetInspection.Kind);
        }

        if (targetInspection.Kind != FileSystemEntryKind.Missing)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.PathAlreadyExists,
                "The target path already exists.",
                workspace,
                normalizedRelativePath,
                fullPath,
                targetInspection.Kind);
        }

        return PathPolicyResult.Allow(
            workspace,
            normalizedRelativePath,
            fullPath,
            FileSystemEntryKind.Missing);
    }

    private static PathPolicyResult ValidateInspection(
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string inspectedPath,
        FileSystemEntryInspection inspection,
        bool requireDirectory)
    {
        if (inspection.Kind == FileSystemEntryKind.SymbolicLink)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.SymbolicLinkNotAllowed,
                $"Symbolic-link traversal is not allowed: {inspectedPath}",
                workspace,
                normalizedRelativePath,
                inspectedPath,
                inspection.Kind);
        }

        if (inspection.Kind == FileSystemEntryKind.Unknown)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.InspectionFailed,
                inspection.Detail ?? $"Unable to inspect filesystem entry: {inspectedPath}",
                workspace,
                normalizedRelativePath,
                inspectedPath,
                inspection.Kind);
        }

        if (inspection.Kind == FileSystemEntryKind.Other)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.UnsupportedEntryType,
                $"Unsupported filesystem entry type at: {inspectedPath}",
                workspace,
                normalizedRelativePath,
                inspectedPath,
                inspection.Kind);
        }

        if (inspection.Kind == FileSystemEntryKind.RegularFile && inspection.HardLinkCount > 1)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.HardLinkNotAllowed,
                $"Multiply-linked regular files are not allowed: {inspectedPath}",
                workspace,
                normalizedRelativePath,
                inspectedPath,
                inspection.Kind);
        }

        if (requireDirectory && inspection.Kind != FileSystemEntryKind.Directory)
        {
            return PathPolicyResult.Deny(
                WorkspaceAccessError.UnsupportedEntryType,
                $"A directory was required in the path chain: {inspectedPath}",
                workspace,
                normalizedRelativePath,
                inspectedPath,
                inspection.Kind);
        }

        return PathPolicyResult.Allow(
            workspace,
            normalizedRelativePath,
            inspectedPath,
            inspection.Kind);
    }

    private bool IsDenied(string normalizedRelativePath, out string? matchedPattern)
    {
        var normalizedForPolicy = normalizedRelativePath.Replace(Path.DirectorySeparatorChar, '/');
        return denyPathMatcher.IsDenied(normalizedForPolicy, out matchedPattern);
    }

    private static bool IsWithinWorkspace(string candidatePath, string rootPath, bool allowRoot)
    {
        var candidate = Path.GetFullPath(candidatePath);
        var root = Path.GetFullPath(rootPath);

        if (string.Equals(candidate, root, StringComparison.Ordinal))
        {
            return allowRoot;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }

    private static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath);
        var root = Path.GetFullPath(rootPath);

        if (string.Equals(candidate, root, StringComparison.Ordinal))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }

    private static PathPolicyResult InvalidPath(
        WorkspaceDescriptor workspace,
        string normalizedRelativePath,
        string detail) =>
        PathPolicyResult.Deny(
            WorkspaceAccessError.InvalidRelativePath,
            $"Relative path is invalid: {detail}",
            workspace,
            normalizedRelativePath);
}
