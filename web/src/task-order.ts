import {TaskItem} from './types';

export function reorderTasks(tasks: readonly TaskItem[], oldIndex: number, newIndex: number): TaskItem[] {
  if (!Number.isInteger(oldIndex) || !Number.isInteger(newIndex) || oldIndex < 0 || newIndex < 0 || oldIndex >= tasks.length || newIndex >= tasks.length) {
    return tasks.map((task, order) => ({...task, order}));
  }
  const reordered = [...tasks];
  const [moved] = reordered.splice(oldIndex, 1);
  if (!moved) return tasks.map((task, order) => ({...task, order}));
  reordered.splice(newIndex, 0, moved);
  return reordered.map((task, order) => ({...task, order}));
}
