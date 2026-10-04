using System.Drawing.Drawing2D;

namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class SurfacePanel : TableLayoutPanel
{
    private Color _fillColor = UiPalette.Surface;
    public SurfacePanel()
    {
        DoubleBuffered = true;
        BackColor = _fillColor;
        ResizeRedraw = true;
    }
    public Color FillColor
    {
        get => _fillColor;
        set { if (_fillColor == value) return; _fillColor = value; BackColor = value; Invalidate(); }
    }
    public Color OutlineColor { get; set; } = UiPalette.Line;
    public int CornerRadius { get; set; } = 10;
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // An opaque parent color prevents each child label/layout from recursively
        // asking this card and every ancestor to paint the full background again.
        var canvas = Parent?.BackColor ?? UiPalette.DarkCanvas;
        if (canvas.A != 255) canvas = UiPalette.DarkCanvas;
        using var cornerBrush = new SolidBrush(canvas);
        e.Graphics.FillRectangle(cornerBrush, e.ClipRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = ModernButton.RoundedRectangle(rectangle, CornerRadius);
        using var fill = new SolidBrush(_fillColor);
        using var border = new Pen(OutlineColor, .8f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}
