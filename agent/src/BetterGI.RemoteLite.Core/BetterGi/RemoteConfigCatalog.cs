using System.Text.Json;
using System.Text.Json.Nodes;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.BetterGi;

public sealed partial class RemoteConfigStore
{
    // Reviewed against BetterGI 0.66.0. Only keys actually persisted by the installed
    // version are offered. Unknown config data is retained, never reflected blindly.
    private static IEnumerable<FieldDefinition> CreateDesktopDefinitions()
    {
        var fields = new List<FieldDefinition>();
        void ToggleField(string group, string path, string label) => fields.Add(Toggle("global." + path, "global", group, label, "false", "与电脑端共用，保存后重新加载 BetterGI 生效。") with { ExistingOnly = true });
        void TextField(string group, string path, string label) => fields.Add(Text("global." + path, "global", group, label, "\"\"") with { ExistingOnly = true });
        void NumberField(string group, string path, string label, double min, double max, bool integer = true) => fields.Add(Number("global." + path, "global", group, label, "0", min, max) with { ExistingOnly = true, IntegerOnly = integer });
        void SelectField(string group, string path, string label, params string[] options) => fields.Add(Select("global." + path, "global", group, label, JsonSerializer.Serialize(options[0]), options) with { ExistingOnly = true });
        void Toggles(string group, string prefix, params (string Key, string Label)[] items)
        {
            foreach (var (key, label) in items) ToggleField(group, prefix + "." + key, label);
        }
        void Texts(string group, string prefix, params (string Key, string Label)[] items)
        {
            foreach (var (key, label) in items) TextField(group, prefix + "." + key, label);
        }

        SelectField("运行与捕获", "captureMode", "画面捕获方式", "BitBlt", "WindowsGraphicsCapture");
        NumberField("运行与捕获", "triggerInterval", "画面检测间隔（毫秒）", 10, 2000);
        ToggleField("运行与捕获", "autoFixWin11BitBlt", "自动修复 Windows 11 画面捕获");
        ToggleField("运行与捕获", "detailedErrorLogs", "记录详细错误日志");
        Toggles("通用设置", "commonConfig", ("screenshotEnabled", "启用截图"), ("screenshotUidCoverEnabled", "截图遮盖 UID"),
            ("rewardRecognitionScreenshotEnabled", "保存奖励识别截图"), ("exitToTray", "关闭窗口时最小化到托盘"),
            ("mainBackgroundEnabled", "启用电脑端背景图"), ("redeemCodeCnFeedsNotificationEnabled", "国服兑换码提醒"),
            ("redeemCodeGlobalFeedsNotificationEnabled", "国际服兑换码提醒"));
        NumberField("通用设置", "commonConfig.mainBackgroundOpacity", "背景图不透明度", 0, 1, false);
        NumberField("通用设置", "commonConfig.currentThemeType", "电脑端主题（0–2 深色，3–5 浅色）", 0, 5);
        Toggles("自动拾取", "autoPickConfig", ("enabled", "启用自动拾取"), ("fastModeEnabled", "急速拾取（忽略识别结果）"),
            ("blacklistModePickEnabled", "黑名单模式启用拾取规则"), ("whitelistModeDoNotPickEnabled", "白名单模式启用不拾取规则"),
            ("whiteListEnabled", "启用旧版拾取白名单"));
        SelectField("自动拾取", "autoPickConfig.mode", "拾取名单模式", "Whitelist", "Blacklist");
        SelectField("自动拾取", "autoPickConfig.ocrEngine", "文字识别引擎", "Paddle", "Yap");
        TextField("自动拾取", "autoPickConfig.pickKey", "拾取按键");
        NumberField("自动拾取", "autoPickConfig.itemIconLeftOffset", "物品图标左边界", 0, 1920);
        NumberField("自动拾取", "autoPickConfig.itemTextLeftOffset", "物品文字左边界", 0, 1920);
        NumberField("自动拾取", "autoPickConfig.itemTextRightOffset", "物品文字右边界", 0, 3840);
        Toggles("自动剧情", "autoSkipConfig", ("enabled", "启用自动剧情"), ("quicklySkipConversationsEnabled", "快速跳过对话"),
            ("autoWaitDialogueOptionVoiceEnabled", "等待语音结束再选择选项"), ("autoGetDailyRewardsEnabled", "自动领取委托奖励"),
            ("autoReExploreEnabled", "自动重新派遣"), ("customPriorityOptionsEnabled", "启用自定义优先选项"),
            ("autoHangoutEventEnabled", "启用自动邀约"), ("autoHangoutPressSkipEnabled", "邀约自动点击跳过"),
            ("runBackgroundEnabled", "后台运行剧情"), ("bringGameToFrontAfterBackgroundDialogEnabled", "后台剧情结束切回游戏"),
            ("submitGoodsEnabled", "自动提交物品"), ("pictureInPictureEnabled", "失去焦点时显示画中画"),
            ("closePopupPagedEnabled", "自动关闭弹出层"), ("skipBuiltInClickOptions", "脚本调用时跳过内置点击选项"));
        SelectField("自动剧情", "autoSkipConfig.clickChatOption", "剧情选项选择策略", "优先选择第一个选项", "随机选择选项", "不选择选项");
        SelectField("自动剧情", "autoSkipConfig.pictureInPictureSourceType", "画中画图像来源", "TriggerDispatcher", "CaptureLoop");
        Texts("自动剧情", "autoSkipConfig", ("customPriorityOptions", "优先选项（每行一个）"), ("autoHangoutEndChoose", "邀约分支"));
        foreach (var (key, label) in new[] { ("afterChooseOptionSleepDelay", "选择选项后等待（毫秒）"), ("beforeClickConfirmDelay", "点击对话前等待（毫秒）"), ("autoHangoutChooseOptionSleepDelay", "邀约选项等待（毫秒）") })
            NumberField("自动剧情", "autoSkipConfig." + key, label, 0, 60000);
        NumberField("自动剧情", "autoSkipConfig.dialogueOptionVoiceMaxWaitSeconds", "最多等待语音（秒）", 1, 300);
        Toggles("自动钓鱼", "autoFishingConfig", ("enabled", "启用自动钓鱼"), ("autoThrowRodEnabled", "自动抛竿"));
        NumberField("自动钓鱼", "autoFishingConfig.autoThrowRodTimeOut", "未上钩等待时间（秒）", 1, 600);
        NumberField("自动钓鱼", "autoFishingConfig.wholeProcessTimeoutSeconds", "整次钓鱼超时（秒）", 30, 86400);
        NumberField("自动钓鱼", "autoFishingConfig.fishingTimePolicy", "昼夜策略（0 全天，1 白天，2 夜晚）", 0, 2);
        Toggles("快速传送", "quickTeleportConfig", ("enabled", "启用快速传送"), ("hotkeyTpEnabled", "启用快捷键传送"));
        NumberField("快速传送", "quickTeleportConfig.teleportListClickDelay", "选择传送点间隔（毫秒）", 0, 10000);
        NumberField("快速传送", "quickTeleportConfig.waitTeleportPanelDelay", "传送面板等待（毫秒）", 0, 10000);
        Toggles("自动伐木", "autoWoodConfig", ("woodCountOcrEnabled", "识别木材数量"), ("useWonderlandRefresh", "通过千星奇域刷新冷却"));
        NumberField("自动伐木", "autoWoodConfig.afterZSleepDelay", "使用小道具后额外等待（毫秒）", 0, 60000);
        Toggles("自动吃药", "autoEatConfig", ("enabled", "启用自动吃药"), ("showNotification", "显示吃药通知"));
        NumberField("自动吃药", "autoEatConfig.checkInterval", "状态检测间隔（毫秒）", 50, 60000);
        NumberField("自动吃药", "autoEatConfig.eatInterval", "吃药间隔（毫秒）", 100, 600000);
        Texts("自动吃药", "autoEatConfig", ("defaultAtkBoostingDishName", "攻击类料理"), ("defaultAdventurersDishName", "冒险类料理"), ("defaultDefBoostingDishName", "防御类料理"));
        NumberField("自动烹饪", "autoCookConfig.checkIntervalMs", "烹饪检测间隔（毫秒）", 1, 1000);
        ToggleField("自动烹饪", "autoCookConfig.stopTaskWhenRecoverButtonDetected", "发现返回按钮后停止");
        SelectField("圣遗物分解", "autoArtifactSalvageConfig.maxArtifactStar", "快速分解最高星级", "1", "2", "3", "4");
        NumberField("圣遗物分解", "autoArtifactSalvageConfig.maxNumToCheck", "最多检查圣遗物数量", 1, 2000);
        NumberField("圣遗物分解", "autoArtifactSalvageConfig.recognitionFailurePolicy", "识别失败策略（0 跳过，1 中止）", 0, 1);
        ToggleField("自动演奏", "autoMusicGameConfig.mustCanorusLevel", "达到大音天籁评级");
        TextField("自动演奏", "autoMusicGameConfig.musicLevel", "乐曲难度");
        NumberField("脚本与调度", "scriptConfig.autoUpdateScriptRepoPeriod", "脚本仓库更新周期（天）", 0, 365);
        Toggles("脚本与调度", "scriptConfig", ("autoUpdateSubscribedScripts", "启动时更新已订阅脚本"), ("autoUpdateBeforeCommandLineRun", "运行前更新已订阅脚本"));
        SelectField("地图追踪", "pathingConditionConfig.mapMatchingMethod", "地图匹配方式", "TemplateMatch", "FeatureMatch");
        ToggleField("地图追踪", "pathingConditionConfig.onlyInTeleportRecover", "仅传送时恢复血量（旧版）");
        NumberField("地图追踪", "pathingConditionConfig.recoverTiming", "低血量恢复时机（0 任意节点，1 传送点，2 不恢复）", 0, 2);
        NumberField("地图追踪", "pathingConditionConfig.useGadgetIntervalMs", "使用小道具间隔（毫秒）", 0, 3600000);
        ToggleField("地图追踪", "pathingConditionConfig.autoEatEnabled", "地图追踪时自动吃药");
        AddFightCatalog(fields, "global", "autoFightConfig", "自动战斗", existingOnly: true);
        Texts("独立首领讨伐", "autoBossConfig", ("bossName", "首领名称"), ("strategyName", "战斗策略"), ("teamName", "战斗队伍"));
        Toggles("独立首领讨伐", "autoBossConfig", ("specifyRunCount", "指定讨伐次数"), ("useTransientResin", "允许使用须臾树脂"), ("useFragileResin", "允许使用脆弱树脂"), ("returnToStatueAfterEachRound", "每轮后返回七天神像"), ("rewardRecognitionEnabled", "识别奖励数量"));
        NumberField("独立首领讨伐", "autoBossConfig.runCount", "讨伐次数", 1, 9999);
        NumberField("独立首领讨伐", "autoBossConfig.reviveRetryCount", "复活重试次数", 0, 99);
        NumberField("独立首领讨伐", "autoBossConfig.timeout", "战斗超时（秒）", 10, 3600);
        Texts("独立秘境任务", "autoDomainConfig", ("partyName", "队伍名称"), ("domainName", "秘境名称"));
        Toggles("独立秘境任务", "autoDomainConfig", ("shortMovement", "挑战开始前小范围移动"), ("walkToF", "走近秘境挑战启动位置"), ("autoEat", "自动吃药"));
        NumberField("独立秘境任务", "autoDomainConfig.fightEndDelay", "战斗结束后等待（秒）", 0, 120, false);
        NumberField("独立秘境任务", "autoDomainConfig.leftRightMoveTimes", "左右移动次数", 0, 100);
        NumberField("独立秘境任务", "autoDomainConfig.originalResin20UseCount", "20 树脂领奖次数", 0, 999);
        NumberField("独立秘境任务", "autoDomainConfig.originalResin40UseCount", "40 树脂领奖次数", 0, 999);
        TextField("自动七圣召唤", "autoGeniusInvokationConfig.strategyName", "七圣召唤策略");
        NumberField("自动七圣召唤", "autoGeniusInvokationConfig.sleepDelay", "出牌额外等待（毫秒）", 0, 60000);
        NumberField("自动七圣召唤", "autoGeniusInvokationConfig.activeCharacterCardSpace", "出战角色卡牌偏移", 0, 300);
        Toggles("遮罩与显示", "maskWindowConfig", ("directionsEnabled", "显示方位"), ("displayRecognitionResultsOnMask", "显示识别结果"),
            ("maskEnabled", "显示遮罩"), ("showLogBox", "显示日志"), ("showStatus", "显示状态"), ("uidCoverEnabled", "遮盖 UID"),
            ("crosshairEnabled", "显示准星"), ("showFps", "显示帧率"), ("showOverlayMetrics", "显示指标栏"),
            ("overlayScalingEnabled", "缩放遮罩界面"), ("logShadowEnabled", "日志文字阴影"), ("statusShadowEnabled", "状态文字阴影"),
            ("metricsShadowEnabled", "指标文字阴影"), ("directionShadowEnabled", "方位文字阴影"), ("recognitionUseDrawableStyle", "自定义识别框样式"));
        NumberField("遮罩与显示", "maskWindowConfig.textOpacity", "遮罩文字透明度", 0, 1, false);
        NumberField("遮罩与显示", "maskWindowConfig.logFontScale", "日志缩放倍数", 0.5, 3, false);
        NumberField("遮罩与显示", "maskWindowConfig.metricsFontScale", "指标栏缩放倍数", 0.5, 3, false);
        NumberField("遮罩与显示", "maskWindowConfig.crosshairSize", "准星大小", 1, 200);
        NumberField("遮罩与显示", "maskWindowConfig.crosshairLineWidth", "准星线宽", 1, 30);
        NumberField("遮罩与显示", "maskWindowConfig.crosshairGap", "准星中心间隔", 0, 100);
        foreach (var (key, label) in new[] { ("logFontSize", "日志字号"), ("statusFontSize", "状态字号"), ("metricsFontSize", "指标字号"), ("directionFontSize", "方位字号"), ("recognitionTextFontSize", "识别文字字号") })
            NumberField("遮罩与显示", "maskWindowConfig." + key, label, 6, 96, false);
        // Credentials, local paths, shell/JavaScript code, HTTP grants, hotkey reserved
        // for cancellation, and arbitrary object trees never become remote fields.
        return fields;
    }

    private static void AddFightCatalog(List<FieldDefinition> fields, string scope, string prefix, string group, bool existingOnly)
    {
        void Add(FieldDefinition definition) => fields.Add(definition with { ExistingOnly = existingOnly });
        foreach (var (key, label) in new[] { ("strategyName", "战斗策略"), ("teamNames", "强制指定队伍角色"), ("actionSchedulerByCd", "技能冷却出招配置"), ("kazuhaPartyName", "万叶拾取队伍"), ("guardianAvatar", "护盾角色") })
            Add(Text(scope + "." + prefix + "." + key, scope, group, label, "\"\""));
        foreach (var (key, label) in new[] { ("fightFinishDetectEnabled", "检测战斗结束"), ("pickDropsAfterFightEnabled", "战后扫描掉落物"), ("kazuhaPickupEnabled", "万叶拾取"), ("qinDoublePickUp", "琴双重拾取"), ("guardianCombatSkip", "跳过护盾角色战斗"), ("guardianAvatarHold", "长按护盾技能"), ("burstEnabled", "启用元素爆发拾取"), ("swimmingEnabled", "允许游泳拾取"), ("expBasedPickupEnabled", "按经验值判断战后拾取"), ("enableCombatTargeting", "战斗中持续索敌"), ("drawRecognitionResults", "显示战斗识别结果") })
            Add(Toggle(scope + "." + prefix + "." + key, scope, group, label, "false"));
        Add(Number(scope + "." + prefix + ".timeout", scope, group, "战斗超时（秒）", "120", 10, 3600));
        Add(Number(scope + "." + prefix + ".pickDropsAfterFightSeconds", scope, group, "战后拾取时长（秒）", "15", 0, 600));
        Add(Number(scope + "." + prefix + ".targetingDetectionInterval", scope, group, "索敌间隔（毫秒）", "50", 10, 5000));
        Add(Number(scope + "." + prefix + ".lockLostWaitTime", scope, group, "脱锁等待（秒）", "0.5", 0, 60) with { IntegerOnly = false });
        Add(Select(scope + "." + prefix + ".onlyPickEliteDropsMode", scope, group, "精英怪拾取策略", "\"Closed\"", ["Closed", "AllowAutoPickupForNonElite", "DisableAutoPickupForNonElite"]));
        foreach (var (key, label) in new[] { ("fastCheckEnabled", "快速检查战斗结束"), ("rotateFindEnemyEnabled", "旋转寻找敌人"), ("checkAfterSwitchAvatar", "切人后检查战斗结束"), ("checkBeforeBurst", "元素爆发前检查"), ("skipFightEndCheckWhenEnemyVisible", "敌人可见时跳过结束检查"), ("paimonEndCheckEnabled", "派蒙辅助结束检测") })
            Add(Toggle(scope + "." + prefix + ".finishDetectConfig." + key, scope, group, label, "false"));
        foreach (var (key, label) in new[] { ("fastCheckParams", "快速检查参数"), ("checkEndDelay", "结束检测延迟配置"), ("beforeDetectDelay", "队伍界面检测延迟配置") })
            Add(Text(scope + "." + prefix + ".finishDetectConfig." + key, scope, group, label, "\"\""));
        Add(Number(scope + "." + prefix + ".finishDetectConfig.rotaryFactor", scope, group, "旋转寻找敌人速度", "12", 1, 13));
        Add(Number(scope + "." + prefix + ".finishDetectConfig.blockCheckBeforeBattleSeconds", scope, group, "开战后暂缓结束检查（秒）", "0", 0, 600) with { IntegerOnly = false });
        Add(Number(scope + "." + prefix + ".finishDetectConfig.paimonEndCheckDelay", scope, group, "派蒙辅助检测延迟（秒）", "0.2", 0, 10) with { IntegerOnly = false });
    }

    private static void ValidateConfigurationRelations(JsonObject global, SchedulerSnapshot scheduler, bool validateGlobalFight)
    {
        if (validateGlobalFight) ValidateFightRelations(GetValue(global, "autoFightConfig") as JsonObject);
        foreach (var document in scheduler.Documents)
        {
            if (!document.PathingChanged) continue;
            var config = GetValue(document.Root, "config.pathingConfig") as JsonObject;
            ValidateFightRelations(config is null ? null : GetValue(config, "autoFightConfig") as JsonObject);
            if (config is not null)
            {
                var distance = ReadNumber(config, "distance", 45);
                if (ReadNumber(config, "approachStopDistance", 25) > distance)
                    throw new InvalidDataException("接近停止距离不能超过赶路临界距离。");
                if (ReadNumber(config, "mwkJumpFlyDistance", 75) <= distance)
                    throw new InvalidDataException("跳飞启用距离必须大于赶路临界距离。");
                var cycle = ReadNumber(config, "taskCycleConfig.cycle", 1);
                if (ReadNumber(config, "taskCycleConfig.index", 1) > cycle)
                    throw new InvalidDataException("执行周期序号不能超过周期天数。");
            }
        }
    }

    private static void ValidateFightRelations(JsonObject? config)
    {
        if (config is null) return;
        if (GetValue(config, "finishDetectConfig.rotateFindEnemyEnabled")?.ToString() == "true" &&
            GetValue(config, "finishDetectConfig.skipFightEndCheckWhenEnemyVisible")?.ToString() == "true")
            throw new InvalidDataException("旋转寻找敌人与敌人可见时跳过结束检查不能同时开启。");
    }

    private static double ReadNumber(JsonObject root, string path, double fallback)
        => GetValue(root, path) is JsonValue value && value.TryGetValue<double>(out var number) ? number : fallback;
}
