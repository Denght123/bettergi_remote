# BetterGI Remote 0.3.4

## Fixes

- Fixed update checks failing when the Windows computer cannot connect directly to `github.com:443` or the GitHub API.
- Remote update metadata and installer downloads now prefer the official BetterGI Remote domain, with GitHub retained only as a fallback metadata source.
- Installer downloads retry transient network failures and remain protected by mandatory SHA-256 verification before launch.
- BetterGI upstream version checks now use a validated, cached VPS endpoint before attempting GitHub directly.
- Added dedicated non-SPA server routes for the update manifest and installer, preventing missing files from falling back to the web application shell.

## Upgrade note

Versions up to 0.3.3 still use GitHub directly. Users affected by the connection error must manually install 0.3.4 once from:

https://bgiremote.163831.xyz/downloads/BetterGI.Remote.Setup.0.3.4.exe

After that one-time manual upgrade, in-app updates use the BetterGI Remote service first.

## Verification

- Full Go, PWA and .NET verification passed.
- 32 .NET tests and 6 PWA tests passed.
- The release manifest parser rejects insecure download URLs and invalid installer metadata.
- The relay tests verify update-file routing and cached BetterGI release lookup.

The Windows installer is unsigned. Verify this SHA-256 before running it:

`1e31daee84956a691583fc57806127c3b130219bf20f17c04eb5f20b1ab81654`
