# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project follows [Semantic Versioning](https://semver.org/).

## [0.6.0] - 2026-06-15

### Added

- `AnalysisStatus` enum (`Unknown/Solvable/Unsolvable/TimedOut/Faulted`): an authoritative outcome classification for `ILevelAnalyzer` runs, so callers can distinguish "proven unsolvable" from "budget hit" or "faulted" instead of overloading a single bool.
- `LevelAnalysisResult`: new generic fields — `Status` (the enum above), `ApsEstimate` + `ApsCalibrated` (measured Attempts-Per-Solve from average-player rollouts, with an uncalibrated flag), `Band` (game-defined difficulty band derived from APS), and `WinPath` (`IReadOnlyList<int>` — machine-readable ordered winning action indices, companion to `SolutionSteps`). `ToString()` appends the APS (marked `(uncalibrated)` until fitted). All additive; existing analyzers (YarnTwist) are unaffected.
- `AnalysisRequest`: `NodeBudget` (search-node cap; a hit reports `TimedOut`/`Unknown`, never `Unsolvable`) and `Seed` (reproducible Monte-Carlo playouts).
- `IImageToGrid` + `ImageToGridAsset` (abstract `ScriptableObject`): a Layer-2 game converts a source image into a `LevelDocument` quantized to the profile's palette. `GameProfile._imageToGrid` slot + `ImageToGrid` accessor. New `ImageToGridModePanel` + a `🖼 Image` toolbar mode (mirrors the generator: source-texture field, the converter asset's inspector, Convert + preview + "Use This Level" via the shared load handoff). `ToolbarPanel` gained `OnImageToggle`/`ImageMode`/`ShowImage`. Colors stay string-keyed in Layer 1; enum/int mapping remains the game's concern.
- `LevelSolution` + `SolutionJson`: a serializable winning solution (`schemaVersion`, `levelId`, `steps` = ordered game-defined action indices = the analyzer's `WinPath`). Flat, lowercase-field JSON so the same file round-trips through both Newtonsoft (editor) and `JsonUtility` (in-game viewer, zero deps). The Auto-fill panel's "Save Solution…" now also writes `<levelId>.solution.json` alongside the human-readable `.txt` whenever the analyzer produced a `WinPath`.
- `LevelThumbnail` (Editor/Batch): renders a `LevelDocument` grid to a `Texture2D` / PNG, one (or N) pixels per cell from the profile palette — a reusable thumbnail for batch review and previews.
- `BatchStaging` + `LevelStats` + `BatchCandidate` + `BatchReviewWindow` (Editor/Batch): a generic curation flow over a batch "staging" folder of `LevelDocument` JSONs (with optional `<id>.stats.json` / `<id>.png` sidecars). `Window ▸ Hoppa ▸ Batch Review` shows candidates as thumbnail + stats, supports multi-select, and imports chosen levels into a target folder. Scan/import logic is split into pure (testable) helpers.

### Notes

- These generalize the analysis contract for the YAK difficulty scorer (measured APS over a simulator) while staying game-agnostic so future games reuse them. No breaking changes.

## [0.5.22] - 2026-06-09

### Added

- `ILevelCompleter.MechanicToggles` / `LevelCompleterAsset.MechanicToggles`: a completer advertises named per-mechanic on/off toggles; the Auto-fill panel renders a checkbox per name and passes the choices back via `CompletionRequest.MechanicToggles`. Keeps the shared panel game-agnostic (no game mechanic names in Layer 1).
- `CompletionRequest.MechanicToggles` (`Dictionary<string,bool>`): per-mechanic include/exclude choices for the completer. Null = completer defaults.

### Changed

- `AutofillPanel`: the **Difficulty 1–10** slider is replaced by an **APS 1–6** slider (Attempts Per Solve — how many tries an average player needs to win; sent as `CompletionRequest.TargetAPS`). The panel also renders the active completer's mechanic checkboxes.
- `SummaryPanel`: trimmed to **ID, Grid, Coins, Notes** — removed the Schema row, exporter info rows (Order), the cell-count list, and the APS metadata field for a cleaner panel.

## [0.5.21] - 2026-06-02

### Added

- `CanvasOverlayAsset`: abstract ScriptableObject base letting a Layer-2 game draw a custom overlay on top of the grid canvas (multi-cell region annotations no single cell could render). `GridCanvasPanel` calls `GameProfile.CanvasOverlay?.DrawOverlay(session, cellRect)` after drawing cells, inside the canvas scroll view. Additive and optional — profiles without an overlay are unaffected. (Enables the YarnTwist Palette mechanic.)

## [0.4.1] - 2026-05-05

### Added

- `ColorSwatchDrawer.Draw()`: optional `ICollection<string> allowedIds` parameter — when supplied, only palette entries in the set are rendered.
- `ColorSwatchDrawer.MeasureHeight()`: matching `allowedIds` parameter so popup window height is correct when filtered.
- `ColorPickerPopup`: accepts optional `ICollection<string> allowedIds` and forwards it to `ColorSwatchDrawer`.

## [0.4.0] - 2026-05-04

### Fixed

- `LevelEditorWindow`: Export now shows a clear "Save Required" dialog instead of the cryptic "returned false" error when the level has not been saved to a file yet. Covers both the "Export Anyway" path and the "Save & Export → cancel Save As" path.

## [0.3.0] - 2026-05-04

### Added

- `LevelEditorWindow`: Save As and Open dialogs now remember the last-used directory via `EditorPrefs` (`Hoppa.LevelEditor.LastSaveDir`). No longer resets to `Application.dataPath` each session.

## [0.2.0] - 2026-05-04

### Added

- `EditorPanelAsset` — abstract `ScriptableObject` base implementing `IEditorPanel`; allows games to expose a custom panel as an Inspector field on `GameProfile`.
- `GameProfile.OrderPanel` (`EditorPanelAsset`) — optional slot for a game-provided order-management panel.
- `ToolbarPanel`: `OnOrderToggle` event, `OrderMode` bool property, and `⇅ Order` toggle button. Button stays depressed while order mode is active.
- `LevelEditorWindow`: `_inOrderMode` flag — when active, renders `GameProfile.OrderPanel` full-window instead of the normal editing layout. Shows a helpful message if no panel is configured.

## [0.1.0] - 2026-04-23

### Added

- Initial package skeleton.
- Directory structure per `PLANNING.md` §1 (Runtime/, Editor/, Tests/, Samples~/).
- `package.json`, `README.md`, `CHANGELOG.md`, `LICENSE.md`.
- Runtime and Editor asmdefs (`Hoppa.LevelEditor.Core.Runtime`, `Hoppa.LevelEditor.Core.Editor`).
- Test asmdefs for Runtime and Editor (`Hoppa.LevelEditor.Core.Tests`, `Hoppa.LevelEditor.Core.Editor.Tests`).
- Hard dependency on `com.unity.nuget.newtonsoft-json@3.2.1`.
