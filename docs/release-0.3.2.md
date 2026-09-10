# BetterGI Remote 0.3.2

## Improvements

- Replaced the previous character banner with the owner-provided spring celebration artwork and Paimon guide image across the mobile web interface and Windows agent.
- Updated buttons, panels, status colors and empty states to a lighter grass green, aqua, violet and cream palette derived from the new artwork.
- Added visible desktop settings actions for the pairing QR code, current status, mobile control page, rebind, unbind and update checks.
- Added an uninstall button that launches the Inno Setup uninstaller from the current installation directory after confirmation. Local settings and pairing data are preserved by default.

## Verification

- Full Go, PWA and .NET verification passed.
- Desktop and 390 px mobile web layouts were visually reviewed.
- Windows settings startup and packaged image loading were smoke-tested.

The Windows installer is unsigned. Verify this SHA-256 before running it:

`4021be0198973f548f6cf0027e29d86818149917959979ced30b7f7ac00fb6d5`
