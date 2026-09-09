import {describe, expect, it} from 'vitest';
import {reorderTasks} from './task-order';
import {TaskItem} from './types';

const tasks: TaskItem[] = [
  {id: 'resin', name: '合成树脂', enabled: true, isCustom: false, order: 0},
  {id: 'domain', name: '自动秘境', enabled: true, isCustom: false, order: 1},
  {id: 'mail', name: '领取邮件', enabled: true, isCustom: false, order: 2},
];

describe('reorderTasks', () => {
  it('移动任务时只保留一份原始项', () => {
    const reordered = reorderTasks(tasks, 0, 2);
    expect(reordered.map(task => task.id)).toEqual(['domain', 'mail', 'resin']);
    expect(new Set(reordered.map(task => task.id)).size).toBe(tasks.length);
    expect(reordered.map(task => task.order)).toEqual([0, 1, 2]);
  });

  it('从后向前移动时正确更新顺序', () => {
    expect(reorderTasks(tasks, 2, 0).map(task => task.id)).toEqual(['mail', 'resin', 'domain']);
  });

  it('拒绝越界索引且不产生重复项', () => {
    const unchanged = reorderTasks(tasks, 0, 99);
    expect(unchanged.map(task => task.id)).toEqual(['resin', 'domain', 'mail']);
    expect(new Set(unchanged.map(task => task.id)).size).toBe(tasks.length);
  });
});
