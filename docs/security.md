# Security model

## Protected assets

- The capability to start or cancel BetterGI automation.
- BetterGI one-dragon and selected global configuration values.
- Task status, log excerpts, recognized rewards, and Feishu credentials.
- The one-to-one pairing secret and browser device identity.

## Invariants

1. The relay never receives the pairing secret or application plaintext.
2. A phone request cannot supply an executable, path, shell command, process name, or arbitrary key.
3. The computer accepts only the locally bound browser device ID.
4. Configuration writes require BetterGI to be closed and use an optimistic SHA-256 revision.
5. Unknown JSON properties are preserved and non-allowlisted paths cannot be written.
6. Expired, replayed, incorrectly directed, or modified encrypted envelopes are rejected.
7. No command is queued while the computer or phone is offline.

## Local storage

Windows protects the pairing secret and Feishu credentials with DPAPI `CurrentUser`. The browser stores non-exportable AES-GCM CryptoKeys in IndexedDB. A compromised Windows session, browser profile, rooted phone, or BetterGI process is outside the v1 threat model.

## Relay metadata

The relay necessarily observes IP addresses, connection timing, channel IDs, role, and frame size. It keeps matching relay-token hashes only while a room has a live socket. It does not write rooms, frames, queries, or tokens to disk.

## Rebinding

Rebinding rotates the pairing secret and derived channel, relay token, and encryption keys. The previous phone can no longer reconnect. A new pairing request is accepted only during a five-minute local pairing window and still requires a Windows confirmation dialog.

