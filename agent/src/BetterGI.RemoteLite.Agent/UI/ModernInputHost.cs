using System.Drawing.Drawing2D;

namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class ModernInputHost : Control
{
    private readonly Control _input;
    private bool _hovered;

    public ModernInputHost(Control input)
    {
        _input = input;
        Height = 43;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 6);
        DoubleBuffered = true;
        Cursor = input.Cursor;
        if (input is TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.None;
        }
        if (input is ComboBox comboBox)
        {
            comboBox.FlatStyle = FlatStyle.Flat;
        }
        input.BackColor = UiPalette.Input;
        input.ForeColor = UiPalette.Text;
        input.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular);
        input.GotFocus += (_, _) => Invalidate();
        input.LostFocus += (_, _) => Invalidate();
        Controls.Add(input);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_input is ComboBox)
        {
            _input.Bounds = new Rectangle(10, 7, Math.Max(0, ClientSize.Width - 20), 29);
        }
        else
        {
            _input.Bounds = new Rectangle(12, 12, Math.Max(0, ClientSize.Width - 24), 22);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _input.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = ModernButton.RoundedRectangle(rectangle, 10);
        using var fill = new SolidBrush(UiPalette.Input);
        using var border = new Pen(_input.Focused ? UiPalette.AccentBlue : _hovered ? UiPalette.LineBright : UiPalette.Line, _input.Focused ? 1.6f : 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}
