import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { access, unlink, rmdir } from 'node:fs/promises';
import { BridgeClient } from '../bridge-client.mjs';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

test('Unity queue, cancellation, Undo batches, rollback, deduplication and recovery', { skip: !process.env.UNITY_TEST_PROJECT, timeout: 90000 }, async t => {
  const client = new BridgeClient(process.env.UNITY_TEST_PROJECT); const prefix = 'QueueTest-' + randomUUID();
  t.after(async () => {
    const state = await client.command('hierarchy');
    for (const item of state.objects.filter(o => o.name.startsWith(prefix))) await client.command('delete', { instanceId: item.instanceId });
    await client.command('clear_errors');
  });
  const jobs = await Promise.all(Array.from({ length: 8 }, (_, i) => client.submit('create_empty', { name: `${prefix}-${i}` })));
  const replies = await Promise.all(jobs.map(j => client.wait(j.id)));
  assert.ok(replies.every(r => r.ok)); assert.equal(new Set(replies.map(r => r.instanceId)).size, 8);
  const hierarchy = await client.command('hierarchy'); assert.equal(hierarchy.objects.filter(o => o.name.startsWith(prefix)).length, 8);
  // Cancel marker exists before enqueue, so this test cannot race the editor tick.
  const cancelled = randomUUID(); await client.cancel(cancelled);
  await client.atomic(path.join('queue', `0000000000000000-${cancelled}.json`), { id: cancelled, action: 'create_empty', name: prefix + '-cancelled' });
  assert.equal((await client.wait(cancelled)).outcome, 'cancelled');
  assert.equal((await client.command('hierarchy')).objects.some(o => o.name === prefix + '-cancelled'), false);
  const batch = await client.command('batch', { label: 'Integration batch', commands: [
    { action: 'create_empty', name: prefix + '-batch' }, { action: 'rename', instanceId: '$0', name: prefix + '-batch-renamed' }
  ] });
  assert.equal(batch.ok, true); assert.equal(batch.results.length, 2);
  await client.command('undo');
  assert.equal((await client.command('hierarchy')).objects.some(o => o.name === prefix + '-batch-renamed'), false);
  const failed = await client.command('batch', { commands: [{ action: 'create_empty', name: prefix + '-rollback' }, { action: 'rename', instanceId: 'missing', name: 'bad' }] });
  assert.equal(failed.outcome, 'rolled_back'); assert.equal((await client.command('hierarchy')).objects.some(o => o.name === prefix + '-rollback'), false);
  const unsupported = await client.command('batch', { commands: [{ action: 'create_empty', name: prefix + '-forbidden' }, { action: 'save' }] });
  assert.equal(unsupported.ok, false); assert.equal((await client.command('hierarchy')).objects.some(o => o.name === prefix + '-forbidden'), false);
  const replay = replies[0];
  await client.atomic(path.join('queue', `0000000000000000-${replay.id}.json`), { id: replay.id, action: 'create_empty', name: prefix + '-replayed' });
  assert.equal((await client.command('hierarchy')).objects.some(o => o.name === prefix + '-replayed'), false);
  const interrupted = randomUUID();
  await client.atomic(path.join('processing', interrupted + '.json'), { id: interrupted, action: 'create_empty', name: prefix + '-interrupted' });
  assert.equal((await client.wait(interrupted)).outcome, 'interrupted');
  assert.equal((await client.command('hierarchy')).objects.some(o => o.name === prefix + '-interrupted'), false);
});

test('real MCP stdio handshake, tool discovery and Unity edit', { skip: !process.env.UNITY_TEST_PROJECT, timeout: 30000 }, async t => {
  const serverPath = fileURLToPath(new URL('../server.mjs', import.meta.url));
  const transport = new StdioClientTransport({ command: process.execPath, args: [serverPath, '--project', process.env.UNITY_TEST_PROJECT] });
  const mcp = new Client({ name: 'bridge-integration-test', version: '1' });
  await mcp.connect(transport); t.after(() => mcp.close());
  const list = await mcp.listTools(); assert.ok(list.tools.length >= 30); assert.ok(list.tools.some(tool => tool.name === 'unity_batch'));
  const status = await mcp.callTool({ name: 'unity_status', arguments: {} }); assert.equal(status.isError, false); assert.equal(status.structuredContent.protocolVersion, 2);
  const create = await mcp.callTool({ name: 'unity_create_empty', arguments: { name: 'MCP Integration Object' } });
  assert.equal(create.isError, false); const id = create.structuredContent.instanceId;
  try {
    const rename = await mcp.callTool({ name: 'unity_rename', arguments: { instanceId: id, name: 'MCP Verified' } }); assert.equal(rename.isError, false);
    const hierarchy = await mcp.callTool({ name: 'unity_hierarchy', arguments: {} }); assert.ok(hierarchy.structuredContent.objects.some(o => o.instanceId === id && o.name === 'MCP Verified'));
    const error = await mcp.callTool({ name: 'unity_rename', arguments: { instanceId: 'missing', name: 'Invalid' } }); assert.equal(error.isError, true);
  } finally { await mcp.callTool({ name: 'unity_delete', arguments: { instanceId: id } }); await mcp.callTool({ name: 'unity_clear_errors', arguments: {} }); }
});

test('cancelling an active batch rolls back already applied steps', { skip: !process.env.UNITY_TEST_PROJECT, timeout: 45000 }, async () => {
  const project = process.env.UNITY_TEST_PROJECT; const client = new BridgeClient(project);
  const type = 'BridgeCancel_' + randomUUID().replaceAll('-', ''); const folder = 'Assets/' + type; let root;
  try {
    // A slow callback gives the external caller a deterministic cancellation window.
    const source = `using UnityEngine; [ExecuteAlways] public class ${type} : MonoBehaviour { void OnEnable() { System.Threading.Thread.Sleep(250); } }`;
    assert.equal((await client.command('write_script', { assetPath: `${folder}/${type}.cs`, source })).ok, true);
    root = (await client.command('create_empty', { name: type })).instanceId;
    const { id } = await client.submit('batch', { commands: Array.from({ length: 5 }, () => ({ action: 'add_component', instanceId: root, componentType: type })) });
    const claim = path.join(project, '.codex-bridge', 'processing', id + '.json');
    const deadline = Date.now() + 10000;
    for (;;) { try { await access(claim); break; } catch { if (Date.now() > deadline) throw new Error('Batch did not start.'); await delay(5); } }
    await delay(60); await client.cancel(id);
    const result = await client.wait(id);
    assert.equal(result.outcome, 'cancelled'); assert.ok(result.results.length >= 1, 'batch had started before cancellation');
    const inspected = await client.command('inspect', { instanceId: root });
    assert.equal(inspected.componentDetails.some(c => c.type === type), false);
  } finally {
    if (root) await client.command('delete', { instanceId: root });
    for (const file of [`${folder}/${type}.cs`, `${folder}/${type}.cs.meta`]) await unlink(path.join(project, file)).catch(error => { if (error.code !== 'ENOENT') throw error; });
    await rmdir(path.join(project, folder)).catch(error => { if (error.code !== 'ENOENT') throw error; });
    await unlink(path.join(project, folder + '.meta')).catch(error => { if (error.code !== 'ENOENT') throw error; });
    await client.command('refresh');
  }
});
