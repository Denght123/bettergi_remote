import type {RunReport} from './types';

export async function readFullReport(
  fetchPage: (params: {offset: number; limit: number; runId?: string}) => Promise<RunReport | undefined>,
  initial?: RunReport,
): Promise<RunReport | undefined> {
  const first = initial ?? await fetchPage({offset: 0, limit: 24});
  if (!first) return undefined;
  const result = {...first, rewards: {...first.rewards}, dailyRewards: {...first.dailyRewards}, pickupObservations: {...first.pickupObservations}, woodEstimates: {...first.woodEstimates}};
  let page = first;
  let offset = 0;
  while (page.nextLootOffset != null) {
    if (page.nextLootOffset <= offset) throw new Error('报告读取尚未完成，请重新同步。');
    offset = page.nextLootOffset;
    const next = await fetchPage({offset, limit: 24, runId: first.runId});
    if (!next || next.runId !== first.runId || next.totalLootEntries !== first.totalLootEntries) throw new Error('报告在读取期间发生变化，请重新同步。');
    for (const key of ['rewards', 'dailyRewards', 'pickupObservations', 'woodEstimates'] as const) Object.assign(result[key], next[key]);
    page = next;
  }
  const count = [result.rewards, result.dailyRewards, result.pickupObservations, result.woodEstimates].reduce((total, map) => total + Object.keys(map).length, 0);
  if (first.totalLootEntries != null && count !== first.totalLootEntries) throw new Error('材料统计没有完整读取，请重新同步报告。');
  return {...result, nextLootOffset: undefined};
}
