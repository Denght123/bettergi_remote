import {decryptMessage, encryptMessage} from './crypto';
import {ConnectionState, EncryptedEnvelope, PairingRecord, PushMessage, RpcResponse} from './types';

const encoder = new TextEncoder();
const decoder = new TextDecoder();

export class RemoteRpcError extends Error {
  constructor(public readonly code: string, message: string, public readonly retryable = false, public readonly data?: unknown) {
    super(message);
  }
}

export class RemoteClient {
  private socket?: WebSocket;
  private stopped = false;
  private reconnectDelay = 1000;
  private readonly pending = new Map<string, {resolve(value: unknown): void; reject(reason: unknown): void; timer: number}>();

  constructor(
    private readonly pairing: PairingRecord,
    private readonly onConnection: (state: ConnectionState) => void,
    private readonly onPush: (message: PushMessage) => void,
  ) {}

  start(): void {
    this.stopped = false;
    void this.connect();
  }

  close(): void {
    this.stopped = true;
    this.socket?.close(1000, 'phone closing');
    this.socket = undefined;
    for (const pending of this.pending.values()) {
      window.clearTimeout(pending.timer);
      pending.reject(new Error('连接已关闭'));
    }
    this.pending.clear();
  }

  async call<T>(method: string, params: unknown = {}, timeoutMs = 15_000): Promise<T> {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) throw new Error('电脑当前不在线');
    const requestId = crypto.randomUUID().replaceAll('-', '');
    const message = {
      type: 'rpc.request',
      requestId,
      senderDeviceId: this.pairing.deviceId,
      method,
      params,
    };
    const envelope = await encryptMessage(message, requestId, this.pairing.phoneToPcKey);
    const response = new Promise<T>((resolve, reject) => {
      const timer = window.setTimeout(() => {
        this.pending.delete(requestId);
        reject(new Error(method === 'pair.request'
          ? '电脑端确认超时，请重新扫码并在电脑上及时确认'
          : '电脑响应超时'));
      }, timeoutMs);
      this.pending.set(requestId, {resolve: value => resolve(value as T), reject, timer});
    });
    this.socket.send(encoder.encode(JSON.stringify(envelope)));
    return response;
  }

  private async connect(): Promise<void> {
    while (!this.stopped) {
      try {
        this.onConnection('connecting');
        await this.openOnce();
        this.reconnectDelay = 1000;
      } catch {
        if (!this.stopped) this.onConnection('offline');
      }
      if (this.stopped) return;
      await new Promise(resolve => window.setTimeout(resolve, this.reconnectDelay));
      this.reconnectDelay = Math.min(30_000, this.reconnectDelay * 2);
    }
  }

  private openOnce(): Promise<void> {
    return new Promise((resolve, reject) => {
      const socket = new WebSocket(toWebSocketUrl(this.pairing.relayBaseUrl));
      socket.binaryType = 'arraybuffer';
      this.socket = socket;
      socket.onopen = () => {
        socket.send(JSON.stringify({
          type: 'register',
          protocolVersion: 1,
          channelId: this.pairing.channelId,
          role: 'phone',
          relayToken: this.pairing.relayToken,
        }));
        this.onConnection('waiting_for_pc');
      };
      socket.onmessage = event => void this.handleMessage(event.data);
      socket.onerror = () => reject(new Error('中转连接失败'));
      socket.onclose = () => {
        if (!this.stopped) this.onConnection('offline');
        resolve();
      };
    });
  }

  private async handleMessage(data: string | ArrayBuffer | Blob): Promise<void> {
    if (typeof data === 'string') {
      this.handleControl(data);
      return;
    }
    const bytes = data instanceof Blob ? new Uint8Array(await data.arrayBuffer()) : new Uint8Array(data);
    try {
      const envelope = JSON.parse(decoder.decode(bytes)) as EncryptedEnvelope;
      const plaintext = await decryptMessage<RpcResponse<unknown> | PushMessage>(envelope, this.pairing.pcToPhoneKey);
      if (plaintext.type === 'rpc.response') {
        const pending = this.pending.get(plaintext.requestId);
        if (!pending) return;
        this.pending.delete(plaintext.requestId);
        window.clearTimeout(pending.timer);
        if (plaintext.ok) pending.resolve(plaintext.result);
        else pending.reject(new RemoteRpcError(plaintext.error?.code ?? 'remote_error', plaintext.error?.message ?? '电脑拒绝了操作', plaintext.error?.retryable, plaintext.error?.data));
      } else if (plaintext.type === 'push') {
        this.onPush(plaintext);
      }
    } catch {
      this.onConnection('error');
    }
  }

  private handleControl(payload: string): void {
    try {
      const control = JSON.parse(payload) as {type?: string; online?: boolean; code?: string};
      if (control.type === 'peer_status') this.onConnection(control.online ? 'online' : 'waiting_for_pc');
      if (control.type === 'error' && control.code === 'role_replaced') {
        this.stopped = true;
        this.onConnection('offline');
        this.socket?.close(1000, 'newer phone page active');
      } else if (control.type === 'error' && control.code !== 'peer_offline') {
        this.onConnection('error');
      }
    } catch {
      this.onConnection('error');
    }
  }
}

function toWebSocketUrl(baseUrl: string): string {
  const url = new URL('/ws', baseUrl);
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  return url.toString();
}
