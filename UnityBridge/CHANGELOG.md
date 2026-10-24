# Changelog

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
