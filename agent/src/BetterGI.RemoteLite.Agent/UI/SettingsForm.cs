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
    private readonly Action _showPairing;
    private readonly Action _rebindPhone;
    private readonly Action _unbindPhone;
    private readonly Action _showStatus;
    private readonly Action _openControlEntry;
    private readonly Func<Task<bool>> _checkForUpdates;
    private readonly Action _exitApplication;
    private readonly TextBox _betterGiPath = new() { ReadOnly = true };
    private readonly Label _versionStatus = new() { AutoSize = true };
    private readonly ComboBox _sourceConfig = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _serverStatus = new() { AutoSize = true };
    private readonly TextBox _relayUrl = new();
    private readonly TextBox _cancelHotkey = new();
    private readonly TextBox _feishuWebhook = new();
    private readonly TextBox _feishuSecret = new() { UseSystemPasswordChar = true };
    private readonly TextBox _qqEmail = new();
    private readonly TextBox _qqSmtpCode = new() { UseSystemPasswordChar = true };
    private readonly TextBox _notificationRecipient = new();
    private readonly LinkLabel _controlEntry = new() { AutoSize = true };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20_000, InitialDelay = 250, ReshowDelay = 100 };
    private readonly Panel _advancedPanel = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false };
    private readonly LinkLabel _advancedToggle = new() { AutoSize = true, Text = "显示通知和高级设置" };
    private readonly Button _saveButton = new() { AutoSize = true };

    public SettingsForm(
        AgentSettingsStore store,
        bool firstRun = false,
        Action? showPairing = null,
        Action? rebindPhone = null,
        Action? unbindPhone = null,
        Action? showStatus = null,
        Action? openControlEntry = null,
        Func<Task<bool>>? checkForUpdates = null,
        Action? exitApplication = null)
    {
        _store = store;
        _firstRun = firstRun;
        _showPairing = showPairing ?? (() => { });
        _rebindPhone = rebindPhone ?? (() => { });
        _unbindPhone = unbindPhone ?? (() => { });
        _showStatus = showStatus ?? (() => { });
        _openControlEntry = openControlEntry ?? (() => { });
        _checkForUpdates = checkForUpdates ?? (() => Task.FromResult(false));
        _exitApplication = exitApplication ?? (() => { });
        Text = firstRun ? "开始使用 BetterGI Remote" : "BetterGI Remote 电脑端设置";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 640);
        Size = new Size(860, 780);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = UiPalette.Paper;

        var settings = store.Current;
        _betterGiPath.Text = settings.BetterGiExecutablePath;
        _relayUrl.Text = string.IsNullOrWhiteSpace(settings.RelayBaseUrl) ? ProductDefaults.RelayBaseUrl : settings.RelayBaseUrl;
        _cancelHotkey.Text = string.IsNullOrWhiteSpace(settings.CancelHotkey) ? "Ctrl+Shift+F12" : settings.CancelHotkey;
        _feishuWebhook.Text = SecretProtector.Unprotect(settings.ProtectedFeishuWebhook) ?? string.Empty;
        _feishuSecret.Text = SecretProtector.Unprotect(settings.ProtectedFeishuSigningSecret) ?? string.Empty;
        _qqEmail.Text = SecretProtector.Unprotect(settings.ProtectedQqEmailAddress) ?? string.Empty;
        _qqSmtpCode.Text = SecretProtector.Unprotect(settings.ProtectedQqSmtpAuthorizationCode) ?? string.Empty;
        _notificationRecipient.Text = SecretProtector.Unprotect(settings.ProtectedNotificationRecipient) ?? string.Empty;
        _controlEntry.Text = ProductDefaults.ControlEntryUrl;
        _controlEntry.LinkColor = UiPalette.Violet;
        _controlEntry.ActiveLinkColor = UiPalette.Pine;
        _advancedToggle.LinkColor = UiPalette.Pine;
        _advancedToggle.ActiveLinkColor = UiPalette.Violet;
        _controlEntry.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(ProductDefaults.ControlEntryUrl) { UseShellExecute = true });
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
        var asset = Path.Combine(AppContext.BaseDirectory, "assets", "spring-adventure-party.jpg");
        var panel = new HeroPanel
        {
            Dock = DockStyle.Top,
            Height = 224,
            BackColor = UiPalette.PineDeep,
            Padding = new Padding(38, 42, 28, 20),
            BackgroundImage = ImageAssetLoader.Load(asset),
            ImageFocusX = 0.52f,
            ImageFocusY = 0.46f,
        };
        panel.Controls.Add(new Label
        {
            Text = _firstRun ? "连接你的 BetterGI" : "电脑端设置",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 25, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Location = new Point(38, 58),
        });
        panel.Controls.Add(new Label
        {
            Text = _firstRun ? "通常只需要一分钟。选择 BetterGI 后，扫描二维码即可在手机上使用。" : "日常任务在手机操作；这里仅用于更换 BetterGI、通知或重新配置。",
            AutoSize = true,
            ForeColor = Color.FromArgb(239, 247, 234),
            BackColor = Color.Transparent,
            MaximumSize = new Size(600, 0),
            Location = new Point(40, 116),
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
            RowCount = 10,
            BackColor = BackColor,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var pathActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var detect = new Button { Text = "自动查找", AutoSize = true };
        var browse = new Button { Text = "手动选择", AutoSize = true };
        UiPalette.StyleSecondary(detect);
        UiPalette.StyleSecondary(browse);
        detect.Click += (_, _) => DetectBetterGi(silent: false);
        browse.Click += (_, _) => BrowseBetterGi();
        pathActions.Controls.Add(detect);
        pathActions.Controls.Add(browse);

        content.Controls.Add(BuildMaintenancePanel(), 0, 0);
        content.Controls.Add(Field("BetterGI", _betterGiPath, "自动查找常见安装位置；找不到时只需手动选择一次 BetterGI.exe。", pathActions), 0, 1);
        content.Controls.Add(StatusRow(), 0, 2);
        content.Controls.Add(Field("远程任务配置", _sourceConfig, "首次创建时会复制为“远程每日”，不会覆盖原配置。"), 0, 3);

        content.Controls.Add(Field("连接服务", _serverStatus, "正式版已内置服务器地址，普通用户不需要填写。"), 0, 4);
        content.Controls.Add(Field("手机控制入口", _controlEntry, "请收藏此固定网址。扫码只用于建立绑定，以后都从这里进入。"), 0, 5);

        _advancedToggle.Margin = new Padding(2, 14, 0, 8);
        content.Controls.Add(_advancedToggle, 0, 6);
        BuildAdvancedPanel();
        content.Controls.Add(_advancedPanel, 0, 7);
        content.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(2, 20, 0, 10),
            ForeColor = UiPalette.Muted,
            Text = "配置、绑定密钥和通知凭据只保存在当前电脑。手机只能调用固定的状态、配置、启动、停止和报告操作。",
        }, 0, 8);
        return content;
    }

    private Control BuildMaintenancePanel()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 14, 0, 0),
            Padding = new Padding(18, 16, 18, 16),
            BackColor = UiPalette.Cream,
            BorderStyle = BorderStyle.FixedSingle,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = $"手机与维护 · BetterGI Remote {ProductDefaults.ProductVersion}",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
            ForeColor = UiPalette.PineDeep,
            Margin = new Padding(2, 0, 0, 5),
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = "绑定、更新和卸载都可以直接在这里完成，不必再到托盘菜单查找。",
            AutoSize = true,
            ForeColor = UiPalette.Muted,
            Margin = new Padding(2, 0, 0, 12),
        }, 0, 1);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = Padding.Empty,
        };
        var pairing = MaintenanceButton("显示手机二维码", _showPairing);
        var status = MaintenanceButton("查看当前状态", _showStatus);
        var openWeb = MaintenanceButton("打开手机控制网页", _openControlEntry);
        var rebind = MaintenanceButton("重新绑定手机", _rebindPhone);
        var unbind = MaintenanceButton("解除当前绑定", _unbindPhone);
        var update = new Button { Text = "检查更新", AutoSize = true };
        UiPalette.StylePrimary(update);
        update.Click += async (_, _) =>
        {
            update.Enabled = false;
            update.Text = "正在检查…";
            var installerLaunched = await _checkForUpdates();
            if (installerLaunched)
            {
                Close();
                _exitApplication();
                return;
            }
            update.Text = "检查更新";
            update.Enabled = true;
        };
        var uninstall = new Button { Text = "卸载 BetterGI Remote", AutoSize = true };
        UiPalette.StyleSecondary(uninstall);
        uninstall.ForeColor = UiPalette.Coral;
        uninstall.FlatAppearance.BorderColor = Color.FromArgb(200, 150, 145);
        uninstall.Click += (_, _) => StartUninstall();

        actions.Controls.Add(pairing);
        actions.Controls.Add(status);
        actions.Controls.Add(openWeb);
        actions.Controls.Add(rebind);
        actions.Controls.Add(unbind);
        actions.Controls.Add(update);
        actions.Controls.Add(uninstall);
        panel.Controls.Add(actions, 0, 2);
        return panel;
    }

    private static Button MaintenanceButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        UiPalette.StyleSecondary(button);
        button.Click += (_, _) => action();
        return button;
    }

    private void StartUninstall()
    {
        var uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!File.Exists(uninstaller))
        {
            MessageBox.Show(this, "当前是开发或便携运行目录，没有找到安装器生成的卸载程序。请从正式安装目录运行后再卸载。", "无法启动卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, "将关闭 BetterGI Remote 并打开卸载向导。电脑上的设置与绑定数据会保留，确认继续吗？", "卸载 BetterGI Remote", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true });
            Close();
            _exitApplication();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "无法启动卸载向导：\r\n" + exception.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = UiPalette.PineDeep, Padding = new Padding(28, 17, 28, 14) };
        UiPalette.StylePrimary(_saveButton);
        _saveButton.BackColor = Color.FromArgb(122, 163, 90);
        footer.Controls.Add(_saveButton);
        footer.Resize += (_, _) => _saveButton.Location = new Point(footer.ClientSize.Width - _saveButton.Width - 28, 17);
        return footer;
    }

    private void BuildAdvancedPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 7, Padding = new Padding(0, 4, 0, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(Field("取消任务快捷键", _cancelHotkey, "默认 Ctrl+Shift+F12，小助手会同步到 BetterGI 全局设置。"), 0, 0);
        layout.Controls.Add(Field("自定义连接服务", _relayUrl, "仅用于开发测试或迁移服务器；日常使用无需修改。"), 0, 1);
        layout.Controls.Add(Field("飞书机器人 Webhook（可选）", _feishuWebhook, "任务结束后由电脑直接发送报告。", HelpIcon("进入飞书群设置 → 群机器人 → 添加自定义机器人，复制 HTTPS Webhook。不要把 Webhook 发给他人。")), 0, 2);
        layout.Controls.Add(Field("飞书签名密钥（可选）", _feishuSecret, null, HelpIcon("在飞书自定义机器人的安全设置中开启“签名校验”，复制显示的签名密钥。未开启签名时可留空。")), 0, 3);
        layout.Controls.Add(Field("QQ 发件邮箱（可选）", _qqEmail, "例如 123456@qq.com。", HelpIcon("登录 mail.qq.com → 设置 → 账号与安全 → 安全设置 → POP3/IMAP/SMTP/Exchange/CardDAV 服务，开启 SMTP 服务。")), 0, 4);
        layout.Controls.Add(Field("QQ 邮箱 SMTP 授权码", _qqSmtpCode, "这是单独生成的授权码，不是 QQ 密码。", HelpIcon("在 QQ 邮箱 SMTP 服务设置中点击“生成授权码”，按提示验证后复制授权码。程序使用 smtp.qq.com:587 加密发送。")), 0, 5);
        layout.Controls.Add(Field("通知收件邮箱（可选）", _notificationRecipient, "留空则发送给上面的 QQ 邮箱，也可以填写其他邮箱。"), 0, 6);
        _advancedPanel.Controls.Add(layout);
    }

    private Control HelpIcon(string text)
    {
        var icon = new Label
        {
            Text = "ⓘ",
            AutoSize = true,
            Cursor = Cursors.Help,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
            ForeColor = UiPalette.Violet,
            Padding = new Padding(5),
        };
        _toolTip.SetToolTip(icon, text);
        return icon;
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
        wrapper.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), ForeColor = UiPalette.Ink, Margin = new Padding(2, 0, 0, 7) }, 0, 0);
        if (action is not null)
        {
            wrapper.Controls.Add(action, 1, 0);
            wrapper.SetRowSpan(action, help is null ? 2 : 3);
            action.Margin = new Padding(12, 1, 0, 0);
        }
        input.Dock = DockStyle.Top;
        input.Margin = new Padding(0, 0, 0, 5);
        input.BackColor = UiPalette.Cream;
        input.ForeColor = UiPalette.Ink;
        wrapper.Controls.Add(input, 0, 1);
        if (!string.IsNullOrWhiteSpace(help))
        {
            wrapper.Controls.Add(new Label { Text = help, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = UiPalette.Muted, Margin = new Padding(2, 0, 0, 0) }, 0, 2);
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
        _versionStatus.ForeColor = check.Supported ? UiPalette.Pine : UiPalette.Coral;
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
            var configuredSource = _store.Current.SourceConfigName;
            _sourceConfig.SelectedItem = _sourceConfig.Items.Contains(configuredSource) ? configuredSource : _sourceConfig.Items[0];
        }
        RefreshServerStatus();
    }

    private void RefreshServerStatus()
    {
        var official = string.Equals(_relayUrl.Text.Trim().TrimEnd('/'), ProductDefaults.RelayBaseUrl, StringComparison.OrdinalIgnoreCase);
        _serverStatus.Text = official ? "BetterGI Remote 正式服务" : "自定义服务（高级设置）";
        _serverStatus.ForeColor = official ? UiPalette.Pine : Color.FromArgb(182, 111, 36);
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
            if (!string.IsNullOrWhiteSpace(_qqEmail.Text) || !string.IsNullOrWhiteSpace(_qqSmtpCode.Text))
            {
                BetterGI.RemoteLite.Notifications.QqEmailNotifier.Validate(new(
                    _qqEmail.Text.Trim(),
                    _qqSmtpCode.Text.Trim(),
                    string.IsNullOrWhiteSpace(_notificationRecipient.Text) ? _qqEmail.Text.Trim() : _notificationRecipient.Text.Trim()));
            }

            var next = _store.Current;
            next.BetterGiExecutablePath = executable;
            next.RelayBaseUrl = relay.GetLeftPart(UriPartial.Authority) + relay.AbsolutePath.TrimEnd('/');
            next.CancelHotkey = _cancelHotkey.Text.Trim();
            next.SourceConfigName = _sourceConfig.SelectedItem?.ToString() ?? next.SourceConfigName;
            next.ProtectedFeishuWebhook = SecretProtector.Protect(_feishuWebhook.Text.Trim());
            next.ProtectedFeishuSigningSecret = SecretProtector.Protect(_feishuSecret.Text);
            next.ProtectedQqEmailAddress = SecretProtector.Protect(_qqEmail.Text.Trim());
            next.ProtectedQqSmtpAuthorizationCode = SecretProtector.Protect(_qqSmtpCode.Text.Trim());
            next.ProtectedNotificationRecipient = SecretProtector.Protect(_notificationRecipient.Text.Trim());
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
