import {expect, it} from 'vitest';
import {readFullReport} from './report-transfer';
import type {RunReport} from './types';

const base: RunReport = {runId: 'run', status: 'success', startedAt: '', finishedAt: '', durationSeconds: 0, tasks: [], rewards: {'摩拉': 100}, errors: [], logExcerpt: [], parserVersion: 'v3', nextLootOffset: 1, totalLootEntries: 2};
it('restores every kind of loot without mixing estimates with exact rewards', async () => {
  const report = await readFullReport(async params => {
    expect(params.runId).toBe('run');
    return {...base, rewards: {}, woodEstimates: {'杉木': 6}, nextLootOffset: undefined};
  }, base);
  expect(report?.rewards).toEqual({'摩拉': 100});
  expect(report?.woodEstimates).toEqual({'杉木': 6});
});
it('rejects switching to another run while paging', async () => {
  await expect(readFullReport(async () => ({...base, runId: 'new'}), base)).rejects.toThrow('发生变化');
});
