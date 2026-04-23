# Hoppa Level Editor Core

A reusable Unity Editor framework for authoring puzzle game level content.

## Status

Phase 0 — package skeleton. Not yet consumable. See [`../../PLANNING.md`](../../PLANNING.md) for the full design document.

## Layering

- **Layer 1 (this package):** EditorWindow host, panels, color palette, validation engine, JSON I/O, schema migration.
- **Layer 2 (consumer game project):** cell/element type definitions, color palette contents, validation rules, top-section panel.

## Requirements

- Unity **2022.3 LTS** or newer.
- `com.unity.nuget.newtonsoft-json` (declared as a hard dependency for JSON polymorphism).

## Consuming (future)

In a game project's `Packages/manifest.json`:

```json
"com.hoppa.leveleditor.core": "file:../hoppa-level-editor-core/Packages/com.hoppa.leveleditor.core"
```

Switch to a private Git URL once the studio repo is set up.

## Design document

Full architecture, phases, and decisions: [`../../PLANNING.md`](../../PLANNING.md).
