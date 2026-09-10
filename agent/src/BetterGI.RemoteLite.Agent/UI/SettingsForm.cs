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
    private readonly Func<Task> _checkForBetterGiUpdates;
    private readonly Action _exitApplication;
    private readonly TextBox _betterGiPath = new() { ReadOnly = true };
    private readonly Label _versionStatus = new() { AutoSize = true };
    private readonly Label _betterGiReleaseStatus = new() { AutoSize = true };
    private readonly Label _maintenanceReleaseStatus = new() { AutoSize = true };
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
    private readonly SurfacePanel _advancedPanel = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false };
    private readonly ModernButton _advancedToggle = new() { Text = "展开通知和高级设置", Glyph = UiGlyph.Chevron, Variant = ModernButtonVariant.Ghost, Height = 44 };
    private Panel? _connectionScrollHost;

    public SettingsForm(
        AgentSettingsStore store,
        bool firstRun = false,
        Action? showPairing = null,
        Action? rebindPhone = null,
        Action? unbindPhone = null,
        Action? showStatus = null,
        Action? openControlEntry = null,
        Func<Task<bool>>? checkForUpdates = null,
        Func<Task>? checkForBetterGiUpdates = null,
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
        _checkForBetterGiUpdates = checkForBetterGiUpdates ?? (() => Task.CompletedTask);
        _exitApplication = exitApplication ?? (() => { });
        Text = firstRun ? "开始使用 BetterGI Remote" : "BetterGI Remote 电脑端设置";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
        MinimumSize = new Size(980, 700);
        Size = new Size(1120, 800);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = UiPalette.DarkCanvas;
        ShowIcon = false;

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
        _controlEntry.LinkColor = UiPalette.AccentCyan;
        _controlEntry.ActiveLinkColor = Color.White;
        _controlEntry.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(ProductDefaults.ControlEntryUrl) { UseShellExecute = true });
        Controls.Add(BuildShell());

        _betterGiPath.TextChanged += (_, _) => RefreshBetterGiDetails();
        _advancedToggle.Click += (_, _) => ToggleAdvanced();
        Shown += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_betterGiPath.Text))
            {
                DetectBetterGi(silent: true);
            }
            RefreshBetterGiDetails();
        };
    }

    private Control BuildShell()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiPalette.DarkCanvas,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        shell.Controls.Add(BuildHeader(), 0, 0);
        shell.Controls.Add(BuildContent(), 0, 1);
        shell.Controls.Add(BuildFooter(), 0, 2);
        return shell;
    }

    private Control BuildHeader()
    {
        var asset = Path.Combine(AppContext.BaseDirectory, "assets", "spring-adventure-party.jpg");
        var panel = new HeroPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Sidebar,
            Padding = new Padding(44, 20, 34, 16),
            BackgroundImage = ImageAssetLoader.Load(asset),
            ImageFocusX = 0.52f,
            ImageFocusY = 0.46f,
            ShadeFrom = Color.FromArgb(235, 8, 31, 48),
            ShadeTo = Color.FromArgb(72, 8, 31, 48),
        };
        panel.Controls.Add(new Label
        {
            Text = $"REMOTE {ProductDefaults.ProductVersion}",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = UiPalette.AccentCyan,
            BackColor = Color.Transparent,
            Location = new Point(47, 18),
        });
        panel.Controls.Add(new Label
        {
            Text = _firstRun ? "连接你的 BetterGI" : "电脑端设置",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 27, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Location = new Point(44, 40),
        });
        panel.Controls.Add(new Label
        {
            Text = _firstRun ? "通常只需要一分钟。选择 BetterGI 后，扫描二维码即可在手机上使用。" : "日常任务在手机操作；这里仅用于更换 BetterGI、通知或重新配置。",
            AutoSize = true,
            ForeColor = Color.FromArgb(215, 232, 240),
            BackColor = Color.Transparent,
            MaximumSize = new Size(600, 0),
            Location = new Point(46, 104),
        });
        return panel;
    }

    private Control BuildContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 14),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiPalette.DarkCanvas,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var pathActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        var detect = new ModernButton { Text = "自动查找", Width = 128, Height = 43, Glyph = UiGlyph.Search, Margin = new Padding(0, 0, 8, 0) };
        var browse = new ModernButton { Text = "手动选择", Width = 128, Height = 43, Glyph = UiGlyph.Folder, Margin = Padding.Empty };
        detect.Click += (_, _) => DetectBetterGi(silent: false);
        browse.Click += (_, _) => BrowseBetterGi();
        pathActions.Controls.Add(detect);
        pathActions.Controls.Add(browse);

        var integration = new SurfacePanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(22, 17, 22, 16),
            Margin = Padding.Empty,
        };
        integration.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        integration.Controls.Add(SectionHeading("BetterGI 连接", "自动识别版本与配置结构，未来版本会先进行兼容探测。", UiGlyph.Game), 0, 0);
        integration.Controls.Add(Field("游戏工具位置", _betterGiPath, "自动查找常见安装位置；找不到时只需手动选择一次 BetterGI.exe。", pathActions, actionInInputRow: true), 0, 1);
        integration.Controls.Add(StatusRow(), 0, 2);
        integration.Controls.Add(Field("远程任务配置", _sourceConfig, "首次创建时复制为“远程每日”，不会覆盖原配置。"), 0, 3);
        integration.Controls.Add(ConnectionDetails(), 0, 4);
        _advancedToggle.Dock = DockStyle.Top;
        _advancedToggle.Margin = new Padding(0, 6, 0, 8);
        integration.Controls.Add(_advancedToggle, 0, 5);
        BuildAdvancedPanel();
        integration.Controls.Add(_advancedPanel, 0, 6);

        _connectionScrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = false, BackColor = UiPalette.DarkCanvas, Margin = new Padding(0, 0, 12, 0) };
        _connectionScrollHost.Controls.Add(integration);
        content.Controls.Add(_connectionScrollHost, 0, 0);
        content.Controls.Add(BuildMaintenancePanel(), 1, 0);
        return content;
    }

    private static Control SectionHeading(string title, string description, UiGlyph glyph)
    {
        var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 14) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new GlyphView { Glyph = glyph, GlyphColor = UiPalette.AccentCyan, Margin = new Padding(4, 4, 8, 0) };
        var copy = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        copy.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            ForeColor = UiPalette.Text,
            Margin = Padding.Empty,
        }, 0, 0);
        copy.Controls.Add(new Label
        {
            AutoSize = true,
            Text = description,
            ForeColor = UiPalette.TextMuted,
            Margin = new Padding(0, 5, 0, 0),
            MaximumSize = new Size(620, 0),
        }, 0, 1);
        panel.Controls.Add(icon, 0, 0);
        panel.Controls.Add(copy, 1, 0);
        return panel;
    }

    private Control ConnectionDetails()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(2, 2, 2, 2),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(CompactLabel("连接服务"), 0, 0);
        panel.Controls.Add(_serverStatus, 1, 0);
        panel.Controls.Add(CompactLabel("手机控制入口"), 0, 1);
        panel.Controls.Add(_controlEntry, 1, 1);
        _serverStatus.Margin = new Padding(0, 3, 0, 4);
        _controlEntry.Margin = new Padding(0, 3, 0, 0);
        return panel;
    }

    private static Label CompactLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
        ForeColor = UiPalette.Text,
        Margin = new Padding(0, 3, 0, 4),
    };

    private Control BuildMaintenancePanel()
    {
        var panel = new SurfacePanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(12, 0, 0, 0),
            Padding = new Padding(20),
            FillColor = UiPalette.Surface,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(SectionHeading("手机与维护", $"BetterGI Remote {ProductDefaults.ProductVersion} · 关键操作固定在此处", UiGlyph.Status), 0, 0);
        _maintenanceReleaseStatus.ForeColor = UiPalette.TextMuted;
        _maintenanceReleaseStatus.Margin = new Padding(4, 0, 4, 12);
        _maintenanceReleaseStatus.MaximumSize = new Size(330, 0);
        panel.Controls.Add(_maintenanceReleaseStatus, 0, 1);

        var actions = new TableLayoutPanel
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Margin = Padding.Empty,
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 4; row++) actions.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        var pairing = MaintenanceButton("显示二维码", UiGlyph.Qr, _showPairing);
        var status = MaintenanceButton("当前状态", UiGlyph.Status, _showStatus);
        var openWeb = MaintenanceButton("打开手机网页", UiGlyph.Globe, _openControlEntry);
        var rebind = MaintenanceButton("重新绑定", UiGlyph.Link, _rebindPhone);
        var unbind = MaintenanceButton("解除绑定", UiGlyph.Unlink, _unbindPhone);
        var update = MaintenanceButton("Remote 更新", UiGlyph.Refresh, () => { }, ModernButtonVariant.Primary);
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
            update.Text = "Remote 更新";
            update.Enabled = true;
        };
        var betterGiUpdate = MaintenanceButton("本体更新", UiGlyph.Game, () => { });
        betterGiUpdate.Click += async (_, _) =>
        {
            betterGiUpdate.Enabled = false;
            betterGiUpdate.Text = "正在检查…";
            await _checkForBetterGiUpdates();
            betterGiUpdate.Text = "本体更新";
            betterGiUpdate.Enabled = true;
            RefreshBetterGiDetails();
        };
        var uninstall = MaintenanceButton("卸载 Remote", UiGlyph.Trash, StartUninstall, ModernButtonVariant.Danger);

        actions.Controls.Add(pairing, 0, 0);
        actions.Controls.Add(status, 1, 0);
        actions.Controls.Add(openWeb, 0, 1);
        actions.Controls.Add(rebind, 1, 1);
        actions.Controls.Add(unbind, 0, 2);
        actions.Controls.Add(betterGiUpdate, 1, 2);
        actions.Controls.Add(update, 0, 3);
        actions.Controls.Add(uninstall, 1, 3);
        panel.Controls.Add(actions, 0, 2);
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(330, 0),
            Margin = new Padding(4, 18, 4, 2),
            ForeColor = UiPalette.TextMuted,
            Text = "配置、绑定密钥和通知凭据只保存在当前电脑；奖励报告由 BetterGI 本地日志生成。",
        }, 0, 3);
        return panel;
    }

    private static ModernButton MaintenanceButton(string text, UiGlyph glyph, Action action, ModernButtonVariant variant = ModernButtonVariant.Secondary)
    {
        var button = new ModernButton { Text = text, Glyph = glyph, Variant = variant, Dock = DockStyle.Fill, Margin = new Padding(5), Height = 50, TextAlign = ContentAlignment.MiddleLeft };
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
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = UiPalette.Sidebar, Padding = new Padding(28, 10, 28, 10) };
        var save = new ModernButton
        {
            Text = _firstRun ? "完成设置并连接手机" : "保存设置",
            Glyph = UiGlyph.Save,
            Variant = ModernButtonVariant.Primary,
            Width = 220,
            Dock = DockStyle.Right,
        };
        save.Click += async (_, _) => await SaveAsync(save);
        footer.Controls.Add(save);
        footer.Controls.Add(new Label
        {
            Text = "安全写入远程专用配置，并自动开启 BetterGI 奖励识别。",
            AutoSize = true,
            ForeColor = UiPalette.TextMuted,
            Location = new Point(30, 25),
        });
        AcceptButton = save;
        return footer;
    }

    private void BuildAdvancedPanel()
    {
        _advancedPanel.FillColor = UiPalette.DarkCanvas;
        _advancedPanel.OutlineColor = UiPalette.Line;
        _advancedPanel.Padding = new Padding(16);
        _advancedPanel.ColumnCount = 1;
        _advancedPanel.RowCount = 1;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 7, Padding = Padding.Empty };
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
        var icon = new GlyphView
        {
            Glyph = UiGlyph.Info,
            GlyphColor = UiPalette.AccentCyan,
            Margin = new Padding(5),
        };
        _toolTip.SetToolTip(icon, text);
        return icon;
    }

    private Control StatusRow()
    {
        var panel = new Panel { Height = 44, Dock = DockStyle.Top, Margin = new Padding(2, 0, 2, 14) };
        _versionStatus.Location = new Point(0, 4);
        _betterGiReleaseStatus.Location = new Point(0, 24);
        panel.Controls.Add(_betterGiReleaseStatus);
        panel.Controls.Add(_versionStatus);
        return panel;
    }

    private static Control Field(string label, Control input, string? help, Control? action = null, bool actionInInputRow = false)
    {
        var wrapper = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = action is null ? 1 : 2, Margin = new Padding(0, 0, 0, 10) };
        wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (action is not null)
        {
            wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        wrapper.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), ForeColor = UiPalette.Text, Margin = new Padding(2, 0, 0, 8) }, 0, 0);
        if (action is not null)
        {
            wrapper.Controls.Add(action, 1, actionInInputRow ? 1 : 0);
            if (!actionInInputRow) wrapper.SetRowSpan(action, help is null ? 2 : 3);
            action.Margin = actionInInputRow ? new Padding(12, 0, 0, 0) : new Padding(12, 1, 0, 0);
        }
        var hostedInput = input is TextBox or ComboBox ? new ModernInputHost(input) : input;
        hostedInput.Dock = DockStyle.Top;
        hostedInput.Margin = new Padding(0, 0, 0, 6);
        input.BackColor = input is TextBox or ComboBox ? UiPalette.Input : Color.Transparent;
        input.ForeColor = input is TextBox or ComboBox ? UiPalette.Text : UiPalette.AccentCyan;
        wrapper.Controls.Add(hostedInput, 0, 1);
        if (!string.IsNullOrWhiteSpace(help))
        {
            wrapper.Controls.Add(new Label { Text = help, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = UiPalette.TextMuted, Margin = new Padding(2, 0, 0, 0) }, 0, 2);
        }
        return wrapper;
    }

    private void ToggleAdvanced()
    {
        _advancedPanel.Visible = !_advancedPanel.Visible;
        if (_connectionScrollHost is not null)
        {
            _connectionScrollHost.AutoScroll = _advancedPanel.Visible;
            if (!_advancedPanel.Visible) _connectionScrollHost.AutoScrollPosition = Point.Empty;
        }
        _advancedToggle.Text = _advancedPanel.Visible ? "收起通知和高级设置" : "展开通知和高级设置";
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
                MessageBox.Show(this, "没有在常见位置找到可兼容的 BetterGI，请点击“手动选择”。", ProductDefaults.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        _versionStatus.Text = check.Supported
            ? check.Verified ? $"BetterGI {check.Version} · 已验证兼容" : $"BetterGI {check.Version} · 结构兼容，待实机验证"
            : check.Message ?? "尚未选择可兼容的 BetterGI";
        _versionStatus.ForeColor = check.Supported ? check.Verified ? UiPalette.Success : UiPalette.Warning : UiPalette.DangerText;
        var latest = _store.Current.LatestKnownBetterGiVersion;
        var latestText = string.IsNullOrWhiteSpace(latest) ? "尚未检查 BetterGI 官方版本" : $"官方最新版 {latest}";
        _betterGiReleaseStatus.Text = latestText;
        _betterGiReleaseStatus.ForeColor = UiPalette.TextMuted;
        _maintenanceReleaseStatus.Text = $"当前 BetterGI：{check.Version ?? "未识别"}\r\n{latestText}";
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

    private async Task SaveAsync(Button saveButton)
    {
        saveButton.Enabled = false;
        var originalText = saveButton.Text;
        saveButton.Text = "正在保存…";
        try
        {
            var executable = Path.GetFullPath(_betterGiPath.Text.Trim());
            var check = BetterGiVersionPolicy.Check(executable);
            if (!check.Supported)
            {
                throw new InvalidOperationException(check.Message ?? "请选择可兼容的官方 BetterGI。");
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
            await configStore.EnsureRewardRecognitionEnabledAsync();
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
            saveButton.Text = originalText;
            saveButton.Enabled = true;
        }
    }
}
