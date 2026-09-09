# BetterGI Remote 0.3.0

## Highlights

- 180-day renewable phone binding with expiry reminders and desktop unbind/rebind controls.
- QQ Mail SMTP notifications alongside the existing Feishu webhook integration.
- Explicit `config.sync` support so newly added BetterGI one-dragon tasks appear on the phone.
- Redesigned mobile and Windows interfaces with a multi-character fantasy-adventure visual system.
- A permanent control-page reminder in the Windows tray and settings window.
- GitHub Release update checks with trusted installer naming and SHA-256 verification.

## Reliability and compatibility

- Existing 0.2.1 bindings without an expiry date migrate automatically to a 180-day lease.
- Older 0.2.1 agents remain compatible with the PWA; manual sync falls back to `config.get` when `config.sync` is unavailable.
- Updates are blocked while a BetterGI task is running.
- Silent upgrades force-close the tray agent only after user confirmation in the app, then restart the new version.
- BetterGI configuration changes remain restricted to the existing whitelist and atomic backup flow.

## Verification

- 22 .NET tests passed.
- 6 PWA tests, TypeScript type checking and production build passed.
- Go relay tests and Windows Agent Release build passed with zero warnings.
- 0.2.1 to 0.3.0 installer overwrite and process restart were tested locally.
- The production asset set contains no previous website screenshot files.

The Windows installer is unsigned. Verify the SHA-256 shown in the GitHub Release and README before running it.
