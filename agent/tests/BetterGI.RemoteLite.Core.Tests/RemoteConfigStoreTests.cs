using System.Text.Json;
using BetterGI.RemoteLite.BetterGi;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class RemoteConfigStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-remote-lite-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreatesDedicatedCopyAndPreservesUnknownFields()
    {
        var executable = PrepareFiles();
        var store = new RemoteConfigStore(executable);
        await store.CreateRemoteCopyAsync("默认配置");
        await store.SetCancelHotkeyAsync("Ctrl+Shift+F12");

        var loaded = await store.LoadAsync();
        Assert.Equal(RemoteConfigStore.DefaultRemoteConfigName, loaded.Name);
        Assert.Equal(RemoteConfigStore.FixedCompletionAction, loaded.CompletionAction);
        Assert.Equal(2, loaded.Tasks.Count);

        var updatedTasks = loaded.Tasks.Select((task, index) => task with { Enabled = !task.Enabled, Order = loaded.Tasks.Count - index }).ToArray();
        var values = loaded.Fields.ToDictionary(field => field.Path, field => field.Value, StringComparer.Ordinal);
        values["oneDragon.minResinToKeep"] = JsonSerializer.SerializeToElement(40);
        var updated = await store.UpdateAsync(new ConfigUpdateRequest(loaded.Revision, updatedTasks, values));

        Assert.NotEqual(loaded.Revision, updated.Revision);
        var remoteJson = await File.ReadAllTextAsync(store.RemoteConfigPath);
        using var document = JsonDocument.Parse(remoteJson);
        Assert.Equal("keep-me", document.RootElement.GetProperty("UnknownProperty").GetString());
        Assert.Equal(40, document.RootElement.GetProperty("MinResinToKeep").GetInt32());
        Assert.Equal(RemoteConfigStore.FixedCompletionAction, document.RootElement.GetProperty("CompletionAction").GetString());

        using var globalDocument = JsonDocument.Parse(await File.ReadAllTextAsync(store.GlobalConfigPath));
        var hotkeys = globalDocument.RootElement.GetProperty("hotKeyConfig");
        Assert.Equal("Ctrl+Shift+F12", hotkeys.GetProperty("cancelTaskHotkey").GetString());
        Assert.Equal("GlobalRegister", hotkeys.GetProperty("cancelTaskHotkeyType").GetString());
        Assert.Equal(123, globalDocument.RootElement.GetProperty("anotherUnknownProperty").GetInt32());
    }

    [Fact]
    public async Task RejectsStaleRevisionAndTaskInsertion()
    {
        var executable = PrepareFiles();
        var store = new RemoteConfigStore(executable);
        await store.CreateRemoteCopyAsync("默认配置");
        var loaded = await store.LoadAsync();
        await File.AppendAllTextAsync(store.GlobalConfigPath, " ");

        await Assert.ThrowsAsync<ConfigConflictException>(() => store.UpdateAsync(new ConfigUpdateRequest(loaded.Revision, loaded.Tasks, new Dictionary<string, JsonElement>())));

        var fresh = await store.LoadAsync();
        var inserted = fresh.Tasks.Append(new TaskItemDto("new", "恶意任务", true, true, 99)).ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(new ConfigUpdateRequest(fresh.Revision, inserted, new Dictionary<string, JsonElement>())));
    }

    [Fact]
    public async Task SynchronizesNewComputerTasksWithoutOverwritingRemoteChoices()
    {
        var executable = PrepareFiles();
        var store = new RemoteConfigStore(executable);
        await store.CreateRemoteCopyAsync("默认配置");
        var initial = await store.LoadAsync();
        var reversed = initial.Tasks.Reverse().Select((task, order) => task with { Enabled = false, Order = order }).ToArray();
        await store.UpdateAsync(new ConfigUpdateRequest(initial.Revision, reversed, new Dictionary<string, JsonElement>()));

        var sourcePath = Path.Combine(Path.GetDirectoryName(executable)!, "User", "OneDragon", "默认配置.json");
        var source = await File.ReadAllTextAsync(sourcePath);
        source = source
            .Replace("\"b\": \"自动秘境\"", "\"b\": \"自动秘境\", \"c\": \"我的新调度组\"")
            .Replace("[\"a\", \"b\"]", "[\"a\", \"b\", \"c\"]")
            .Replace("\"b\": false", "\"b\": false, \"c\": true");
        await File.WriteAllTextAsync(sourcePath, source);

        var synchronized = await store.SynchronizeTasksFromSourceAsync("默认配置");

        Assert.Equal(3, synchronized.Tasks.Count);
        Assert.Equal(["自动秘境", "领取邮件", "我的新调度组"], synchronized.Tasks.Select(task => task.Name));
        Assert.False(synchronized.Tasks[0].Enabled);
        Assert.False(synchronized.Tasks[1].Enabled);
        Assert.True(synchronized.Tasks[2].Enabled);
        Assert.True(synchronized.Tasks[2].IsCustom);
    }

    private string PrepareFiles()
    {
        var betterGi = Path.Combine(_root, "BetterGI");
        var oneDragon = Path.Combine(betterGi, "User", "OneDragon");
        Directory.CreateDirectory(oneDragon);
        File.WriteAllText(Path.Combine(betterGi, "BetterGI.exe"), string.Empty);
        File.WriteAllText(Path.Combine(oneDragon, "默认配置.json"), """
        {
          "Name": "默认配置",
          "CompletionAction": "无",
          "TaskDefinitions": { "a": "领取邮件", "b": "自动秘境" },
          "TaskOrder": ["a", "b"],
          "TaskEnabledList": { "a": true, "b": false },
          "MinResinToKeep": 0,
          "SecretTreasureObjects": ["每天重复", "须臾树脂", "大英雄的经验", "摩拉"],
          "UnknownProperty": "keep-me"
        }
        """);
        File.WriteAllText(Path.Combine(betterGi, "User", "config.json"), """
        {
          "autoDomainConfig": { "rewardRecognitionEnabled": false },
          "autoStygianOnslaughtConfig": {},
          "autoLeyLineOutcropConfig": {},
          "hotKeyConfig": {
            "cancelTaskHotkey": "F12",
            "cancelTaskHotkeyType": "KeyboardMonitor"
          },
          "anotherUnknownProperty": 123
        }
        """);
        return Path.Combine(betterGi, "BetterGI.exe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
