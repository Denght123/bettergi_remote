import {PairingRecord} from './types';

const DATABASE = 'bettergi-remote-lite';
const STORE = 'settings';
const PAIRING_KEY = 'pairing';

export async function loadPairing(): Promise<PairingRecord | undefined> {
  return transaction('readonly', store => store.get(PAIRING_KEY));
}

export async function savePairing(pairing: PairingRecord): Promise<void> {
  await transaction('readwrite', store => store.put(pairing, PAIRING_KEY));
}

export async function clearPairing(): Promise<void> {
  await transaction('readwrite', store => store.delete(PAIRING_KEY));
}

export async function persistentStorageState(): Promise<'persistent' | 'managed' | 'unsupported'> {
  if (!navigator.storage?.persisted) return 'unsupported';
  return (await navigator.storage.persisted()) ? 'persistent' : 'managed';
}

export async function requestPersistentStorage(): Promise<'persistent' | 'managed' | 'unsupported'> {
  if (!navigator.storage?.persist) return 'unsupported';
  return (await navigator.storage.persist()) ? 'persistent' : 'managed';
}

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const open = indexedDB.open(DATABASE, 1);
    open.onupgradeneeded = () => {
      if (!open.result.objectStoreNames.contains(STORE)) open.result.createObjectStore(STORE);
    };
    open.onsuccess = () => resolve(open.result);
    open.onerror = () => reject(open.error ?? new Error('无法打开手机安全存储'));
  });
}

async function transaction<T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const database = await openDatabase();
  return new Promise<T>((resolve, reject) => {
    const tx = database.transaction(STORE, mode);
    const operation = action(tx.objectStore(STORE));
    tx.oncomplete = () => {
      const result = operation.result;
      database.close();
      resolve(result);
    };
    tx.onerror = () => {
      database.close();
      reject(tx.error ?? operation.error ?? new Error('手机安全存储写入失败'));
    };
    tx.onabort = () => {
      database.close();
      reject(tx.error ?? new Error('手机安全存储操作已取消'));
    };
    operation.onerror = () => reject(operation.error ?? new Error('手机安全存储操作失败'));
  });
}
