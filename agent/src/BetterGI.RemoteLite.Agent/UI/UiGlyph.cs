using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.UI;

internal enum UiGlyph { None, Devices, Qr, Status, Globe, Link, Unlink, Refresh, Game, Trash, Folder, Search, Save, Info, Chevron, Bell, Mail }

/// <summary>Official Fluent Regular glyphs in one fixed square; no free-form curve reconstruction.</summary>
internal static class UiGlyphPainter
{
    private static readonly PrivateFontCollection Fonts = new();
    private static readonly FontFamily Family = LoadFamily();
    public static void Draw(Graphics graphics, UiGlyph glyph, Rectangle bounds, Color color, float strokeWidth = 1.8f)
    {
        if (glyph == UiGlyph.None || bounds.Width <= 0 || bounds.Height <= 0) return;
        if (glyph == UiGlyph.Devices)
        {
            // Keep the already-approved linked PC/phone mark; all functional icons use Fluent glyphs.
            var state = graphics.Save();
            var brandSide = Math.Min(bounds.Width, bounds.Height);
            graphics.TranslateTransform(bounds.X + (bounds.Width - brandSide) / 2f, bounds.Y + (bounds.Height - brandSide) / 2f);
            graphics.ScaleTransform(brandSide / 24f, brandSide / 24f);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.35f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            graphics.DrawRectangle(pen, 2, 5, 12, 10);
            graphics.DrawLine(pen, 8, 15, 8, 19);
            graphics.DrawLine(pen, 5, 19, 11, 19);
            graphics.DrawRectangle(pen, 17, 2, 5, 18);
            graphics.DrawLine(pen, 19, 17, 20, 17);
            graphics.Restore(state);
            return;
        }
        var codepoint = Codepoint(glyph);
        if (codepoint == 0) return;
        var side = Math.Min(bounds.Width, bounds.Height);
        var square = new Rectangle(bounds.X + (bounds.Width - side) / 2, bounds.Y + (bounds.Height - side) / 2, side, side);
        using var font = new Font(Family, side, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(graphics, char.ConvertFromUtf32(codepoint), font, square, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
    private static FontFamily LoadFamily()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "RemoteFluentIcons-Regular.ttf");
        if (File.Exists(path))
        {
            _ = AddFontResourceEx(path, 0x10, IntPtr.Zero);
            Fonts.AddFontFile(path);
            var family = Fonts.Families.FirstOrDefault(face => face.Name == "Remote Fluent Icons");
            if (family is not null) return family;
        }
        return new FontFamily("Segoe UI Symbol");
    }
    private static int Codepoint(UiGlyph glyph) => glyph switch
    {
        UiGlyph.Devices => 0xF85E,
        UiGlyph.Qr => 0xF635,
        UiGlyph.Status => 0xE702,
        UiGlyph.Globe => 0xF45B,
        UiGlyph.Link => 0xF4E5,
        UiGlyph.Unlink => 0xE774,
        UiGlyph.Refresh => 0xF13E,
        UiGlyph.Game => 0xF451,
        UiGlyph.Trash => 0xF34D,
        UiGlyph.Folder => 0xF419,
        UiGlyph.Search => 0xF690,
        UiGlyph.Save => 0xF680,
        UiGlyph.Info => 0xF4A4,
        UiGlyph.Chevron => 0xF2A4,
        UiGlyph.Bell => 0xF115,
        UiGlyph.Mail => 0xF507,
        _ => 0,
    };
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "AddFontResourceExW")]
    private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
}
