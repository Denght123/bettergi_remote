namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class BufferedScrollPanel : Panel
{
    public BufferedScrollPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
        UpdateStyles();
    }
    // ScrollableControl already moves child windows and invalidates exposed pixels.
    // Invalidating every descendant on each thumb-track event defeats that native path.
}
