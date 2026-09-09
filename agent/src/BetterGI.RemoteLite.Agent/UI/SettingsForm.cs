using System.Diagnostics;
using System.Security.Cryptography;
using BetterGI.RemoteLite.Agent.Native;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.BetterGi;
using BetterGI.RemoteLite.Security;

namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class SettingsForm : Form
{
    private readonly AgentSettingsStore _store;
    private readonly bool _firstRun;
    private readonly TextBox _betterGiPath = new() { ReadOnly = true };
    private readonly Label _versionStatus = new() { AutoSize = true };
    private readonly ComboBox _sourceConfig = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _serverStatus = new() { AutoSize = true };
    private readonly TextBox _relayUrl = new();
    private readonly TextBox _cancelHotkey = new();
    private readonly TextBox _feishuWebhook = new();
    private readonly TextBox _feishuSecret = new() { UseSystemPasswordChar = true };
    private readonly Panel _advancedPanel = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false };
    private readonly LinkLabel _advancedToggle = new() { AutoSize = true, Text = "显示通知和高级设置" };
    private readonly Button _saveButton = new() { AutoSize = true };

    public SettingsForm(AgentSettingsStore store, bool firstRun = false)
    {
        _store = store;
        _firstRun = firstRun;
        Text = firstRun ? "开始使用 BetterGI Remote" : "BetterGI Remote 电脑端设置";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 590);
        Size = new Size(780, 680);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.FromArgb(246, 249, 247);

        var settings = store.Current;
        _betterGiPath.Text = settings.BetterGiExecutablePath;
        _relayUrl.Text = string.IsNullOrWhiteSpace(settings.RelayBaseUrl) ? ProductDefaults.RelayBaseUrl : settings.RelayBaseUrl;
        _cancelHotkey.Text = string.IsNullOrWhiteSpace(settings.CancelHotkey) ? "Ctrl+Shift+F12" : settings.CancelHotkey;
        _feishuWebhook.Text = SecretProtector.Unprotect(settings.ProtectedFeishuWebhook) ?? string.Empty;
        _feishuSecret.Text = SecretProtector.Unprotect(settings.ProtectedFeishuSigningSecret) ?? string.Empty;
        _saveButton.Text = firstRun ? "完成设置并连接手机" : "保存设置";

        Controls.Add(BuildContent());
        Controls.Add(BuildFooter());
        Controls.Add(BuildHeader());
        AcceptButton = _saveButton;

        _betterGiPath.TextChanged += (_, _) => RefreshBetterGiDetails();
        _advancedToggle.LinkClicked += (_, _) => ToggleAdvanced();
        _saveButton.Click += async (_, _) => await SaveAsync();
        Shown += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_betterGiPath.Text))
            {
                DetectBetterGi(silent: true);
            }
            RefreshBetterGiDetails();
        };
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.White, Padding = new Padding(28, 22, 28, 16) };
        panel.Controls.Add(new Label
        {
            Text = _firstRun ? "连接你的 BetterGI" : "电脑端设置",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            ForeColor = Color.FromArgb(23, 55, 43),
            Location = new Point(28, 20),
        });
        panel.Controls.Add(new Label
        {
            Text = _firstRun ? "通常只需要一分钟。选择 BetterGI 后，扫描二维码即可在手机上使用。" : "日常任务在手机操作；这里仅用于更换 BetterGI、通知或重新配置。",
            AutoSize = true,
            ForeColor = Color.FromArgb(82, 101, 92),
            Location = new Point(30, 64),
        });
        return panel;
    }

    private Control BuildContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(28, 22, 28, 20),
            ColumnCount = 1,
            RowCount = 8,
            BackColor = BackColor,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var pathActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var detect = new Button { Text = "自动查找", AutoSize = true };
        var browse = new Button { Text = "手动选择", AutoSize = true };
        detect.Click += (_, _) => DetectBetterGi(silent: false);
        browse.Click += (_, _) => BrowseBetterGi();
        pathActions.Controls.Add(detect);
        pathActions.Controls.Add(browse);

        content.Controls.Add(Field("BetterGI", _betterGiPath, "自动查找常见安装位置；找不到时只需手动选择一次 BetterGI.exe。", pathActions), 0, 0);
        content.Controls.Add(StatusRow(), 0, 1);
        content.Controls.Add(Field("远程任务配置", _sourceConfig, "首次创建时会复制为“远程每日”，不会覆盖原配置。"), 0, 2);

        content.Controls.Add(Field("连接服务", _serverStatus, "正式版已内置服务器地址，普通用户不需要填写。"), 0, 3);

        _advancedToggle.Margin = new Padding(2, 14, 0, 8);
        content.Controls.Add(_advancedToggle, 0, 4);
        BuildAdvancedPanel();
        content.Controls.Add(_advancedPanel, 0, 5);
        content.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(2, 18, 0, 10),
            ForeColor = Color.FromArgb(82, 101, 92),
            Text = "配置、绑定密钥和通知凭据只保存在当前电脑。手机只能调用固定的状态、配置、启动、停止和报告操作。",
        }, 0, 6);
        return content;
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Color.White, Padding = new Padding(28, 17, 28, 14) };
        _saveButton.BackColor = Color.FromArgb(15, 118, 88);
        _saveButton.ForeColor = Color.White;
        _saveButton.FlatStyle = FlatStyle.Flat;
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.Padding = new Padding(18, 7, 18, 7);
        footer.Controls.Add(_saveButton);
        footer.Resize += (_, _) => _saveButton.Location = new Point(footer.ClientSize.Width - _saveButton.Width - 28, 17);
        return footer;
    }

    private void BuildAdvancedPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 4, Padding = new Padding(0, 4, 0, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(Field("取消任务快捷键", _cancelHotkey, "默认 Ctrl+Shift+F12，小助手会同步到 BetterGI 全局设置。"), 0, 0);
        layout.Controls.Add(Field("自定义连接服务", _relayUrl, "仅用于开发测试或迁移服务器；日常使用无需修改。"), 0, 1);
        layout.Controls.Add(Field("飞书机器人 Webhook（可选）", _feishuWebhook, "任务结束后由电脑直接发送报告。"), 0, 2);
        layout.Controls.Add(Field("飞书签名密钥（可选）", _feishuSecret, null), 0, 3);
        _advancedPanel.Controls.Add(layout);
    }

    private Control StatusRow()
    {
        var panel = new Panel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(2, 0, 2, 12) };
        panel.Controls.Add(_versionStatus);
        return panel;
    }

    private static Control Field(string label, Control input, string? help, Control? action = null)
    {
        var wrapper = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = action is null ? 1 : 2, Margin = new Padding(0, 0, 0, 14) };
        wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (action is not null)
        {
            wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        wrapper.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Margin = new Padding(2, 0, 0, 7) }, 0, 0);
        if (action is not null)
        {
            wrapper.Controls.Add(action, 1, 0);
            wrapper.SetRowSpan(action, help is null ? 2 : 3);
            action.Margin = new Padding(12, 1, 0, 0);
        }
        input.Dock = DockStyle.Top;
        input.Margin = new Padding(0, 0, 0, 5);
        wrapper.Controls.Add(input, 0, 1);
        if (!string.IsNullOrWhiteSpace(help))
        {
            wrapper.Controls.Add(new Label { Text = help, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Color.FromArgb(82, 101, 92), Margin = new Padding(2, 0, 0, 0) }, 0, 2);
        }
        return wrapper;
    }

    private void ToggleAdvanced()
    {
        _advancedPanel.Visible = !_advancedPanel.Visible;
        _advancedToggle.Text = _advancedPanel.Visible ? "收起高级设置" : "显示通知和高级设置";
    }

    private void DetectBetterGi(bool silent)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var found = BetterGiDiscovery.FindSupportedExecutable(_betterGiPath.Text);
            if (found is not null)
            {
                _betterGiPath.Text = found;
                return;
            }
            if (!silent)
            {
                MessageBox.Show(this, "没有在常见位置找到受支持的 BetterGI 0.64.x，请点击“手动选择”。", ProductDefaults.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BrowseBetterGi()
    {
        using var dialog = new OpenFileDialog { Filter = "BetterGI|BetterGI.exe|可执行文件|*.exe", CheckFileExists = true, Title = "选择 BetterGI.exe" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _betterGiPath.Text = dialog.FileName;
        }
    }

    private void RefreshBetterGiDetails()
    {
        var check = BetterGiVersionPolicy.Check(_betterGiPath.Text.Trim());
        _versionStatus.Text = check.Supported ? $"已识别 BetterGI {check.Version}" : check.Message ?? "尚未选择受支持的 BetterGI";
        _versionStatus.ForeColor = check.Supported ? Color.FromArgb(15, 118, 88) : Color.Firebrick;
        var selected = _sourceConfig.SelectedItem?.ToString();
        _sourceConfig.Items.Clear();
        if (check.Configured)
        {
            try
            {
                var configStore = new RemoteConfigStore(_betterGiPath.Text.Trim());
                foreach (var name in configStore.ListSourceConfigurations())
                {
                    _sourceConfig.Items.Add(name);
                }
            }
            catch
            {
            }
        }
        if (selected is not null && _sourceConfig.Items.Contains(selected))
        {
            _sourceConfig.SelectedItem = selected;
        }
        else if (_sourceConfig.Items.Count > 0)
        {
            _sourceConfig.SelectedIndex = 0;
        }
        RefreshServerStatus();
    }

    private void RefreshServerStatus()
    {
        var official = string.Equals(_relayUrl.Text.Trim().TrimEnd('/'), ProductDefaults.RelayBaseUrl, StringComparison.OrdinalIgnoreCase);
        _serverStatus.Text = official ? "BetterGI Remote 正式服务" : "自定义服务（高级设置）";
        _serverStatus.ForeColor = official ? Color.FromArgb(15, 118, 88) : Color.DarkOrange;
    }

    private async Task SaveAsync()
    {
        _saveButton.Enabled = false;
        try
        {
            var executable = Path.GetFullPath(_betterGiPath.Text.Trim());
            var check = BetterGiVersionPolicy.Check(executable);
            if (!check.Supported)
            {
                throw new InvalidOperationException(check.Message ?? "请选择官方 BetterGI 0.64.x。");
            }
            var runningProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
            try
            {
                if (runningProcesses.Length > 0)
                {
                    throw new InvalidOperationException("请先关闭 BetterGI，再完成电脑端设置。");
                }
            }
            finally
            {
                foreach (var process in runningProcesses) process.Dispose();
            }
            if (!Uri.TryCreate(_relayUrl.Text.Trim(), UriKind.Absolute, out var relay) ||
                (relay.Scheme != Uri.UriSchemeHttps && !(relay.Scheme == Uri.UriSchemeHttp && relay.IsLoopback)))
            {
                throw new InvalidOperationException("连接服务必须使用 HTTPS；本机开发只允许 HTTP localhost。");
            }
            if (!HotkeySender.IsValid(_cancelHotkey.Text.Trim()))
            {
                throw new InvalidOperationException("取消快捷键必须包含 Ctrl、Shift 或 Alt，并搭配字母、数字或 F1-F24。");
            }
            if (!string.IsNullOrWhiteSpace(_feishuWebhook.Text) &&
                (!Uri.TryCreate(_feishuWebhook.Text.Trim(), UriKind.Absolute, out var webhook) || webhook.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("飞书 Webhook 必须是 HTTPS 地址。");
            }

            var next = _store.Current;
            next.BetterGiExecutablePath = executable;
            next.RelayBaseUrl = relay.GetLeftPart(UriPartial.Authority) + relay.AbsolutePath.TrimEnd('/');
            next.CancelHotkey = _cancelHotkey.Text.Trim();
            next.ProtectedFeishuWebhook = SecretProtector.Protect(_feishuWebhook.Text.Trim());
            next.ProtectedFeishuSigningSecret = SecretProtector.Protect(_feishuSecret.Text);
            if (string.IsNullOrEmpty(next.ProtectedPairingSecret))
            {
                var secret = PairingKeyMaterial.GenerateSecret();
                try
                {
                    next.ProtectedPairingSecret = SecretProtector.ProtectBytes(secret);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
            }

            var configStore = new RemoteConfigStore(executable, next.RemoteConfigName);
            if (!File.Exists(configStore.RemoteConfigPath))
            {
                var source = _sourceConfig.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new InvalidOperationException("请选择一份现有一条龙配置作为远程副本来源。");
                }
                await configStore.CreateRemoteCopyAsync(source);
            }
            await configStore.SetCancelHotkeyAsync(next.CancelHotkey);
            _store.Save(next);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法完成设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _saveButton.Enabled = true;
        }
    }
}
