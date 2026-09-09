namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class HeroPanel : Panel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (BackgroundImage is not null)
        {
            var target = ClientRectangle;
            e.Graphics.DrawImage(BackgroundImage, target);
            using var shade = new System.Drawing.Drawing2D.LinearGradientBrush(
                target,
                Color.FromArgb(225, 7, 18, 31),
                Color.FromArgb(55, 7, 18, 31),
                0f);
            e.Graphics.FillRectangle(shade, target);
        }
        using var line = new Pen(Color.FromArgb(180, 214, 181, 106));
        e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}
