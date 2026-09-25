using System.Runtime.InteropServices;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Xunit;

namespace LocalAgent.UnitTests;

public sealed class WindowsFileSystemAdapterTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            $"codicks-lite-win-fs-{Guid.NewGuid():N}");

    public WindowsFileSystemAdapterTests()
    {
        Directory.CreateDirectory(
            _root);
    }

    [Fact]
    public void EntryInspector_MapsRegularDirectoryAndMissing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inspector =
            new WindowsFileSystemEntryInspector();

        var filePath =
            Path.Combine(
                _root,
                "sample.txt");

        var directoryPath =
            Path.Combine(
                _root,
                "folder");

        File.WriteAllText(
            filePath,
            "sample");

        Directory.CreateDirectory(
            directoryPath);

        var file =
            inspector.Inspect(
                filePath);

        var directory =
            inspector.Inspect(
                directoryPath);

        var missing =
            inspector.Inspect(
                Path.Combine(
                    _root,
                    "missing.txt"));

        Assert.Equal(
            FileSystemEntryKind.RegularFile,
            file.Kind);

        Assert.Equal(
            1,
            file.HardLinkCount);

        Assert.Equal(
            FileSystemEntryKind.Directory,
            directory.Kind);

        Assert.Equal(
            FileSystemEntryKind.Missing,
            missing.Kind);
    }

    [Fact]
    public void EntryInspector_TreatsReparsePointAsSymbolicLink()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var target =
            Path.Combine(
                _root,
                "target.txt");

        var link =
            Path.Combine(
                _root,
                "link.txt");

        File.WriteAllText(
            target,
            "target");

        try
        {
            File.CreateSymbolicLink(
                link,
                target);
        }
        catch (Exception exception)
            when (exception is
                UnauthorizedAccessException or
                IOException or
                PlatformNotSupportedException)
        {
            return;
        }

        var result =
            new WindowsFileSystemEntryInspector()
                .Inspect(
                    link);

        Assert.Equal(
            FileSystemEntryKind.SymbolicLink,
            result.Kind);

        Assert.Contains(
            "reparse",
            result.Detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntryInspector_ReportsHardLinkCount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var original =
            Path.Combine(
                _root,
                "original.txt");

        var alias =
            Path.Combine(
                _root,
                "alias.txt");

        File.WriteAllText(
            original,
            "same file");

        var created =
            CreateHardLinkW(
                alias,
                original,
                IntPtr.Zero);

        Assert.True(
            created,
            $"CreateHardLinkW failed with Windows error {Marshal.GetLastWin32Error()}.");

        var result =
            new WindowsFileSystemEntryInspector()
                .Inspect(
                    alias);

        Assert.Equal(
            FileSystemEntryKind.RegularFile,
            result.Kind);

        Assert.True(
            result.HardLinkCount >= 2);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("CON.txt")]
    [InlineData("NUL")]
    [InlineData("COM1.log")]
    [InlineData("LPT9")]
    public void EntryInspector_RejectsReservedDosDeviceNames(
        string fileName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result =
            new WindowsFileSystemEntryInspector()
                .Inspect(
                    Path.Combine(
                        _root,
                        fileName));

        Assert.Equal(
            FileSystemEntryKind.Other,
            result.Kind);

        Assert.Contains(
            "device",
            result.Detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sample.txt:secret")]
    [InlineData("folder.\\sample.txt")]
    [InlineData("folder \\sample.txt")]
    public void EntryInspector_RejectsWindowsAliasSyntax(
        string relativePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result =
            new WindowsFileSystemEntryInspector()
                .Inspect(
                    Path.Combine(
                        _root,
                        relativePath));

        Assert.Equal(
            FileSystemEntryKind.Other,
            result.Kind);
    }

    [Fact]
    public void DeviceInspector_ReturnsSameIdentityForSameVolume()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var file =
            Path.Combine(
                _root,
                "volume.txt");

        File.WriteAllText(
            file,
            "volume");

        var inspector =
            new WindowsFileSystemDeviceInspector();

        var directoryId =
            inspector.GetDeviceId(
                _root);

        var fileId =
            inspector.GetDeviceId(
                file);

        Assert.Equal(
            directoryId,
            fileId);

        Assert.StartsWith(
            "WIN",
            directoryId,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceInspector_DistinguishesAvailableVolumes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var roots =
            DriveInfo.GetDrives()
                .Where(
                    drive =>
                        drive.IsReady)
                .Select(
                    drive =>
                        drive.RootDirectory.FullName)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();

        if (roots.Length < 2)
        {
            return;
        }

        var inspector =
            new WindowsFileSystemDeviceInspector();

        Assert.NotEqual(
            inspector.GetDeviceId(
                roots[0]),
            inspector.GetDeviceId(
                roots[1]));
    }

    [Fact]
    public void WorkspacePolicy_WithWindowsInspector_RejectsHardLink()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var original =
            Path.Combine(
                _root,
                "policy-original.txt");

        var alias =
            Path.Combine(
                _root,
                "policy-alias.txt");

        File.WriteAllText(
            original,
            "same file");

        var created =
            CreateHardLinkW(
                alias,
                original,
                IntPtr.Zero);

        Assert.True(
            created,
            $"CreateHardLinkW failed with Windows error {Marshal.GetLastWin32Error()}.");

        var policy =
            CreatePolicy();

        var result =
            policy.ValidateExisting(
                "windows",
                "policy-alias.txt",
                WorkspaceOperation.Read);

        Assert.False(
            result.Allowed);

        Assert.Equal(
            WorkspaceAccessError.HardLinkNotAllowed,
            result.Error);
    }

    [Fact]
    public void WorkspacePolicy_WithWindowsInspector_AllowsCaseInsensitivePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory =
            Path.Combine(
                _root,
                "CaseDir");

        Directory.CreateDirectory(
            directory);

        File.WriteAllText(
            Path.Combine(
                directory,
                "Sample.txt"),
            "case");

        var result =
            CreatePolicy()
                .ValidateExisting(
                    "windows",
                    "casedir\\sample.txt",
                    WorkspaceOperation.Read);

        Assert.True(
            result.Allowed,
            result.Message);

        Assert.Equal(
            FileSystemEntryKind.RegularFile,
            result.EntryKind);
    }

    [Fact]
    public void WorkspacePolicy_RejectsUncRootedInput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result =
            CreatePolicy()
                .ValidateExisting(
                    "windows",
                    @"\\server\share\secret.txt",
                    WorkspaceOperation.Read);

        Assert.False(
            result.Allowed);

        Assert.Equal(
            WorkspaceAccessError.InvalidRelativePath,
            result.Error);
    }

    private WorkspacePathPolicy CreatePolicy()
    {
        var configuration =
            new AgentConfiguration();

        configuration.Agent.Security.DenyGlobs =
            [];

        configuration.Agent.Workspaces[
            "windows"] =
            new WorkspaceOptions
            {
                Root =
                    _root,
                Enabled =
                    true,
                AllowedOperations =
                    [
                        "read",
                        "create",
                        "update",
                        "move",
                        "delete",
                        "restore"
                    ]
            };

        var registry =
            new WorkspaceRegistry(
                configuration,
                new UserPathResolver());

        return new WorkspacePathPolicy(
            new WorkspaceResolver(
                registry),
            new WorkspacePermissionEvaluator(
                configuration),
            new DenyPathMatcher(
                AgentSecurityDefaults
                    .GetEffectiveDenyGlobs(
                        configuration.Agent.Security.DenyGlobs)),
            new WindowsFileSystemEntryInspector());
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    public void Dispose()
    {
        if (Directory.Exists(
                _root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }
}
