using System.Text.Json;
using BetterGI.RemoteLite.BetterGi;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class BetterGiStartShortcutTests
{
    [Theory]
    [InlineData("远程每日", "GlobalRegister", "Ctrl+Shift+F11", true)]
    [InlineData("默认配置", "GlobalRegister", "Ctrl+Shift+F11", false)]
    [InlineData("远程每日", "KeyboardMonitor", "Ctrl+Shift+F11", false)]
    [InlineData("远程每日", "GlobalRegister", "", false)]
    [InlineData("远程每日", "GlobalRegister", "Alt+F4", false)]
    [InlineData("远程每日", "GlobalRegister", "Win+R", false)]
    public void OnlyVerifiedCurrentConfigurationAndRegisteredShortcutCanStartDirectly(string name, string type, string shortcut, bool expected)
    {
        var config = JsonSerializer.SerializeToElement(new { selectedOneDragonFlowConfigName = name, hotKeyConfig = new { onedragonHotkeyType = type, onedragonHotkey = shortcut } });
        Assert.Equal(expected, BetterGiStartShortcut.FromConfiguration(config, "远程每日") is not null);
    }
}
