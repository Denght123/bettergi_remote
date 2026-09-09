using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.Native;

internal static class SessionProbe
{
    private const uint DesktopSwitchDesktop = 0x0100;

    public static bool IsUnlocked()
    {
        var desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }
        CloseDesktop(desktop);
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
}

