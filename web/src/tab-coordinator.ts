export class TabCoordinator {
  private readonly id = crypto.randomUUID();
  private readonly channel = typeof BroadcastChannel === 'undefined' ? undefined : new BroadcastChannel('bettergi-remote-lite-tabs');
  private active = true;

  constructor(private readonly onActiveChanged: (active: boolean) => void) {
    this.channel?.addEventListener('message', event => {
      const message = event.data as {type?: string; id?: string};
      if (message.type !== 'active' || message.id === this.id || document.visibilityState !== 'visible') return;
      this.setActive(false);
    });
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') this.claim();
    });
    window.addEventListener('pageshow', () => this.claim());
    this.claim();
  }

  claim(): void {
    this.setActive(true);
    this.channel?.postMessage({type: 'active', id: this.id});
  }

  close(): void {
    this.channel?.close();
  }

  private setActive(value: boolean): void {
    if (this.active === value) return;
    this.active = value;
    this.onActiveChanged(value);
  }
}
