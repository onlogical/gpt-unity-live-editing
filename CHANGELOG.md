# Changelog

## 0.3.0

- Timestamp-ordered file queue with per-request results, duplicate-ID protection, cancellation markers, and interrupted-command recovery without automatic replay.
- Batches of scene edits share one Undo operation and roll back on failure/cancellation; prior result IDs can be referenced within a batch.
- Local stdio MCP server with named editing tools, status, asynchronous submit/result, cancellation and batching. Includes a locked dependency file and real protocol tests.
- Importable URP demo scene and a menu/tool to generate a fresh scene, with an editable runtime spinner component.
- Unity 6.4 compatibility branch and a reproducible batch-mode compatibility runner, plus an optional cross-platform mock-test CI template.
- Add LayerMask Inspector values and isolate the demo camera from ordinary scene objects.
- Fix shared heartbeat reads on Windows and avoid recursive Unity JSON serializer types in batch requests/replies.

## 0.2.0

- Connection window with recent client activity, last command/result, compilation/import status, and captured errors.
- Rotate, rename, duplicate, delete, and parent objects; create/assign materials; instantiate prefabs.
- Target specific component instances by ID or type/index. Read and write scene references, array elements, and list elements; resize arrays/lists.
- Reject invalid hierarchy cycles, reference types, array sizes, and material slots without applying the edit.
- Fix Undo grouping so Undo reverses one command instead of multiple unrelated commands.
- Retry locked response-file writes without executing the scene command again.
- Add live integration tests and an editor-only assembly definition for Package Manager installation.

## 0.1.0

- Initial local file bridge with object creation, scripts, basic serialized Inspector editing, scene saving, and Play/Stop.
