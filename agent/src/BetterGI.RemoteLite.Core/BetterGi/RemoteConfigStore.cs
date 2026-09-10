using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.BetterGi;

public sealed class RemoteConfigStore
{
    public const string DefaultRemoteConfigName = "远程每日";
    public const string FixedCompletionAction = "关闭游戏和软件";

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] BuiltInTasks =
    [
        "领取邮件",
        "合成树脂",
        "自动秘境",
        "自动首领讨伐",
        "自动幽境危战",
        "自动地脉花",
        "领取每日奖励",
        "领取尘歌壶奖励",
    ];

    private static readonly IReadOnlyList<FieldDefinition> Definitions = CreateDefinitions();
    private static readonly IReadOnlyDictionary<string, FieldDefinition> DefinitionMap = Definitions.ToDictionary(item => item.Path, StringComparer.Ordinal);

    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly string _betterGiDirectory;
    private readonly string _remoteConfigName;

    public RemoteConfigStore(string betterGiExecutable, string remoteConfigName = DefaultRemoteConfigName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(betterGiExecutable);
        _betterGiDirectory = Path.GetDirectoryName(Path.GetFullPath(betterGiExecutable))
            ?? throw new ArgumentException("BetterGI executable directory is invalid.", nameof(betterGiExecutable));
        _remoteConfigName = ValidateConfigName(remoteConfigName);
    }

    public string OneDragonDirectory => Path.Combine(_betterGiDirectory, "User", "OneDragon");
    public string GlobalConfigPath => Path.Combine(_betterGiDirectory, "User", "config.json");
    public string RemoteConfigPath => Path.Combine(OneDragonDirectory, _remoteConfigName + ".json");
    public string RemoteConfigName => _remoteConfigName;

    public IReadOnlyList<string> ListSourceConfigurations()
    {
        if (!Directory.Exists(OneDragonDirectory))
        {
            return [];
        }
        return Directory.EnumerateFiles(OneDragonDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), Path.GetFileName(RemoteConfigPath), StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray()!;
    }

    public async Task CreateRemoteCopyAsync(string sourceConfigName, CancellationToken cancellationToken = default)
    {
        var sourceName = ValidateConfigName(sourceConfigName);
        Directory.CreateDirectory(OneDragonDirectory);
        var sourcePath = Path.Combine(OneDragonDirectory, sourceName + ".json");
        EnsureContainedFile(sourcePath, OneDragonDirectory);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到源一条龙配置。", sourcePath);
        }

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ParseObject(await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false), sourcePath);
            SetRootValue(document, "Name", JsonValue.Create(_remoteConfigName));
            SetRootValue(document, "CompletionAction", JsonValue.Create(FixedCompletionAction));
            if (GetRootValue(document, "TaskDefinitions") is not JsonObject || GetRootValue(document, "TaskOrder") is not JsonArray || GetRootValue(document, "TaskEnabledList") is not JsonObject)
            {
                throw new InvalidDataException("源配置缺少 BetterGI 0.64.x 的任务定义字段。");
            }
            await BackupAndWriteAsync(RemoteConfigPath, Serialize(document), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<RemoteConfigDto> SynchronizeTasksFromSourceAsync(string? sourceConfigName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceConfigName))
        {
            return await LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        var sourceName = ValidateConfigName(sourceConfigName);
        var sourcePath = Path.Combine(OneDragonDirectory, sourceName + ".json");
        EnsureContainedFile(sourcePath, OneDragonDirectory);
        if (!File.Exists(sourcePath) || string.Equals(sourcePath, RemoteConfigPath, StringComparison.OrdinalIgnoreCase))
        {
            return await LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remoteBytes = await ReadRequiredAsync(RemoteConfigPath, cancellationToken).ConfigureAwait(false);
            var globalBytes = await ReadRequiredAsync(GlobalConfigPath, cancellationToken).ConfigureAwait(false);
            var source = ParseObject(await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false), sourcePath);
            var remote = ParseObject(remoteBytes, RemoteConfigPath);
            var sourceDefinitions = GetRootValue(source, "TaskDefinitions") as JsonObject
                ?? throw new InvalidDataException("源一条龙配置缺少 TaskDefinitions。");
            var remoteDefinitions = GetRootValue(remote, "TaskDefinitions") as JsonObject
                ?? throw new InvalidDataException("远程一条龙配置缺少 TaskDefinitions。");
            var sourceOrder = GetRootValue(source, "TaskOrder") as JsonArray
                ?? throw new InvalidDataException("源一条龙配置缺少 TaskOrder。");
            var remoteOrder = GetRootValue(remote, "TaskOrder") as JsonArray
                ?? throw new InvalidDataException("远程一条龙配置缺少 TaskOrder。");
            var sourceEnabled = GetRootValue(source, "TaskEnabledList") as JsonObject
                ?? throw new InvalidDataException("源一条龙配置缺少 TaskEnabledList。");
            var remoteEnabled = GetRootValue(remote, "TaskEnabledList") as JsonObject
                ?? throw new InvalidDataException("远程一条龙配置缺少 TaskEnabledList。");

            var knownIds = remoteDefinitions.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
            var changed = false;
            foreach (var node in sourceOrder)
            {
                var id = node?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id) || knownIds.Contains(id) || sourceDefinitions[id] is null)
                {
                    continue;
                }
                remoteDefinitions[id] = sourceDefinitions[id]!.DeepClone();
                remoteOrder.Add(JsonValue.Create(id));
                remoteEnabled[id] = sourceEnabled[id]?.DeepClone() ?? JsonValue.Create(false);
                knownIds.Add(id);
                changed = true;
            }
            foreach (var pair in sourceDefinitions)
            {
                if (knownIds.Contains(pair.Key) || pair.Value is null)
                {
                    continue;
                }
                remoteDefinitions[pair.Key] = pair.Value.DeepClone();
                remoteOrder.Add(JsonValue.Create(pair.Key));
                remoteEnabled[pair.Key] = sourceEnabled[pair.Key]?.DeepClone() ?? JsonValue.Create(false);
                knownIds.Add(pair.Key);
                changed = true;
            }

            if (changed)
            {
                SetRootValue(remote, "Name", JsonValue.Create(_remoteConfigName));
                SetRootValue(remote, "CompletionAction", JsonValue.Create(FixedCompletionAction));
                var updated = Serialize(remote);
                await BackupAndWriteAsync(RemoteConfigPath, updated, cancellationToken).ConfigureAwait(false);
                remoteBytes = updated;
            }
            return BuildDto(remote, ParseObject(globalBytes, GlobalConfigPath), ComputeRevision(remoteBytes, globalBytes));
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<RemoteConfigDto> LoadAsync(CancellationToken cancellationToken = default)
    {
        var remoteBytes = await ReadRequiredAsync(RemoteConfigPath, cancellationToken).ConfigureAwait(false);
        var globalBytes = await ReadRequiredAsync(GlobalConfigPath, cancellationToken).ConfigureAwait(false);
        var remote = ParseObject(remoteBytes, RemoteConfigPath);
        var global = ParseObject(globalBytes, GlobalConfigPath);
        return BuildDto(remote, global, ComputeRevision(remoteBytes, globalBytes));
    }

    public async Task SetCancelHotkeyAsync(string hotkey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hotkey);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var original = await ReadRequiredAsync(GlobalConfigPath, cancellationToken).ConfigureAwait(false);
            var global = ParseObject(original, GlobalConfigPath);
            SetValue(global, "hotKeyConfig.cancelTaskHotkey", JsonValue.Create(hotkey.Trim()));
            SetValue(global, "hotKeyConfig.cancelTaskHotkeyType", JsonValue.Create("GlobalRegister"));
            var updated = Serialize(global);
            if (!original.AsSpan().SequenceEqual(updated))
            {
                await BackupAndWriteAsync(GlobalConfigPath, updated, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task EnsureRewardRecognitionEnabledAsync(CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remoteBytes = await ReadRequiredAsync(RemoteConfigPath, cancellationToken).ConfigureAwait(false);
            var globalBytes = await ReadRequiredAsync(GlobalConfigPath, cancellationToken).ConfigureAwait(false);
            var remote = ParseObject(remoteBytes, RemoteConfigPath);
            var global = ParseObject(globalBytes, GlobalConfigPath);
            SetValue(remote, "AutoBossRewardRecognitionEnabled", JsonValue.Create(true));
            SetValue(global, "autoDomainConfig.rewardRecognitionEnabled", JsonValue.Create(true));
            var updatedRemote = Serialize(remote);
            var updatedGlobal = Serialize(global);
            if (!remoteBytes.AsSpan().SequenceEqual(updatedRemote) || !globalBytes.AsSpan().SequenceEqual(updatedGlobal))
            {
                await WritePairWithRollbackAsync(updatedRemote, updatedGlobal, remoteBytes, globalBytes, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<RemoteConfigDto> UpdateAsync(ConfigUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remoteBytes = await ReadRequiredAsync(RemoteConfigPath, cancellationToken).ConfigureAwait(false);
            var globalBytes = await ReadRequiredAsync(GlobalConfigPath, cancellationToken).ConfigureAwait(false);
            var currentRevision = ComputeRevision(remoteBytes, globalBytes);
            if (!string.Equals(currentRevision, request.BaseRevision, StringComparison.Ordinal))
            {
                throw new ConfigConflictException(currentRevision);
            }

            var remote = ParseObject(remoteBytes, RemoteConfigPath);
            var global = ParseObject(globalBytes, GlobalConfigPath);
            ApplyTasks(remote, request.Tasks);
            ApplyFields(remote, global, request.Values);
            SetRootValue(remote, "Name", JsonValue.Create(_remoteConfigName));
            SetRootValue(remote, "CompletionAction", JsonValue.Create(FixedCompletionAction));

            var updatedRemoteBytes = Serialize(remote);
            var updatedGlobalBytes = Serialize(global);
            await WritePairWithRollbackAsync(updatedRemoteBytes, updatedGlobalBytes, remoteBytes, globalBytes, cancellationToken).ConfigureAwait(false);
            return BuildDto(remote, global, ComputeRevision(updatedRemoteBytes, updatedGlobalBytes));
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private RemoteConfigDto BuildDto(JsonObject remote, JsonObject global, string revision)
    {
        var tasks = ReadTasks(remote);
        var fields = Definitions.Select(definition => definition.ToDto(ReadValue(definition, remote, global))).ToArray();
        return new RemoteConfigDto(_remoteConfigName, revision, tasks, fields, FixedCompletionAction, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<TaskItemDto> ReadTasks(JsonObject remote)
    {
        var definitions = GetRootValue(remote, "TaskDefinitions") as JsonObject
            ?? throw new InvalidDataException("一条龙配置缺少 TaskDefinitions。");
        var enabled = GetRootValue(remote, "TaskEnabledList") as JsonObject
            ?? throw new InvalidDataException("一条龙配置缺少 TaskEnabledList。");
        var order = GetRootValue(remote, "TaskOrder") as JsonArray
            ?? throw new InvalidDataException("一条龙配置缺少 TaskOrder。");

        var ids = order.Select(node => node?.GetValue<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToList();
        foreach (var id in definitions.Select(pair => pair.Key))
        {
            if (!ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }

        return ids.Select((id, index) =>
        {
            var name = definitions[id]?.GetValue<string>() ?? "未知任务";
            var isEnabled = enabled[id]?.GetValue<bool>() ?? false;
            return new TaskItemDto(id, name, isEnabled, !BuiltInTasks.Contains(name, StringComparer.Ordinal), index);
        }).ToArray();
    }

    private static void ApplyTasks(JsonObject remote, IReadOnlyList<TaskItemDto> requested)
    {
        if (requested.Count == 0 || requested.Count > 128 || requested.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != requested.Count)
        {
            throw new InvalidDataException("任务列表无效。");
        }
        var current = ReadTasks(remote);
        var currentById = current.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (currentById.Count != requested.Count || requested.Any(item => !currentById.ContainsKey(item.Id)))
        {
            throw new InvalidDataException("手机不能增加或删除一条龙任务。");
        }
        foreach (var item in requested)
        {
            var existing = currentById[item.Id];
            if (!string.Equals(existing.Name, item.Name, StringComparison.Ordinal) || existing.IsCustom != item.IsCustom)
            {
                throw new InvalidDataException("手机不能修改任务身份或名称。");
            }
        }

        var sorted = requested.OrderBy(item => item.Order).ToArray();
        SetRootValue(remote, "TaskOrder", new JsonArray(sorted.Select(item => (JsonNode?)JsonValue.Create(item.Id)).ToArray()));
        var enabled = GetRootValue(remote, "TaskEnabledList") as JsonObject ?? new JsonObject();
        foreach (var item in sorted)
        {
            enabled[item.Id] = item.Enabled;
        }
        SetRootValue(remote, "TaskEnabledList", enabled);
    }

    private static void ApplyFields(JsonObject remote, JsonObject global, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (values.Count > Definitions.Count)
        {
            throw new InvalidDataException("提交的配置字段过多。");
        }
        foreach (var pair in values)
        {
            if (!DefinitionMap.TryGetValue(pair.Key, out var definition))
            {
                throw new InvalidDataException($"字段不允许远程修改: {pair.Key}");
            }
            definition.Validate(pair.Value);
            var root = definition.Scope == "global" ? global : remote;
            SetValue(root, definition.JsonPath, JsonNode.Parse(pair.Value.GetRawText()));
        }
    }

    private static JsonElement ReadValue(FieldDefinition definition, JsonObject remote, JsonObject global)
    {
        var root = definition.Scope == "global" ? global : remote;
        var node = GetValue(root, definition.JsonPath) ?? JsonNode.Parse(definition.DefaultJson);
        return JsonSerializer.SerializeToElement(node, RemoteJson.Options);
    }

    private async Task WritePairWithRollbackAsync(byte[] remote, byte[] global, byte[] originalRemote, byte[] originalGlobal, CancellationToken cancellationToken)
    {
        await BackupAsync(RemoteConfigPath, originalRemote, cancellationToken).ConfigureAwait(false);
        await BackupAsync(GlobalConfigPath, originalGlobal, cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicAsync(RemoteConfigPath, remote, cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(GlobalConfigPath, global, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await WriteAtomicAsync(RemoteConfigPath, originalRemote, CancellationToken.None).ConfigureAwait(false);
            await WriteAtomicAsync(GlobalConfigPath, originalGlobal, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task BackupAndWriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            await BackupAsync(path, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
        await WriteAtomicAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task BackupAsync(string sourcePath, byte[] content, CancellationToken cancellationToken)
    {
        var backupDirectory = Path.Combine(_betterGiDirectory, "User", "RemoteLiteBackups");
        Directory.CreateDirectory(backupDirectory);
        var safeName = Path.GetFileName(sourcePath).Replace('.', '_');
        var backupPath = Path.Combine(backupDirectory, $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.bak");
        await File.WriteAllBytesAsync(backupPath, content, cancellationToken).ConfigureAwait(false);
        var stale = Directory.EnumerateFiles(backupDirectory, safeName + "_*.bak")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(5)
            .ToArray();
        foreach (var path in stale)
        {
            File.Delete(path);
        }
    }

    private static async Task WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".remote-lite-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonNode? GetValue(JsonObject root, string jsonPath)
    {
        JsonNode? current = root;
        foreach (var segment in jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current is JsonObject currentObject ? GetRootValue(currentObject, segment) : null;
            if (current is null)
            {
                return null;
            }
        }
        return current;
    }

    private static void SetValue(JsonObject root, string jsonPath, JsonNode? value)
    {
        var segments = jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonObject current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (GetRootValue(current, segments[index]) is not JsonObject child)
            {
                child = new JsonObject();
                SetRootValue(current, segments[index], child);
            }
            current = child;
        }
        SetRootValue(current, segments[^1], value);
    }

    private static JsonNode? GetRootValue(JsonObject root, string name)
    {
        foreach (var property in root)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }
        return null;
    }

    private static void SetRootValue(JsonObject root, string name, JsonNode? value)
    {
        var existingName = root.Select(property => property.Key)
            .FirstOrDefault(propertyName => string.Equals(propertyName, name, StringComparison.OrdinalIgnoreCase));
        root[existingName ?? name] = value;
    }

    private static JsonObject ParseObject(byte[] bytes, string path)
    {
        try
        {
            return JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }) as JsonObject ?? throw new InvalidDataException($"JSON 根节点不是对象: {path}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"配置文件 JSON 无效: {path}", exception);
        }
    }

    private static byte[] Serialize(JsonObject value) => Encoding.UTF8.GetBytes(value.ToJsonString(PrettyJson));

    private static async Task<byte[]> ReadRequiredAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("缺少 BetterGI 配置文件。", path);
        }
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeRevision(ReadOnlySpan<byte> remote, ReadOnlySpan<byte> global)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(remote);
        hash.AppendData([0]);
        hash.AppendData(global);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ValidateConfigName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 64 || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed is "." or "..")
        {
            throw new ArgumentException("配置名称无效。", nameof(name));
        }
        return trimmed;
    }

    private static void EnsureContainedFile(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("配置文件不在 BetterGI 一条龙目录内。");
        }
    }

    private static IReadOnlyList<FieldDefinition> CreateDefinitions()
    {
        var countries = new[] { "挪德卡莱", "枫丹", "稻妻", "璃月", "蒙德" };
        var resin = new[] { "浓缩树脂", "原粹树脂", "须臾树脂", "脆弱树脂" };
        var sunday = new[] { "", "0", "1", "2", "3" };
        var items = new List<FieldDefinition>
        {
            Select("oneDragon.craftingBenchCountry", "oneDragon", "合成树脂", "合成台国家", "\"枫丹\"", countries),
            Number("oneDragon.minResinToKeep", "oneDragon", "合成树脂", "保留原粹树脂", "0", 0, 200),
            Select("oneDragon.adventurersGuildCountry", "oneDragon", "领取每日奖励", "冒险家协会国家", "\"枫丹\"", countries),
            Text("oneDragon.dailyRewardPartyName", "oneDragon", "领取每日奖励", "好感队伍", "\"\""),
            Text("oneDragon.partyName", "oneDragon", "自动秘境", "队伍名称", "\"\""),
            Text("oneDragon.domainName", "oneDragon", "自动秘境", "秘境名称", "\"\""),
            Toggle("oneDragon.weeklyDomainEnabled", "oneDragon", "自动秘境", "启用每周秘境配置", "false"),
            Select("oneDragon.sundayEverySelectedValue", "oneDragon", "自动秘境", "普通周日奖励序号", "\"0\"", sunday),
            Select("oneDragon.sundayWeeklySelectedValue", "oneDragon", "自动秘境", "周计划默认奖励序号", "\"0\"", sunday),
            Text("oneDragon.autoBossName", "oneDragon", "自动首领讨伐", "首领名称", "\"\""),
            Text("oneDragon.autoBossStrategyName", "oneDragon", "自动首领讨伐", "战斗策略", "\"根据队伍自动选择\""),
            Text("oneDragon.autoBossTeamName", "oneDragon", "自动首领讨伐", "队伍名称", "\"\""),
            Toggle("oneDragon.autoBossSpecifyRunCount", "oneDragon", "自动首领讨伐", "指定讨伐次数", "false"),
            Number("oneDragon.autoBossRunCount", "oneDragon", "自动首领讨伐", "讨伐次数", "1", 1, 30),
            Toggle("oneDragon.autoBossUseTransientResin", "oneDragon", "自动首领讨伐", "允许使用须臾树脂", "false"),
            Toggle("oneDragon.autoBossUseFragileResin", "oneDragon", "自动首领讨伐", "允许使用脆弱树脂", "false"),
            Number("oneDragon.autoBossReviveRetryCount", "oneDragon", "自动首领讨伐", "复活重试次数", "3", 0, 10),
            Toggle("oneDragon.autoBossReturnToStatueAfterEachRound", "oneDragon", "自动首领讨伐", "每轮后返回七天神像", "false"),
            Toggle("oneDragon.autoBossRewardRecognitionEnabled", "oneDragon", "自动首领讨伐", "奖励识别", "false"),
            Select("oneDragon.sereniteaPotTpType", "oneDragon", "领取尘歌壶奖励", "传送方式", "\"地图传送\"", ["地图传送", "尘歌壶道具"]),
            Multi("oneDragon.secretTreasureObjects", "oneDragon", "领取尘歌壶奖励", "购买物品", "[]", ["每天重复", "布匹", "须臾树脂", "大英雄的经验", "流浪者的经验", "精锻用魔矿", "摩拉", "祝圣精华", "祝圣油膏"]),
            Toggle("oneDragon.leyLineOneDragonMode", "oneDragon", "自动地脉花", "一条龙快速模式", "false"),
            Number("oneDragon.leyLineRunCount", "oneDragon", "自动地脉花", "刷取次数", "0", 0, 30),
            Toggle("oneDragon.leyLineResinExhaustionMode", "oneDragon", "自动地脉花", "树脂耗尽模式", "false"),
            Toggle("oneDragon.leyLineOpenModeCountMin", "oneDragon", "自动地脉花", "耗尽模式取较小次数", "false"),
            Toggle("global.autoDomainConfig.autoArtifactSalvage", "global", "自动秘境", "完成后分解圣遗物", "false", "全局 BetterGI 设置"),
            Toggle("global.autoDomainConfig.specifyResinUse", "global", "自动秘境", "指定树脂次数", "false", "全局 BetterGI 设置"),
            Multi("global.autoDomainConfig.resinPriorityList", "global", "自动秘境", "树脂使用顺序", "[\"浓缩树脂\",\"原粹树脂\"]", resin, "全局 BetterGI 设置"),
            Number("global.autoDomainConfig.originalResinUseCount", "global", "自动秘境", "原粹树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Number("global.autoDomainConfig.condensedResinUseCount", "global", "自动秘境", "浓缩树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Number("global.autoDomainConfig.transientResinUseCount", "global", "自动秘境", "须臾树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Number("global.autoDomainConfig.fragileResinUseCount", "global", "自动秘境", "脆弱树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Number("global.autoDomainConfig.reviveRetryCount", "global", "自动秘境", "复活重试次数", "3", 0, 10, "全局 BetterGI 设置"),
            Toggle("global.autoDomainConfig.rewardRecognitionEnabled", "global", "自动秘境", "奖励识别", "false", "全局 BetterGI 设置"),
            Text("global.autoStygianOnslaughtConfig.strategyName", "global", "自动幽境危战", "战斗策略", "\"\"", "全局 BetterGI 设置"),
            Number("global.autoStygianOnslaughtConfig.bossNum", "global", "自动幽境危战", "首领序号", "1", 1, 3, "全局 BetterGI 设置"),
            Text("global.autoStygianOnslaughtConfig.fightTeamName", "global", "自动幽境危战", "队伍名称", "\"\"", "全局 BetterGI 设置"),
            Toggle("global.autoStygianOnslaughtConfig.autoArtifactSalvage", "global", "自动幽境危战", "完成后分解圣遗物", "false", "全局 BetterGI 设置"),
            Toggle("global.autoStygianOnslaughtConfig.specifyResinUse", "global", "自动幽境危战", "指定树脂次数", "false", "全局 BetterGI 设置"),
            Multi("global.autoStygianOnslaughtConfig.resinPriorityList", "global", "自动幽境危战", "树脂使用顺序", "[\"浓缩树脂\",\"原粹树脂\"]", resin, "全局 BetterGI 设置"),
            Number("global.autoStygianOnslaughtConfig.originalResinUseCount", "global", "自动幽境危战", "原粹树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Number("global.autoStygianOnslaughtConfig.condensedResinUseCount", "global", "自动幽境危战", "浓缩树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Number("global.autoStygianOnslaughtConfig.transientResinUseCount", "global", "自动幽境危战", "须臾树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Number("global.autoStygianOnslaughtConfig.fragileResinUseCount", "global", "自动幽境危战", "脆弱树脂次数", "0", 0, 30, "全局 BetterGI 设置"),
            Text("global.autoLeyLineOutcropConfig.team", "global", "自动地脉花", "战斗队伍", "\"\"", "全局 BetterGI 设置"),
            Text("global.autoLeyLineOutcropConfig.friendshipTeam", "global", "自动地脉花", "好感队伍", "\"\"", "全局 BetterGI 设置"),
            Toggle("global.autoLeyLineOutcropConfig.useTransientResin", "global", "自动地脉花", "允许使用须臾树脂", "false", "全局 BetterGI 设置"),
            Toggle("global.autoLeyLineOutcropConfig.useFragileResin", "global", "自动地脉花", "允许使用脆弱树脂", "false", "全局 BetterGI 设置"),
            Number("global.autoLeyLineOutcropConfig.timeout", "global", "自动地脉花", "单轮超时秒数", "120", 30, 600, "全局 BetterGI 设置"),
            Toggle("global.autoLeyLineOutcropConfig.scanDropsAfterRewardEnabled", "global", "自动地脉花", "领奖后扫描掉落物", "false", "全局 BetterGI 设置"),
            Number("global.autoLeyLineOutcropConfig.scanDropsAfterRewardSeconds", "global", "自动地脉花", "扫描时长秒数", "12", 0, 60, "全局 BetterGI 设置"),
        };

        foreach (var day in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" })
        {
            var label = day switch
            {
                "Monday" => "周一",
                "Tuesday" => "周二",
                "Wednesday" => "周三",
                "Thursday" => "周四",
                "Friday" => "周五",
                "Saturday" => "周六",
                _ => "周日",
            };
            var lower = char.ToLowerInvariant(day[0]) + day[1..];
            items.Add(Toggle($"oneDragon.leyLineRun{day}", "oneDragon", "自动地脉花", $"{label}运行", "true"));
            items.Add(Text($"oneDragon.leyLine{day}Type", "oneDragon", "自动地脉花", $"{label}类型", "\"\""));
            items.Add(Select($"oneDragon.leyLine{day}Country", "oneDragon", "自动地脉花", $"{label}国家", "\"\"", ["", .. countries]));
            items.Add(Text($"oneDragon.{lower}PartyName", "oneDragon", "自动秘境", $"{label}队伍", "\"\""));
            items.Add(Text($"oneDragon.{lower}DomainName", "oneDragon", "自动秘境", $"{label}秘境", "\"\""));
            items.Add(Select($"oneDragon.{lower}SelectedValue", "oneDragon", "自动秘境", $"{label}奖励序号", "\"0\"", sunday));
        }
        return items;
    }

    private static FieldDefinition Text(string path, string scope, string group, string label, string defaultJson, string? description = null)
        => new(path, scope, ToJsonPath(path, scope), group, label, EditableFieldType.Text, defaultJson, null, null, null, description);

    private static FieldDefinition Number(string path, string scope, string group, string label, string defaultJson, double minimum, double maximum, string? description = null)
        => new(path, scope, ToJsonPath(path, scope), group, label, EditableFieldType.Number, defaultJson, null, minimum, maximum, description);

    private static FieldDefinition Toggle(string path, string scope, string group, string label, string defaultJson, string? description = null)
        => new(path, scope, ToJsonPath(path, scope), group, label, EditableFieldType.Toggle, defaultJson, null, null, null, description);

    private static FieldDefinition Select(string path, string scope, string group, string label, string defaultJson, IReadOnlyList<string> options, string? description = null)
        => new(path, scope, ToJsonPath(path, scope), group, label, EditableFieldType.Select, defaultJson, options, null, null, description);

    private static FieldDefinition Multi(string path, string scope, string group, string label, string defaultJson, IReadOnlyList<string> options, string? description = null)
        => new(path, scope, ToJsonPath(path, scope), group, label, EditableFieldType.MultiSelect, defaultJson, options, null, null, description);

    private static string ToJsonPath(string path, string scope)
    {
        var jsonPath = path[(path.IndexOf('.') + 1)..];
        return scope == "oneDragon" && jsonPath.Length > 0
            ? char.ToUpperInvariant(jsonPath[0]) + jsonPath[1..]
            : jsonPath;
    }

    private sealed record FieldDefinition(
        string Path,
        string Scope,
        string JsonPath,
        string Group,
        string Label,
        EditableFieldType Type,
        string DefaultJson,
        IReadOnlyList<string>? Options,
        double? Minimum,
        double? Maximum,
        string? Description)
    {
        public EditableFieldDto ToDto(JsonElement value)
            => new(Path, Scope, Group, Label, Type, value, Options, Minimum, Maximum, Description);

        public void Validate(JsonElement value)
        {
            switch (Type)
            {
                case EditableFieldType.Toggle when value.ValueKind is not JsonValueKind.True and not JsonValueKind.False:
                    throw new InvalidDataException($"{Label} 必须是开关值。");
                case EditableFieldType.Number:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
                        (Minimum.HasValue && number < Minimum.Value) || (Maximum.HasValue && number > Maximum.Value))
                    {
                        throw new InvalidDataException($"{Label} 超出允许范围。");
                    }
                    break;
                case EditableFieldType.Text:
                    if (value.ValueKind != JsonValueKind.String || (value.GetString()?.Length ?? 0) > 160)
                    {
                        throw new InvalidDataException($"{Label} 文本无效。");
                    }
                    break;
                case EditableFieldType.Select:
                    if (value.ValueKind != JsonValueKind.String || Options is null || !Options.Contains(value.GetString() ?? string.Empty, StringComparer.Ordinal))
                    {
                        throw new InvalidDataException($"{Label} 选项无效。");
                    }
                    break;
                case EditableFieldType.MultiSelect:
                    if (value.ValueKind != JsonValueKind.Array || Options is null)
                    {
                        throw new InvalidDataException($"{Label} 多选值无效。");
                    }
                    var selected = value.EnumerateArray().Select(item => item.GetString()).ToArray();
                    if (selected.Any(item => item is null || !Options.Contains(item, StringComparer.Ordinal)) || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
                    {
                        throw new InvalidDataException($"{Label} 包含不允许的选项。");
                    }
                    break;
            }
        }
    }
}

public sealed class ConfigConflictException(string latestRevision) : Exception("BetterGI 配置已在电脑端发生变化。")
{
    public string LatestRevision { get; } = latestRevision;
}
