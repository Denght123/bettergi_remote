using System.Security.Cryptography;
using BetterGI.RemoteLite.Agent.Runtime;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.Agent.UI;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Agent;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly AgentSettingsStore _settingsStore = new();
    private readonly SynchronizationContext _uiContext;
    private readonly NotifyIcon _tray;
    private AgentRuntime? _runtime;
    private bool _exiting;

    public AgentApplicationContext(bool forceSetup = false)
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var menu = new ContextMenuStrip();
        menu.Items.Add("电脑端设置", null, (_, _) => OpenSettings());
        menu.Items.Add("连接手机", null, (_, _) => ShowPairing());
        menu.Items.Add("重新绑定手机", null, (_, _) => RebindPhone());
        menu.Items.Add("查看当前状态", null, (_, _) => ShowStatus());
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
                () => !string.IsNullOrEmpty(_settingsStore.Current.BoundPhoneDeviceId));
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
        var secret = BetterGI.RemoteLite.Security.PairingKeyMaterial.GenerateSecret();
        try
        {
            _settingsStore.Update(settings =>
            {
                settings.ProtectedPairingSecret = SecretProtector.ProtectBytes(secret);
                settings.BoundPhoneDeviceId = null;
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
        RestartRuntime();
        ShowPairing();
    }

    private void ShowStatus()
    {
        var status = _runtime?.GetStatus();
        var text = status is null
            ? "电脑端设置尚未完成。"
            : $"电脑: {status.PcName}\r\n手机连接: {(status.PhonePeerOnline ? "在线" : "离线")}\r\nWindows: {(status.WindowsUnlocked ? "未锁屏" : "已锁屏")}\r\nBetterGI: {(status.BetterGiRunning ? "运行中" : "已关闭")}\r\n原神: {(status.GameRunning ? "运行中" : "已关闭")}\r\n任务状态: {status.State}";
        MessageBox.Show(text, "当前状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
