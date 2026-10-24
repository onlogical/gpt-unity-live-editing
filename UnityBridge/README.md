# GPT Unity Live Editing

A local Unity editor bridge that lets coding assistants edit a Unity project without clicking the editor. Open source under the MIT license.

## What works

- Create primitive and empty GameObjects; move, scale, rotate, rename, duplicate, delete, and parent them.
- List scene objects and attached components.
- Add built-in or custom script components.
- Read Inspector properties; edit values, scene/asset references, arrays, and lists; select specific component instances.
- Create C# scripts, refresh assets, save scenes, create and spawn prefab assets, create and assign materials.
- Request Play/Stop, inspect heartbeat/errors, and undo individual edits.
- Open **Tools > Local Unity Bridge > Connection** for live status, recent client activity, the last command, compilation/import state, and errors.

This is an early prototype, tested on **Unity 6.5 / 6000.5.2f1 on Windows**. It is a file-based bridge, not an MCP server and not a complete replacement for every Unity editor API. Scripts still compile normally. No OpenAI API key or network server is required.

## Install

1. Download and extract this repository.
2. In Unity, open **Window > Package Management > Package Manager**.
3. Choose **Install package from disk** and select `UnityBridge/package.json` from the extracted folder. Keep that folder on disk.
4. Alternatively, copy `UnityBridge/Editor` into `Assets/UnityLiveBridge/Editor` (install only one copy).
5. Let Unity compile. The bridge starts automatically and writes `.codex-bridge/status.json` in your project root.

Once hosted on GitHub, you can also install using this Git URL in Package Manager:

```text
https://github.com/onlogical/gpt-unity-live-editing.git?path=/UnityBridge
```

## Use from PowerShell

```powershell
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action hierarchy
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action create -ArgumentsJson '{"name":"Demo","primitive":"Cube","position":{"x":0,"y":1,"z":0},"scale":{"x":1,"y":1,"z":1}}'
```

Use the string `instanceId` returned by `hierarchy` when targeting an object. IDs can change after scene reloads: inspect again rather than storing them permanently.

```powershell
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action add_component -ArgumentsJson '{"instanceId":"YOUR_ID","componentType":"UnityEngine.Rigidbody"}'
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action inspect -ArgumentsJson '{"instanceId":"YOUR_ID","componentType":"UnityEngine.Rigidbody"}'
./Send-UnityCommand.ps1 -ProjectPath 'C:/Projects/MyGame' -Action set_property -ArgumentsJson '{"instanceId":"YOUR_ID","componentType":"UnityEngine.Rigidbody","propertyPath":"m_Mass","value":"3.5"}'
```

Use the exact serialized `propertyPath` returned by `inspect`; Inspector display labels can differ. Supply a fully qualified component type. Select a component with `componentId` from `componentDetails`, or `componentType` plus a zero-based `componentIndex`. Omitting the index selects the first match. `editable` is Unity's flag, while `supported` indicates whether the bridge can write that property type. Use `referenceId` for a scene GameObject/component reference, an asset path in `value` for an asset reference, or `value: "null"` to clear it. Incompatible references return an error without applying the change.

## Protocol

Write one JSON request atomically to `.codex-bridge/request.json`. Include unique `id` and `action` strings. Read `response.json` for the same id; `ok: false` includes an error. The bundled client handles this. Only one client should send commands at a time. Local processes with write access to your project can issue commands; use trusted projects and clients.

Actions: `ping`, `hierarchy`, `refresh`, `create`, `create_empty`, `transform`, `add_component`, `inspect`, `set_property`, `write_script`, `save_prefab`, `save`, `play`, `stop`, `undo`, `open_panel`, `clear_errors`, `rotate`, `rename`, `duplicate`, `delete`, `parent`, `create_material`, `assign_material`, `instantiate_prefab`, `resize_array`.

Additional fields:

| Action | Fields |
| --- | --- |
| create | name, primitive, position, scale |
| create_empty | name, position |
| transform | instanceId, position, scale |
| add_component / inspect | instanceId, componentType; inspect also accepts componentId or componentIndex |
| set_property | instanceId, componentId or componentType + componentIndex, propertyPath, value (string) or referenceId |
| write_script | assetPath ending .cs, source |
| save_prefab | instanceId, assetPath ending .prefab |
| save | optional scenePath under Assets ending .unity |
| rotate / rename / duplicate / delete | instanceId; rotate: rotation (world Euler angles); rename/duplicate: name |
| parent | instanceId, parentId (empty to unparent), worldPositionStays (default true) |
| create_material | assetPath ending .mat, optional name |
| assign_material | instanceId, assetPath, materialIndex (default 0), optional componentId |
| instantiate_prefab | assetPath, position, optional name |
| resize_array | instanceId, component selector, propertyPath, arraySize (0..10000) |

`write_script` and `save_prefab` refuse to overwrite existing files. Edit existing scripts through normal file tools. Explicitly supply scale for transform requests. Persistent scene mutations are blocked during Play mode; undo should only be used in Edit mode. Create/transform/Inspector/component operations record Unity Undo entries. Asset creation and script writes do not.

The editor polls every 250 ms. Unity must be open and responsive; compilation or dialogs delay requests. A timeout does not cancel a queued command: inspect status before retrying. Play and Stop acknowledgments confirm the transition request; check later `status.json` for the settled state. Captured errors cover the current editor domain and reset after recompilation. Historical compile errors are in Unity's editor log.

## Validation

Version 0.2.0 was tested live in Unity 6000.5.2f1 on Windows: 32 automated checks cover object actions, Undo, prefab/material workflows, multiple component selection, arrays/lists, scene references, invalid input, Play mode restrictions, and compiler-error recovery. Separate regressions verify locked-response retries without repeating object creation and stale-heartbeat rejection before sending commands. The connection panel was visually checked in Unity. Broader Unity versions and all property types have not been validated.

Run `./tests/Test-LiveBridge.ps1 -ProjectPath "C:/Projects/MyGame"` against a disposable Edit-mode project with the bridge installed. It creates temporary fixture objects/assets, deliberately triggers and repairs a compiler error, enters/exits Play mode, and cleans up its own fixtures. The test changes Undo history and leaves the scene dirty; do not run during other editing.

## Contributing

Submit focused issues or pull requests with your Unity version, a minimal command, and expected versus observed behavior. Avoid credentials and private project files in reports. Keep real-time editing on the bridge; desktop automation is not required.

### Arrays and lists

Inspect a component to find array property paths. Use `resize_array` on the array/list path (for example `numbers`), then `set_property` on a specific element such as `numbers.Array.data[1]`. Elements use the same value/reference rules as ordinary fields. Nested struct fields can use their full serialized paths. Arrays larger than 10,000 elements are rejected.

### Connection status

The panel calls the client connected when a command was received in the last 30 seconds; this is recent activity, not a persistent socket connection. Status survives script reloads for the last command. Error capture restarts after successful recompilation. During compilation, the existing loaded bridge reports progress as a compiling state and collects compiler errors. It cannot run if its own initial installation fails to compile.

Run `./tests/Test-ResponseLock.ps1 -ProjectPath "C:/Projects/MyGame"` for the Windows file-lock regression. Run `./tests/Test-Client.ps1` for the standalone UTC heartbeat regression.
