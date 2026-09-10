# BetterGI Remote 0.3.1

## Fixes

- Fixed the Windows settings window crashing with a misleading `System.OutOfMemoryException` when GDI+ attempted to decode the WebP header artwork.
- The desktop agent now ships a JPEG copy of the banner and safely falls back to its solid-color header if the image is missing or damaged.
- Added an optional “创建桌面快捷方式” task to the installer. It is unchecked by default so upgrades do not add a shortcut without the user's choice.

## Verification

- Windows settings startup was tested with a clean temporary data directory.
- The missing-image fallback was tested, and the Inno Setup script compiled successfully with the optional desktop-shortcut task.
- Full .NET, PWA and Go verification passed before release.

The Windows installer is unsigned. Verify this SHA-256 before running it:

`B9E28BAE574AB6606D251FA00E6BEEBB2CA2592A6BB67CCF06CCB559A4362674`
