namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Bottom, Height = 12, Style = ProgressBarStyle.Continuous };
    private readonly Label _label = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };

    public UpdateProgressForm(string version)
    {
        Text = "BetterGI Remote 更新";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ClientSize = new Size(430, 128);
        BackColor = Color.FromArgb(15, 31, 51);
        _label.ForeColor = Color.FromArgb(244, 228, 187);
        _label.Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold);
        _label.Text = $"正在安全下载 BetterGI Remote {version}…";
        Controls.Add(_label);
        Controls.Add(_progress);
    }

    public void SetProgress(int value)
    {
        _progress.Value = Math.Clamp(value, 0, 100);
        _label.Text = $"正在下载并校验更新… {_progress.Value}%";
    }
}
