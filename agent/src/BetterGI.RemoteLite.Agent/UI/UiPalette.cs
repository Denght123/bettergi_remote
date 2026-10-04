using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.UI;

internal static class UiPalette
{
    public static readonly Color DarkCanvas = Color.FromArgb(245, 247, 250);
    public static readonly Color Sidebar = Color.FromArgb(237, 241, 245);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceRaised = Color.FromArgb(242, 246, 250);
    public static readonly Color Input = Color.FromArgb(249, 251, 253);
    public static readonly Color Line = Color.FromArgb(231, 235, 240);
    public static readonly Color LineBright = Color.FromArgb(208, 218, 228);
    public static readonly Color Text = Color.FromArgb(43, 49, 57);
    public static readonly Color TextMuted = Color.FromArgb(95, 107, 120);
    public static readonly Color AccentBlue = Color.FromArgb(63, 106, 155);
    public static readonly Color AccentCyan = Color.FromArgb(66, 106, 151);
    public static readonly Color Success = Color.FromArgb(43, 116, 87);
    public static readonly Color Warning = Color.FromArgb(136, 92, 35);
    public static readonly Color DangerText = Color.FromArgb(162, 66, 57);
    public static readonly Color Ink = Text;
    public static readonly Color Muted = TextMuted;
    public static readonly Color Pine = AccentBlue;
    public static readonly Color PineDeep = Sidebar;
    public static readonly Color Violet = AccentCyan;
    public static readonly Color VioletSoft = SurfaceRaised;
    public static readonly Color Paper = DarkCanvas;
    public static readonly Color Cream = Input;
    public static readonly Color Coral = DangerText;

    public static Font Font(float points = 10.5f, FontStyle style = FontStyle.Regular)
        => UiTypography.Create(points, style);

    public static void ApplyWindow(Form form)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = Font();
        form.BackColor = DarkCanvas;
        form.ForeColor = Text;
        form.ShowIcon = false;
        form.HandleCreated += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var value = 0;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int));
            var caption = ColorTranslator.ToWin32(Sidebar);
            _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            var foreground = ColorTranslator.ToWin32(Text);
            _ = DwmSetWindowAttribute(form.Handle, 36, ref foreground, sizeof(int));
        };
    }

    public static void StylePrimary(Button button)
    {
        button.BackColor = AccentBlue;
        button.ForeColor = DarkCanvas;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(16, 6, 16, 6);
    }
    public static void StyleSecondary(Button button)
    {
        button.BackColor = SurfaceRaised;
        button.ForeColor = Text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = LineBright;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(12, 4, 12, 4);
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
