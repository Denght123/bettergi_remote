# BetterGI Remote 0.2.0

This release turns the development setup into a one-install, one-scan onboarding flow.

## Changes

- Built-in production relay: `https://bgiremote.163831.xyz`.
- Automatic discovery of BetterGI 0.64.x on common Windows drive locations.
- Simplified first-run window; relay, hotkey, and Feishu fields are under advanced settings.
- Pairing window reports completion without requiring the user to revisit settings.
- PWA requests persistent browser storage and reports whether binding data is protected.
- Multiple PWA pages coordinate ownership; a newer authenticated connection safely replaces a stale same-role WebSocket.
- The scheduled task restarts the Windows agent after an unexpected exit.
- Temporary `trycloudflare.com` development settings migrate to the production relay and require one final production pairing.

## Compatibility

The encrypted protocol and BetterGI configuration format remain compatible with 0.1.0. Deploy the 0.2.0 relay and PWA only after local end-to-end verification.
