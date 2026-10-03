using System.Runtime.InteropServices;
using System.Text;

namespace BetterGI.RemoteLite.Agent.Native;

internal static class BetterGiWindowCloser
{
    public static bool RequestClose(int processId, nint mainWindow)
    {
        // Process.MainWindowHandle is zero for a window hidden in the tray. Locate
        // only BetterGI's own main WPF window; never close a game or arbitrary dialog.
        if (mainWindow == 0 || !IsMainWindow(mainWindow))
        {
            var candidates = new List<nint>();
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner != processId) return true;
                if (IsMainWindow(window)) candidates.Add(window);
                return true;
            }, 0);
            if (candidates.Count != 1) return false;
            mainWindow = candidates[0];
        }
        GetWindowThreadProcessId(mainWindow, out var processOwner);
        return processOwner == processId && PostMessage(mainWindow, 0x0010, 0, 0); // WM_CLOSE, normal application shutdown
    }

    private static bool IsMainWindow(nint window)
    {
        var title = new StringBuilder(256);
        var className = new StringBuilder(256);
        GetWindowText(window, title, title.Capacity);
        GetClassName(window, className, className.Capacity);
        return className.ToString().StartsWith("HwndWrapper[", StringComparison.Ordinal) &&
            (title.ToString() == "更好的原神" || title.ToString().StartsWith("BetterGI · 更好的原神", StringComparison.Ordinal));
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
