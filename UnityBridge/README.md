# GPT Unity Live Editing · 0.3.0

Local editor tools for creating objects, editing components/Inspector fields, scripts, materials and prefabs, with a queued command protocol, cancellation and grouped Undo batches.

Open **Tools > Local Unity Bridge > Connection** to see status, errors and queue depth.

Import **Live Editing Demo** from the package Samples section for an animated URP scene. Or use **Tools > Local Unity Bridge > Create Demo Scene** to generate a new additive scene. The demo uses layer 30 to keep ordinary objects from existing scenes out of its camera. Only URP rendering has been visually verified.

The [repository README](https://github.com/onlogical/gpt-unity-live-editing#readme) includes PowerShell usage, installation, the local MCP server and full protocol details. Installing this Unity package alone does not install the separate Node.js MCP server.

Verified on Windows with Unity 6000.5.2f1 (editing/integration/demo) and 6000.4.4f1 (batch-mode smoke test). Other Unity 6 versions include compatibility code but have not been verified.

The bridge polls local files beneath .codex-bridge in your project. It opens no network listener. Use trusted clients and projects; scripts compile normally. Active synchronous Unity commands cannot be interrupted halfway through. Batches can roll back their Undo scene edits between steps. An interrupted request may have applied edits and is never automatically replayed.

See [the changelog](CHANGELOG.md) and [MIT license](LICENSE.md).
