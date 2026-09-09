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

Allowed methods are `pair.request`, `status.get`, `config.get`, `config.update`, `task.start`, `task.stop`, and `report.latest`.

