namespace BetterGI.RemoteLite.Agent.UI;

internal static class UiPalette
{
    public static readonly Color Ink = Color.FromArgb(48, 56, 65);
    public static readonly Color Muted = Color.FromArgb(91, 105, 97);
    public static readonly Color Pine = Color.FromArgb(36, 96, 78);
    public static readonly Color PineDeep = Color.FromArgb(23, 72, 59);
    public static readonly Color Violet = Color.FromArgb(105, 85, 143);
    public static readonly Color VioletSoft = Color.FromArgb(235, 228, 243);
    public static readonly Color Paper = Color.FromArgb(247, 244, 232);
    public static readonly Color Cream = Color.FromArgb(255, 252, 244);
    public static readonly Color Coral = Color.FromArgb(185, 79, 74);

    public static void StylePrimary(Button button)
    {
        button.BackColor = Pine;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(16, 6, 16, 6);
    }

    public static void StyleSecondary(Button button)
    {
        button.BackColor = VioletSoft;
        button.ForeColor = Violet;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(150, 130, 177);
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(12, 4, 12, 4);
    }
}
