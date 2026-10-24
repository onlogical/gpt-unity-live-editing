# GPT Unity Live Editing

A local Unity editor bridge that lets coding assistants edit a Unity project without clicking the editor. Open source under the MIT license.

## What works

- Create primitive and empty GameObjects; move and scale them.
- List scene objects and attached components.
- Add built-in or custom script components.
- Read Inspector properties and change integers, floats, booleans, strings, enums, Vector3, colors, and asset references.
- Create C# scripts, refresh assets, save scenes and create prefab assets.
- Request Play/Stop, inspect heartbeat/errors, and undo edits.

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

Use the exact serialized `propertyPath` returned by `inspect`; Inspector display labels can differ. Supply a fully qualified component type. Multiple components of the same type currently target the first component. `editable` reflects Unity's flag; property types not listed above cannot yet be written. Object references currently accept asset paths, not scene objects.

## Protocol

Write one JSON request atomically to `.codex-bridge/request.json`. Include unique `id` and `action` strings. Read `response.json` for the same id; `ok: false` includes an error. The bundled client handles this. Only one client should send commands at a time. Local processes with write access to your project can issue commands; use trusted projects and clients.

Actions: `ping`, `hierarchy`, `refresh`, `create`, `create_empty`, `transform`, `add_component`, `inspect`, `set_property`, `write_script`, `save_prefab`, `save`, `play`, `stop`, `undo`.

Additional fields:

| Action | Fields |
| --- | --- |
| create | name, primitive, position, scale |
| create_empty | name, position |
| transform | instanceId, position, scale |
| add_component / inspect | instanceId, componentType |
| set_property | instanceId, componentType, propertyPath, value (string) |
| write_script | assetPath ending .cs, source |
| save_prefab | instanceId, assetPath ending .prefab |
| save | optional scenePath under Assets ending .unity |

`write_script` and `save_prefab` refuse to overwrite existing files. Edit existing scripts through normal file tools. Explicitly supply scale for transform requests. Persistent scene mutations are blocked during Play mode; undo should only be used in Edit mode. Create/transform/Inspector/component operations record Unity Undo entries. Asset creation and script writes do not.

The editor polls every 250 ms. Unity must be open and responsive; compilation or dialogs delay requests. A timeout does not cancel a queued command: inspect status before retrying. Play and Stop acknowledgments confirm the transition request; check later `status.json` for the settled state. Captured errors cover the current editor domain and reset after recompilation. Historical compile errors are in Unity's editor log.

## Validation

Live-tested in Unity 6000.5.2f1: hierarchy, cube creation and transforms, saving a scene, Play/Stop, adding Rigidbody, editing its mass, creating and compiling a custom MonoBehaviour, attaching it, and reading back a changed public float. Broader Unity versions and all property types have not been validated.

## Contributing

Submit focused issues or pull requests with your Unity version, a minimal command, and expected versus observed behavior. Avoid credentials and private project files in reports. Keep real-time editing on the bridge; desktop automation is not required.
