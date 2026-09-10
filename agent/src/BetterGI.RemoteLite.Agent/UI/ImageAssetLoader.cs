using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.UI;

internal static class ImageAssetLoader
{
    public static Image? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var source = Image.FromFile(path);
            return new Bitmap(source);
        }
        catch (Exception exception) when (exception is ArgumentException or OutOfMemoryException or ExternalException)
        {
            return null;
        }
    }
}
