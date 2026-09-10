namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class GlyphView : Control
{
    public GlyphView()
    {
        Size = new Size(24, 24);
        Cursor = Cursors.Help;
        TabStop = false;
        DoubleBuffered = true;
    }

    public UiGlyph Glyph { get; set; } = UiGlyph.Info;
    public Color GlyphColor { get; set; } = UiPalette.Text;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        UiGlyphPainter.Draw(e.Graphics, Glyph, new Rectangle(2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4)), GlyphColor);
    }
}
