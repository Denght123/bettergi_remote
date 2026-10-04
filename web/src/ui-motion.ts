/** Presentation only. No networking, storage, configuration or task state is changed here. */
export class UiMotion {
  private lastView?: string;
  private readonly preference = matchMedia('(prefers-reduced-motion: reduce)');
  private animations: Animation[] = [];
  constructor(private readonly root: HTMLElement) {
    this.preference.addEventListener('change', () => this.animations.forEach(animation => animation.cancel()));
  }
  settle(view: string): void {
    if (view === this.lastView) return;
    const previous = this.lastView;
    this.lastView = view;
    this.animations.forEach(animation => animation.cancel());
    this.animations = [];
    if (this.preference.matches) return;
    const content = this.root.querySelector<HTMLElement>('main.content');
    if (content && previous) {
      this.animations.push(content.animate([
        {opacity: .65, transform: 'translateY(12px)', clipPath: 'inset(0 0 12px 0)'},
        {opacity: 1, transform: 'translateY(0)', clipPath: 'inset(0)'},
      ], {duration: 360, easing: 'cubic-bezier(.16,1,.3,1)'}));
    }
    if (view === 'home') {
      const art = this.root.querySelector<HTMLElement>('.hero-image');
      if (art) this.animations.push(art.animate([
        {transform: 'scale(1.07)', filter: 'brightness(.75)'},
        {transform: 'scale(1.015)', filter: 'brightness(1)'},
      ], {duration: 1100, easing: 'cubic-bezier(.16,1,.3,1)'}));
    }
  }
}
