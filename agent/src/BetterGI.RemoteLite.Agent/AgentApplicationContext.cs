using System.Diagnostics;
using System.Security.Cryptography;
using BetterGI.RemoteLite.Agent.Runtime;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.Agent.UI;
using BetterGI.RemoteLite.Protocol;
using BetterGI.RemoteLite.Security;

namespace BetterGI.RemoteLite.Agent;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly AgentSettingsStore _settingsStore = new();
    private readonly SynchronizationContext _uiContext;
    private readonly NotifyIcon _tray;
    private AgentRuntime? _runtime;
    private readonly UpdateService _updates = new();
    private bool _exiting;

    public AgentApplicationContext(bool forceSetup = false)
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var menu = new ContextMenuStrip();
        menu.Items.Add("电脑端设置", null, (_, _) => OpenSettings());
        menu.Items.Add("显示手机绑定二维码", null, (_, _) => ShowPairing());
        menu.Items.Add("重新绑定手机（新二维码）", null, (_, _) => RebindPhone());
        menu.Items.Add("解除当前手机绑定", null, (_, _) => UnbindPhone());
        menu.Items.Add("查看当前状态", null, (_, _) => ShowStatus());
        menu.Items.Add("打开手机控制网页", null, (_, _) => OpenControlEntry());
        menu.Items.Add("检查更新", null, async (_, _) => await CheckForUpdatesAsync(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitAgent());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BetterGI Remote",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        StartRuntime();
        ScheduleAutomaticUpdateCheck();
        ShowStartupReminder();

        if (forceSetup || !_settingsStore.Current.IsConfigured || string.IsNullOrEmpty(_settingsStore.Current.BoundPhoneDeviceId))
        {
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                if (forceSetup || !_settingsStore.Current.IsConfigured)
                {
                    OpenSettings(firstRun: true);
                }
                else
                {
                    ShowPairing();
                }
            };
            timer.Start();
        }
    }

    private void OpenSettings(bool firstRun = false)
    {
        using var form = new SettingsForm(_settingsStore, firstRun);
        if (form.ShowDialog() == DialogResult.OK)
        {
            RestartRuntime();
            if (string.IsNullOrEmpty(_settingsStore.Current.BoundPhoneDeviceId))
            {
                ShowPairing();
            }
        }
    }

    private void ShowPairing()
    {
        var settings = _settingsStore.Current;
        if (!string.IsNullOrEmpty(settings.BoundPhoneDeviceId) && BindingLeasePolicy.IsExpired(settings.BindingExpiresAt, DateTimeOffset.UtcNow))
        {
            RotatePairingSecret();
            RestartRuntime();
            settings = _settingsStore.Current;
        }
        if (!settings.IsConfigured || string.IsNullOrEmpty(settings.ProtectedPairingSecret))
        {
            MessageBox.Show("请先完成电脑端设置。", ProductDefaults.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _runtime?.AllowPairing(TimeSpan.FromMinutes(5));
        var secret = SecretProtector.UnprotectBytes(settings.ProtectedPairingSecret);
        if (secret is null)
        {
            MessageBox.Show("绑定密钥无法解密，请执行重新绑定。", ProductDefaults.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        try
        {
            using var form = new PairingForm(
                settings.RelayBaseUrl,
                secret,
                !string.IsNullOrEmpty(settings.BoundPhoneDeviceId),
                () => !string.IsNullOrEmpty(_settingsStore.Current.BoundPhoneDeviceId),
                settings.BindingExpiresAt);
            form.ShowDialog();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private void RebindPhone()
    {
        if (MessageBox.Show("重新绑定会立即使旧手机失效。确认继续吗？", "重新绑定", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        RotatePairingSecret();
        RestartRuntime();
        ShowPairing();
    }

    private void UnbindPhone()
    {
        if (MessageBox.Show("解除后当前手机会立即失效。确认解除绑定吗？", "解除手机绑定", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        RotatePairingSecret();
        RestartRuntime();
        MessageBox.Show("已解除手机绑定。需要再次使用时，请从托盘菜单打开新的绑定二维码。", ProductDefaults.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RotatePairingSecret()
    {
        var secret = BetterGI.RemoteLite.Security.PairingKeyMaterial.GenerateSecret();
        try
        {
            _settingsStore.Update(settings =>
            {
                settings.ProtectedPairingSecret = SecretProtector.ProtectBytes(secret);
                settings.BoundPhoneDeviceId = null;
                settings.BoundAt = null;
                settings.BindingExpiresAt = null;
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private void ShowStartupReminder()
    {
        var settings = _settingsStore.Current;
        var expired = BindingLeasePolicy.IsExpired(settings.BindingExpiresAt, DateTimeOffset.UtcNow);
        var expiry = settings.BindingExpiresAt;
        var expiring = !expired && expiry is not null && expiry.Value - DateTimeOffset.UtcNow <= BindingLeasePolicy.WarningThreshold;
        _tray.BalloonTipTitle = expired ? "手机绑定已过期" : expiring ? "手机绑定即将到期" : "BetterGI Remote 已就绪";
        _tray.BalloonTipText = expired
            ? $"请从托盘菜单打开新的绑定二维码并重新扫码。\n手机入口：{ProductDefaults.ControlEntryUrl}"
            : expiring
            ? $"绑定将在 {expiry!.Value.ToLocalTime():yyyy-MM-dd} 到期。连接成功会自动续期。\n手机入口：{ProductDefaults.ControlEntryUrl}"
            : $"手机入口：{ProductDefaults.ControlEntryUrl}";
        _tray.ShowBalloonTip(expired || expiring ? 8000 : 4000);
    }

    private void ShowStatus()
    {
        var status = _runtime?.GetStatus();
        var text = status is null
            ? "电脑端设置尚未完成。"
            : $"电脑: {status.PcName}\r\n手机连接: {(status.PhonePeerOnline ? "在线" : "离线")}\r\n绑定有效期: {(status.BindingExpiresAt is { } expiry ? expiry.ToLocalTime().ToString("yyyy-MM-dd") : "未绑定")}\r\nWindows: {(status.WindowsUnlocked ? "未锁屏" : "已锁屏")}\r\nBetterGI: {(status.BetterGiRunning ? "运行中" : "已关闭")}\r\n原神: {(status.GameRunning ? "运行中" : "已关闭")}\r\n任务状态: {status.State}\r\n手机入口: {ProductDefaults.ControlEntryUrl}";
        MessageBox.Show(text, "当前状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void OpenControlEntry()
    {
        Process.Start(new ProcessStartInfo(ProductDefaults.ControlEntryUrl) { UseShellExecute = true });
    }

    private void ScheduleAutomaticUpdateCheck()
    {
        var last = _settingsStore.Current.LastUpdateCheckAt;
        if (last is not null && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24)) return;
        var timer = new System.Windows.Forms.Timer { Interval = 12_000 };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            await CheckForUpdatesAsync(manual: false);
        };
        timer.Start();
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        try
        {
            var update = await _updates.CheckAsync();
            _settingsStore.Update(value => value.LastUpdateCheckAt = DateTimeOffset.UtcNow);
            if (update is null)
            {
                if (manual) MessageBox.Show($"当前已是最新版 {ProductDefaults.ProductVersion}。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.Equals(_runtime?.GetStatus().State, "running", StringComparison.OrdinalIgnoreCase))
            {
                if (manual) MessageBox.Show("当前任务仍在执行。请等待任务结束后再安装更新。", "暂不能更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var answer = MessageBox.Show(
                $"发现 BetterGI Remote {update.Version}。\r\n\r\n点击“是”将从官方 GitHub Release 下载、校验并安装。",
                "发现新版本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;
            using var progress = new UpdateProgressForm(update.Version.ToString());
            progress.Show();
            var reporter = new Progress<int>(progress.SetProgress);
            var installer = await _updates.DownloadAsync(update, reporter);
            progress.Close();
            UpdateService.LaunchInstaller(installer);
            ExitAgent();
        }
        catch (Exception exception)
        {
            if (manual) MessageBox.Show("检查或安装更新失败：\r\n" + exception.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Task<bool> ConfirmPairingAsync(PairRequest request)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            var shortId = request.DeviceId.Length > 8 ? request.DeviceId[..8] : request.DeviceId;
            var result = MessageBox.Show(
                $"手机名称: {request.DeviceLabel}\r\n设备代码: {shortId}\r\n\r\n确认将这台手机绑定到当前电脑吗？",
                "确认手机绑定",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            completion.TrySetResult(result == DialogResult.Yes);
        }, null);
        return completion.Task;
    }

    private void StartRuntime()
    {
        if (!_settingsStore.Current.IsConfigured)
        {
            return;
        }
        _runtime = new AgentRuntime(_settingsStore, ConfirmPairingAsync);
        _runtime.Start();
    }

    private void RestartRuntime()
    {
        var previous = _runtime;
        _runtime = null;
        if (previous is not null)
        {
            previous.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        StartRuntime();
    }

    private void ExitAgent()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        _tray.Visible = false;
        _runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _tray.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
        {
            ExitAgent();
        }
        base.Dispose(disposing);
    }
}
