import {describe, expect, it} from 'vitest';
import vector from '../../protocol/test-vector-v1.json';
import {base64UrlDecode, base64UrlEncode, decryptMessage, derivePairingRecord, encryptMessage} from './crypto';

describe('crypto interoperability primitives', () => {
  it('derives stable channel and relay identifiers', async () => {
    const secret = Uint8Array.from({length: 32}, (_, index) => index);
    const first = await derivePairingRecord(secret, 'https://remote.example.com', 'device');
    const second = await derivePairingRecord(secret, 'https://remote.example.com', 'device');
    expect(first.channelId).toBe(second.channelId);
    expect(first.relayToken).toBe(second.relayToken);
    expect(first.channelId).toHaveLength(22);
    expect(first.relayToken).toHaveLength(43);
  });

  it('uses canonical base64url and AES-GCM envelopes', async () => {
    const bytes = Uint8Array.from([0, 1, 2, 253, 254, 255]);
    expect(base64UrlDecode(base64UrlEncode(bytes))).toEqual(bytes);
    const pairing = await derivePairingRecord(Uint8Array.from({length: 32}, (_, index) => index), 'https://remote.example.com', 'device');
    const envelope = await encryptMessage({type: 'test'}, 'request', pairing.phoneToPcKey);
    expect(envelope.direction).toBe('phone-to-pc');
    expect(base64UrlDecode(envelope.nonce)).toHaveLength(12);
  });

  it('decrypts the published .NET and Node vector', async () => {
    const pairing = await derivePairingRecord(base64UrlDecode(vector.secret), 'https://remote.example.com', 'device');
    expect(pairing.channelId).toBe(vector.channelId);
    expect(pairing.relayToken).toBe(vector.relayToken);
    const plaintext = await decryptMessage<{result: {value: string}}>({
      protocolVersion: 1,
      direction: 'pc-to-phone',
      requestId: vector.requestId,
      issuedAtUnixSeconds: vector.issuedAtUnixSeconds,
      expiresAtUnixSeconds: vector.expiresAtUnixSeconds,
      nonce: vector.nonce,
      ciphertext: vector.ciphertext,
    }, pairing.pcToPhoneKey, vector.issuedAtUnixSeconds);
    expect(plaintext.result.value).toBe('跨平台');
  });
});
