import {svg, nothing} from 'lit';
export type UiIcon = 'home' | 'config' | 'reports' | 'settings' | 'spark' | 'windows' | 'game' | 'version' | 'drag' | 'play' | 'stop' | 'devices';
export function icon(name: UiIcon, slot?: string) {
  const paths = {
    devices: svg`<rect x="2" y="5" width="12" height="10" rx="1.5"/><path d="M5 19h6M8 15v4"/><rect x="17" y="2" width="5" height="18" rx="1.5"/><path d="M19 17h1"/>`,
    home: svg`<path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z"/>`,
    config: svg`<path d="M4 6h16M4 12h16M4 18h16"/><circle cx="8" cy="6" r="2"/><circle cx="16" cy="12" r="2"/><circle cx="9" cy="18" r="2"/>`,
    reports: svg`<path d="M6 3h9l3 3v15H6Z"/><path d="M14 3v5h4M9 12h6M9 16h6"/>`,
    settings: svg`<path d="m9 3-1 3-3 1v3l-2 2 2 2v3l3 1 1 3h6l1-3 3-1v-3l2-2-2-2V7l-3-1-1-3Z"/><circle cx="12" cy="12" r="3"/>`,
    spark: svg`<path d="m12 2 3 7 7 3-7 3-3 7-3-7-7-3 7-3Z"/><path d="m12 7 2 5-2 5-2-5Z"/>`,
    windows: svg`<rect x="3" y="4" width="18" height="14" rx="2"/><path d="M8 21h8M12 18v3M3 8h18"/>`,
    game: svg`<path d="M7 7h10c3 0 5 12 2 12-2 0-3-4-4-4H9c-1 0-2 4-4 4-3 0-1-12 2-12Z"/><path d="M6 11h4M8 9v4M16 10v1M18 12v1"/>`,
    version: svg`<path d="m12 3 8 5v8l-8 5-8-5V8Z"/><path d="m4 8 8 5 8-5M12 13v8"/>`,
    drag: svg`<circle cx="9" cy="6" r=".75"/><circle cx="15" cy="6" r=".75"/><circle cx="9" cy="12" r=".75"/><circle cx="15" cy="12" r=".75"/><circle cx="9" cy="18" r=".75"/><circle cx="15" cy="18" r=".75"/>`,
    play: svg`<path d="m8 5 12 7-12 7Z"/>`, stop: svg`<rect x="6" y="6" width="12" height="12" rx="2"/>`,
  };
  return svg`<svg slot=${slot ?? nothing} viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths[name]}</svg>`;
}
