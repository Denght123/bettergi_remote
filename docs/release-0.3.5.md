# BetterGI Remote 0.3.5

## Improvements

- Refined the Windows settings interface with larger consistent icons, more balanced spacing, clearer maintenance actions, and a first-screen notification status entry.
- Replaced native-looking input artifacts with dark custom controls, including a fully drawn configuration selector without white borders or misaligned arrows.
- Fixed high-DPI title, label, input, and icon clipping issues.
- Added composited double-buffered scrolling to reduce visual tearing while expanded notification settings are scrolled.
- Added a single-window guard and click debounce so repeated tray interactions activate the existing settings window instead of opening duplicates.

## Verification

- Full Go, PWA, and .NET verification passed before release.
- 32 .NET tests and 6 PWA tests passed.
- The Windows installer was rebuilt from the verified release output and staged with a SHA-256 update manifest.

The Windows installer is unsigned. Verify this SHA-256 before running it:

`93d0bcaf680432d2464b8c1c150685834dd59119357c959eb6b7a461669e2371`
