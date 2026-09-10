# BetterGI Remote 0.3.3

## Improvements

- Rebuilt the Windows settings experience as a compact dark desktop console with consistent line icons, aligned controls, modern inputs, clear primary and destructive actions, and native 16 ms hover/pressed transitions.
- Removed the intrusive native scrollbar from the normal collapsed settings view and tightened the layout so the important configuration and maintenance actions remain visible together.
- Added daily BetterGI upstream release checks, tray notifications, manual update checks, and mobile warnings when the installed BetterGI version is behind the official release.
- Kept BetterGI 0.64.x as the verified baseline while allowing newer releases only after their global and OneDragon configuration structures pass compatibility probing.
- Automatically enables BetterGI reward recognition when settings are saved.
- Added per-run and per-day reward totals to the completion report, including explicit complete, partial, and failed recognition states.

## Verification

- Full Go, PWA and .NET verification passed.
- 29 .NET tests and 6 PWA tests passed.
- Windows Release build completed with zero warnings and zero errors.
- The 1120 × 800 first-run window was visually reviewed in two bounded passes.
- BetterGI 0.64.0 compatibility and representative real reward-log formats were verified.

The Windows installer is unsigned. Verify this SHA-256 before running it:

`27346c27efd31323ef78ef2424719d296d6b4c1917a4e9251b11b5f54fbce414`
