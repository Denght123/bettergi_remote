import type {RemoteConfig} from './types';

export async function readFullConfig(
  fetchPage: (params: {offset: number; limit: number; revision?: string}) => Promise<RemoteConfig>,
  initial?: RemoteConfig,
  onProgress?: (read: number, total: number) => void,
): Promise<RemoteConfig> {
  const first = initial ?? await fetchPage({offset: 0, limit: 32});
  const fields = [...first.fields];
  const seen = new Set(fields.map(field => field.path));
  onProgress?.(fields.length, first.totalFields ?? fields.length);
  let page = first;
  let offset = 0;
  while (page.nextFieldOffset != null) {
    if (page.nextFieldOffset <= offset) throw new Error('电脑配置同步没有完成，请重新同步。');
    offset = page.nextFieldOffset;
    page = await fetchPage({offset, limit: 64, revision: first.revision});
    if (page.revision !== first.revision || page.totalFields !== first.totalFields) {
      throw new Error('电脑配置在同步期间发生变化，请重新同步后再编辑。');
    }
    for (const field of page.fields) {
      if (seen.has(field.path)) throw new Error('电脑配置重复，请重新同步。');
      seen.add(field.path);
      fields.push(field);
    }
    onProgress?.(fields.length, first.totalFields ?? fields.length);
  }
  if (first.totalFields != null && fields.length !== first.totalFields) {
    throw new Error('电脑配置没有完整读取，请重新同步后再保存。');
  }
  return {...first, fields, nextFieldOffset: undefined};
}
