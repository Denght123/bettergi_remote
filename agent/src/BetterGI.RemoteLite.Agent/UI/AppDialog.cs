namespace BetterGI.RemoteLite.Agent.UI;

/// <summary>Presentation-only MessageBox replacement; button results preserve the callers' existing decisions.</summary>
internal sealed class AppDialog : StudioForm
{
    public AppDialog(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        Text = caption;
        UiPalette.ApplyWindow(this);
        ResizeEnabled = false;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var tone = icon is MessageBoxIcon.Error ? UiPalette.DangerText : icon is MessageBoxIcon.Warning ? UiPalette.Warning : UiPalette.AccentCyan;
        var measured = TextRenderer.MeasureText(text, UiPalette.Font(10.5f), new Size(468, 480), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var bodyHeight = Math.Clamp(measured.Height + 20, 90, 390);
        SetContentSize(new Size(560, bodyHeight + 180));
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 3 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        header.Controls.Add(new GlyphView { Glyph = UiGlyph.Info, GlyphColor = tone, Size = new Size(32, 32), Margin = new Padding(0, 3, 14, 0) });
        header.Controls.Add(new Label { Text = caption, AutoSize = true, Font = UiPalette.Font(18, FontStyle.Bold), ForeColor = UiPalette.Text, Margin = Padding.Empty, MaximumSize = new Size(450, 0) });
        shell.Controls.Add(header, 0, 0);
        var details = new TextBox { Text = text, Multiline = true, ReadOnly = true, TabStop = false, BorderStyle = BorderStyle.None, BackColor = UiPalette.DarkCanvas, ForeColor = UiPalette.TextMuted, Font = UiPalette.Font(10.5f), Dock = DockStyle.Fill, ScrollBars = bodyHeight >= 390 ? ScrollBars.Vertical : ScrollBars.None, AccessibleName = "提示详情" };
        shell.Controls.Add(details, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty };
        var choices = buttons switch
        {
            MessageBoxButtons.YesNo => new[] { ("确认", DialogResult.Yes), ("取消", DialogResult.No) },
            MessageBoxButtons.YesNoCancel => new[] { ("确认", DialogResult.Yes), ("否", DialogResult.No), ("取消", DialogResult.Cancel) },
            MessageBoxButtons.OKCancel => new[] { ("确定", DialogResult.OK), ("取消", DialogResult.Cancel) },
            MessageBoxButtons.RetryCancel => new[] { ("重试", DialogResult.Retry), ("取消", DialogResult.Cancel) },
            MessageBoxButtons.AbortRetryIgnore => new[] { ("中止", DialogResult.Abort), ("重试", DialogResult.Retry), ("忽略", DialogResult.Ignore) },
            _ => new[] { ("知道了", DialogResult.OK) },
        };
        var controls = new List<ModernButton>();
        for (var i = 0; i < choices.Length; i++)
        {
            var button = new ModernButton { Text = choices[i].Item1, DialogResult = choices[i].Item2, Width = 116, Height = 46, CanvasColor = UiPalette.DarkCanvas, Variant = i == 0 ? ModernButtonVariant.Primary : ModernButtonVariant.Secondary, Margin = new Padding(10, 0, 0, 0) };
            controls.Add(button);
            actions.Controls.Add(button);
        }
        var selected = Math.Clamp((int)defaultButton / 256, 0, controls.Count - 1);
        AcceptButton = controls[selected];
        CancelButton = controls[^1];
        Shown += (_, _) => controls[selected].Focus();
        shell.Controls.Add(actions, 0, 2);
        Controls.Add(shell);
    }
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        => Show(Form.ActiveForm, text, caption, buttons, icon, defaultButton);
    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        using var dialog = new AppDialog(text, caption, buttons, icon, defaultButton);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}
