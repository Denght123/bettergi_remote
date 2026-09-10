namespace BetterGI.RemoteLite.Agent.UI;

internal static class UiPalette
{
    public static readonly Color DarkCanvas = Color.FromArgb(12, 30, 45);
    public static readonly Color Sidebar = Color.FromArgb(14, 42, 64);
    public static readonly Color Surface = Color.FromArgb(22, 52, 72);
    public static readonly Color SurfaceRaised = Color.FromArgb(29, 65, 88);
    public static readonly Color Input = Color.FromArgb(15, 39, 56);
    public static readonly Color Line = Color.FromArgb(50, 82, 103);
    public static readonly Color LineBright = Color.FromArgb(73, 112, 136);
    public static readonly Color Text = Color.FromArgb(239, 247, 251);
    public static readonly Color TextMuted = Color.FromArgb(171, 194, 207);
    public static readonly Color AccentBlue = Color.FromArgb(44, 145, 194);
    public static readonly Color AccentCyan = Color.FromArgb(92, 190, 210);
    public static readonly Color Success = Color.FromArgb(91, 197, 151);
    public static readonly Color Warning = Color.FromArgb(235, 184, 100);
    public static readonly Color DangerText = Color.FromArgb(255, 170, 170);

    public static readonly Color Ink = Text;
    public static readonly Color Muted = TextMuted;
    public static readonly Color Pine = AccentBlue;
    public static readonly Color PineDeep = Sidebar;
    public static readonly Color Violet = AccentCyan;
    public static readonly Color VioletSoft = SurfaceRaised;
    public static readonly Color Paper = DarkCanvas;
    public static readonly Color Cream = Input;
    public static readonly Color Coral = DangerText;

    public static void StylePrimary(Button button)
    {
        button.BackColor = AccentBlue;
        button.ForeColor = Color.White;
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
}
