using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.BetterGi;

public sealed partial class RemoteConfigStore
{
    private const int MaxConfigFileBytes = 4 * 1024 * 1024;
    private static readonly string[] ScheduleOptions = ["Daily", "EveryTwoDays", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private static readonly Regex UnsafeSettingName = new("password|passwd|secret|token|cookie|credential|webhook|authorization|apikey|api_key|privatekey|private_key|shell|command|script|file|path|directory|url|endpoint|密码|密钥|令牌|口令|脚本|命令|路径|文件|网址|链接", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string ScriptGroupDirectory => Path.Combine(_betterGiDirectory, "User", "ScriptGroup");

    private async Task<SchedulerSnapshot> LoadSchedulerAsync(CancellationToken cancellationToken)
    {
        var result = new SchedulerSnapshot();
        if (!Directory.Exists(ScriptGroupDirectory)) return result;
        EnsureSafeLocalFile(ScriptGroupDirectory, Path.Combine(_betterGiDirectory, "User"));
        var paths = Directory.EnumerateFiles(ScriptGroupDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (paths.Length > 512) throw new InvalidDataException("调度配置组过多，请先在电脑端整理后再同步。");
        foreach (var path in paths)
        {
            var bytes = await ReadSafeConfigAsync(path, ScriptGroupDirectory, cancellationToken).ConfigureAwait(false);
            result.RevisionFiles.Add(path, bytes);
            JsonObject root;
            try { root = ParseObject(bytes, "调度配置组"); }
            catch (InvalidDataException) { result.Warnings.Add("有调度配置组格式异常，请在电脑端修复后同步。"); continue; }
            if (GetRootValue(root, "projects") is not JsonArray projects)
            {
                result.Warnings.Add("有调度配置组缺少任务列表，请在电脑端检查。");
                continue;
            }
            if (projects.Count > 1024) throw new InvalidDataException("单个调度配置组任务过多，请先在电脑端拆分配置组。");
            var document = new SchedulerDocument(path, bytes, root);
            result.Documents.Add(document);
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(path)))).ToLowerInvariant()[..24];
            var name = ReadText(root, "name") ?? Path.GetFileNameWithoutExtension(path);
            var prefix = "scriptGroup." + id;
            var title = "调度器 · " + SafeDisplay(name);
            AddPathingFields(result, document, prefix, title);
            for (var index = 0; index < projects.Count; index++)
            {
                if (projects[index] is not JsonObject project) continue;
                var type = ReadText(project, "type");
                // A Shell project's name is itself executable code; do not expose it.
                if (type is not ("Javascript" or "KeyMouse" or "Pathing")) continue;
                var projectTitle = title + " / " + (index + 1) + ". " + SafeDisplay(ReadText(project, "name") ?? "未命名任务");
                var projectPrefix = prefix + ".project." + index;
                AddBinding(result, document, project, Select(projectPrefix + ".status", "scriptGroup", projectTitle, "任务状态", "\"Enabled\"", ["Enabled", "Disabled"]), "status");
                var schedule = ReadText(project, "schedule") ?? "Daily";
                var options = ScheduleOptions.Contains(schedule, StringComparer.Ordinal) || schedule.Length > 120 ? ScheduleOptions : [.. ScheduleOptions, schedule];
                AddBinding(result, document, project, Select(projectPrefix + ".schedule", "scriptGroup", projectTitle, "执行周期", "\"Daily\"", options), "schedule");
                AddBinding(result, document, project, Number(projectPrefix + ".runNum", "scriptGroup", projectTitle, "执行次数", "1", 1, 9999), "runNum");
                AddBinding(result, document, project, Number(projectPrefix + ".index", "scriptGroup", projectTitle, "执行顺序（从 1 开始）", (index + 1).ToString(), 1, projects.Count), "index", displayOffset: 1);
                if (type == "Javascript") await AddScriptSettingsAsync(result, document, project, projectPrefix, projectTitle, cancellationToken).ConfigureAwait(false);
            }
        }
        return result;
    }

    private static void AddPathingFields(SchedulerSnapshot snapshot, SchedulerDocument document, string prefix, string title)
    {
        if (GetValue(document.Root, "config.pathingConfig") is not JsonObject pathing) return;
        var definitions = new List<FieldDefinition>();
        foreach (var (key, label) in new[] { ("enabled", "启用地图追踪配置"), ("autoPickEnabled", "自动拾取"), ("isVisitStatueBeforeSwitchParty", "切换队伍前前往七天神像"), ("guardianElementalSkillLongPress", "护盾角色长按技能"), ("jsScriptUseEnabled", "脚本使用本组追踪配置"), ("soloTaskUseFightEnabled", "独立战斗使用本组策略"), ("autoSkipEnabled", "自动脱离剧情"), ("autoRunEnabled", "自动冲刺"), ("autoEatEnabled", "自动吃药"), ("hideOnRepeat", "连续执行时隐藏"), ("autoFightEnabled", "启用本组战斗配置"), ("switchToWalkEnabled", "接近节点时切换步行"), ("mwkJumpFlyEnabled", "玛薇卡跳飞"), ("mwkDisableSprintEnabled", "玛薇卡骑乘时禁用冲刺"), ("onlyInTeleportRecover", "仅传送时恢复（旧版）"), ("taskCycleConfig.enable", "启用执行周期"), ("taskCycleConfig.isBoundaryTimeBasedOnServerTime", "周期使用服务器时间"), ("taskCompletionSkipRuleConfig.enable", "跳过已完成任务"), ("taskCompletionSkipRuleConfig.isBoundaryTimeBasedOnServerTime", "跳过规则使用服务器时间") })
            definitions.Add(Toggle("scriptGroup." + key, "scriptGroup", title, label, "false"));
        foreach (var (key, label) in new[] { ("partyName", "队伍名称"), ("guardianElementalSkillSecondInterval", "护盾技能间隔（秒）"), ("skipDuring", "不执行的时间段") })
            definitions.Add(Text("scriptGroup." + key, "scriptGroup", title, label, "\"\""));
        foreach (var (key, label) in new[] { ("mainAvatarIndex", "行走角色位置"), ("guardianAvatarIndex", "护盾角色位置") })
            definitions.Add(Select("scriptGroup." + key, "scriptGroup", title, label, "\"\"", ["", "1", "2", "3", "4"]));
        foreach (var (key, label, min, max) in new[] {
            ("recoverTiming", "恢复时机（0 任意节点，1 传送点，2 不恢复）", 0, 2),
            ("useGadgetIntervalMs", "小道具使用间隔（毫秒）", 0, 3600000),
            ("distance", "赶路临界距离（米）", 1, 500), ("approachStopDistance", "接近停止距离（米）", 0, 500),
            ("hurryOnFrameInterval", "赶路检测间隔（毫秒）", 1, 150), ("mwkJumpFlyDistance", "跳飞启用距离（米）", 2, 1000),
            ("mwkJumpFlySprintCount", "跳飞前冲刺次数", 0, 100), ("taskCycleConfig.boundaryTime", "周期分界时刻（小时）", -1, 23),
            ("taskCycleConfig.cycle", "执行周期（天）", 1, 365), ("taskCycleConfig.index", "执行周期序号", 1, 365),
            ("taskCompletionSkipRuleConfig.boundaryTime", "跳过规则分界时刻（小时）", -1, 23),
            ("taskCompletionSkipRuleConfig.lastRunGapSeconds", "距上次执行间隔（秒，-1 禁用）", -1, 31536000) })
            definitions.Add(Number("scriptGroup." + key, "scriptGroup", title, label, "0", min, max));
        definitions.Add(Number("scriptGroup.mwkJumpFlyIntervalSeconds", "scriptGroup", title, "跳飞间隔（秒）", "1", 0.1, 60) with { IntegerOnly = false });
        definitions.Add(Select("scriptGroup.travelMode", "scriptGroup", title, "赶路模式", "\"精准靠近\"", ["精准靠近", "连续赶路"]));
        definitions.Add(Select("scriptGroup.hurryOnAvatar", "scriptGroup", title, "赶路角色", "\"\"", ["", "自动", "玛薇卡", "闲云", "桑多涅", "恰斯卡", "流浪者", "伊法", "希诺宁", "法尔伽", "夜兰"]));
        definitions.Add(Select("scriptGroup.taskCompletionSkipRuleConfig.skipPolicy", "scriptGroup", title, "完成任务跳过策略", "\"GroupPhysicalPathSkipPolicy\"", ["GroupPhysicalPathSkipPolicy", "PhysicalPathSkipPolicy", "SameNameSkipPolicy"]));
        definitions.Add(Select("scriptGroup.taskCompletionSkipRuleConfig.referencePoint", "scriptGroup", title, "执行间隔参照", "\"EndTime\"", ["StartTime", "EndTime"]));
        AddFightCatalog(definitions, "scriptGroup", "autoFightConfig", title + " · 战斗", existingOnly: true);
        foreach (var definition in definitions)
        {
            if (GetValue(pathing, definition.JsonPath) is null) continue;
            AddBinding(snapshot, document, pathing, definition with { Path = prefix + ".config.pathingConfig." + definition.JsonPath }, definition.JsonPath);
        }
    }

    private async Task AddScriptSettingsAsync(SchedulerSnapshot snapshot, SchedulerDocument document, JsonObject project, string prefix, string title, CancellationToken cancellationToken)
    {
        var folder = ReadText(project, "folderName");
        if (string.IsNullOrWhiteSpace(folder) || Path.IsPathRooted(folder)) return;
        var scriptsRoot = Path.Combine(_betterGiDirectory, "User", "JsScript");
        var scriptRoot = Path.GetFullPath(Path.Combine(scriptsRoot, folder));
        try
        {
            EnsureSafeLocalFile(scriptRoot, scriptsRoot);
            var manifestPath = Path.Combine(scriptRoot, "manifest.json");
            if (!File.Exists(manifestPath)) return;
            var manifestBytes = await ReadSafeConfigAsync(manifestPath, scriptRoot, cancellationToken).ConfigureAwait(false);
            snapshot.RevisionFiles[manifestPath] = manifestBytes;
            var manifest = ParseObject(manifestBytes, "脚本设置说明");
            var settingsUi = ReadText(manifest, "settings_ui") ?? ReadText(manifest, "settingsUi");
            if (string.IsNullOrWhiteSpace(settingsUi) || Path.IsPathRooted(settingsUi)) return;
            var settingsPath = Path.GetFullPath(Path.Combine(scriptRoot, settingsUi));
            EnsureSafeLocalFile(settingsPath, scriptRoot);
            if (!File.Exists(settingsPath)) return;
            var settingsBytes = await ReadSafeConfigAsync(settingsPath, scriptRoot, cancellationToken).ConfigureAwait(false);
            snapshot.RevisionFiles[settingsPath] = settingsBytes;
            if (JsonNode.Parse(settingsBytes, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is not JsonArray definitions) return;
            if (definitions.Count > 512) return;
            var settings = GetRootValue(project, "jsScriptSettingsObject") as JsonObject;
            foreach (var raw in definitions.OfType<JsonObject>())
            {
                var name = ReadText(raw, "name");
                var label = ReadText(raw, "label") ?? name;
                if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || !Regex.IsMatch(name, @"^[\p{L}_][\p{L}\p{N}_]*$") ||
                    UnsafeSettingName.IsMatch(name) || UnsafeSettingName.IsMatch(label ?? "")) continue;
                var key = prefix + ".settings." + name;
                var value = settings is null ? GetRootValue(raw, "default") : GetRootValue(settings, name) ?? GetRootValue(raw, "default");
                var options = (GetRootValue(raw, "options") as JsonArray)?.OfType<JsonValue>()
                    .Select(item => item.TryGetValue<string>(out var option) ? option : null).Where(item => item is not null && item.Length <= 512).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
                FieldDefinition? definition = ReadText(raw, "type") switch
                {
                    "input-text" => Text(key, "scriptGroup", title, SafeDisplay(label ?? name), JsonSerializer.Serialize(value?.ToString() ?? "")),
                    "checkbox" => Toggle(key, "scriptGroup", title, SafeDisplay(label ?? name), bool.TryParse(value?.ToString(), out var flag) && flag ? "true" : "false"),
                    "select" when options is { Length: > 0 and <= 256 } => Select(key, "scriptGroup", title, SafeDisplay(label ?? name), JsonSerializer.Serialize(value?.ToString() ?? options[0]), options),
                    "multi-checkbox" when options is { Length: > 0 and <= 256 } => Multi(key, "scriptGroup", title, SafeDisplay(label ?? name), value is JsonArray ? value.ToJsonString() : "[]", options),
                    _ => null,
                };
                if (definition is null) continue;
                // Keep the dynamic setting name as a single property, never a JSON path.
                var binding = new SchedulerBinding(document, project, "jsScriptSettingsObject", definition, name);
                try { definition.Validate(binding.Read()); }
                catch (InvalidDataException) { continue; }
                snapshot.Fields.TryAdd(key, binding);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            snapshot.Warnings.Add("部分脚本设置暂时无法同步，请在电脑端检查脚本配置。");
        }
    }

    private static void AddBinding(SchedulerSnapshot snapshot, SchedulerDocument document, JsonObject target, FieldDefinition definition, string jsonPath, int displayOffset = 0)
        => snapshot.Fields.Add(definition.Path, new SchedulerBinding(document, target, jsonPath, definition, DisplayOffset: displayOffset));

    private async Task WriteConfigurationSetAsync(byte[] remote, byte[] global, byte[] originalRemote, byte[] originalGlobal, SchedulerSnapshot scheduler, CancellationToken cancellationToken)
    {
        // Recheck every participating document after validation, immediately before writes.
        var freshRemote = await ReadRequiredAsync(RemoteConfigPath, cancellationToken).ConfigureAwait(false);
        var freshGlobal = await ReadRequiredAsync(GlobalConfigPath, cancellationToken).ConfigureAwait(false);
        var freshScheduler = await LoadSchedulerAsync(cancellationToken).ConfigureAwait(false);
        var freshRevision = ComputeRevision(freshRemote, freshGlobal, freshScheduler);
        if (freshRevision != ComputeRevision(originalRemote, originalGlobal, scheduler)) throw new ConfigConflictException(freshRevision);
        var changes = new List<(string Path, byte[] Before, byte[] After)>();
        if (!remote.AsSpan().SequenceEqual(originalRemote)) changes.Add((RemoteConfigPath, originalRemote, remote));
        if (!global.AsSpan().SequenceEqual(originalGlobal)) changes.Add((GlobalConfigPath, originalGlobal, global));
        foreach (var document in scheduler.Documents.Where(item => item.Changed))
        {
            NormalizeProjectOrder(document);
            changes.Add((document.Path, document.Original, Serialize(document.Root)));
        }
        foreach (var change in changes) await BackupAsync(change.Path, change.Before, cancellationToken).ConfigureAwait(false);
        var written = new List<(string Path, byte[] Before)>();
        try
        {
            foreach (var change in changes)
            {
                await WriteAtomicAsync(change.Path, change.After, cancellationToken).ConfigureAwait(false);
                written.Add((change.Path, change.Before));
            }
        }
        catch
        {
            foreach (var change in written.AsEnumerable().Reverse()) await WriteAtomicAsync(change.Path, change.Before, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static void NormalizeProjectOrder(SchedulerDocument document)
    {
        if (!document.OrderChanged || GetRootValue(document.Root, "projects") is not JsonArray projects) return;
        var ordered = projects.OfType<JsonObject>().OrderBy(item => ReadNumber(item, "index", -1)).ToArray();
        if (ordered.Length != projects.Count || ordered.Select(item => ReadNumber(item, "index", -1)).Distinct().Count() != ordered.Length ||
            ordered.Where((item, index) => ReadNumber(item, "index", -1) != index).Any())
            throw new InvalidDataException("调度任务顺序不能重复或缺少序号，请为每个任务设置不同的顺序。");
        SetRootValue(document.Root, "projects", new JsonArray(ordered.Select(item => item.DeepClone()).ToArray()));
    }

    private static string? ReadText(JsonObject root, string name) => GetRootValue(root, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string SafeDisplay(string value) => new string(value.Where(character => !char.IsControl(character)).Take(120).ToArray());

    private static async Task<byte[]> ReadSafeConfigAsync(string path, string directory, CancellationToken cancellationToken)
    {
        EnsureSafeLocalFile(path, directory);
        if (new FileInfo(path).Length > MaxConfigFileBytes) throw new InvalidDataException("配置文件过大，请在电脑端整理后再同步。");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxConfigFileBytes) throw new InvalidDataException("配置文件过大，请在电脑端整理后再同步。");
        return bytes;
    }

    private static void EnsureSafeLocalFile(string path, string directory)
    {
        EnsureContainedFile(path, directory);
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("远程配置不能访问链接到其他目录的文件。");
            if (string.Equals(current, fullDirectory, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private sealed class SchedulerSnapshot
    {
        public List<SchedulerDocument> Documents { get; } = [];
        public Dictionary<string, byte[]> RevisionFiles { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, SchedulerBinding> Fields { get; } = new(StringComparer.Ordinal);
        public List<string> Warnings { get; } = [];
    }

    private sealed class SchedulerDocument(string path, byte[] original, JsonObject root)
    {
        public string Path { get; } = path;
        public byte[] Original { get; } = original;
        public JsonObject Root { get; } = root;
        public bool Changed { get; set; }
        public bool OrderChanged { get; set; }
        public bool PathingChanged { get; set; }
    }

    private sealed record SchedulerBinding(SchedulerDocument Document, JsonObject Target, string JsonPath, FieldDefinition Definition, string? SettingName = null, int DisplayOffset = 0)
    {
        public JsonElement Read()
        {
            var node = SettingName is null ? GetValue(Target, JsonPath) : (GetValue(Target, JsonPath) as JsonObject)?[SettingName];
            if (node is null) return JsonSerializer.Deserialize<JsonElement>(Definition.DefaultJson);
            if (DisplayOffset != 0 && node is JsonValue value && value.TryGetValue<int>(out var index)) return JsonSerializer.SerializeToElement(index + DisplayOffset);
            return JsonSerializer.SerializeToElement(node, RemoteJson.Options);
        }
        public EditableFieldDto ToDto() => Definition.ToDto(Read());
        public void Apply(JsonElement value)
        {
            Definition.Validate(value);
            if (JsonNode.DeepEquals(JsonNode.Parse(Read().GetRawText()), JsonNode.Parse(value.GetRawText()))) return;
            var updated = DisplayOffset == 0 ? JsonNode.Parse(value.GetRawText()) : JsonValue.Create(value.GetInt32() - DisplayOffset);
            if (SettingName is null) SetValue(Target, JsonPath, updated);
            else
            {
                var settings = GetValue(Target, JsonPath) as JsonObject;
                if (settings is null) { settings = new JsonObject(); SetValue(Target, JsonPath, settings); }
                settings[SettingName] = updated;
            }
            Document.Changed = true;
            if (Definition.Path.Contains(".config.pathingConfig.", StringComparison.Ordinal)) Document.PathingChanged = true;
            if (DisplayOffset != 0) Document.OrderChanged = true;
        }
    }
}
