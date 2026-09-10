using System.Drawing.Drawing2D;

namespace BetterGI.RemoteLite.Agent.UI;

internal enum UiGlyph
{
    None,
    Qr,
    Status,
    Globe,
    Link,
    Unlink,
    Refresh,
    Game,
    Trash,
    Folder,
    Search,
    Save,
    Info,
    Chevron,
    Bell,
    Mail,
}

internal static class UiGlyphPainter
{
    public static void Draw(Graphics graphics, UiGlyph glyph, Rectangle bounds, Color color, float strokeWidth = 1.8f)
    {
        if (glyph == UiGlyph.None) return;
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var x = bounds.X;
        var y = bounds.Y;
        var w = bounds.Width;
        var h = bounds.Height;
        switch (glyph)
        {
            case UiGlyph.Qr:
                DrawQr(graphics, pen, bounds);
                break;
            case UiGlyph.Status:
                graphics.DrawEllipse(pen, x + 2, y + 2, w - 4, h - 4);
                graphics.DrawLines(pen, [new PointF(x + 5, y + h * .56f), new PointF(x + w * .38f, y + h * .56f), new PointF(x + w * .48f, y + h * .32f), new PointF(x + w * .61f, y + h * .72f), new PointF(x + w - 5, y + h * .46f)]);
                break;
            case UiGlyph.Globe:
                graphics.DrawEllipse(pen, x + 2, y + 2, w - 4, h - 4);
                graphics.DrawArc(pen, x + w * .3f, y + 2, w * .4f, h - 4, 90, 180);
                graphics.DrawArc(pen, x + w * .3f, y + 2, w * .4f, h - 4, 270, 180);
                graphics.DrawLine(pen, x + 3, y + h / 2f, x + w - 3, y + h / 2f);
                break;
            case UiGlyph.Link:
            case UiGlyph.Unlink:
                graphics.DrawArc(pen, x + 1, y + h * .24f, w * .58f, h * .52f, 125, 230);
                graphics.DrawArc(pen, x + w * .41f, y + h * .24f, w * .58f, h * .52f, -55, 230);
                graphics.DrawLine(pen, x + w * .35f, y + h * .5f, x + w * .65f, y + h * .5f);
                if (glyph == UiGlyph.Unlink)
                {
                    graphics.DrawLine(pen, x + w * .2f, y + h * .12f, x + w * .8f, y + h * .88f);
                }
                break;
            case UiGlyph.Refresh:
                graphics.DrawArc(pen, x + 3, y + 3, w - 6, h - 6, -45, 285);
                graphics.DrawLines(pen, [new PointF(x + w - 2, y + 3), new PointF(x + w - 2, y + h * .36f), new PointF(x + w * .68f, y + 3)]);
                break;
            case UiGlyph.Game:
                graphics.DrawBezier(pen, new PointF(x + 3, y + h * .65f), new PointF(x + 5, y + h * .18f), new PointF(x + w - 5, y + h * .18f), new PointF(x + w - 3, y + h * .65f));
                graphics.DrawBezier(pen, new PointF(x + 3, y + h * .65f), new PointF(x + 2, y + h), new PointF(x + w * .34f, y + h * .72f), new PointF(x + w / 2f, y + h * .58f));
                graphics.DrawBezier(pen, new PointF(x + w - 3, y + h * .65f), new PointF(x + w - 2, y + h), new PointF(x + w * .66f, y + h * .72f), new PointF(x + w / 2f, y + h * .58f));
                graphics.DrawLine(pen, x + w * .27f, y + h * .43f, x + w * .27f, y + h * .65f);
                graphics.DrawLine(pen, x + w * .16f, y + h * .54f, x + w * .38f, y + h * .54f);
                graphics.DrawEllipse(pen, x + w * .68f, y + h * .42f, 2, 2);
                graphics.DrawEllipse(pen, x + w * .8f, y + h * .55f, 2, 2);
                break;
            case UiGlyph.Trash:
                graphics.DrawRectangle(pen, x + 5, y + 7, w - 10, h - 10);
                graphics.DrawLine(pen, x + 3, y + 6, x + w - 3, y + 6);
                graphics.DrawLine(pen, x + w * .38f, y + 3, x + w * .62f, y + 3);
                graphics.DrawLine(pen, x + w * .42f, y + 10, x + w * .42f, y + h - 6);
                graphics.DrawLine(pen, x + w * .58f, y + 10, x + w * .58f, y + h - 6);
                break;
            case UiGlyph.Folder:
                using (var path = new GraphicsPath())
                {
                    path.AddLines([new PointF(x + 2, y + 6), new PointF(x + w * .4f, y + 6), new PointF(x + w * .5f, y + 10), new PointF(x + w - 2, y + 10), new PointF(x + w - 3, y + h - 3), new PointF(x + 3, y + h - 3)]);
                    path.CloseFigure();
                    graphics.DrawPath(pen, path);
                }
                break;
            case UiGlyph.Search:
                graphics.DrawEllipse(pen, x + 2, y + 2, w * .62f, h * .62f);
                graphics.DrawLine(pen, x + w * .58f, y + h * .58f, x + w - 2, y + h - 2);
                break;
            case UiGlyph.Save:
                graphics.DrawRectangle(pen, x + 2, y + 2, w - 4, h - 4);
                graphics.DrawRectangle(pen, x + 6, y + 3, w - 12, h * .28f);
                graphics.DrawRectangle(pen, x + 6, y + h * .57f, w - 12, h * .29f);
                break;
            case UiGlyph.Info:
                graphics.DrawEllipse(pen, x + 2, y + 2, w - 4, h - 4);
                graphics.DrawLine(pen, x + w / 2f, y + h * .43f, x + w / 2f, y + h * .72f);
                using (var dot = new SolidBrush(color))
                {
                    graphics.FillEllipse(dot, x + w / 2f - 1, y + h * .25f, 2, 2);
                }
                break;
            case UiGlyph.Chevron:
                graphics.DrawLines(pen, [new PointF(x + 3, y + h * .38f), new PointF(x + w / 2f, y + h * .64f), new PointF(x + w - 3, y + h * .38f)]);
                break;
            case UiGlyph.Bell:
                graphics.DrawArc(pen, x + w * .25f, y + h * .18f, w * .5f, h * .55f, 180, 180);
                graphics.DrawLine(pen, x + w * .25f, y + h * .45f, x + w * .18f, y + h * .72f);
                graphics.DrawLine(pen, x + w * .75f, y + h * .45f, x + w * .82f, y + h * .72f);
                graphics.DrawLine(pen, x + w * .18f, y + h * .72f, x + w * .82f, y + h * .72f);
                graphics.DrawArc(pen, x + w * .4f, y + h * .69f, w * .2f, h * .17f, 0, 180);
                break;
            case UiGlyph.Mail:
                graphics.DrawRectangle(pen, x + 2, y + 4, w - 4, h - 8);
                graphics.DrawLines(pen, [new PointF(x + 3, y + 5), new PointF(x + w / 2f, y + h * .58f), new PointF(x + w - 3, y + 5)]);
                break;
        }
        graphics.SmoothingMode = previous;
    }

    private static void DrawQr(Graphics graphics, Pen pen, Rectangle bounds)
    {
        var size = bounds.Width * .34f;
        graphics.DrawRectangle(pen, bounds.X + 2, bounds.Y + 2, size, size);
        graphics.DrawRectangle(pen, bounds.Right - size - 2, bounds.Y + 2, size, size);
        graphics.DrawRectangle(pen, bounds.X + 2, bounds.Bottom - size - 2, size, size);
        graphics.DrawLine(pen, bounds.X + bounds.Width * .58f, bounds.Y + bounds.Height * .58f, bounds.Right - 2, bounds.Y + bounds.Height * .58f);
        graphics.DrawLine(pen, bounds.X + bounds.Width * .58f, bounds.Y + bounds.Height * .58f, bounds.X + bounds.Width * .58f, bounds.Bottom - 2);
        graphics.DrawLine(pen, bounds.X + bounds.Width * .78f, bounds.Y + bounds.Height * .72f, bounds.Right - 2, bounds.Bottom - 2);
    }
}
