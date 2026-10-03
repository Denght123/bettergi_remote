# Protocol v1

## Pair material

A computer generates 32 random bytes called the pairing secret. The secret appears only in the URL fragment of a five-minute pairing link and is never sent in an HTTP request.

HKDF-SHA256 derives:

- channel ID: first 16 bytes of `SHA-256("BetterGI Remote Lite channel" || secret)`, Base64Url encoded;
- relay token: `HMAC-SHA256(secret, "BetterGI Remote Lite relay token")`, Base64Url encoded;
- phone-to-PC key: HKDF info `BetterGI Remote Lite v1 phone-to-pc`;
- PC-to-phone key: HKDF info `BetterGI Remote Lite v1 pc-to-phone`.

The HKDF salt is `SHA-256("BetterGI Remote Lite v1" || channel-id-utf8)`.

## Relay registration

The first WebSocket message is UTF-8 text:

```json
{
  "type": "register",
  "protocolVersion": 1,
  "channelId": "base64url-16-bytes",
  "role": "pc",
  "relayToken": "base64url-32-bytes"
}
```

After registration, application messages are binary UTF-8 JSON. The relay checks only frame size, rate, room role, and matching relay-token hash. It never decrypts the envelope.

## Encrypted envelope

```json
{
  "protocolVersion": 1,
  "direction": "phone-to-pc",
  "requestId": "uuid",
  "issuedAtUnixSeconds": 1788364800,
  "expiresAtUnixSeconds": 1788364860,
  "nonce": "base64url-12-bytes",
  "ciphertext": "base64url-ciphertext-and-tag"
}
```

AES-256-GCM uses a random 12-byte nonce. WebCrypto returns ciphertext with the 16-byte authentication tag appended, and .NET uses the same representation. Associated data is the UTF-8 string:

```text
1\ndirection\nrequestId\nissuedAtUnixSeconds\nexpiresAtUnixSeconds
```

Messages expire after at most 60 seconds. Receivers reject duplicate request IDs for ten minutes and reject unexpected directions or bound device IDs.

## Plaintext messages

RPC request:

```json
{"type":"rpc.request","requestId":"uuid","senderDeviceId":"uuid","method":"status.get","params":{}}
```

RPC response:

```json
{"type":"rpc.response","requestId":"uuid","ok":true,"result":{}}
```

Push:

```json
{"type":"push","event":"status.changed","data":{}}
```

Allowed methods are `pair.request`, `status.get`, `config.get`, `config.sync`, `config.update`, `task.start`, `task.stop`, and `report.latest`.

## Optional expanded configuration and reports

Protocol v1 encryption, binding, replay protection and the 48 KiB plaintext limit remain unchanged.

- `config.get` and `config.sync` accept optional `offset`, `limit` and `revision`. Results may contain `nextFieldOffset` and `totalFields`; subsequent pages use the first page's revision. A five-minute PC snapshot keeps the read consistent. Save checks the actual participating files again, so an outdated snapshot cannot overwrite a desktop edit.
- `config.update` still accepts `baseRevision`, `tasks`, and `values`. New phones send changed values only. Config replies are paged and use scopes `oneDragon`, `global`, and `scriptGroup`. Metadata comes from the PC's reviewed catalog and installed script settings; RPC never accepts a file path, code or arbitrary new setting.
- `status.get` and `status.changed` may contain `currentProgress`. Progress adds `currentStep`, `currentLocation` and task state rows, allowing reconnecting phones to recover the active run.
- `report.latest` accepts `offset`, `limit` and `runId`; results may contain `nextLootOffset` and `totalLootEntries`. Pages must belong to the same run. `run.completed` carries the first page, then the phone retrieves the rest.
- `rewards`/`dailyRewards` are reward-recognition quantities; `pickupObservations` counts logged interactions; `woodEstimates` is an estimate. These sources must not be summed into an inventory-gain total. Raw `logExcerpt` stays in PC report files rather than being sent in the mobile result.

Existing phones can read the first configuration/report page but need the updated PWA to use the expanded catalog and complete paging. The new phone accepts old replies with no paging or progress fields. Release the client and PWA together to expose the new features consistently.

