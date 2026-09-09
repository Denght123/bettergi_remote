using BetterGI.RemoteLite.Security;
using QRCoder;

namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class PairingForm : Form
{
    private readonly Bitmap _bitmap;
    private readonly System.Windows.Forms.Timer? _boundTimer;

    public PairingForm(string relayBaseUrl, ReadOnlySpan<byte> secret, bool alreadyBound, Func<bool>? isBound = null, DateTimeOffset? bindingExpiresAt = null)
    {
        Text = alreadyBound ? "手机连接二维码" : "连接手机";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 680);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(13, 29, 49);

        var url = relayBaseUrl.TrimEnd('/') + "/#pair=" + Base64Url.Encode(secret);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8, drawQuietZones: true);
        using var stream = new MemoryStream(png);
        _bitmap = new Bitmap(stream);

        var title = new Label
        {
            Text = alreadyBound ? "在手机上恢复控制端" : "最后一步：用手机扫码",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 64,
        };
        var picture = new PictureBox
        {
            Image = _bitmap,
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Top,
            Height = 350,
            Padding = new Padding(16),
        };
        var notice = new Label
        {
            Text = alreadyBound
                ? "使用之前绑定的手机浏览器扫描。二维码包含绑定密钥，请不要截图或转发。"
                : "推荐用系统相机扫码，便于添加到主屏幕。若用微信扫码，绑定后请立即收藏页面或发送给文件传输助手。",
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(24, 8, 24, 8),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var hint = new Label
        {
            Text = "二维码有效 5 分钟",
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = Color.FromArgb(214, 181, 106),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        if (alreadyBound && bindingExpiresAt is { } expiry)
        {
            hint.Text = $"当前绑定有效至 {expiry.ToLocalTime():yyyy-MM-dd}，正常连接会自动续期";
        }
        var link = new TextBox
        {
            Text = relayBaseUrl.TrimEnd('/'),
            ReadOnly = true,
            Dock = DockStyle.Top,
            Margin = new Padding(20),
            TextAlign = HorizontalAlignment.Center,
        };
        var copy = new Button
        {
            Text = "复制日常控制网址",
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = Color.FromArgb(214, 181, 106),
            ForeColor = Color.FromArgb(35, 27, 13),
            FlatStyle = FlatStyle.Flat,
        };
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(relayBaseUrl.TrimEnd('/'));
            copy.Text = "已复制，可以发到文件传输助手";
        };
        var done = new Button
        {
            Text = "完成",
            Dock = DockStyle.Bottom,
            Height = 44,
            Visible = alreadyBound,
        };
        done.Click += (_, _) => Close();
        Controls.Add(done);
        Controls.Add(copy);
        Controls.Add(link);
        Controls.Add(hint);
        Controls.Add(notice);
        Controls.Add(picture);
        Controls.Add(title);

        title.ForeColor = Color.FromArgb(248, 239, 215);
        title.BackColor = Color.Transparent;
        notice.ForeColor = Color.FromArgb(205, 216, 225);
        notice.BackColor = Color.Transparent;
        picture.BackColor = Color.FromArgb(245, 239, 225);
        done.BackColor = Color.FromArgb(214, 181, 106);
        done.ForeColor = Color.FromArgb(35, 27, 13);
        done.FlatStyle = FlatStyle.Flat;

        if (!alreadyBound && isBound is not null)
        {
            _boundTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _boundTimer.Tick += (_, _) =>
            {
                if (!isBound()) return;
                _boundTimer.Stop();
                title.Text = "手机连接成功";
                notice.Text = "设置已经完成。以后登录 Windows 后会自动连接，日常操作只需使用手机。";
                hint.Text = "可以关闭此窗口";
                done.Visible = true;
                Activate();
            };
            _boundTimer.Start();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boundTimer?.Stop();
            _boundTimer?.Dispose();
            _bitmap.Dispose();
        }
        base.Dispose(disposing);
    }
}
