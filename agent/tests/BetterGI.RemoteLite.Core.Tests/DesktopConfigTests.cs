using System.Text.Json;
using BetterGI.RemoteLite.BetterGi;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class DesktopConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgrl-desktop-" + Guid.NewGuid().ToString("N"));
    private async Task<RemoteConfigStore> Prepare()
    {
        Directory.CreateDirectory(Path.Combine(_root, "User", "OneDragon"));
        Directory.CreateDirectory(Path.Combine(_root, "User", "ScriptGroup"));
        Directory.CreateDirectory(Path.Combine(_root, "User", "JsScript", "farming"));
        File.WriteAllText(Path.Combine(_root, "BetterGI.exe"), "");
        File.WriteAllText(Path.Combine(_root, "User", "config.json"), """
        {"autoPickConfig":{"enabled":false,"mode":"Blacklist","unknownOption":42},"autoWoodConfig":{"woodCountOcrEnabled":true},"commonConfig":{"mainBackgroundOpacity":0.7},"notificationConfig":{"secret":"not-for-phone"}}
        """);
        File.WriteAllText(Path.Combine(_root, "User", "OneDragon", "默认配置.json"), """
        {"Name":"默认配置","TaskDefinitions":{"a":"领取邮件","b":"采集组"},"TaskOrder":["a","b"],"TaskEnabledList":{"a":true,"b":true},"CompletionAction":"无"}
        """);
        File.WriteAllText(Path.Combine(_root, "User", "ScriptGroup", "采集组.json"), """
        {"name":"采集组","unknown":7,"projects":[{"name":"采集脚本","type":"Javascript","folderName":"farming","status":"Enabled","schedule":"Daily","runNum":1,"index":0,"jsScriptSettingsObject":{"region":"蒙德","loops":"2","secretToken":"keep"}},{"name":"地图路线","type":"Pathing","status":"Enabled","schedule":"Monday","runNum":1,"index":1}]}
        """);
        File.WriteAllText(Path.Combine(_root, "User", "JsScript", "farming", "manifest.json"), """{"settings_ui":"settings.json"}""");
        File.WriteAllText(Path.Combine(_root, "User", "JsScript", "farming", "settings.json"), """
        [{"name":"region","label":"采集地区","type":"select","options":["蒙德","璃月"],"default":"蒙德"},{"name":"loops","label":"采集轮数","type":"input-text","default":"1"},{"name":"secretToken","label":"服务令牌","type":"input-text"}]
        """);
        var store = new RemoteConfigStore(Path.Combine(_root, "BetterGI.exe"));
        await store.CreateRemoteCopyAsync("默认配置");
        return store;
    }

    [Fact]
    public async Task GlobalAndScriptSettingsRoundTripWithoutChangingUnknownOrSensitiveFields()
    {
        var store = await Prepare();
        var loaded = await store.LoadAsync();
        Assert.Contains(loaded.Fields, field => field.Path == "global.autoPickConfig.enabled");
        Assert.DoesNotContain(loaded.Fields, field => field.Path.Contains("notification") || field.Path.Contains("secretToken"));
        Assert.DoesNotContain(loaded.Fields, field => field.Path == "global.autoFishingConfig.enabled");
        var region = loaded.Fields.Single(field => field.Label == "采集地区");
        var count = loaded.Fields.First(field => field.Scope == "scriptGroup" && field.Label == "执行次数");
        var values = new Dictionary<string,JsonElement>
        {
            ["global.autoPickConfig.enabled"] = JsonSerializer.SerializeToElement(true),
            ["global.commonConfig.mainBackgroundOpacity"] = JsonSerializer.SerializeToElement(0.55),
            [region.Path] = JsonSerializer.SerializeToElement("璃月"),
            [count.Path] = JsonSerializer.SerializeToElement(3),
        };
        var updated = await store.UpdateAsync(new(loaded.Revision, loaded.Tasks, values));
        Assert.NotEqual(loaded.Revision, updated.Revision);
        using var group = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(store.ScriptGroupDirectory, "采集组.json")));
        Assert.Equal(7, group.RootElement.GetProperty("unknown").GetInt32());
        var project = group.RootElement.GetProperty("projects")[0];
        Assert.Equal(3, project.GetProperty("runNum").GetInt32());
        Assert.Equal("璃月", project.GetProperty("jsScriptSettingsObject").GetProperty("region").GetString());
        Assert.Equal("keep", project.GetProperty("jsScriptSettingsObject").GetProperty("secretToken").GetString());
        using var global = JsonDocument.Parse(await File.ReadAllTextAsync(store.GlobalConfigPath));
        Assert.True(global.RootElement.GetProperty("autoPickConfig").GetProperty("enabled").GetBoolean());
        Assert.Equal(42, global.RootElement.GetProperty("autoPickConfig").GetProperty("unknownOption").GetInt32());
        Assert.Equal(0.55, global.RootElement.GetProperty("commonConfig").GetProperty("mainBackgroundOpacity").GetDouble());
    }

    [Fact]
    public async Task ScriptMetadataAndSchedulerEditsInvalidateTheWholeRevision()
    {
        var store = await Prepare();
        var loaded = await store.LoadAsync();
        await File.AppendAllTextAsync(Path.Combine(_root, "User", "JsScript", "farming", "settings.json"), " ");
        await Assert.ThrowsAsync<ConfigConflictException>(() => store.UpdateAsync(new(loaded.Revision, loaded.Tasks, new Dictionary<string,JsonElement>())));
        var fresh = await store.LoadAsync();
        await File.AppendAllTextAsync(Path.Combine(store.ScriptGroupDirectory, "采集组.json"), " ");
        await Assert.ThrowsAsync<ConfigConflictException>(() => store.UpdateAsync(new(fresh.Revision, fresh.Tasks, new Dictionary<string,JsonElement>())));
    }

    [Fact]
    public async Task InvalidOptionsAndFractionalRunCountNeverWriteFiles()
    {
        var store = await Prepare();
        var loaded = await store.LoadAsync();
        var count = loaded.Fields.First(field => field.Scope == "scriptGroup" && field.Label == "执行次数");
        var path = Path.Combine(store.ScriptGroupDirectory, "采集组.json");
        var original = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(new(loaded.Revision, loaded.Tasks,
            new Dictionary<string,JsonElement> { [count.Path] = JsonSerializer.SerializeToElement(1.5) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(new(loaded.Revision, loaded.Tasks,
            new Dictionary<string,JsonElement> { ["global.autoPickConfig.mode"] = JsonSerializer.SerializeToElement("untrusted-option") })));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
