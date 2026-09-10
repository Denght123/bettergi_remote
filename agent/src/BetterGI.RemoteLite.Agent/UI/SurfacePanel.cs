using System.Drawing.Drawing2D;

namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class SurfacePanel : TableLayoutPanel
{
    public SurfacePanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        ResizeRedraw = true;
    }

    public Color FillColor { get; set; } = UiPalette.Surface;
    public Color OutlineColor { get; set; } = UiPalette.Line;
    public int CornerRadius { get; set; } = 15;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = ModernButton.RoundedRectangle(rectangle, CornerRadius);
        using var fill = new SolidBrush(FillColor);
        using var border = new Pen(OutlineColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}
