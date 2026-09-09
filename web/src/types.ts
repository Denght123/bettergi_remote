export const PROTOCOL_VERSION = 1;

export type ConnectionState = 'unpaired' | 'connecting' | 'waiting_for_pc' | 'online' | 'offline' | 'error';
export type ViewName = 'home' | 'config' | 'reports' | 'settings';

export interface PairingRecord {
  deviceId: string;
  channelId: string;
  relayToken: string;
  relayBaseUrl: string;
  phoneToPcKey: CryptoKey;
  pcToPhoneKey: CryptoKey;
  pcDeviceId?: string;
  pcName?: string;
}

export interface EncryptedEnvelope {
  protocolVersion: number;
  direction: 'phone-to-pc' | 'pc-to-phone';
  requestId: string;
  issuedAtUnixSeconds: number;
  expiresAtUnixSeconds: number;
  nonce: string;
  ciphertext: string;
}

export interface RpcError {
  code: string;
  message: string;
  retryable: boolean;
  data?: unknown;
}

export interface RpcResponse<T> {
  type: 'rpc.response';
  requestId: string;
  ok: boolean;
  result?: T;
  error?: RpcError;
}

export interface PushMessage<T = unknown> {
  type: 'push';
  event: 'status.changed' | 'run.progress' | 'run.completed';
  data: T;
}

export interface AgentStatus {
  pcName: string;
  pcDeviceId: string;
  phonePeerOnline: boolean;
  windowsUnlocked: boolean;
  betterGiConfigured: boolean;
  betterGiVersionSupported: boolean;
  betterGiVersion?: string;
  betterGiRunning: boolean;
  gameRunning: boolean;
  state: string;
  activeRunId?: string;
  activeTask?: string;
  observedAt: string;
  message?: string;
}

export interface TaskItem {
  id: string;
  name: string;
  enabled: boolean;
  isCustom: boolean;
  order: number;
}

export type EditableFieldType = 'text' | 'number' | 'toggle' | 'select' | 'multiSelect';

export interface EditableField {
  path: string;
  scope: 'oneDragon' | 'global';
  group: string;
  label: string;
  type: EditableFieldType;
  value: unknown;
  options?: string[];
  minimum?: number;
  maximum?: number;
  description?: string;
}

export interface RemoteConfig {
  name: string;
  revision: string;
  tasks: TaskItem[];
  fields: EditableField[];
  completionAction: string;
  readAt: string;
}

export interface RunProgress {
  runId: string;
  state: string;
  currentTask?: string;
  completedTasks: number;
  totalTasks: number;
  message?: string;
  observedAt: string;
}

export interface RunTaskResult {
  name: string;
  state: string;
  message?: string;
}

export interface RunReport {
  runId: string;
  status: string;
  startedAt: string;
  finishedAt: string;
  durationSeconds: number;
  tasks: RunTaskResult[];
  rewards: Record<string, number>;
  dailyRewardStatus?: string;
  errors: string[];
  logExcerpt: string[];
  parserVersion: string;
}

