using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.UI;

internal static class UiMotion
{
    // Honor Windows' "Animation effects" setting, including for owner-drawn controls.
    public static bool Enabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            return SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled;
        }
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
