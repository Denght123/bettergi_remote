import {EncryptedEnvelope, PairingRecord, PROTOCOL_VERSION} from './types';

const encoder = new TextEncoder();
const decoder = new TextDecoder();

export function base64UrlEncode(value: ArrayBuffer | Uint8Array): string {
  const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

export function base64UrlDecode(value: string): Uint8Array<ArrayBuffer> {
  if (!/^[A-Za-z0-9_-]+$/.test(value) || value.includes('=')) throw new Error('配对密钥格式无效');
  const padding = value.length % 4 === 0 ? '' : value.length % 4 === 2 ? '==' : value.length % 4 === 3 ? '=' : null;
  if (padding === null) throw new Error('配对密钥长度无效');
  const binary = atob(value.replace(/-/g, '+').replace(/_/g, '/') + padding);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

export async function derivePairingRecord(secret: Uint8Array, relayBaseUrl: string, existingDeviceId?: string): Promise<PairingRecord> {
  if (secret.byteLength !== 32) throw new Error('配对密钥必须是 32 字节');
  const channelHash = await crypto.subtle.digest('SHA-256', toArrayBuffer(concat(encoder.encode('BetterGI Remote Lite channel'), secret)));
  const channelId = base64UrlEncode(new Uint8Array(channelHash).slice(0, 16));

  const hmacKey = await crypto.subtle.importKey('raw', toArrayBuffer(secret), {name: 'HMAC', hash: 'SHA-256'}, false, ['sign']);
  const relayToken = base64UrlEncode(await crypto.subtle.sign('HMAC', hmacKey, toArrayBuffer(encoder.encode('BetterGI Remote Lite relay token'))));
  const salt = await crypto.subtle.digest('SHA-256', toArrayBuffer(concat(encoder.encode('BetterGI Remote Lite v1'), encoder.encode(channelId))));
  const hkdfKey = await crypto.subtle.importKey('raw', toArrayBuffer(secret), 'HKDF', false, ['deriveKey']);
  const phoneToPcKey = await deriveAesKey(hkdfKey, salt, 'BetterGI Remote Lite v1 phone-to-pc');
  const pcToPhoneKey = await deriveAesKey(hkdfKey, salt, 'BetterGI Remote Lite v1 pc-to-phone');

  return {
    deviceId: existingDeviceId ?? crypto.randomUUID().replaceAll('-', ''),
    channelId,
    relayToken,
    relayBaseUrl: relayBaseUrl.replace(/\/$/, ''),
    phoneToPcKey,
    pcToPhoneKey,
  };
}

export async function encryptMessage(message: unknown, requestId: string, key: CryptoKey): Promise<EncryptedEnvelope> {
  const issuedAtUnixSeconds = Math.floor(Date.now() / 1000);
  const expiresAtUnixSeconds = issuedAtUnixSeconds + 60;
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const direction = 'phone-to-pc' as const;
  const associatedData = associated(direction, requestId, issuedAtUnixSeconds, expiresAtUnixSeconds);
  const ciphertext = await crypto.subtle.encrypt(
    {name: 'AES-GCM', iv: toArrayBuffer(nonce), additionalData: toArrayBuffer(associatedData), tagLength: 128},
    key,
    toArrayBuffer(encoder.encode(JSON.stringify(message))),
  );
  return {
    protocolVersion: PROTOCOL_VERSION,
    direction,
    requestId,
    issuedAtUnixSeconds,
    expiresAtUnixSeconds,
    nonce: base64UrlEncode(nonce),
    ciphertext: base64UrlEncode(ciphertext),
  };
}

export async function decryptMessage<T>(envelope: EncryptedEnvelope, key: CryptoKey, now = Math.floor(Date.now() / 1000)): Promise<T> {
  if (envelope.protocolVersion !== PROTOCOL_VERSION || envelope.direction !== 'pc-to-phone') throw new Error('消息版本或方向无效');
  if (envelope.expiresAtUnixSeconds <= envelope.issuedAtUnixSeconds || envelope.expiresAtUnixSeconds - envelope.issuedAtUnixSeconds > 60) {
    throw new Error('消息有效期无效');
  }
  if (envelope.issuedAtUnixSeconds > now + 30 || envelope.expiresAtUnixSeconds < now - 30) throw new Error('消息已过期');
  const plaintext = await crypto.subtle.decrypt(
    {
      name: 'AES-GCM',
      iv: toArrayBuffer(base64UrlDecode(envelope.nonce)),
      additionalData: toArrayBuffer(associated(envelope.direction, envelope.requestId, envelope.issuedAtUnixSeconds, envelope.expiresAtUnixSeconds)),
      tagLength: 128,
    },
    key,
    toArrayBuffer(base64UrlDecode(envelope.ciphertext)),
  );
  return JSON.parse(decoder.decode(plaintext)) as T;
}

function associated(direction: string, requestId: string, issuedAt: number, expiresAt: number): Uint8Array {
  return encoder.encode(`${PROTOCOL_VERSION}\n${direction}\n${requestId}\n${issuedAt}\n${expiresAt}`);
}

function concat(left: Uint8Array, right: Uint8Array): Uint8Array<ArrayBuffer> {
  const result = new Uint8Array(new ArrayBuffer(left.length + right.length));
  result.set(left);
  result.set(right, left.length);
  return result;
}

function deriveAesKey(base: CryptoKey, salt: ArrayBuffer, info: string): Promise<CryptoKey> {
  return crypto.subtle.deriveKey(
    {name: 'HKDF', hash: 'SHA-256', salt, info: toArrayBuffer(encoder.encode(info))},
    base,
    {name: 'AES-GCM', length: 256},
    false,
    ['encrypt', 'decrypt'],
  );
}

function toArrayBuffer(value: Uint8Array): ArrayBuffer {
  return value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength) as ArrayBuffer;
}
