using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LocalAgent.Infrastructure.Security;

internal static class WindowsNamedPipeClientLocality
{
    private const int ErrorPipeLocal =
        229;

    public static bool IsLocalClient(
        SafePipeHandle pipeHandle)
    {
        ArgumentNullException.ThrowIfNull(
            pipeHandle);

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var computerName =
            new char[256];

        if (GetNamedPipeClientComputerNameW(
                pipeHandle,
                computerName,
                checked(
                    (uint)(
                        computerName.Length *
                        sizeof(char)))))
        {
            // A computer name means the request arrived through the
            // remote named-pipe transport. Local control never accepts it.
            return false;
        }

        return Marshal.GetLastWin32Error() ==
            ErrorPipeLocal;
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "GetNamedPipeClientComputerNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientComputerNameW(
        SafePipeHandle pipe,
        [Out] char[] clientComputerName,
        uint clientComputerNameLength);
}
