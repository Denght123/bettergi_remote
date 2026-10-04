namespace BetterGI.RemoteLite.Agent.UI;

internal sealed class DesktopNavigationPanel : Panel
{
    public DesktopNavigationPanel(Action connection, Action pairing, Action notifications, Action updates)
    {
        Dock = DockStyle.Fill;
        BackColor = UiPalette.Sidebar;
        Padding = new Padding(16, 25, 16, 22);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = UiPalette.Sidebar };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        var brand = new Panel { Dock = DockStyle.Fill, BackColor = UiPalette.Sidebar };
        brand.Controls.Add(new GlyphView { Glyph = UiGlyph.Devices, GlyphColor = UiPalette.AccentCyan, Size = new Size(24, 24), Location = new Point(2, 0) });
        brand.Controls.Add(new Label { Text = "电脑端", AutoSize = true, ForeColor = UiPalette.Text, Font = UiPalette.Font(11), Location = new Point(0, 38) });
        layout.Controls.Add(brand, 0, 0);
        var links = new[] { ("连接", UiGlyph.Game, connection), ("手机", UiGlyph.Qr, pairing), ("通知", UiGlyph.Bell, notifications), ("更新", UiGlyph.Refresh, updates) };
        for (var i = 0; i < links.Length; i++)
        {
            var entry = links[i];
            var button = new ModernButton { Text = entry.Item1, Glyph = entry.Item2, GlyphSize = 20, TextAlign = ContentAlignment.MiddleLeft, ContentPadding = 11, GlyphGap = 9, Dock = DockStyle.Fill, Variant = ModernButtonVariant.Ghost, CanvasColor = UiPalette.Sidebar, Margin = new Padding(0, 7, 0, 7), AccessibleName = "快捷入口：" + entry.Item1 };
            button.Click += (_, _) => entry.Item3();
            layout.Controls.Add(button, 0, i + 1);
        }
        layout.Controls.Add(new Label { Text = "手机控制\n电脑执行", Dock = DockStyle.Fill, ForeColor = UiPalette.TextMuted, Font = UiPalette.Font(9), TextAlign = ContentAlignment.BottomLeft }, 0, 6);
        Controls.Add(layout);
    }
}
