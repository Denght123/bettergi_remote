using System.Drawing.Drawing2D;

namespace BetterGI.RemoteLite.Agent.UI;

internal enum ModernButtonVariant
{
    Primary,
    Secondary,
    Danger,
    Ghost,
}

internal sealed class ModernButton : Button
{
    private readonly System.Windows.Forms.Timer _animationTimer;
    private float _hoverProgress;
    private float _targetProgress;
    private bool _pressed;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Height = 46;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        _animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animationTimer.Tick += (_, _) => Animate();
    }

    public UiGlyph Glyph { get; set; }
    public ModernButtonVariant Variant { get; set; } = ModernButtonVariant.Secondary;
    public int CornerRadius { get; set; } = 11;
    public int GlyphSize { get; set; } = 21;
    public int ContentPadding { get; set; } = 16;
    public int GlyphGap { get; set; } = 10;
    public Color CanvasColor { get; set; } = UiPalette.Surface;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _targetProgress = 1;
        _animationTimer.Start();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _pressed = false;
        _targetProgress = 0;
        _animationTimer.Start();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(CanvasColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(1, _pressed ? 2 : 1, Width - 3, Height - 4);
        var (normal, hover, foreground, border) = Colors();
        var background = Blend(normal, hover, _hoverProgress);
        if (!Enabled)
        {
            background = Blend(background, UiPalette.DarkCanvas, .48f);
            foreground = Color.FromArgb(118, 139, 154);
            border = Color.FromArgb(55, 80, 98);
        }
        using var path = RoundedRectangle(rectangle, CornerRadius);
        using var brush = new SolidBrush(background);
        e.Graphics.FillPath(brush, path);
        using var borderPen = new Pen(border, 1);
        e.Graphics.DrawPath(borderPen, path);

        var iconSize = Math.Max(14, GlyphSize);
        var textSize = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var contentWidth = textSize.Width + (Glyph == UiGlyph.None ? 0 : iconSize + GlyphGap);
        var startX = TextAlign == ContentAlignment.MiddleLeft ? ContentPadding : Math.Max(ContentPadding, (Width - contentWidth) / 2);
        if (Glyph != UiGlyph.None)
        {
            UiGlyphPainter.Draw(e.Graphics, Glyph, new Rectangle(startX, (Height - iconSize) / 2 + (_pressed ? 1 : 0), iconSize, iconSize), foreground);
            startX += iconSize + GlyphGap;
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(startX, _pressed ? 2 : 1, Math.Max(0, Width - startX - 10), Height - 3), foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            using var focusPen = new Pen(Color.FromArgb(180, UiPalette.AccentBlue)) { DashStyle = DashStyle.Dot };
            using var focusPath = RoundedRectangle(Rectangle.Inflate(rectangle, -3, -3), Math.Max(4, CornerRadius - 3));
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animationTimer.Dispose();
        base.Dispose(disposing);
    }

    private void Animate()
    {
        var delta = _targetProgress - _hoverProgress;
        if (Math.Abs(delta) < .03f)
        {
            _hoverProgress = _targetProgress;
            _animationTimer.Stop();
        }
        else
        {
            _hoverProgress += delta * .28f;
        }
        Invalidate();
    }

    private (Color Normal, Color Hover, Color Foreground, Color Border) Colors() => Variant switch
    {
        ModernButtonVariant.Primary => (UiPalette.AccentBlue, Color.FromArgb(69, 174, 226), Color.White, Color.FromArgb(96, 189, 230)),
        ModernButtonVariant.Danger => (Color.FromArgb(63, 43, 51), Color.FromArgb(91, 48, 57), UiPalette.DangerText, Color.FromArgb(135, 73, 79)),
        ModernButtonVariant.Ghost => (UiPalette.DarkCanvas, UiPalette.SurfaceRaised, UiPalette.Text, UiPalette.Line),
        _ => (UiPalette.SurfaceRaised, Color.FromArgb(43, 78, 103), UiPalette.Text, UiPalette.LineBright),
    };

    private static Color Blend(Color from, Color to, float amount)
        => Color.FromArgb(
            (int)(from.A + (to.A - from.A) * amount),
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));

    internal static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
