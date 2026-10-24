import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import { BridgeClient } from './bridge-client.mjs';
import { actions, batchSchema } from './actions.mjs';

const projectArg = process.argv.indexOf('--project');
const project = projectArg >= 0 ? process.argv[projectArg + 1] : process.env.UNITY_PROJECT_PATH;
if (!project) { console.error('Usage: node server.mjs --project /absolute/path/to/UnityProject'); process.exit(1); }
const client = new BridgeClient(project);
const server = new McpServer({ name: 'gpt-unity-live-editing', version: '0.3.0' });
const wrap = async fn => {
  try { const result = await fn(); return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }], isError: result.ok === false, structuredContent: result }; }
  catch (error) { return { content: [{ type: 'text', text: error.message }], isError: true }; }
};
for (const [action, definition] of Object.entries(actions)) {
  server.registerTool('unity_' + action, { description: definition.description, inputSchema: definition.shape, annotations: { readOnlyHint: !!definition.readOnly, destructiveHint: !definition.readOnly, openWorldHint: false } },
    (args, extra) => wrap(() => client.command(action, args, { signal: extra.signal })));
}
server.registerTool('unity_status', { description: 'Read Unity heartbeat, version, queue depth, compilation, Play mode and errors without waiting behind edits.', inputSchema: {}, annotations: { readOnlyHint: true, openWorldHint: false } }, () => wrap(() => client.status()));
server.registerTool('unity_batch', { description: 'Apply 1..100 scene commands as one Undo operation. Failure or cancellation rolls back the batch. IDs can reference earlier results using $0, $1 or $last. No asset writes, saves, Play mode or nested batches.', inputSchema: batchSchema },
  (args, extra) => wrap(() => client.command('batch', args, { signal: extra.signal })));
server.registerTool('unity_submit', { description: 'Queue a command without waiting; returns a request ID. Inspect with unity_result or cancel with unity_cancel.', inputSchema: { action: z.enum([...Object.keys(actions), 'batch']), arguments: z.record(z.string(), z.unknown()).optional() } },
  args => wrap(() => client.submit(args.action, args.arguments)));
const requestId = { id: z.string().regex(/^[A-Za-z0-9_-]{1,100}$/) };
server.registerTool('unity_result', { description: 'Read a queued request outcome. pending is not completion. interrupted means inspect the scene before retrying.', inputSchema: requestId, annotations: { readOnlyHint: true, openWorldHint: false } }, args => wrap(() => client.result(args.id)));
server.registerTool('unity_cancel', { description: 'Request cancellation by ID. Queued work is skipped; active batches roll back between steps. Single commands cannot be interrupted mid-execution. Inspect the final result.', inputSchema: requestId }, args => wrap(() => client.cancel(args.id)));
await server.connect(new StdioServerTransport());
