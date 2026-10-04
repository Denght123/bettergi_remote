using System.Drawing.Drawing2D;

namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class UpdateProgressForm : StudioForm
{
    private readonly Label _percent;
    private readonly Label _description;
    private readonly Label _stage;
    private readonly AnimatedProgressTrack _track;

    public UpdateProgressForm(string version)
    {
        Text = "BetterGI Remote · 更新";
        UiPalette.ApplyWindow(this);
        StartPosition = FormStartPosition.CenterScreen;
        ResizeEnabled = false;
        ControlBox = false;
        SetContentSize(new Size(560, 360));
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32), ColumnCount = 1, RowCount = 6 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 52, 36, 96, 18, 44, 42 }) shell.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        shell.Controls.Add(new Label { Text = "正在获取新版本", Dock = DockStyle.Fill, Font = UiPalette.Font(19, FontStyle.Bold), ForeColor = UiPalette.Text, AutoEllipsis = true }, 0, 0);
        _description = new Label { Text = $"BetterGI Remote {version} · 下载并验证安装包", Dock = DockStyle.Fill, ForeColor = UiPalette.TextMuted, Font = UiPalette.Font(10) };
        shell.Controls.Add(_description, 0, 1);
        _percent = new Label { Text = "0%", Dock = DockStyle.Fill, Font = UiPalette.Font(45), ForeColor = UiPalette.AccentCyan, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "更新下载进度" };
        shell.Controls.Add(_percent, 0, 2);
        _track = new AnimatedProgressTrack { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 4), AccessibleName = "安装包下载进度条" };
        shell.Controls.Add(_track, 0, 3);
        _stage = new Label { Text = "正在下载   /   SHA-256 校验   /   准备安装", Dock = DockStyle.Fill, Font = UiPalette.Font(10), ForeColor = UiPalette.TextMuted, TextAlign = ContentAlignment.MiddleLeft };
        shell.Controls.Add(_stage, 0, 4);
        shell.Controls.Add(new Label { Text = "验证通过后才会启动安装器。现有设置和手机绑定将沿用。", Dock = DockStyle.Fill, ForeColor = UiPalette.TextMuted, Font = UiPalette.Font(9), AutoSize = false }, 0, 5);
        Controls.Add(shell);
    }
    public void SetProgress(int value)
    {
        var actual = Math.Clamp(value, 0, 100);
        _percent.Text = actual + "%";
        _track.Value = actual;
        if (actual == 100)
        {
            _stage.Text = "下载完成   /   校验通过   /   正在准备安装";
            _stage.ForeColor = UiPalette.Success;
            _description.Text = "安装包已通过验证，正在交给安装器。";
        }
    }
}

internal sealed class AnimatedProgressTrack : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private float _display;
    private int _value;
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            AccessibleDescription = _value + "%";
            if (UiMotion.Enabled && Visible) _timer.Start();
            else { _display = _value; Invalidate(); }
        }
    }
    public AnimatedProgressTrack()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = UiPalette.DarkCanvas;
        _timer.Tick += (_, _) =>
        {
            _display += (_value - _display) * .18f;
            if (Math.Abs(_value - _display) < .05f) { _display = _value; _timer.Stop(); }
            Invalidate();
        };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(2, Height - 1));
        using var shape = ModernButton.RoundedRectangle(bounds, Math.Min(5, Height / 2));
        using var background = new SolidBrush(UiPalette.SurfaceRaised);
        e.Graphics.FillPath(background, shape);
        if (_display <= 0) return;
        var fill = new Rectangle(0, 0, Math.Max(1, (int)(Width * _display / 100)), Height);
        using var brush = new LinearGradientBrush(bounds, UiPalette.AccentBlue, UiPalette.Warning, 0f);
        var previous = e.Graphics.Save();
        e.Graphics.SetClip(shape);
        e.Graphics.FillRectangle(brush, fill);
        e.Graphics.Restore(previous);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
