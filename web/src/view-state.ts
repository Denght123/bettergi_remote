import type {AgentStatus, ConnectionState} from './types';

export const activeRunStates = new Set(['starting', 'running', 'stopping']);

export function canStart(status: AgentStatus | undefined, connection: ConnectionState): boolean {
  return Boolean(status && connection === 'online' && status.windowsUnlocked &&
    status.betterGiConfigured && status.betterGiVersionSupported && status.state === 'idle' && (status.bindingDaysRemaining == null || status.bindingDaysRemaining > 0) &&
    (!status.betterGiRunning || status.capabilities?.includes('runningStart')));
}

export function canSave(status: AgentStatus | undefined, connection: ConnectionState): boolean {
  return canStart(status, connection);
}

export function readableFailure(message: string | undefined): string {
  if (!message) return '任务没有完成，请查看电脑状态后重试。';
  const lines = message.split(/\r?\n/).filter(line => !/^\s*(at |在 |---)/.test(line));
  const safe = lines.join(' ').replace(/\b(?:System|BetterGenshinImpact)\.[A-Za-z0-9_.<>`]+(?:Exception)?\s*:?/g, '')
    .replace(/\b0x[0-9a-f]+\b/gi, '').trim();
  return /[\u3400-\u9fff]/.test(safe)
    ? safe.slice(0, 320)
    : '任务在当前步骤没有完成，请检查游戏画面、队伍和任务配置后重试。';
}
