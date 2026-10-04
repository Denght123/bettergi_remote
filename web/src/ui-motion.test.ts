import {afterEach, describe, expect, it, vi} from 'vitest';
import {UiMotion} from './ui-motion';
afterEach(() => vi.unstubAllGlobals());
function fixture(reduce = false) {
  const cancel = vi.fn();
  const animate = vi.fn(() => ({cancel}));
  const listeners: Array<() => void> = [];
  const preference = {matches: reduce, addEventListener: (_: string, listener: () => void) => listeners.push(listener)};
  vi.stubGlobal('matchMedia', () => preference);
  const root = {querySelector: () => ({animate})} as unknown as HTMLElement;
  return {motion: new UiMotion(root), animate, cancel, listeners};
}
describe('presentation-only route motion', () => {
  it('does not restart animations for repeated status/config redraws', () => {
    const {motion, animate} = fixture();
    motion.settle('home'); motion.settle('home'); motion.settle('home');
    expect(animate).toHaveBeenCalledTimes(1);
  });
  it('keeps reduced-motion mode static', () => {
    const {motion, animate} = fixture(true);
    motion.settle('home'); motion.settle('config');
    expect(animate).not.toHaveBeenCalled();
  });
  it('cancels old motion before transitioning and responds to reduced-motion changes', () => {
    const {motion, animate, cancel, listeners} = fixture();
    motion.settle('home'); motion.settle('reports');
    expect(animate).toHaveBeenCalledTimes(2); expect(cancel).toHaveBeenCalledTimes(1);
    listeners[0]!(); expect(cancel).toHaveBeenCalledTimes(2);
  });
});
