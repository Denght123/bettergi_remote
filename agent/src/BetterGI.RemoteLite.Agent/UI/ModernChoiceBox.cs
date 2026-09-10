using System.Drawing.Drawing2D;

namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class ModernChoiceBox : Control
{
    private readonly ContextMenuStrip _menu;
    private int _selectedIndex = -1;
    private bool _hovered;

    public ModernChoiceBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            true);
        Height = 46;
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular);
        AccessibleRole = AccessibleRole.ComboBox;
        _menu = new ContextMenuStrip
        {
            AutoSize = false,
            BackColor = UiPalette.Input,
            ForeColor = UiPalette.Text,
            Font = Font,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(4),
            Renderer = new ChoiceMenuRenderer(),
        };
        _menu.Closed += (_, _) => Invalidate();
    }

    public List<string> Items { get; } = [];

    public string? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = value >= 0 && value < Items.Count ? value : -1;
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            AccessibleName = SelectedItem ?? "请选择远程任务配置";
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? SelectedIndexChanged;

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
        Focus();
        ShowChoices();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space || e.Alt && e.KeyCode == Keys.Down)
        {
            ShowChoices();
            e.Handled = true;
            return;
        }
        if (Items.Count == 0) return;
        if (e.KeyCode == Keys.Down)
        {
            SelectedIndex = Math.Min(Items.Count - 1, Math.Max(0, SelectedIndex + 1));
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up)
        {
            SelectedIndex = Math.Max(0, SelectedIndex < 0 ? 0 : SelectedIndex - 1);
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = ModernButton.RoundedRectangle(bounds, 10);
        using var fill = new SolidBrush(_hovered || Focused || _menu.Visible ? Color.FromArgb(17, 44, 63) : UiPalette.Input);
        using var border = new Pen(Focused || _menu.Visible ? UiPalette.AccentBlue : _hovered ? UiPalette.LineBright : UiPalette.Line, Focused || _menu.Visible ? 1.6f : 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            e.Graphics,
            SelectedItem ?? "请选择配置",
            Font,
            new Rectangle(13, 0, Math.Max(0, Width - 54), Height),
            SelectedItem is null ? UiPalette.TextMuted : UiPalette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        using var arrow = new Pen(Focused || _menu.Visible ? UiPalette.AccentCyan : UiPalette.TextMuted, 1.9f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var centerX = Width - 25f;
        var centerY = Height / 2f;
        e.Graphics.DrawLines(arrow,
        [
            new PointF(centerX - 5f, centerY - 2.5f),
            new PointF(centerX, centerY + 2.5f),
            new PointF(centerX + 5f, centerY - 2.5f),
        ]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }

    private void ShowChoices()
    {
        if (_menu.Visible || Items.Count == 0) return;
        _menu.Items.Clear();
        foreach (var item in Items)
        {
            var menuItem = new ToolStripMenuItem(item)
            {
                AutoSize = false,
                Size = new Size(Math.Max(120, Width - 8), 36),
                Checked = string.Equals(item, SelectedItem, StringComparison.Ordinal),
                CheckOnClick = false,
                Padding = new Padding(12, 0, 12, 0),
            };
            menuItem.Click += (_, _) => SelectedItem = item;
            _menu.Items.Add(menuItem);
        }
        _menu.Size = new Size(Math.Max(120, Width), Math.Min(Items.Count * 36 + 8, 260));
        _menu.Show(this, new Point(0, Height + 4));
        Invalidate();
    }

    private sealed class ChoiceMenuRenderer : ToolStripProfessionalRenderer
    {
        public ChoiceMenuRenderer() : base(new ChoiceColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            using var fill = new SolidBrush(e.Item.Selected ? UiPalette.SurfaceRaised : UiPalette.Input);
            e.Graphics.FillRectangle(fill, bounds);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using var pen = new Pen(UiPalette.AccentCyan, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var y = e.Item.Height / 2f;
            e.Graphics.DrawLines(pen, [new PointF(10, y), new PointF(14, y + 4), new PointF(21, y - 4)]);
        }
    }

    private sealed class ChoiceColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => UiPalette.Input;
        public override Color MenuBorder => UiPalette.LineBright;
        public override Color MenuItemBorder => UiPalette.LineBright;
        public override Color MenuItemSelected => UiPalette.SurfaceRaised;
        public override Color MenuItemSelectedGradientBegin => UiPalette.SurfaceRaised;
        public override Color MenuItemSelectedGradientEnd => UiPalette.SurfaceRaised;
        public override Color ImageMarginGradientBegin => UiPalette.Input;
        public override Color ImageMarginGradientMiddle => UiPalette.Input;
        public override Color ImageMarginGradientEnd => UiPalette.Input;
    }
}
