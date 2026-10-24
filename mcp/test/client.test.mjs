import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readdir, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { BridgeClient } from '../bridge-client.mjs';
async function fixture(t) {
  const project = await mkdtemp(path.join(os.tmpdir(), 'unity-bridge-'));
  t.after(() => rm(project, { recursive: true, force: true }));
  const root = path.join(project, '.codex-bridge'); await mkdir(root);
  await writeFile(path.join(root, 'status.json'), JSON.stringify({ timestamp: new Date().toISOString(), protocolVersion: 2, unityVersion: 'mock' }));
  return { client: new BridgeClient(project), root };
}
test('requires absolute project path', () => assert.throws(() => new BridgeClient('relative')));
test('stale heartbeat does not submit work', async t => {
  const { client, root } = await fixture(t);
  await writeFile(path.join(root, 'status.json'), JSON.stringify({ timestamp: new Date(0).toISOString(), protocolVersion: 2 }));
  await assert.rejects(client.submit('create'), /stale/);
  assert.deepEqual(await readdir(root), ['status.json']);
});
test('parallel submissions have independent queue files and preserve reserved fields', async t => {
  const { client, root } = await fixture(t);
  const jobs = await Promise.all(Array.from({ length: 20 }, () => client.submit('ping', { id: 'override', action: 'delete' })));
  assert.equal(new Set(jobs.map(j => j.id)).size, 20);
  const files = await readdir(path.join(root, 'queue')); assert.equal(files.length, 20);
  for (const file of files) { const body = JSON.parse(await readFile(path.join(root, 'queue', file), 'utf8')); assert.equal(body.action, 'ping'); assert.notEqual(body.id, 'override'); }
});
test('pending requests can be cancelled', async t => {
  const { client, root } = await fixture(t); const { id } = await client.submit('ping');
  assert.equal((await client.cancel(id)).outcome, 'cancellation_requested');
  assert.equal(JSON.parse(await readFile(path.join(root, 'cancel', id + '.json'), 'utf8')).id, id);
});
test('finished results are preserved when cancellation is late', async t => {
  const { client, root } = await fixture(t); const { id } = await client.submit('ping');
  await mkdir(path.join(root, 'responses')); await writeFile(path.join(root, 'responses', id + '.json'), JSON.stringify({ id, ok: true, outcome: 'completed' }));
  assert.equal((await client.cancel(id)).outcome, 'already_finished');
});
test('timeouts request cancellation and report the request ID', async t => {
  const { client, root } = await fixture(t); const { id } = await client.submit('ping');
  await assert.rejects(client.wait(id, { timeoutMs: 1 }), error => error.message.includes(id));
  assert.equal((await readdir(path.join(root, 'cancel'))).length, 1);
});
test('caller abort requests cancellation', async t => {
  const { client } = await fixture(t); const { id } = await client.submit('ping'); const controller = new AbortController(); controller.abort();
  await assert.rejects(client.wait(id, { signal: controller.signal }), /cancelled by caller/);
});
test('request paths cannot escape bridge directories', async t => {
  const { client } = await fixture(t);
  await assert.rejects(client.result('../secret'), /Invalid/); await assert.rejects(client.cancel('../secret'), /Invalid/);
});
