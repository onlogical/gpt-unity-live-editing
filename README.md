# GPT Unity Live Editing

Edit Unity through local tools instead of mouse automation. Version **0.3.0** includes a Unity package, a PowerShell client, and a stdio MCP server for assistants. MIT licensed. No OpenAI API key, cloud service or network listener is required.

## Install the Unity package

In Unity Package Manager, choose **Install package from Git URL**:

```text
https://github.com/onlogical/gpt-unity-live-editing.git?path=/UnityBridge
```

Or download this repository and install `UnityBridge/package.json` from disk. Keep the downloaded folder in place. For a manual Assets install, copy **both** `UnityBridge/Editor` and `UnityBridge/Runtime` into `Assets/UnityLiveBridge`; install only one copy.

Let Unity compile, then open **Tools > Local Unity Bridge > Connection**. The panel shows recent client activity, queue depth, compilation/import state, last command/result, and errors. Connected means a command arrived in the last 30 seconds, not a persistent network connection.

## Connect an assistant through MCP

Install Node.js 20 or later, then run from the downloaded repository:

```text
npm ci --prefix mcp --ignore-scripts
```

Add a local stdio server in your MCP host. The host must launch:

```text
node /absolute/path/to/gpt-unity-live-editing/mcp/server.mjs --project /absolute/path/to/UnityProject
```

A typical host configuration is in [examples/mcp.json](examples/mcp.json); replace its placeholder paths. Configuration location and syntax depend on your host. Restart/reconnect that server after editing its settings. Unity must remain open, with this package installed in the selected project.

The server advertises named tools for objects, components, Inspector fields, scripts, prefabs, materials, Play/Stop and the demo, plus:

- `unity_status`: read heartbeat, Unity version, queue depth and errors without waiting behind edits.
- `unity_batch`: apply scene edits as one Undo operation; roll back the batch if a step fails.
- `unity_submit`: queue work and return its ID immediately.
- `unity_result`: inspect that ID's final outcome.
- `unity_cancel`: request cancellation by ID.

## What can be edited

Create primitives or empty objects; move, scale, rotate, rename, duplicate, delete and parent objects; add components; inspect and edit serialized fields; create and assign materials; save and spawn prefabs; create C# scripts; refresh imports; save scenes; start/stop Play mode; Undo.

Use hierarchy results to get object IDs. Inspect an object to obtain `componentDetails` and exact serialized property paths. IDs can change after scene reloads.

Select a component by `componentId` or by fully qualified `componentType` plus zero-based `componentIndex`. Supported Inspector values: integers, floats, booleans, strings, enums, Vector3, colors, LayerMasks, asset/scene references, array/list sizes and elements. `editable` is Unity's flag; `supported` indicates bridge support for writing the type.

`set_property` uses a string `value`: a number, enum name, text, JSON vector/color, asset path, or `"null"` to clear a reference. Use `referenceId` instead for a scene GameObject/component. Invalid reference types are rejected without applying the edit. For arrays/lists, use `resize_array` on the field, then edit `field.Array.data[0]`. The size limit is 10,000.

## PowerShell client

```powershell
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action hierarchy
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action create -ArgumentsJson '{"name":"Demo","primitive":"Cube","position":{"x":0,"y":1,"z":0},"scale":{"x":1,"y":1,"z":1}}'
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action inspect -ArgumentsJson '{"instanceId":"OBJECT_ID","componentType":"UnityEngine.Transform"}'
```

Use `-NoWait` to submit without waiting. Then use `-Action result -RequestId ID` or `-Action cancel -RequestId ID`. `-Action status` reads the heartbeat directly. The default wait is 45 seconds. On timeout, cancellation is requested and the error includes the request ID; inspect its result before retrying.

## Batches and Undo

```json
{
  "label": "Build a small platform",
  "commands": [
    { "action": "create", "name": "Platform", "primitive": "Cube", "scale": { "x": 4, "y": 0.5, "z": 4 } },
    { "action": "rename", "instanceId": "$0", "name": "Starting Platform" }
  ]
}
```

Send this as `unity_batch`, or use PowerShell `-Action batch -ArgumentsJson ...`. `$0`, `$1` and `$last` refer to earlier command result IDs, and work in instanceId, componentId, parentId and referenceId fields. Successful batches are one Undo operation. Failed/cancelled batches roll back their Unity Undo scene edits. Batches accept 1..100 commands and exclude saves, script/asset writes, Play/Stop, nested batches and Undo. Effects outside Unity's Undo system, such as user component callbacks writing external files, are not transactional.

## Queue, cancellation and recovery

Each queued request gets its own response. Concurrent clients do not overwrite one shared reply. Files are sorted by timestamp-prefixed filename; UUID breaks ties. Keep local system clocks consistent when several clients share a project.

Cancellation skips work that has not started. An active batch checks cancellation between steps and rolls its changes back. An individual synchronous Unity command cannot be interrupted halfway through: it may complete before cancellation arrives. Always inspect the final outcome.

Outcomes include `completed`, `failed`, `cancelled`, `rolled_back` and `interrupted`; `pending` is not completion. A surviving processing claim without a response after an editor interruption is marked `interrupted`, never silently replayed. Its edits may already exist: inspect the scene before retrying. Completed request IDs reuse their stored result instead of executing again. These semantics do not promise filesystem durability across power failure.

Response writes blocked by Windows readers are retried without repeating the edit. Pending replies survive script-domain reloads in the same editor session. Response and cancellation files are retained locally; archive/delete old records only when no client is waiting for them. Deleting stored responses also removes their duplicate-ID protection.

## Try the demo

In Package Manager, import **Live Editing Demo** and open its scene. The prebuilt sample uses URP. Press Play to see three rotating sculptures. Their `UnityLiveBridge.BridgeDemoSpinner` fields include speed, a scene reference and an editable string array.

Alternatively choose **Tools > Local Unity Bridge > Create Demo Scene** or call `unity_demo`. This creates and saves an additional scene under `Assets/UnityLiveBridgeDemo` without closing existing scenes or overwriting an existing scene file. The generator chooses a shader for the current render pipeline. Only URP has been visually validated; the prebuilt sample uses URP materials. Its camera uses layer 30 to exclude ordinary objects from other loaded scenes. Play mode runs all loaded scenes, so isolate the sample to compare lighting/cameras.

## Supported versions and testing

| Environment | Verification |
| --- | --- |
| Unity 6000.5.2f1, Windows, URP | 32 editing checks; queue and MCP integration; file-lock and heartbeat regressions; demo animation |
| Unity 6000.4.4f1, Windows, built-in | Clean Package Manager install and 5 batch-mode smoke checks: create, rotate, inspect, batch and grouped Undo |
| Other Unity 6 versions | Compatibility code is included, but not verified |
| Node.js 26 on Windows | Local client/MCP test suite passed, including real stdio handshake and tool calls |
| macOS/Linux and Node 20/22 | Test workflow template supplied; not verified locally |

Tests against Unity require a disposable project, a licensed editor, and this package installed. They create temporary fixtures, modify Undo history, briefly use Play mode, and deliberately break/fix a script to test compiler recovery. Do not run during other editing.

```powershell
./tests/Test-LiveBridge.ps1 -ProjectPath 'C:/Projects/DisposableGame'
./tests/Test-ResponseLock.ps1 -ProjectPath 'C:/Projects/DisposableGame'
./tests/Test-Client.ps1
./tests/Test-UnityVersion.ps1 -UnityExe 'C:/Unity/6000.4.4f1/Editor/Unity.exe' -Version '6000.4.4f1'
```

```text
npm test --prefix mcp
```

The Node suite runs mock filesystem/client tests by default; set `UNITY_TEST_PROJECT` to an absolute open project path to enable the three live Unity/MCP integration tests. Mock tests are not Unity compatibility evidence. [examples/github-actions.yml](examples/github-actions.yml) is an optional CI template for the mock suite on three operating systems; copy it into `.github/workflows/` to enable it. Hosted Unity testing would require separate editor/license setup.

## File protocol

- `.codex-bridge/status.json`: heartbeat, protocolVersion 2, Unity version, errors, queue depth.
- `queue/TIMESTAMP-REQUEST_ID.json`: atomically submitted command (`id`, `action`, arguments).
- `processing/REQUEST_ID.json`: claimed command; not automatically replayed after interruption.
- `responses/REQUEST_ID.json`: final result with `ok`, `outcome`, errors and returned data.
- `cancel/REQUEST_ID.json`: cancellation marker.

Request IDs must contain 1..100 letters, numbers, hyphens or underscores. Write temporary files and rename them into queue only after writing is complete. Do not reuse IDs. The old request.json/response.json single-client transport remains for compatibility but lacks the queue's per-request guarantees.

Actions and fields are defined in [mcp/actions.mjs](mcp/actions.mjs). Asset paths must be beneath Assets. Script/prefab/material creation refuses existing files; edit existing scripts with normal file tools. Persistent scene edits are blocked in Play mode. Script edits require Unity compilation; this is not code hot reload. Unity must stay open and responsive; dialogs or compilation can delay processing.

This local tool can modify project files and scene objects. Any local process with project-folder write access can issue commands; use trusted clients/projects. The bridge is not a sandbox for untrusted code. It does not expose every Unity editor API.

## Contributing

Submit focused issues or pull requests with the Unity version, a minimal command, and expected versus observed behavior. Avoid credentials and private project files in reports. See [CHANGELOG.md](CHANGELOG.md) for release history.
