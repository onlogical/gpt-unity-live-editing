import { mkdir, readFile, writeFile, rename } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';

const validId = id => typeof id === 'string' && /^[A-Za-z0-9_-]{1,100}$/.test(id);
export class BridgeClient {
  constructor(projectPath, { timeoutMs = 45000 } = {}) {
    if (!projectPath || !path.isAbsolute(projectPath)) throw new Error('Supply an absolute Unity project path.');
    this.root = path.join(projectPath, '.codex-bridge'); this.timeoutMs = timeoutMs;
  }
  async status() {
    const status = JSON.parse(await readFile(path.join(this.root, 'status.json'), 'utf8'));
    const age = Date.now() - Date.parse(status.timestamp);
    if (!Number.isFinite(age) || age > 30000 || age < -30000) throw new Error('Unity heartbeat is stale. Open Unity and finish installing the bridge.');
    if (status.protocolVersion < 2 || !status.protocolVersion) throw new Error('Update Unity Live Bridge to 0.3.0 or later (queue protocol 2).');
    return status;
  }
  async atomic(relative, payload) {
    const target = path.join(this.root, relative); await mkdir(path.dirname(target), { recursive: true });
    const temp = target + '.' + randomUUID() + '.tmp';
    await writeFile(temp, JSON.stringify(payload), { flag: 'wx' });
    await rename(temp, target);
  }
  async submit(action, args = {}) {
    await this.status();
    const id = randomUUID();
    await this.atomic(path.join('queue', `${Date.now().toString().padStart(16, '0')}-${id}.json`), { ...args, id, action });
    return { id, outcome: 'queued' };
  }
  async result(id) {
    if (!validId(id)) throw new Error('Invalid request ID.');
    try { return JSON.parse(await readFile(path.join(this.root, 'responses', id + '.json'), 'utf8')); }
    catch (error) { if (error.code === 'ENOENT') return { id, outcome: 'pending' }; throw error; }
  }
  async cancel(id) {
    if (!validId(id)) throw new Error('Invalid request ID.');
    const result = await this.result(id);
    if (result.outcome !== 'pending') return { id, outcome: 'already_finished', result };
    await this.atomic(path.join('cancel', id + '.json'), { id });
    return { id, outcome: 'cancellation_requested', note: 'Queued work is skipped; an active single command may already have completed. Check its result.' };
  }
  async wait(id, { signal, timeoutMs = this.timeoutMs } = {}) {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      const result = await this.result(id);
      if (result.outcome !== 'pending') return result;
      if (signal?.aborted || Date.now() >= deadline) {
        await this.cancel(id);
        throw new Error(`Request ${id}: ${signal?.aborted ? 'cancelled by caller' : 'timed out'}; cancellation requested. Inspect unity_result before retrying.`);
      }
      await delay(100);
    }
  }
  async command(action, args = {}, options = {}) { const { id } = await this.submit(action, args); return this.wait(id, options); }
}
