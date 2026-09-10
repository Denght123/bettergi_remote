namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class HeroPanel : Panel
{
    public Color ShadeFrom { get; init; } = Color.FromArgb(225, 23, 72, 59);
    public Color ShadeTo { get; init; } = Color.FromArgb(28, 23, 72, 59);
    public float ImageFocusX { get; init; } = 0.5f;
    public float ImageFocusY { get; init; } = 0.5f;

    public HeroPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            BackgroundImage?.Dispose();
            BackgroundImage = null;
        }
        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (BackgroundImage is not null)
        {
            var target = ClientRectangle;
            var source = CoverSourceRectangle(BackgroundImage.Size, target.Size, ImageFocusX, ImageFocusY);
            e.Graphics.DrawImage(BackgroundImage, target, source, GraphicsUnit.Pixel);
            using var shade = new System.Drawing.Drawing2D.LinearGradientBrush(
                target,
                ShadeFrom,
                ShadeTo,
                0f);
            e.Graphics.FillRectangle(shade, target);
        }
        using var line = new Pen(Color.FromArgb(165, 255, 250, 230));
        e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }

    private static RectangleF CoverSourceRectangle(Size image, Size target, float focusX, float focusY)
    {
        var imageRatio = (float)image.Width / image.Height;
        var targetRatio = (float)target.Width / target.Height;
        if (imageRatio > targetRatio)
        {
            var width = image.Height * targetRatio;
            var x = Math.Clamp((image.Width - width) * focusX, 0, image.Width - width);
            return new RectangleF(x, 0, width, image.Height);
        }

        var height = image.Width / targetRatio;
        var y = Math.Clamp((image.Height - height) * focusY, 0, image.Height - height);
        return new RectangleF(0, y, image.Width, height);
    }
}
