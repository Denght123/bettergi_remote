using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BetterGI.RemoteLite.Agent.UI;

/// <summary>Client-drawn light window chrome. Resize/drag/maximize are window operations only.</summary>
internal class StudioForm : Form
{
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TableLayoutPanel _window = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(1) };
    private readonly Panel _caption = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _title = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty };
    private readonly FlowLayoutPanel _captionActions = new() { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, Width = 144 };
    private readonly ChromeButton _minimize = new(0);
    private readonly ChromeButton _maximize = new(1);
    private readonly ChromeButton _close = new(2);
    private bool _resizeEnabled;
    public new Control.ControlCollection Controls => _content.Controls;
    public bool ResizeEnabled
    {
        get => _resizeEnabled;
        set { if (_resizeEnabled == value) return; _resizeEnabled = value; if (IsHandleCreated) RecreateHandle(); SyncChrome(); }
    }
    public StudioForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = UiPalette.DarkCanvas;
        ForeColor = UiPalette.Text;
        DoubleBuffered = true;
        _window.BackColor = UiPalette.Line;
        _window.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _window.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _window.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _content.BackColor = UiPalette.DarkCanvas;
        _caption.BackColor = UiPalette.Sidebar;
        var mark = new Panel { Dock = DockStyle.Left, Width = 42, BackColor = UiPalette.Sidebar };
        mark.Controls.Add(new GlyphView { Glyph = UiGlyph.Devices, GlyphColor = UiPalette.AccentCyan, Size = new Size(22, 22), Location = new Point(12, 13) });
        _title.Font = UiPalette.Font(10.5f);
        _title.ForeColor = UiPalette.Text;
        _title.BackColor = UiPalette.Sidebar;
        _caption.Controls.Add(_title);
        _caption.Controls.Add(mark);
        _caption.Controls.Add(_captionActions);
        foreach (var button in new[] { _minimize, _maximize, _close }) _captionActions.Controls.Add(button);
        _minimize.AccessibleName = "最小化窗口";
        _maximize.AccessibleName = "最大化或还原窗口";
        _close.AccessibleName = "关闭窗口";
        _minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _maximize.Click += (_, _) => ToggleMaximize();
        _close.Click += (_, _) => Close();
        _title.MouseDown += DragCaption;
        _caption.MouseDown += DragCaption;
        _title.DoubleClick += (_, _) => ToggleMaximize();
        _caption.DoubleClick += (_, _) => ToggleMaximize();
        TextChanged += (_, _) => _title.Text = Text;
        _window.Controls.Add(_caption, 0, 0);
        _window.Controls.Add(_content, 0, 1);
        base.Controls.Add(_window);
    }
    protected void SetContentSize(Size size)
    {
        var captionHeight = Math.Max(48, 48 * DeviceDpi / 96);
        base.ClientSize = new Size(size.Width + 2, size.Height + captionHeight + 2);
    }
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style &= ~0x00C00000; // no OS caption frame
            if (_resizeEnabled) cp.Style |= 0x00040000 | 0x00020000 | 0x00010000;
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SyncChrome();
        if (AcceptButton is Control accept) accept.Focus();
        else _content.SelectNextControl(null, true, true, true, false);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SyncChrome();
        UpdateRegion();
    }
    protected override void OnStyleChanged(EventArgs e) { base.OnStyleChanged(e); SyncChrome(); }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_window is null) return;
        _maximize.Restored = WindowState == FormWindowState.Maximized;
        UpdateRegion();
    }
    private void SyncChrome()
    {
        if (_captionActions is null || _window.RowStyles.Count == 0) return;
        _minimize.Visible = ControlBox && MinimizeBox;
        _maximize.Visible = ControlBox && MaximizeBox && _resizeEnabled;
        _close.Visible = ControlBox;
        var scale = Math.Max(1f, DeviceDpi / 96f);
        foreach (var button in new[] { _minimize, _maximize, _close }) button.Size = new Size((int)(44 * scale), (int)(46 * scale));
        _captionActions.Width = (ControlBox ? ((MinimizeBox ? 1 : 0) + (MaximizeBox && _resizeEnabled ? 1 : 0) + 1) : 0) * (int)(44 * scale);
        _captionActions.Height = (int)(48 * scale);
        _window.RowStyles[0].Height = 48 * scale;
    }
    private void UpdateRegion()
    {
        if (Width < 4 || Height < 4) return;
        var old = Region;
        if (WindowState == FormWindowState.Maximized) Region = null;
        else
        {
            using var path = ModernButton.RoundedRectangle(new Rectangle(0, 0, Width, Height), Math.Max(8, 12 * DeviceDpi / 96));
            Region = new Region(path);
        }
        old?.Dispose();
    }
    private void ToggleMaximize()
    {
        if (!MaximizeBox || !_resizeEnabled) return;
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }
    private void DragCaption(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!ControlBox && keyData == (Keys.Alt | Keys.F4)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0083) { m.Result = IntPtr.Zero; return; }
        if (m.Msg == 0x0084 && _resizeEnabled && WindowState == FormWindowState.Normal)
        {
            var point = PointToClient(new Point(unchecked((short)(m.LParam.ToInt64() & 0xffff)), unchecked((short)((m.LParam.ToInt64() >> 16) & 0xffff))));
            var edge = Math.Max(6, 6 * DeviceDpi / 96);
            var left = point.X < edge; var right = point.X >= Width - edge;
            var top = point.Y < edge; var bottom = point.Y >= Height - edge;
            var hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
            if (hit != 0) { m.Result = (IntPtr)hit; return; }
        }
        base.WndProc(ref m);
        if (m.Msg == 0x0024 && m.LParam != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(m.LParam);
            var monitor = Screen.FromHandle(Handle);
            var work = monitor.WorkingArea; var bounds = monitor.Bounds;
            info.MaxPosition = new Point(work.Left - bounds.Left, work.Top - bounds.Top);
            info.MaxSize = new Point(work.Width, work.Height);
            Marshal.StructureToPtr(info, m.LParam, false);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public Point Reserved; public Point MaxSize; public Point MaxPosition; public Point MinTrackSize; public Point MaxTrackSize; }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}

internal sealed class ChromeButton : Button
{
    private readonly int _kind;
    private bool _hover;
    public bool Restored { get; set; }
    public ChromeButton(int kind)
    {
        _kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(44, 46); Margin = Padding.Empty; TabStop = true; AccessibleRole = AccessibleRole.PushButton;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_hover ? _kind == 2 ? Color.FromArgb(251, 226, 223) : Color.FromArgb(223, 231, 240) : UiPalette.Sidebar);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Max(10, 10 * DeviceDpi / 96); var x = (Width - size) / 2; var y = (Height - size) / 2;
        using var pen = new Pen(_hover && _kind == 2 ? UiPalette.DangerText : UiPalette.Text, 1.2f);
        if (_kind == 0) e.Graphics.DrawLine(pen, x, y + size / 2, x + size, y + size / 2);
        else if (_kind == 1)
        {
            if (Restored) { e.Graphics.DrawRectangle(pen, x + 2, y - 2, size - 2, size - 2); using var background = new SolidBrush(UiPalette.Sidebar); e.Graphics.FillRectangle(background, x, y + 2, size - 2, size - 2); }
            e.Graphics.DrawRectangle(pen, x, y, size, size);
        }
        else { e.Graphics.DrawLine(pen, x, y, x + size, y + size); e.Graphics.DrawLine(pen, x + size, y, x, y + size); }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), UiPalette.AccentCyan, UiPalette.Sidebar);
    }
}
