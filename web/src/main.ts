import {html, nothing, render, TemplateResult} from 'lit';
import '@material/web/button/filled-button.js';
import '@material/web/button/outlined-button.js';
import '@material/web/button/text-button.js';
import '@material/web/checkbox/checkbox.js';
import '@material/web/progress/linear-progress.js';
import '@material/web/select/outlined-select.js';
import '@material/web/select/select-option.js';
import '@material/web/switch/switch.js';
import '@material/web/textfield/outlined-text-field.js';
import Sortable, {type SortableEvent} from 'sortablejs';
import {version as appVersion} from '../package.json';
import {base64UrlDecode, derivePairingRecord} from './crypto';
import {RemoteClient, RemoteRpcError} from './client';
import {clearPairing, loadPairing, persistentStorageState, requestPersistentStorage, savePairing} from './storage';
import {TabCoordinator} from './tab-coordinator';
import {reorderTasks} from './task-order';
import {readFullConfig} from './config-transfer';
import {readFullReport} from './report-transfer';
import {activeRunStates, canStart, canSave, readableFailure} from './view-state';
import {AgentStatus, ConnectionState, EditableField, PairingRecord, PushMessage, RemoteConfig, RunProgress, RunReport, ViewName} from './types';
import {icon} from './ui-icons';
import {UiMotion} from './ui-motion';
import './styles.css';

type BusyAction = 'save' | 'start' | 'stop' | 'sync' | 'storage' | 'install';
type NoticeTone = 'success' | 'error' | 'info';

interface InstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{outcome: 'accepted' | 'dismissed'}>;
}

const ENTRY_GUIDE_KEY = 'bettergi-remote-entry-guide-dismissed';
const PRODUCTION_ORIGIN = 'https://bgiremote.163831.xyz';

class App {
  private motion?: UiMotion;
  private previewTimer?: number;
  private get previewMode(): boolean { return import.meta.env.DEV && new URLSearchParams(location.search).has('demo'); }

  /** Dev-only visual rehearsal. This never creates a RemoteClient or executes an RPC. */
  private runUiAction(action: 'save' | 'start' | 'stop' | 'sync', realAction: () => Promise<void>): void {
    if (!this.previewMode) { void realAction(); return; }
    if (action === 'sync') { this.showNotice('演示配置已同步；没有读取真实电脑', 'info'); return; }
    if (action === 'save') {
      this.loading = true; this.busyAction = 'save'; this.draw();
      window.setTimeout(() => {
        this.loading = false; this.busyAction = undefined;
        this.dirtyFields.clear(); this.draftInputs.clear(); this.tasksDirty = false;
        this.showNotice('演示修改已保存；没有写入电脑配置', 'info');
      }, 650);
      return;
    }
    if (!this.status || !this.config) return;
    if (this.previewTimer) window.clearInterval(this.previewTimer);
    if (action === 'stop') {
      this.status = {...this.status, state: 'stopping'};
      if (this.progress) this.progress = {...this.progress, state: 'stopping'};
      this.draw();
      window.setTimeout(() => {
        this.status = {...this.status!, state: 'idle', activeRunId: undefined, gameRunning: false};
        this.progress = undefined;
        this.showNotice('演示任务已停止；没有发送真实快捷键', 'info');
      }, 800);
      return;
    }
    const tasks = this.config.tasks.filter(task => task.enabled).slice(0, 3);
    const startedAt = new Date().toISOString();
    let completed = 0;
    const update = () => {
      if (completed >= tasks.length) {
        window.clearInterval(this.previewTimer); this.previewTimer = undefined;
        this.status = {...this.status!, state: 'idle', activeRunId: undefined, gameRunning: false};
        this.progress = undefined;
        if (this.report) this.report = {...this.report, status: 'success', startedAt, finishedAt: new Date().toISOString(), durationSeconds: 7,
          tasks: tasks.map(task => ({name: task.name, state: 'success' as const, step: '演示完成'})), errors: [], lootCoverage: '仅用于界面演示：物品数量为预设示例，不是实际任务收获。'};
        this.showNotice('演示完成，可以查看示例报告；没有启动游戏', 'info');
        return;
      }
      this.status = {...this.status!, state: 'running', activeRunId: 'ui-demo-run', gameRunning: true};
      this.progress = {runId: 'ui-demo-run', state: 'running', currentTask: tasks[completed]!.name,
        currentStep: ['正在进入任务', '正在执行任务步骤', '正在读取演示结果'][completed % 3],
        currentLocation: '演示数据 · 不控制真实电脑', completedTasks: completed, totalTasks: tasks.length, observedAt: new Date().toISOString(),
        tasks: tasks.map((task, index) => ({name: task.name, state: index < completed ? 'success' as const : index === completed ? 'running' as const : 'pending' as const}))};
      this.draw();
    };
    update();
    this.previewTimer = window.setInterval(() => { completed++; update(); }, 2400);
  }

  private pairing?: PairingRecord;
  private client?: RemoteClient;
  private connection: ConnectionState = 'unpaired';
  private view: ViewName = 'home';
  private status?: AgentStatus;
  private config?: RemoteConfig;
  private report?: RunReport;
  private progress?: RunProgress;
  private loading = false;
  private busyAction?: BusyAction;
  private error?: string;
  private notice?: {message: string; tone: NoticeTone};
  private noticeTimer?: number;
  private sortable?: Sortable;
  private taskListDomReordered = false;
  private tabCoordinator?: TabCoordinator;
  private installPrompt?: InstallPromptEvent;
  private entryGuideDismissed = readLocalFlag(ENTRY_GUIDE_KEY);
  private storageState: 'checking' | 'persistent' | 'managed' | 'unsupported' = 'checking';
  private readonly openFieldGroups = new Set<string>(['oneDragon:自动秘境']);
  private readonly draftInputs = new Map<string, string>();
  private configScope: 'oneDragon' | 'global' | 'scriptGroup' = 'oneDragon';
  private configSearch = '';
  private readonly dirtyFields = new Set<string>();
  private tasksDirty = false;
  private configReadProgress?: {read: number; total: number};

  private get hasDraft(): boolean { return this.tasksDirty || this.dirtyFields.size > 0; }

  private async loadConfig(initial?: RemoteConfig): Promise<RemoteConfig> {
    try {
      return normalizeConfig(await readFullConfig(params => this.client!.call<RemoteConfig>('config.get', params), initial, (read, total) => {
        this.configReadProgress = {read, total};
        this.draw();
      }));
    } finally { this.configReadProgress = undefined; }
  }

  private async loadReport(initial?: RunReport): Promise<RunReport | undefined> {
    return readFullReport(params => this.client!.call<RunReport | undefined>('report.latest', params), initial);
  }

  private acceptConfig(config: RemoteConfig): void {
    this.config = config;
    this.dirtyFields.clear();
    this.tasksDirty = false;
    this.draftInputs.clear();
  }

  constructor(private readonly root: HTMLElement) {
    window.addEventListener('beforeinstallprompt', event => {
      event.preventDefault();
      this.installPrompt = event as InstallPromptEvent;
      this.draw();
    });
    window.addEventListener('appinstalled', () => {
      this.installPrompt = undefined;
      this.entryGuideDismissed = true;
      writeLocalFlag(ENTRY_GUIDE_KEY);
      this.showNotice('已添加到主屏幕，以后直接点图标即可打开');
    });
  }

  async start(): Promise<void> {
    if (import.meta.env.DEV && new URLSearchParams(location.search).has('demo')) {
      this.loadVisualDemo();
      this.draw();
      return;
    }
    await this.importPairingFromFragment();
    this.pairing = await loadPairing();
    this.storageState = await persistentStorageState();
    if (!this.pairing) {
      this.connection = 'unpaired';
      this.draw();
      return;
    }
    this.createClient();
    this.tabCoordinator = new TabCoordinator(active => {
      if (active) this.client?.start();
      else this.client?.close();
    });
    this.draw();
  }

  private loadVisualDemo(): void {
    this.pairing = {deviceId: 'visualdemo123456', pcName: '旅行者的电脑'} as PairingRecord;
    this.connection = 'online';
    this.status = {
      pcName: '旅行者的电脑', pcDeviceId: 'visualdemo', phonePeerOnline: true, windowsUnlocked: true,
      betterGiConfigured: true, betterGiVersionSupported: true, betterGiVersion: '0.64.0', betterGiRunning: false,
      gameRunning: false, state: 'idle', observedAt: new Date().toISOString(), bindingDaysRemaining: 176,
      bindingExpiresAt: new Date(Date.now() + 176 * 86_400_000).toISOString(), agentVersion: appVersion,
      betterGiCompatibilityVerified: true, betterGiLatestVersion: '0.64.0', betterGiUpdateAvailable: false,
      capabilities: ['runningStart', 'desktopConfig', 'detailedProgress', 'lootSources', 'paging'],
    };
    this.config = {
      name: '远程每日', revision: 'demo', completionAction: '关闭游戏和软件', readAt: new Date().toISOString(), fields: [],
      tasks: ['领取邮件', '合成树脂', '自动秘境', '自动首领讨伐', '自动幽境危战', '自动地脉花', '领取每日奖励', '领取尘歌壶奖励', '朋友新增调度组']
        .map((name, order) => ({id: `demo-${order}`, name, enabled: order < 4, isCustom: order === 8, order})),
    };
    this.config.fields = [
      {path: 'oneDragon.partyName', scope: 'oneDragon', group: '自动秘境', label: '队伍名称', type: 'text', value: '日常队伍'},
      {path: 'oneDragon.domainName', scope: 'oneDragon', group: '自动秘境', label: '秘境名称', type: 'text', value: '忘却之峡'},
      {path: 'global.autoPickConfig.enabled', scope: 'global', group: '自动拾取', label: '启用自动拾取', type: 'toggle', value: true},
      {path: 'global.autoPickConfig.mode', scope: 'global', group: '自动拾取', label: '拾取名单模式', type: 'select', value: 'Blacklist', options: ['Blacklist', 'Whitelist']},
      {path: 'global.autoPickConfig.itemTextLeftOffset', scope: 'global', group: '自动拾取', label: '物品文字左边界', type: 'number', value: 120, minimum: 0, maximum: 1920},
      {path: 'global.autoFishingConfig.enabled', scope: 'global', group: '自动钓鱼', label: '启用自动钓鱼', type: 'toggle', value: false},
      {path: 'global.autoFishingConfig.autoThrowRodTimeOut', scope: 'global', group: '自动钓鱼', label: '未上钩等待时间（秒）', type: 'number', value: 20, minimum: 1, maximum: 600},
      {path: 'global.autoFightConfig.lockLostWaitTime', scope: 'global', group: '自动战斗', label: '脱锁等待（秒）', type: 'number', value: 0.5, minimum: 0, maximum: 60},
      {path: 'scriptGroup.demo.project.0.status', scope: 'scriptGroup', group: '调度器 · 每日采集组 / 1. 采集路线', label: '任务状态', type: 'select', value: 'Enabled', options: ['Enabled', 'Disabled']},
      {path: 'scriptGroup.demo.project.0.runNum', scope: 'scriptGroup', group: '调度器 · 每日采集组 / 1. 采集路线', label: '执行次数', type: 'number', value: 2, minimum: 1, maximum: 9999},
      {path: 'scriptGroup.demo.project.0.schedule', scope: 'scriptGroup', group: '调度器 · 每日采集组 / 1. 采集路线', label: '执行周期', type: 'select', value: 'Daily', options: ['Daily', 'EveryTwoDays', 'Monday']},
      {path: 'scriptGroup.demo.project.0.settings.region', scope: 'scriptGroup', group: '调度器 · 每日采集组 / 1. 采集路线', label: '采集地区', type: 'select', value: '蒙德', options: ['蒙德', '璃月']},
    ];
    const query = new URLSearchParams(location.search);
    const view = query.get('view');
    if (view === 'config' || view === 'reports' || view === 'settings') this.view = view;
    const scope = query.get('scope');
    if (scope === 'global' || scope === 'scriptGroup') this.configScope = scope;
    this.openFieldGroups.add('global:自动拾取');
    this.openFieldGroups.add('scriptGroup:调度器 · 每日采集组 / 1. 采集路线');
    this.report = {
      runId: 'demo-report', status: 'failed', startedAt: new Date(Date.now() - 720000).toISOString(), finishedAt: new Date().toISOString(), durationSeconds: 720,
      tasks: [{name: '自动秘境', state: 'success', step: '领取奖励'}, {name: '每日采集组', state: 'failed', step: '沿路线移动与采集', location: '清心采集路线 · 第 12 个路径点', message: '当前步骤等待超时，请检查角色是否卡住或路线是否可达。'}],
      rewards: {'摩拉': 12000, '自由的教导': 6}, dailyRewards: {'摩拉': 24000, '自由的教导': 12}, dailyRewardDate: '演示日期',
      pickupObservations: {'清心': 8, '薄荷': 5}, woodEstimates: {'杉木': 18},
      errors: ['每日采集组 / 清心采集路线 / 第 12 个路径点：当前步骤等待超时，请检查角色位置和游戏画面。'], logExcerpt: [], parserVersion: 'adaptive-v3',
      lootCoverage: '演示数据。未提供数量的采集记录单独展示，不计入确切奖励累计。',
    };
    this.status.betterGiRunning = true;
    if (query.get('scenario') === 'running') {
      this.status.state = 'running';
      this.status.activeRunId = 'demo-run';
      this.progress = {runId: 'demo-run', state: 'running', currentTask: '每日采集组', currentStep: '沿路线移动与采集', currentLocation: '清心采集路线 · 第 12 个路径点', completedTasks: 1, totalTasks: 3, observedAt: new Date().toISOString(), tasks: [{name: '领取邮件', state: 'success'}, {name: '每日采集组', state: 'running'}, {name: '自动秘境', state: 'pending'}]};
    }
  }

  private createClient(): void {
    if (!this.pairing) return;
    this.client = new RemoteClient(this.pairing, state => this.onConnection(state), message => this.onPush(message));
    this.client.start();
  }

  private async importPairingFromFragment(): Promise<void> {
    const match = location.hash.match(/^#pair=([A-Za-z0-9_-]+)$/);
    if (!match?.[1]) return;
    const previous = await loadPairing();
    const pairing = await derivePairingRecord(base64UrlDecode(match[1]), location.origin, previous?.deviceId);
    await savePairing(pairing);
    await requestPersistentStorage();
    history.replaceState(null, '', location.pathname + location.search);
  }

  private onConnection(state: ConnectionState): void {
    const becameOnline = state === 'online' && this.connection !== 'online';
    this.connection = state;
    this.draw();
    if (becameOnline) void this.connectToComputer();
  }

  private async connectToComputer(): Promise<void> {
    if (!this.client || !this.pairing) return;
    try {
      if (!this.pairing.pcDeviceId) {
        const paired = await this.client.call<{pcDeviceId: string; pcName: string; bound: boolean; bindingExpiresAt?: string}>('pair.request', {
          deviceId: this.pairing.deviceId,
          deviceLabel: mobileLabel(),
        }, 120_000);
        this.pairing = {...this.pairing, pcDeviceId: paired.pcDeviceId, pcName: paired.pcName, boundAt: new Date().toISOString(), bindingExpiresAt: paired.bindingExpiresAt};
        await savePairing(this.pairing);
      }
      await this.syncAll();
    } catch (error) {
      this.setError(error);
    }
  }

  private async syncAll(showFeedback = false): Promise<void> {
    if (!this.client) return;
    if (this.loading) return;
    if (this.hasDraft && !showFeedback) return;
    if (this.hasDraft && !confirm('同步电脑配置会替换手机上尚未保存的修改。确认重新读取吗？')) return;
    const feedbackStartedAt = performance.now();
    this.loading = true;
    this.busyAction = showFeedback ? 'sync' : undefined;
    this.error = undefined;
    this.draw();
    try {
      let usedLegacySync = false;
      const configRequest = showFeedback
        ? this.client.call<RemoteConfig>('config.sync', {limit: 32}, 90_000).then(config => this.loadConfig(config)).catch(error => {
            if (error instanceof RemoteRpcError && error.code === 'method_not_allowed') {
              usedLegacySync = true;
              return this.loadConfig();
            }
            throw error;
          })
        : this.loadConfig();
      const [status, config, report] = await Promise.all([
        this.client.call<AgentStatus>('status.get'),
        configRequest,
        this.loadReport(),
      ]);
      this.status = status;
      this.acceptConfig(config);
      this.progress = status.currentProgress ?? (activeRunStates.has(status.state) ? this.progress : undefined);
      this.report = report;
      if (status.bindingExpiresAt && this.pairing && this.pairing.bindingExpiresAt !== status.bindingExpiresAt) {
        this.pairing = {...this.pairing, bindingExpiresAt: status.bindingExpiresAt};
        await savePairing(this.pairing);
      }
      if (showFeedback) this.showNotice(usedLegacySync
        ? `已读取 ${config.tasks.length} 项任务；更新电脑端后可自动发现新增任务`
        : `已同步电脑配置，共 ${config.tasks.length} 项任务`);
    } catch (error) {
      if (!showFeedback) throw error;
      this.setError(error);
    } finally {
      if (showFeedback) await keepFeedbackVisible(feedbackStartedAt);
      this.loading = false;
      this.busyAction = undefined;
      this.draw();
    }
  }

  private onPush(message: PushMessage): void {
    if (message.event === 'status.changed') {
      this.status = message.data as AgentStatus;
      this.progress = this.status.currentProgress ?? (activeRunStates.has(this.status.state) ? this.progress : undefined);
    }
    if (message.event === 'run.progress') this.progress = message.data as RunProgress;
    if (message.event === 'run.completed') {
      this.report = message.data as RunReport;
      this.progress = undefined;
      void this.loadReport(this.report).then(report => { this.report = report; this.draw(); }).catch(error => this.setError(error));
      void this.refreshConfig();
      this.showNotice('任务已结束，执行报告已生成');
    }
    this.draw();
  }

  private async refreshConfig(): Promise<void> {
    if (!this.client || this.connection !== 'online' || this.hasDraft || this.loading) return;
    try {
      this.acceptConfig(await this.loadConfig());
      this.draw();
    } catch {
    }
  }

  private navigate(view: ViewName): void {
    this.view = view;
    this.error = undefined;
    this.draw();
  }

  private async saveConfig(): Promise<void> {
    if (!this.client || !this.config || this.loading) return;
    const invalid = this.config.fields.find(field => this.dirtyFields.has(field.path) && field.type === 'number' &&
      (typeof field.value !== 'number' || !Number.isFinite(field.value) || field.minimum != null && field.value < field.minimum || field.maximum != null && field.value > field.maximum));
    if (invalid) { this.setError(new Error(`请检查“${invalid.label}”的数值${invalid.minimum != null && invalid.maximum != null ? `，允许范围 ${invalid.minimum} 至 ${invalid.maximum}` : ''}。`)); return; }
    const feedbackStartedAt = performance.now();
    this.loading = true;
    this.busyAction = 'save';
    this.error = undefined;
    this.draw();
    try {
      const updated = await this.client.call<RemoteConfig>('config.update', {
        baseRevision: this.config.revision,
        tasks: this.config.tasks.map((task, order) => ({...task, order})),
        values: Object.fromEntries(this.config.fields.filter(field => this.dirtyFields.has(field.path)).map(field => [field.path, field.value])),
      }, 90_000);
      this.acceptConfig(await this.loadConfig(updated));
      this.showNotice('配置已安全保存到电脑');
    } catch (error) {
      if (error instanceof RemoteRpcError && error.code === 'config_conflict') {
        this.error = '电脑端配置已经变化。手机草稿已保留，请先同步最新配置，再重新确认你的修改。';
      } else {
        this.setError(error);
      }
    } finally {
      await keepFeedbackVisible(feedbackStartedAt);
      this.loading = false;
      this.busyAction = undefined;
      this.draw();
    }
  }

  private async startTask(): Promise<void> {
    if (!this.client || !this.config || this.loading) return;
    if (this.hasDraft) {
      this.error = '有尚未保存的任务或参数，请先保存到电脑，再启动任务。';
      this.draw();
      return;
    }
    const enabled = this.config.tasks.filter(task => task.enabled).map(task => task.name);
    if (enabled.length === 0) {
      this.error = '请至少启用一个任务。';
      this.draw();
      return;
    }
    if (!confirm(`确认启动“${this.config.name}”吗？\n\n${enabled.join('、')}\n\nBetterGI 未启动时会自动打开；已启动时会自动应用配置后执行。正常完成后将关闭原神和 BetterGI。`)) return;
    await this.action('start', '启动指令已被电脑接受', async () => {
      const accepted = await this.client!.call<{runId: string}>('task.start', {expectedRevision: this.config!.revision, confirmed: true}, 45_000);
      if (this.progress?.runId !== accepted.runId) this.progress = {runId: accepted.runId, state: 'starting', currentStep: '正在让 BetterGI 应用配置并启动', completedTasks: 0, totalTasks: enabled.length, observedAt: new Date().toISOString()};
    });
  }

  private async stopTask(): Promise<void> {
    if (!this.client || !confirm('确认发送 BetterGI 取消任务快捷键吗？')) return;
    await this.action('stop', '', async () => {
      const result = await this.client!.call<{state: string; shortcutSent: boolean}>('task.stop', {runId: this.status?.activeRunId ?? this.progress?.runId ?? null}, 30_000);
      if (['idle', 'success', 'stopped', 'failed', 'warning'].includes(result.state)) {
        this.progress = undefined;
        if (this.status) this.status = {...this.status, state: 'idle', activeRunId: undefined, activeTask: undefined, currentProgress: undefined};
        this.showNotice(result.shortcutSent ? '任务已结束，执行报告已生成' : '任务已经结束，无需重复停止', 'info');
      } else {
        if (this.progress) this.progress = {...this.progress, state: 'stopping'};
        this.showNotice('停止请求已发送，正在等待 BetterGI 确认', 'info');
      }
    });
  }

  private async action(action: BusyAction, successMessage: string, operation: () => Promise<void>): Promise<void> {
    if (this.loading) return;
    const feedbackStartedAt = performance.now();
    this.loading = true;
    this.busyAction = action;
    this.error = undefined;
    this.draw();
    try {
      await operation();
      if (successMessage) this.showNotice(successMessage);
    } catch (error) {
      this.setError(error);
    } finally {
      await keepFeedbackVisible(feedbackStartedAt);
      this.loading = false;
      this.busyAction = undefined;
      this.draw();
    }
  }

  private updateTask(id: string, enabled: boolean): void {
    if (!this.config) return;
    const task = this.config.tasks.find(item => item.id === id);
    if (task) { task.enabled = enabled; this.tasksDirty = true; this.draw(); }
  }

  private moveTask(id: string, direction: number, event: KeyboardEvent): void {
    if (!event.altKey || !this.config || this.loading || !canSave(this.status, this.connection)) return;
    event.preventDefault();
    const index = this.config.tasks.findIndex(task => task.id === id);
    const next = index + direction;
    if (index < 0 || next < 0 || next >= this.config.tasks.length) return;
    this.config.tasks = reorderTasks(this.config.tasks, index, next);
    this.tasksDirty = true;
    this.draw();
    queueMicrotask(() => this.root.querySelector<HTMLButtonElement>(`[data-id="${CSS.escape(id)}"] .drag-handle`)?.focus());
  }

  private updateField(path: string, value: unknown): void {
    const field = this.config?.fields.find(item => item.path === path);
    if (field) { field.value = value; this.dirtyFields.add(path); this.draw(); }
  }

  private updateInputField(field: EditableField, event: Event): void {
    const raw = (event.currentTarget as HTMLInputElement).value;
    this.draftInputs.set(field.path, raw);
    this.updateField(field.path, field.type === 'number' && raw.trim() !== '' ? Number(raw) : raw);
  }

  private toggleMulti(field: EditableField, option: string, checked: boolean): void {
    const values = new Set(Array.isArray(field.value) ? field.value.map(String) : []);
    if (checked) values.add(option);
    else values.delete(option);
    field.value = [...values];
    this.dirtyFields.add(field.path);
    this.draw();
  }

  private async removePairing(): Promise<void> {
    if (!confirm('只清除这台手机浏览器中的绑定信息。电脑端仍需执行“重新绑定手机”才能生成新密钥。确认继续吗？')) return;
    this.tabCoordinator?.close();
    this.client?.close();
    await clearPairing();
    location.reload();
  }

  private async protectStorage(): Promise<void> {
    await this.action('storage', '已完成绑定数据保护检查', async () => {
      this.storageState = await requestPersistentStorage();
    });
  }

  private async copyEntry(): Promise<void> {
    const entry = isTemporaryOrigin() ? location.origin : PRODUCTION_ORIGIN;
    try {
      await copyText(entry);
      this.showNotice('控制入口已复制，可发送给文件传输助手保存');
    } catch (error) {
      this.setError(error);
    }
  }

  private async installApp(): Promise<void> {
    if (!this.installPrompt) {
      this.showNotice(isWechatBrowser()
        ? '请点击微信右上角“…”，选择收藏或发送给文件传输助手'
        : '请使用浏览器菜单中的“添加到主屏幕”', 'info');
      return;
    }
    this.loading = true;
    this.busyAction = 'install';
    this.draw();
    try {
      await this.installPrompt.prompt();
      const choice = await this.installPrompt.userChoice;
      if (choice.outcome === 'accepted') this.showNotice('正在添加到主屏幕');
      else this.showNotice('未添加，你仍可以随时从设置页重试', 'info');
      this.installPrompt = undefined;
    } finally {
      this.loading = false;
      this.busyAction = undefined;
      this.draw();
    }
  }

  private dismissEntryGuide(): void {
    this.entryGuideDismissed = true;
    writeLocalFlag(ENTRY_GUIDE_KEY);
    this.draw();
  }

  private showNotice(message: string, tone: NoticeTone = 'success'): void {
    if (this.noticeTimer) window.clearTimeout(this.noticeTimer);
    this.notice = {message, tone};
    this.draw();
    this.noticeTimer = window.setTimeout(() => {
      this.notice = undefined;
      this.noticeTimer = undefined;
      this.draw();
    }, tone === 'error' ? 6000 : 3500);
  }

  private setError(error: unknown): void {
    this.error = readableFailure(error instanceof Error ? error.message : '操作失败，请重新同步后再试');
    this.showNotice(this.error, 'error');
  }

  private draw(): void {
    this.sortable?.destroy();
    this.sortable = undefined;
    if (this.taskListDomReordered) {
      render(nothing, this.root);
      this.taskListDomReordered = false;
    }
    render(this.template(), this.root);
    this.motion ??= new UiMotion(this.root);
    this.motion.settle(this.view);
    if (this.view === 'config' && this.config) queueMicrotask(() => this.mountSortable());
  }

  private template(): TemplateResult {
    return html`
      <div class="app-shell">
        <header class="topbar">
          <div class="brand-lockup">
            <span class="brand-symbol">${icon('devices')}</span><div>
            <p class="brand">BetterGI Remote</p>
            <p class="computer-name">${this.pairing?.pcName ?? '等待绑定电脑'}${import.meta.env.DEV && new URLSearchParams(location.search).has('demo') ? ' · 演示数据' : ''}</p>
          </div></div>
          ${connectionBadge(this.connection)}
        </header>
        ${this.previewMode ? html`<div class="demo-ribbon">交互演示 · 模拟数据，不控制电脑或游戏</div>` : nothing}
        ${this.loading ? html`<md-linear-progress indeterminate aria-label="正在处理"></md-linear-progress>` : nothing}
        ${this.configReadProgress ? html`<p class="sync-progress" role="status">正在读取电脑设置：${this.configReadProgress.read} / ${this.configReadProgress.total} 项</p>` : nothing}
        <main data-view=${this.view} class=${this.view === 'config' ? 'content config-content' : 'content'}>
          ${this.error ? html`<section class="inline-error" role="alert">${this.error}</section>` : nothing}
          ${this.status && !this.status.capabilities?.includes('desktopConfig') ? html`<section class="binding-reminder"><strong>请更新电脑端 BetterGI Remote</strong><p>网页已升级，电脑端当前为 ${this.status.agentVersion ?? '旧版'}。请在电脑托盘中检查更新，安装 0.4.0 或更高版本后使用扩展配置、已打开时启动和详细报告。现有绑定仍可使用。</p></section>` : nothing}
          ${this.view === 'home' ? this.homeView() : nothing}
          ${this.view === 'config' ? this.configView() : nothing}
          ${this.view === 'reports' ? this.reportView() : nothing}
          ${this.view === 'settings' ? this.settingsView() : nothing}
        </main>
        ${this.notice ? html`<div class="action-notice ${this.notice.tone}" role=${this.notice.tone === 'error' ? 'alert' : 'status'} aria-live=${this.notice.tone === 'error' ? 'assertive' : 'polite'} aria-atomic="true">${this.notice.message}</div>` : nothing}
        ${this.pairing ? html`<nav class="bottom-nav" style=${`--active-tab: ${['home', 'config', 'reports', 'settings'].indexOf(this.view)}`} aria-label="主要页面">
          ${navButton('home', '首页', this.view, () => this.navigate('home'))}
          ${navButton('config', '配置', this.view, () => this.navigate('config'))}
          ${navButton('reports', '报告', this.view, () => this.navigate('reports'))}
          ${navButton('settings', '设置', this.view, () => this.navigate('settings'))}
        </nav>` : nothing}
      </div>`;
  }

  private homeView(): TemplateResult {
    if (!this.pairing) return emptyPairing();
    if (!this.status) return loadingState(this.connection === 'online' ? '正在读取电脑状态' : '等待电脑上线');
    const ready = canStart(this.status, this.connection);
    const running = activeRunStates.has(this.status.state) || Boolean(this.progress);
    return html`
      <section class="command-hero">
        <div class="hero-image" role="img" aria-label="原神角色在春日花园中庆祝的群像画面"></div>
        <div class="hero-shade"></div>
        <div class="hero-content">
          <h1>${running ? '远征正在进行' : ready ? '今日委托已就绪' : '等待终端就绪'}</h1>
          <p>${running ? this.progress?.currentTask ?? this.status.activeTask ?? '正在准备任务' : ready ? '电脑状态正常，可以从这里启程。' : this.status.message ?? statusReason(this.status)}</p>
          <div class="hero-signal"><span class="status-dot ${ready ? 'good' : running ? 'busy' : 'bad'}"></span>${this.connection === 'online' ? this.pairing.pcName ?? '已连接电脑' : '电脑连接中'}</div>
        </div>
        <small class="asset-credit">角色画面 © 米哈游 / HoYoverse</small>
      </section>
      <section class="action-panel">
        <div>
          <h2>${this.config?.name ?? '远程每日'}</h2>
          <p>${this.hasDraft ? '有未保存的修改，请先在配置页保存' : this.config ? `${this.config.tasks.filter(task => task.enabled).length} 项任务已启用` : '请先同步配置'}</p>
        </div>
        ${running
          ? html`<md-filled-button class="danger-button" ?disabled=${this.loading || this.connection !== 'online' || this.progress?.state === 'stopping' || this.status.state === 'stopping'} @click=${() => this.runUiAction('stop', () => this.stopTask())}>${icon('stop', 'icon')}${this.busyAction === 'stop' ? '正在发送…' : this.progress?.state === 'stopping' || this.status.state === 'stopping' ? '等待停止确认…' : '停止任务'}</md-filled-button>`
          : html`<md-filled-button ?disabled=${!ready || !this.config || this.loading || this.hasDraft} @click=${() => this.runUiAction('start', () => this.startTask())}>${icon('play', 'icon')}${this.busyAction === 'start' ? '正在启动…' : '确认并启动'}</md-filled-button>`}
      </section>
      ${this.bindingReminder()}
      ${this.status.betterGiUpdateAvailable ? html`<section class="binding-reminder"><strong>BetterGI 有新版本 ${this.status.betterGiLatestVersion}</strong><p>电脑当前为 ${this.status.betterGiVersion ?? '未知版本'}。更新 BetterGI 后，也请同步检查 BetterGI Remote 更新。</p></section>` : nothing}
      ${this.entryGuide()}
      ${this.progress ? html`
        <section class="run-panel">
          <h2>${this.progress.currentTask ?? '正在准备'}</h2>
          ${this.progress.currentStep ? html`<p aria-live="polite">${this.progress.currentStep}</p>` : nothing}
          ${this.progress.currentLocation ? html`<p class="progress-location">${this.progress.currentLocation}</p>` : nothing}
          <p>${this.progress.completedTasks} / ${this.progress.totalTasks} 项已完成</p>
          <md-linear-progress value=${this.progress.totalTasks ? this.progress.completedTasks / this.progress.totalTasks : 0}></md-linear-progress>
          ${this.progress.tasks?.length ? html`<ol class="run-task-list">${this.progress.tasks.map(task => html`<li class=${task.state}><span>${task.name}</span><strong>${statusText(task.state)}</strong>${task.message ? html`<small>${readableFailure(task.message)}</small>` : nothing}</li>`)}</ol>` : nothing}
        </section>` : nothing}
      <section class="status-panel" aria-label="电脑状态">
        <div class="status-primary">
          <span class="status-dot ${ready ? 'good' : running ? 'busy' : 'bad'}"></span>
          <div>
            <strong>${running ? '任务执行中' : ready ? '可以启动' : '暂时不能启动'}</strong>
            <p>${this.status.message ?? statusReason(this.status)}</p>
          </div>
        </div>
        <div class="metric-grid">
          ${metric('Windows', this.status.windowsUnlocked ? '未锁屏' : '已锁屏')}
          ${metric('BetterGI', this.status.betterGiRunning ? '运行中' : '已关闭')}
          ${metric('原神', this.status.gameRunning ? '运行中' : '已关闭')}
          ${metric('版本', this.status.betterGiVersion ?? '未知')}
        </div>
      </section>
`;
  }

  private bindingReminder(): TemplateResult | typeof nothing {
    const remaining = this.status?.bindingDaysRemaining ?? daysUntil(this.pairing?.bindingExpiresAt);
    if (remaining === undefined || remaining > 14) return nothing;
    const expired = remaining <= 0;
    return html`<section class="binding-reminder ${expired ? 'expired' : ''}" role=${expired ? 'alert' : 'status'}>
      <div><strong>${expired ? '手机绑定已过期' : `绑定将在 ${remaining} 天后到期`}</strong><p>${expired ? '请在电脑托盘中选择“显示手机绑定二维码”并重新扫码。' : '保持电脑和手机正常连接会自动续期，无需手动操作。'}</p></div>
    </section>`;
  }

  private entryGuide(): TemplateResult | typeof nothing {
    if (this.entryGuideDismissed || isStandaloneMode()) return nothing;
    const temporary = isTemporaryOrigin();
    const wechat = isWechatBrowser();
    const title = temporary ? '当前是临时测试入口' : wechat ? '保存好下次打开的入口' : '把控制端放到主屏幕';
    const copy = temporary
      ? '本轮 trycloudflare 地址会在测试结束后失效。正式上线后将使用固定域名，日常打开无需再扫码。'
      : wechat
        ? '点击微信右上角“…”收藏页面，或复制入口发给文件传输助手。以后在同一个微信中打开即可恢复绑定。'
        : '添加到主屏幕后，以后像 App 一样点图标打开，无需再扫码。';
    return html`<section class="entry-guide" aria-label="保存手机控制入口">
      <div class="guide-portrait" role="img" aria-label="派蒙挥手提示"></div>
      <div><h2>${title}</h2><p>${copy}</p></div>
      <div class="entry-guide-actions">
        ${!temporary && this.installPrompt ? html`<md-filled-button ?disabled=${this.loading} @click=${() => void this.installApp()}>${this.busyAction === 'install' ? '正在添加…' : '添加到主屏幕'}</md-filled-button>` : nothing}
        <md-outlined-button @click=${() => void this.copyEntry()}>复制日常入口</md-outlined-button>
        <md-text-button @click=${() => this.dismissEntryGuide()}>${temporary ? '了解' : '我已保存'}</md-text-button>
      </div>
    </section>`;
  }

  private configView(): TemplateResult {
    if (!this.pairing) return emptyPairing();
    if (!this.config) return loadingState(this.connection === 'online' ? '正在读取远程配置' : '电脑上线后才能读取配置');
    const query = this.configSearch.trim().toLocaleLowerCase();
    const fields = this.config.fields.filter(field => field.scope === this.configScope && (!query || `${field.group} ${field.label} ${field.description ?? ''}`.toLocaleLowerCase().includes(query)));
    const grouped = new Map<string, EditableField[]>();
    for (const field of fields) {
      const group = grouped.get(field.group) ?? [];
      group.push(field);
      grouped.set(field.group, group);
    }
    const groups = [...grouped.keys()];
    const editable = canSave(this.status, this.connection) && !this.loading;
    return html`
      <section class="page-heading">
        <h1>电脑配置</h1>
        <p>任务、参数与脚本调度</p>
      </section>
      <section class="config-controls" aria-label="配置分类与搜索">
        <div class="config-tabs" role="group" aria-label="配置分类">
          ${([['oneDragon', '一条龙'], ['global', '全局设置'], ['scriptGroup', '脚本调度']] as const).map(([scope, label]) => html`<button type="button" aria-pressed=${scope === this.configScope} @click=${() => { this.configScope = scope; this.draw(); }}>${label}<small>${this.config!.fields.filter(field => field.scope === scope).length}</small></button>`)}
        </div>
        <label class="config-search"><span>查找设置</span><input type="search" placeholder="例如：拾取、钓鱼、秘境、队伍" .value=${this.configSearch} @input=${(event: Event) => { this.configSearch = (event.currentTarget as HTMLInputElement).value; this.draw(); }}></label>
        <details class="context-help config-help"><summary>保存与生效规则</summary><p>${this.config.applicationNotice ?? '保存时会自动让空闲的 BetterGI 重新载入配置。任务执行中不能修改配置。'}</p></details>
        ${this.hasDraft ? html`<p class="draft-note" role="status">手机有未保存的修改，保存后才会用于下一次任务。</p>` : nothing}
        <md-text-button ?disabled=${this.loading || this.connection !== 'online'} @click=${() => this.runUiAction('sync', () => this.syncAll(true))}>重新同步电脑配置</md-text-button>
      </section>
      ${this.configScope === 'oneDragon' && !query ? html`<section class="task-editor">
        <div class="section-heading">
          <div><h2>任务列表</h2><p>拖动排序，按需启用</p></div>
        </div>
        <div id="task-list" class="task-list">
          ${this.config.tasks.map(task => html`
            <div class="task-row" data-id=${task.id}>
              <button class="drag-handle" type="button" ?disabled=${!editable} aria-label=${`拖动 ${task.name}`} title="也可按 Alt+上下方向键调整顺序" @keydown=${(event: KeyboardEvent) => { if (event.key === 'ArrowUp' || event.key === 'ArrowDown') this.moveTask(task.id, event.key === 'ArrowUp' ? -1 : 1, event); }}>${icon('drag')}<span class="sr-only">拖动</span></button>
              <div class="task-copy"><strong>${task.name}</strong>${task.isCustom ? html`<small>自定义配置组</small>` : nothing}</div>
              <md-switch ?disabled=${!editable} ?selected=${task.enabled} @change=${(event: Event) => this.updateTask(task.id, (event.currentTarget as HTMLInputElement & {selected: boolean}).selected)} aria-label=${`启用 ${task.name}`}></md-switch>
            </div>`)}
        </div>
      </section>` : nothing}
      <section class="field-groups">
        ${groups.map(group => html`
          <details class="field-group" ?open=${Boolean(query) || this.openFieldGroups.has(this.configScope + ':' + group)} @toggle=${(event: Event) => this.rememberFieldGroup(this.configScope + ':' + group, (event.currentTarget as HTMLDetailsElement).open)}>
            <summary>${group}<span>${grouped.get(group)!.length} 项</span></summary>
            <div class="field-grid">
              ${query || this.openFieldGroups.has(this.configScope + ':' + group) ? grouped.get(group)!.map(field => this.fieldTemplate(field)) : nothing}
            </div>
          </details>`)}
        ${groups.length === 0 ? html`<p class="empty-copy">${query ? '没有匹配的设置，请换一个关键词。' : '电脑中尚未配置此类任务或参数。请在 BetterGI 中配置并保存后重新同步。'}</p>` : nothing}
      </section>
      <section class="save-bar">
        <div><strong>${this.hasDraft ? '有待保存的修改' : '已与电脑同步'}</strong><p>${editable ? '保存后电脑与手机使用同一组参数' : this.loading ? '正在同步或保存，请稍候' : '任务结束且电脑在线时可保存'}</p></div>
        <md-filled-button ?disabled=${!editable || !this.hasDraft} @click=${() => this.runUiAction('save', () => this.saveConfig())}>${this.busyAction === 'save' ? '正在保存…' : '保存到电脑'}</md-filled-button>
      </section>`;
  }

  private fieldTemplate(field: EditableField): TemplateResult {
    const disabled = this.loading || !canSave(this.status, this.connection);
    const help = field.description ? html`<details class="context-help"><summary aria-label=${field.label + '：帮助'}>说明</summary><p>${field.description}</p></details>` : nothing;
    if (field.type === 'toggle') {
      return html`<div class="toggle-setting"><label class="toggle-field"><span><strong>${field.label}</strong></span><md-switch ?disabled=${disabled} aria-label=${field.label} ?selected=${Boolean(field.value)} @change=${(event: Event) => this.updateField(field.path, (event.currentTarget as HTMLElement & {selected: boolean}).selected)}></md-switch></label>${help}</div>`;
    }
    if (field.type === 'select') {
      return html`<div class="setting-field"><label class="input-field"><span>${field.label}</span><md-outlined-select ?disabled=${disabled} aria-label=${field.label} .value=${String(field.value ?? '')} @change=${(event: Event) => this.updateField(field.path, (event.currentTarget as HTMLSelectElement).value)}>${(field.options ?? []).map(option => html`<md-select-option .value=${option}><div slot="headline">${optionLabel(option)}</div></md-select-option>`)}</md-outlined-select></label>${help}</div>`;
    }
    if (field.type === 'multiSelect') {
      const selected = new Set(Array.isArray(field.value) ? field.value.map(String) : []);
      return html`<fieldset class="multi-field"><legend>${field.label}</legend><div class="multi-options">${(field.options ?? []).map(option => html`<label><md-checkbox ?disabled=${disabled} ?checked=${selected.has(option)} @change=${(event: Event) => this.toggleMulti(field, option, (event.currentTarget as HTMLInputElement).checked)}></md-checkbox><span>${optionLabel(option)}</span></label>`)}</div>${help}</fieldset>`;
    }
    return html`<div class="setting-field"><label class="input-field"><span>${field.label}</span><md-outlined-text-field ?disabled=${disabled} aria-label=${field.label} type=${field.type === 'number' ? 'number' : 'text'} step="any" .value=${this.draftInputs.get(field.path) ?? String(field.value ?? '')} min=${field.minimum ?? nothing} max=${field.maximum ?? nothing} @input=${(event: Event) => this.updateInputField(field, event)}></md-outlined-text-field></label>${help}</div>`;
  }

  private rememberFieldGroup(group: string, open: boolean): void {
    if (this.openFieldGroups.has(group) === open) return;
    if (open) this.openFieldGroups.add(group);
    else this.openFieldGroups.delete(group);
    this.draw();
  }

  private reportView(): TemplateResult {
    if (!this.report) return loadingState('还没有执行报告');
    return html`
      <section class="page-heading"><h1>最近报告</h1><p>${formatDate(this.report.finishedAt)}</p></section>
      <section class="report-summary ${this.report.status}">
        <div><p class="section-label">执行结果</p><h2>${statusText(this.report.status)}</h2></div>
        <strong>${formatDuration(this.report.durationSeconds)}</strong>
      </section>
      <section class="report-section"><h2>任务与步骤</h2><div class="result-grid">${this.report.tasks.map(task => html`<div class="result-item"><span>${task.name}</span><strong>${statusText(task.state)}</strong>${task.step ? html`<small>步骤：${task.step}</small>` : nothing}${task.location ? html`<small>位置：${task.location}</small>` : nothing}${task.message ? html`<small>${readableFailure(task.message)}</small>` : nothing}${Object.keys(task.rewards ?? {}).length ? html`<small>获得：${Object.entries(task.rewards ?? {}).map(([name, count]) => `${name} × ${count}`).join('、')}</small>` : nothing}</div>`)}</div></section>
      <section class="report-section"><h2>本次任务获得</h2>${Object.keys(this.report.rewards).length ? html`<div class="reward-grid">${Object.entries(this.report.rewards).map(([name, count]) => html`<div><span>${name}</span><strong>x${count}</strong></div>`)}</div>` : html`<p class="empty-copy">本次没有识别到可汇总的任务道具。</p>`}${this.report.rewardRecognitionStatus ? html`<p class="reward-note">${this.report.rewardRecognitionStatus}</p>` : nothing}</section>
      ${Object.keys(this.report.pickupObservations ?? {}).length ? html`<section class="report-section"><h2>采集与拾取记录</h2><p class="reward-note">以下次数来自拾取识别记录，可能包含交互或重复识别，不能当作背包实际新增数量。</p><div class="reward-grid">${Object.entries(this.report.pickupObservations ?? {}).map(([name, count]) => html`<div><span>${name}</span><strong>识别 ${count} 次</strong></div>`)}</div></section>` : nothing}
      ${Object.keys(this.report.woodEstimates ?? {}).length ? html`<section class="report-section"><h2>木材获取估算</h2><p class="reward-note">BetterGI 根据伐木画面识别的数量，可能存在识别误差，独立于确切奖励累计。</p><div class="reward-grid">${Object.entries(this.report.woodEstimates ?? {}).map(([name, count]) => html`<div><span>${name}</span><strong>约 ${count} 个</strong></div>`)}</div></section>` : nothing}
      ${this.report.lootCoverage ? html`<p class="reward-note" role="note">${this.report.lootCoverage}</p>` : nothing}
      <section class="report-section"><h2>今日累计获得</h2><p class="report-date">${this.report.dailyRewardDate ?? '电脑本地日期'}</p>${Object.keys(this.report.dailyRewards ?? {}).length ? html`<div class="reward-grid">${Object.entries(this.report.dailyRewards ?? {}).map(([name, count]) => html`<div><span>${name}</span><strong>x${count}</strong></div>`)}</div>` : html`<p class="empty-copy">今天暂时没有可累计的奖励识别结果。</p>`}</section>
      ${this.report.dailyRewardStatus ? html`<section class="report-section"><h2>每日奖励</h2><p>${this.report.dailyRewardStatus}</p></section>` : nothing}
      ${this.report.errors.length ? html`<section class="report-section error-list"><h2>未完成的原因</h2>${this.report.errors.map(error => html`<p>${readableFailure(error)}</p>`)}</section>` : nothing}`;
  }

  private settingsView(): TemplateResult {
    return html`
      <section class="page-heading"><h1>手机设置</h1><p>一个浏览器配置只绑定一台电脑。</p></section>
      <section class="settings-list">
        <div><span>绑定电脑</span><strong>${this.pairing?.pcName ?? '未绑定'}</strong></div>
        <div><span>手机设备代码</span><strong>${this.pairing?.deviceId.slice(0, 8) ?? '无'}</strong></div>
        <div><span>连接服务</span><strong>${this.pairing ? 'BetterGI Remote 正式服务' : '未连接'}</strong></div>
        <div><span>PWA 版本</span><strong>${appVersion}</strong></div>
        <div><span>电脑端版本</span><strong>${this.status?.agentVersion ?? '等待同步'}</strong></div>
        <div><span>BetterGI 兼容性</span><strong>${this.status ? this.status.betterGiCompatibilityVerified ? '已验证' : this.status.betterGiVersionSupported ? '结构兼容，待实机验证' : '需要更新 BetterGI Remote' : '等待同步'}</strong></div>
        <div><span>BetterGI 官方最新版</span><strong>${this.status?.betterGiLatestVersion ?? '等待电脑检查'}</strong></div>
        <div><span>绑定有效期</span><strong>${this.status?.bindingExpiresAt ? formatDateOnly(this.status.bindingExpiresAt) : this.pairing?.bindingExpiresAt ? formatDateOnly(this.pairing.bindingExpiresAt) : '连接后自动获取'}</strong></div>
        <div><span>绑定存储</span><strong>${storageStateText(this.storageState)}</strong></div>
        <div><span>日常打开方式</span><strong>${isWechatBrowser() ? '从微信收藏或文件传输助手打开' : isStandaloneMode() ? '已从手机主屏幕打开' : '建议添加到手机主屏幕'}</strong></div>
        <div><span>固定控制入口</span><strong>${isTemporaryOrigin() ? '当前为临时测试地址' : PRODUCTION_ORIGIN}</strong></div>
      </section>
      <section class="settings-actions">
        <md-outlined-button ?disabled=${this.connection !== 'online' || this.loading} @click=${() => this.runUiAction('sync', () => this.syncAll(true))}>${this.busyAction === 'sync' ? '正在同步…' : '立即同步'}</md-outlined-button>
        <md-outlined-button @click=${() => void this.copyEntry()}>复制控制入口</md-outlined-button>
        ${!isStandaloneMode() ? html`<md-outlined-button ?disabled=${this.loading} @click=${() => void this.installApp()}>${this.busyAction === 'install' ? '正在处理…' : '添加到主屏幕'}</md-outlined-button>` : nothing}
        ${this.storageState === 'managed' ? html`<md-outlined-button ?disabled=${this.loading} @click=${() => void this.protectStorage()}>${this.busyAction === 'storage' ? '正在检查…' : '保护绑定数据'}</md-outlined-button>` : nothing}
        <md-outlined-button class="danger-outline" ?disabled=${!this.pairing} @click=${() => void this.removePairing()}>清除此手机绑定</md-outlined-button>
      </section>`;
  }

  private mountSortable(): void {
    const element = document.querySelector<HTMLElement>('#task-list');
    if (!element || !this.config) return;
    this.sortable = Sortable.create(element, {
      animation: 150,
      disabled: this.loading || !canSave(this.status, this.connection),
      handle: '.drag-handle',
      ghostClass: 'task-ghost',
      onEnd: (event: SortableEvent) => {
        const oldIndex = event.oldDraggableIndex ?? event.oldIndex;
        const newIndex = event.newDraggableIndex ?? event.newIndex;
        if (oldIndex === undefined || newIndex === undefined || oldIndex === newIndex) return;
        this.config!.tasks = reorderTasks(this.config!.tasks, oldIndex, newIndex);
        this.tasksDirty = true;
        this.taskListDomReordered = true;
        window.setTimeout(() => this.showNotice('任务顺序已调整，点击“保存到电脑”后生效', 'info'), 0);
      },
    });
  }
}

function normalizeConfig(config: RemoteConfig): RemoteConfig {
  return {...config, tasks: [...config.tasks].sort((left, right) => left.order - right.order), fields: config.fields.map(field => ({...field}))};
}

function connectionBadge(state: ConnectionState): TemplateResult {
  const label = state === 'online' ? '电脑在线' : state === 'waiting_for_pc' ? '等待电脑' : state === 'connecting' ? '连接中' : state === 'unpaired' ? '未绑定' : state === 'error' ? '连接异常' : '电脑离线';
  return html`<span class="connection-badge ${state}"><span class="status-dot ${state === 'online' ? 'good' : state === 'connecting' || state === 'waiting_for_pc' ? 'busy' : 'bad'}"></span>${label}</span>`;
}

function navButton(view: ViewName, label: string, current: ViewName, action: () => void): TemplateResult {
  return html`<button type="button" class=${view === current ? 'active' : ''} aria-current=${view === current ? 'page' : nothing} @click=${action}>${icon(view)}<span>${label}</span></button>`;
}

function metric(label: string, value: string): TemplateResult {
  return html`<div class="metric"><span>${icon(label === 'Windows' ? 'windows' : label === '版本' ? 'version' : 'game')}${label}</span><strong>${value}</strong></div>`;
}

function emptyPairing(): TemplateResult {
  return html`<section class="empty-state pairing-onboarding">
    <div class="paimon-portrait" role="img" aria-label="派蒙向你挥手"></div>
    <h1>用手机连接 BetterGI</h1>
    <p>电脑安装 BetterGI Remote 后会直接显示二维码。扫描一次，以后从手机主屏幕打开即可。</p>
    <ol>
      <li><strong>在电脑完成安装</strong><span>程序会自动查找 BetterGI</span></li>
      <li><strong>显示连接二维码</strong><span>无需注册账号或填写服务器</span></li>
      <li><strong>用手机相机扫码</strong><span>回到电脑确认本次绑定</span></li>
    </ol>
    <p class="pairing-help">已经安装？在电脑右下角找到 BetterGI Remote，选择“连接手机”。</p>
  </section>`;
}

function loadingState(message: string): TemplateResult {
  return html`<section class="empty-state loading-state"><div class="paimon-portrait" role="img" aria-label="派蒙正在等待"></div><h1>${message}</h1><p>连接恢复后页面会自动同步，不会提交离线命令。</p></section>`;
}

function statusReason(status: AgentStatus): string {
  if (!status.windowsUnlocked) return 'Windows 已锁屏';
  if (!status.betterGiVersionSupported) return 'BetterGI 版本不受支持';
  if (!status.betterGiConfigured) return '请在电脑端选择 BetterGI 安装目录';
  if (status.state === 'updating') return '正在保存并应用电脑配置，请稍候';
  if (activeRunStates.has(status.state)) return '任务正在执行，可查看当前步骤';
  if (status.state !== 'idle') return '电脑正在执行其他任务，请等待完成';
  return status.betterGiRunning ? 'BetterGI 已打开，启动时会自动应用任务配置' : '启动任务时会自动打开 BetterGI';
}

function statusText(status: string): string {
  return ({success: '成功', failed: '失败', stopped: '已停止', warning: '有警告', starting: '准备中', running: '执行中', stopping: '正在停止', pending: '等待', skipped: '跳过'} as Record<string, string>)[status] ?? '待确认';
}

function optionLabel(value: string): string {
  return ({Enabled: '启用', Disabled: '停用', Daily: '每天', EveryTwoDays: '每两天', Monday: '周一', Tuesday: '周二', Wednesday: '周三', Thursday: '周四', Friday: '周五', Saturday: '周六', Sunday: '周日', Whitelist: '白名单', Blacklist: '黑名单', BitBlt: '传统画面捕获', WindowsGraphicsCapture: 'Windows 图形捕获', Closed: '关闭', AllowAutoPickupForNonElite: '普通敌人允许拾取', DisableAutoPickupForNonElite: '普通敌人不拾取'} as Record<string, string>)[value] ?? (value || '未设置');
}

function formatDate(value: string): string {
  return new Intl.DateTimeFormat('zh-CN', {dateStyle: 'medium', timeStyle: 'short'}).format(new Date(value));
}

function formatDuration(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const remaining = Math.round(seconds % 60);
  return `${minutes} 分 ${remaining} 秒`;
}

function formatDateOnly(value: string): string {
  return new Intl.DateTimeFormat('zh-CN', {dateStyle: 'long'}).format(new Date(value));
}

function daysUntil(value?: string): number | undefined {
  if (!value) return undefined;
  return Math.max(0, Math.ceil((new Date(value).getTime() - Date.now()) / 86_400_000));
}

function mobileLabel(): string {
  const platform = navigator.userAgent.includes('iPhone') ? 'iPhone 浏览器' : navigator.userAgent.includes('Android') ? 'Android 浏览器' : '手机浏览器';
  return platform;
}

function storageStateText(state: 'checking' | 'persistent' | 'managed' | 'unsupported'): string {
  if (state === 'persistent') return '已保护，系统不会自动清理';
  if (state === 'managed') return '由浏览器管理';
  if (state === 'unsupported') return '当前浏览器不支持持久保护';
  return '正在检查';
}

function isWechatBrowser(): boolean {
  return /MicroMessenger/i.test(navigator.userAgent);
}

function isStandaloneMode(): boolean {
  return matchMedia('(display-mode: standalone)').matches || Boolean((navigator as Navigator & {standalone?: boolean}).standalone);
}

function isTemporaryOrigin(): boolean {
  return location.hostname.endsWith('.trycloudflare.com') || location.hostname === '127.0.0.1' || location.hostname === 'localhost';
}

function readLocalFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}

function writeLocalFlag(key: string): void {
  try {
    localStorage.setItem(key, '1');
  } catch {
  }
}

async function copyText(value: string): Promise<void> {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value);
    return;
  }
  const field = document.createElement('textarea');
  field.value = value;
  field.readOnly = true;
  field.style.position = 'fixed';
  field.style.opacity = '0';
  document.body.append(field);
  field.select();
  const copied = document.execCommand('copy');
  field.remove();
  if (!copied) throw new Error('无法自动复制，请从设置页手动记录固定域名');
}

async function keepFeedbackVisible(startedAt: number, minimumMs = 500): Promise<void> {
  const remaining = minimumMs - (performance.now() - startedAt);
  if (remaining > 0) await new Promise(resolve => window.setTimeout(resolve, remaining));
}

const root = document.querySelector<HTMLElement>('#app');
if (!root) throw new Error('应用挂载点不存在');
void new App(root).start().catch(error => {
  const message = error instanceof Error ? error.message : '手机控制端初始化失败';
  render(html`<main class="startup-error" role="alert"><h1>无法打开控制端</h1><p>${message}</p><button type="button" @click=${() => location.reload()}>重新加载</button></main>`, root);
});

if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', () => void registerServiceWorker());
}

async function registerServiceWorker(): Promise<void> {
  const hadController = Boolean(navigator.serviceWorker.controller);
  let refreshed = false;
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!hadController || refreshed) return;
    refreshed = true;
    location.reload();
  });
  const registration = await navigator.serviceWorker.register('/sw.js', {updateViaCache: 'none'});
  await registration.update();
}
