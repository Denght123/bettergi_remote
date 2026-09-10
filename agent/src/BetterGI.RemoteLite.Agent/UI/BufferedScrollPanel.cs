namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class BufferedScrollPanel : Panel
{
    public BufferedScrollPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        DoubleBuffered = true;
        ResizeRedraw = true;
        UpdateStyles();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate(true);
    }
}
