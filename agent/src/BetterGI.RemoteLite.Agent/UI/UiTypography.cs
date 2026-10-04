using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.UI;

internal static class UiTypography
{
    private static readonly PrivateFontCollection Fonts = new();
    private static readonly FontFamily Family = LoadFamily();
    public static Font Create(float points = 10.5f, FontStyle style = FontStyle.Regular)
        => new(Family, points, style, GraphicsUnit.Point);
    private static FontFamily LoadFamily()
    {
        try
        {
            foreach (var style in new[] { "Regular", "Bold" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "RemoteUISans-" + style + ".ttf");
                if (!File.Exists(path)) continue;
                // GDI's TextRenderer and GDI+ both need the same privately loaded face.
                if (OperatingSystem.IsWindows()) _ = AddFontResourceEx(path, 0x10, IntPtr.Zero);
                Fonts.AddFontFile(path);
            }
            var family = Fonts.Families.FirstOrDefault(face => face.Name == "Remote UI Sans");
            if (family is not null) return family;
        }
        catch (ArgumentException) { }
        catch (IOException) { }
        return new FontFamily("Microsoft YaHei UI");
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "AddFontResourceExW")]
    private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
}
