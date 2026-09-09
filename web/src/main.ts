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
import {AgentStatus, ConnectionState, EditableField, PairingRecord, PushMessage, RemoteConfig, RunProgress, RunReport, ViewName} from './types';
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
  private readonly openFieldGroups = new Set<string>(['自动秘境']);

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
    };
    this.config = {
      name: '远程每日', revision: 'demo', completionAction: '关闭游戏和软件', readAt: new Date().toISOString(), fields: [],
      tasks: ['领取邮件', '合成树脂', '自动秘境', '自动首领讨伐', '自动幽境危战', '自动地脉花', '领取每日奖励', '领取尘歌壶奖励', '朋友新增调度组']
        .map((name, order) => ({id: `demo-${order}`, name, enabled: order < 4, isCustom: order === 8, order})),
    };
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
    const feedbackStartedAt = performance.now();
    this.loading = true;
    this.busyAction = showFeedback ? 'sync' : undefined;
    this.error = undefined;
    this.draw();
    try {
      let usedLegacySync = false;
      const configRequest = showFeedback
        ? this.client.call<RemoteConfig>('config.sync').catch(error => {
            if (error instanceof RemoteRpcError && error.code === 'method_not_allowed') {
              usedLegacySync = true;
              return this.client!.call<RemoteConfig>('config.get');
            }
            throw error;
          })
        : this.client.call<RemoteConfig>('config.get');
      const [status, config, report] = await Promise.all([
        this.client.call<AgentStatus>('status.get'),
        configRequest,
        this.client.call<RunReport | undefined>('report.latest'),
      ]);
      this.status = status;
      this.config = normalizeConfig(config);
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
    if (message.event === 'status.changed') this.status = message.data as AgentStatus;
    if (message.event === 'run.progress') this.progress = message.data as RunProgress;
    if (message.event === 'run.completed') {
      this.report = message.data as RunReport;
      this.progress = undefined;
      void this.refreshConfig();
      this.showNotice('任务已结束，执行报告已生成');
    }
    this.draw();
  }

  private async refreshConfig(): Promise<void> {
    if (!this.client || this.connection !== 'online') return;
    try {
      this.config = normalizeConfig(await this.client.call<RemoteConfig>('config.get'));
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
    if (!this.client || !this.config) return;
    const feedbackStartedAt = performance.now();
    this.loading = true;
    this.busyAction = 'save';
    this.error = undefined;
    this.draw();
    try {
      const updated = await this.client.call<RemoteConfig>('config.update', {
        baseRevision: this.config.revision,
        tasks: this.config.tasks.map((task, order) => ({...task, order})),
        values: Object.fromEntries(this.config.fields.map(field => [field.path, field.value])),
      });
      this.config = normalizeConfig(updated);
      this.showNotice('配置已安全保存到电脑');
    } catch (error) {
      if (error instanceof RemoteRpcError && error.code === 'config_conflict') {
        await this.refreshConfig();
        this.error = '电脑端配置已经变化，已加载最新内容。请重新确认后保存。';
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
    if (!this.client || !this.config) return;
    const enabled = this.config.tasks.filter(task => task.enabled).map(task => task.name);
    if (enabled.length === 0) {
      this.error = '请至少启用一个任务。';
      this.draw();
      return;
    }
    if (!confirm(`确认启动“${this.config.name}”吗？\n\n${enabled.join('、')}\n\n正常完成后将关闭原神和 BetterGI。`)) return;
    await this.action('start', '启动指令已被电脑接受', async () => {
      const accepted = await this.client!.call<{runId: string}>('task.start', {expectedRevision: this.config!.revision, confirmed: true});
      this.progress = {runId: accepted.runId, state: 'running', completedTasks: 0, totalTasks: enabled.length, observedAt: new Date().toISOString()};
    });
  }

  private async stopTask(): Promise<void> {
    if (!this.client || !confirm('确认发送 BetterGI 取消任务快捷键吗？')) return;
    await this.action('stop', '停止指令已发送，正在等待 BetterGI 确认', async () => {
      await this.client!.call('task.stop', {runId: this.status?.activeRunId ?? this.progress?.runId ?? null});
    });
  }

  private async action(action: BusyAction, successMessage: string, operation: () => Promise<void>): Promise<void> {
    const feedbackStartedAt = performance.now();
    this.loading = true;
    this.busyAction = action;
    this.error = undefined;
    this.draw();
    try {
      await operation();
      this.showNotice(successMessage);
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
    if (task) task.enabled = enabled;
  }

  private updateField(path: string, value: unknown): void {
    const field = this.config?.fields.find(item => item.path === path);
    if (field) field.value = value;
  }

  private toggleMulti(field: EditableField, option: string, checked: boolean): void {
    const values = new Set(Array.isArray(field.value) ? field.value.map(String) : []);
    if (checked) values.add(option);
    else values.delete(option);
    field.value = [...values];
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
    this.error = error instanceof Error ? error.message : '操作失败';
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
    if (this.view === 'config' && this.config) queueMicrotask(() => this.mountSortable());
  }

  private template(): TemplateResult {
    return html`
      <div class="app-shell">
        <header class="topbar">
          <div>
            <p class="brand">BetterGI Remote</p>
            <p class="computer-name">${this.pairing?.pcName ?? '等待绑定电脑'}</p>
          </div>
          ${connectionBadge(this.connection)}
        </header>
        ${this.loading ? html`<md-linear-progress indeterminate aria-label="正在处理"></md-linear-progress>` : nothing}
        <main class="content">
          ${this.error ? html`<section class="inline-error" role="alert">${this.error}</section>` : nothing}
          ${this.view === 'home' ? this.homeView() : nothing}
          ${this.view === 'config' ? this.configView() : nothing}
          ${this.view === 'reports' ? this.reportView() : nothing}
          ${this.view === 'settings' ? this.settingsView() : nothing}
        </main>
        ${this.notice ? html`<div class="action-notice ${this.notice.tone}" role=${this.notice.tone === 'error' ? 'alert' : 'status'} aria-live=${this.notice.tone === 'error' ? 'assertive' : 'polite'} aria-atomic="true">${this.notice.message}</div>` : nothing}
        ${this.pairing ? html`<nav class="bottom-nav" aria-label="主要页面">
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
    const ready = this.connection === 'online' && this.status.windowsUnlocked && this.status.betterGiVersionSupported && !this.status.betterGiRunning && this.status.state === 'idle';
    const running = this.status.state === 'running' || Boolean(this.progress);
    return html`
      <section class="command-hero">
        <div class="hero-image" role="img" aria-label="原神角色桑多涅、胡桃与纳西妲组合画面"></div>
        <div class="hero-shade"></div>
        <div class="hero-content">
          <p class="hero-brand">BetterGI Remote</p>
          <h1>${running ? '远征正在进行' : ready ? '今日委托已就绪' : '等待终端就绪'}</h1>
          <p>${running ? `${this.progress?.currentTask ?? '正在准备任务'}` : ready ? '电脑状态正常，可以从这里启程。' : this.status.message ?? statusReason(this.status)}</p>
          <div class="hero-signal"><span class="status-dot ${ready ? 'good' : running ? 'busy' : 'bad'}"></span>${this.connection === 'online' ? this.pairing.pcName ?? '已连接电脑' : '电脑连接中'}</div>
        </div>
        <small class="asset-credit">角色画面 © 米哈游 / HoYoverse</small>
      </section>
      ${this.bindingReminder()}
      ${this.entryGuide()}
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
      ${this.progress ? html`
        <section class="run-panel">
          <p class="section-label">当前任务</p>
          <h2>${this.progress.currentTask ?? '正在准备'}</h2>
          <p>${this.progress.completedTasks} / ${this.progress.totalTasks} 项已完成</p>
          <md-linear-progress value=${this.progress.totalTasks ? this.progress.completedTasks / this.progress.totalTasks : 0}></md-linear-progress>
        </section>` : nothing}
      <section class="action-panel">
        <div>
          <h2>${this.config?.name ?? '远程每日'}</h2>
          <p>${this.config ? `${this.config.tasks.filter(task => task.enabled).length} 项任务已启用` : '请先同步配置'}</p>
        </div>
        ${running
          ? html`<md-filled-button class="danger-button" ?disabled=${this.loading || this.connection !== 'online'} @click=${() => void this.stopTask()}>${this.busyAction === 'stop' ? '正在发送…' : '停止任务'}</md-filled-button>`
          : html`<md-filled-button ?disabled=${!ready || !this.config || this.loading} @click=${() => void this.startTask()}>${this.busyAction === 'start' ? '正在启动…' : '确认并启动'}</md-filled-button>`}
      </section>`;
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
    const groups = [...new Set(this.config.fields.map(field => field.group))];
    return html`
      <section class="page-heading">
        <h1>一条龙配置</h1>
        <p>拖动任务调整顺序。带“全局设置”的字段也会影响电脑端同类任务。</p>
      </section>
      <section class="task-editor">
        <div class="section-heading">
          <div><h2>任务列表</h2><p>自定义配置组只允许开关和排序</p></div>
        </div>
        <div id="task-list" class="task-list">
          ${this.config.tasks.map(task => html`
            <div class="task-row" data-id=${task.id}>
              <button class="drag-handle" type="button" aria-label="拖动 ${task.name}">拖动</button>
              <div class="task-copy"><strong>${task.name}</strong>${task.isCustom ? html`<small>自定义配置组</small>` : nothing}</div>
              <md-switch ?selected=${task.enabled} @change=${(event: Event) => this.updateTask(task.id, (event.currentTarget as HTMLInputElement & {selected: boolean}).selected)} aria-label=${`启用 ${task.name}`}></md-switch>
            </div>`)}
        </div>
      </section>
      <section class="field-groups">
        ${groups.map(group => html`
          <details class="field-group" ?open=${this.openFieldGroups.has(group)} @toggle=${(event: Event) => this.rememberFieldGroup(group, (event.currentTarget as HTMLDetailsElement).open)}>
            <summary>${group}<span>${this.config!.fields.filter(field => field.group === group).length} 项</span></summary>
            <div class="field-grid">
              ${this.config!.fields.filter(field => field.group === group).map(field => this.fieldTemplate(field))}
            </div>
          </details>`)}
      </section>
      <section class="save-bar">
        <div><strong>完成动作</strong><p>${this.config.completionAction}</p></div>
        <md-filled-button ?disabled=${this.connection !== 'online' || this.status?.betterGiRunning || this.loading} @click=${() => void this.saveConfig()}>${this.busyAction === 'save' ? '正在保存…' : '保存到电脑'}</md-filled-button>
      </section>`;
  }

  private fieldTemplate(field: EditableField): TemplateResult {
    const help = field.description ? html`<small class="field-help">${field.description}</small>` : nothing;
    if (field.type === 'toggle') {
      return html`<label class="toggle-field"><span><strong>${field.label}</strong>${help}</span><md-switch ?selected=${Boolean(field.value)} @change=${(event: Event) => this.updateField(field.path, (event.currentTarget as HTMLElement & {selected: boolean}).selected)}></md-switch></label>`;
    }
    if (field.type === 'select') {
      return html`<label class="input-field"><span>${field.label}${field.scope === 'global' ? html`<small>全局设置</small>` : nothing}</span><md-outlined-select .value=${String(field.value ?? '')} @change=${(event: Event) => this.updateField(field.path, (event.currentTarget as HTMLSelectElement).value)}>${(field.options ?? []).map(option => html`<md-select-option .value=${option}><div slot="headline">${option || '未设置'}</div></md-select-option>`)}</md-outlined-select>${help}</label>`;
    }
    if (field.type === 'multiSelect') {
      const selected = new Set(Array.isArray(field.value) ? field.value.map(String) : []);
      return html`<fieldset class="multi-field"><legend>${field.label}${field.scope === 'global' ? html`<small>全局设置</small>` : nothing}</legend><div class="multi-options">${(field.options ?? []).map(option => html`<label><md-checkbox ?checked=${selected.has(option)} @change=${(event: Event) => this.toggleMulti(field, option, (event.currentTarget as HTMLInputElement).checked)}></md-checkbox><span>${option}</span></label>`)}</div>${help}</fieldset>`;
    }
    return html`<label class="input-field"><span>${field.label}${field.scope === 'global' ? html`<small>全局设置</small>` : nothing}</span><md-outlined-text-field type=${field.type === 'number' ? 'number' : 'text'} .value=${String(field.value ?? '')} min=${field.minimum ?? nothing} max=${field.maximum ?? nothing} @input=${(event: Event) => this.updateField(field.path, field.type === 'number' ? Number((event.currentTarget as HTMLInputElement).value) : (event.currentTarget as HTMLInputElement).value)}></md-outlined-text-field>${help}</label>`;
  }

  private rememberFieldGroup(group: string, open: boolean): void {
    if (open) this.openFieldGroups.add(group);
    else this.openFieldGroups.delete(group);
  }

  private reportView(): TemplateResult {
    if (!this.report) return loadingState('还没有执行报告');
    return html`
      <section class="page-heading"><h1>最近报告</h1><p>${formatDate(this.report.finishedAt)}</p></section>
      <section class="report-summary ${this.report.status}">
        <div><p class="section-label">执行结果</p><h2>${statusText(this.report.status)}</h2></div>
        <strong>${formatDuration(this.report.durationSeconds)}</strong>
      </section>
      <section class="report-section"><h2>任务</h2><div class="result-grid">${this.report.tasks.map(task => html`<div class="result-item"><span>${task.name}</span><strong>${statusText(task.state)}</strong>${task.message ? html`<small>${task.message}</small>` : nothing}</div>`)}</div></section>
      <section class="report-section"><h2>识别奖励</h2>${Object.keys(this.report.rewards).length ? html`<div class="reward-grid">${Object.entries(this.report.rewards).map(([name, count]) => html`<div><span>${name}</span><strong>x${count}</strong></div>`)}</div>` : html`<p class="empty-copy">本次没有可汇总的奖励识别结果。</p>`}</section>
      ${this.report.dailyRewardStatus ? html`<section class="report-section"><h2>每日奖励</h2><p>${this.report.dailyRewardStatus}</p></section>` : nothing}
      ${this.report.errors.length ? html`<section class="report-section error-list"><h2>错误</h2>${this.report.errors.map(error => html`<p>${error}</p>`)}</section>` : nothing}`;
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
        <div><span>绑定有效期</span><strong>${this.status?.bindingExpiresAt ? formatDateOnly(this.status.bindingExpiresAt) : this.pairing?.bindingExpiresAt ? formatDateOnly(this.pairing.bindingExpiresAt) : '连接后自动获取'}</strong></div>
        <div><span>绑定存储</span><strong>${storageStateText(this.storageState)}</strong></div>
        <div><span>日常打开方式</span><strong>${isWechatBrowser() ? '从微信收藏或文件传输助手打开' : isStandaloneMode() ? '已从手机主屏幕打开' : '建议添加到手机主屏幕'}</strong></div>
        <div><span>固定控制入口</span><strong>${isTemporaryOrigin() ? '当前为临时测试地址' : PRODUCTION_ORIGIN}</strong></div>
      </section>
      <section class="settings-actions">
        <md-outlined-button ?disabled=${this.connection !== 'online' || this.loading} @click=${() => void this.syncAll(true)}>${this.busyAction === 'sync' ? '正在同步…' : '立即同步'}</md-outlined-button>
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
      handle: '.drag-handle',
      ghostClass: 'task-ghost',
      onEnd: (event: SortableEvent) => {
        const oldIndex = event.oldDraggableIndex ?? event.oldIndex;
        const newIndex = event.newDraggableIndex ?? event.newIndex;
        if (oldIndex === undefined || newIndex === undefined || oldIndex === newIndex) return;
        this.config!.tasks = reorderTasks(this.config!.tasks, oldIndex, newIndex);
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
  return html`<button type="button" class=${view === current ? 'active' : ''} aria-current=${view === current ? 'page' : nothing} @click=${action}>${label}</button>`;
}

function metric(label: string, value: string): TemplateResult {
  return html`<div class="metric"><span>${label}</span><strong>${value}</strong></div>`;
}

function emptyPairing(): TemplateResult {
  return html`<section class="empty-state pairing-onboarding">
    <div class="pairing-mark" aria-hidden="true"><span></span><span></span><span></span><span></span></div>
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
  return html`<section class="empty-state"><h1>${message}</h1><p>连接恢复后页面会自动同步，不会提交离线命令。</p></section>`;
}

function statusReason(status: AgentStatus): string {
  if (!status.windowsUnlocked) return 'Windows 已锁屏';
  if (!status.betterGiVersionSupported) return 'BetterGI 版本不受支持';
  if (status.betterGiRunning) return '请先关闭 BetterGI';
  return '电脑状态需要检查';
}

function statusText(status: string): string {
  return ({success: '成功', failed: '失败', stopped: '已停止', warning: '有警告', running: '执行中', pending: '等待', skipped: '跳过'} as Record<string, string>)[status] ?? '未知';
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
