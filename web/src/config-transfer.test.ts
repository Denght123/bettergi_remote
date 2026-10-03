import {describe, expect, it} from 'vitest';
import {readFullConfig} from './config-transfer';
import {canStart, readableFailure} from './view-state';
import type {AgentStatus, RemoteConfig} from './types';

const page = (revision: string, offset?: number): RemoteConfig => ({
  name: '测试配置', revision, tasks: [], fields: [{path: offset == null ? 'global.a' : 'global.b', scope: 'global', group: '拾取', label: '自动拾取', type: 'toggle', value: true}],
  completionAction: '无', readAt: '', nextFieldOffset: offset == null ? 1 : undefined, totalFields: 2,
});

describe('configuration and run state consistency', () => {
  it('assembles all pages and requests the same revision', async () => {
    const params: unknown[] = [];
    const result = await readFullConfig(async input => {
      params.push(input);
      return page('one', input.offset === 0 ? undefined : input.offset);
    });
    expect(result.fields.map(field => field.path)).toEqual(['global.a', 'global.b']);
    expect(params[1]).toEqual({offset: 1, limit: 64, revision: 'one'});
  });
  it('rejects mixed revisions or incomplete config before editing', async () => {
    await expect(readFullConfig(async () => page('two', 1), page('one'))).rejects.toThrow('发生变化');
    await expect(readFullConfig(async () => ({...page('one'), nextFieldOffset: undefined}))).rejects.toThrow('完整读取');
  });
  it('permits idle open BetterGI but never permits a concurrent run', () => {
    const status = {windowsUnlocked: true, betterGiConfigured: true, betterGiVersionSupported: true, betterGiRunning: true, state: 'idle', capabilities: ['runningStart']} as AgentStatus;
    expect(canStart(status, 'online')).toBe(true);
    expect(canStart({...status, state: 'starting'}, 'online')).toBe(false);
    expect(canStart({...status, state: 'running'}, 'online')).toBe(false);
    expect(canStart(status, 'offline')).toBe(false);
    expect(canStart({...status, bindingDaysRemaining: 0}, 'online')).toBe(false);
    expect(canStart({...status, capabilities: undefined}, 'online')).toBe(false);
    expect(canStart({...status, capabilities: undefined, betterGiRunning: false}, 'online')).toBe(true);
  });
  it('does not present raw exception traces as player feedback', () => {
    expect(readableFailure('System.NullReferenceException: Object reference\n at Test.Run()')).not.toContain('Exception');
    expect(readableFailure('自动秘境：第 2 轮领取奖励时未找到地脉之花')).toContain('第 2 轮');
  });
});
