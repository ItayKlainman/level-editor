# Hoppa Level Editor — Architecture Summary

> Self-contained reference for an external collaborator with no repo access.
> Purpose: understand the Layer 1 (generic) / Layer 2 (game-specific) split precisely,
> so new generic systems (image→grid conversion, automated level generation,
> simulator/difficulty-scorer) get built generically in Layer 1 with only game-specifics
> in Layer 2. All paths, type names, namespaces, and assembly names are real.

## 1. Overview
This is a **Unity Editor-only, game-agnostic level-editor framework**, shipped as a UPM package and consumed by individual games in the same Unity project. It is deliberately split into **two layers**: **Layer 1** is the generic, reusable editor (grid model, serialization, validation, palette, panels, and a set of game-extension contracts) — it knows nothing about any specific game. **Layer 2** is per-game code that plugs concrete behavior into Layer 1 via ScriptableObject assets and a handful of interfaces/abstract bases. There are currently two Layer 2 games: **YarnTwist** (mature) and **YAK / YarnKingdom** (newer). A reference Layer 2 also ships inside the package as a sample (`DemoColorGridGame`).

---

## 2. Physical structure

### Folder tree (relevant only)
```
Packages/com.hoppa.leveleditor.core/        ← LAYER 1 (the UPM package)
  Runtime/      Cells/ Data/ Serialization/ Validation/(Rules/)
  Editor/       Analysis/ ColorPalette/ Exporters/ Generator/ Infrastructure/
                Mapping/ Panels/ Registry/ Validation/ Window/
  Samples~/DemoColorGridGame/   (reference Layer 2, not compiled in consumers)
  Tests/

Assets/YarnTwist/                            ← LAYER 2 (game)
  Runtime/  Editor/(Analysis Cells Generator Inspectors TopSection Validation)
  Data/Config/(CellDefs Exporters Palette Rules)  Data/Levels/  Tests/Editor/

Assets/YAK/                                   ← LAYER 2 (game)
  Runtime/  Editor/(Cells TopSection Validation)  Data/Config/(...)  TestConfigs/
```

### Assemblies (.asmdef) and dependency direction
| Assembly | Layer | Platform | References |
|---|---|---|---|
| `Hoppa.LevelEditor.Core.Runtime` | 1 | all | **none** |
| `Hoppa.LevelEditor.Core.Editor` | 1 | Editor only | Core.Runtime |
| `Hoppa.YarnTwist.Runtime` | 2 | all | Core.Runtime |
| `Hoppa.YarnTwist.Editor` | 2 | Editor only | Core.Runtime, Core.Editor, YarnTwist.Runtime |
| `Hoppa.YAK.Runtime` | 2 | all | Core.Runtime |
| `Hoppa.YAK.Editor` | 2 | Editor only | Core.Runtime, Core.Editor, YAK.Runtime |
| `Hoppa.YarnTwist.Editor.Tests` | 2 | Editor | + TestRunner, nunit, Newtonsoft |

### Main namespaces
- Layer 1: `Hoppa.LevelEditor.Core` (runtime), `Hoppa.LevelEditor.Core.Editor` (editor).
- Layer 2: `Hoppa.YarnTwist` / `Hoppa.YarnTwist.Editor`; `Hoppa.YAK` / `Hoppa.YAK.Editor`.

### How separation is enforced (3 ways, all physical)
1. **Asmdef dependency is strictly one-directional.** Core references nothing back toward games. A Layer 1 type literally cannot reference a Layer 2 type — it won't compile.
2. **Package vs. `Assets/`.** Layer 1 lives in the immutable UPM package (`com.hoppa.leveleditor.core`); games live under `Assets/`. (Project rule: never hand-edit package assets — Layer 1 changes ship via git tag + `manifest.json` pin bump; current line ~`v0.5.22`.)
3. **Runtime/Editor split** inside each layer: data models are in `*.Runtime` (plain C#, engine-agnostic, no `UnityEditor`), all UI/authoring is in `*.Editor`.

---

## 3. The extension mechanism — how a new game plugs in

Everything funnels through one ScriptableObject: **`GameProfile`** (`Packages/.../Editor/Infrastructure/GameProfile.cs`, `Hoppa.LevelEditor.Core.Editor.GameProfile`). A game = one `GameProfile.asset` with references to that game's concrete assets. The editor window holds **one active `GameProfile`** (selected via an ObjectField, persisted by GUID in `EditorPrefs["Hoppa.LevelEditor.ProfileGuid"]`).

`GameProfile`'s slots (each is the extension point):

| Slot (field) | Type a new game implements | Required? |
|---|---|---|
| `_schemaId` | string (stamped into `schemaVersion`) | yes |
| `_colorPalette` | `ColorPaletteAsset` (or subclass) | yes |
| `_gridWidth/_gridHeight` | ints | yes |
| `_cellTypes` | `List<CellTypeDefinition>` (abstract SO) — **first entry must be the "empty" type** | yes |
| `_rules` | `List<ValidationRuleBase>` (abstract SO) | optional |
| `_exporters` | `List<LevelExporterAsset>` (abstract SO) | for runtime export |
| `_topSectionScript` / `_bottomSectionScript` | `MonoScript` → `TopSectionPanel` subclass | optional |
| `_orderPanel` | `EditorPanelAsset` → `IEditorPanel` | optional |
| `_levelGenerator` + `_generatorConfig` | `LevelGeneratorAsset` + tuning SO | optional |
| `_levelAnalyzer` | `LevelAnalyzerAsset` → `ILevelAnalyzer` | optional |
| `_levelCompleter` | `LevelCompleterAsset` → `ILevelCompleter` | optional |
| `_canvasOverlay` | `CanvasOverlayAsset` | optional |
| `_extensions` | `List<ScriptableObject>`, typed lookup via `GetExtension<T>()` | escape hatch |

**The concrete contracts a new game must implement:**

- **A cell type** = a runtime data class implementing `ICellData` (`Runtime/Cells/ICellData.cs`; one property `string CellTypeId`) **+** an editor `CellTypeDefinition` subclass (`Editor/Registry/CellTypeDefinition.cs`; `CreateDefault()`, `DrawCell()`, `DrawInspector()`, optional `OnAfterPlaced`/`OnAfterInspectorChanged`). Opt-in marker interfaces let generic Layer 1 rules see into cells without knowing the type: `IColoredCell` (`string ColorId`), `IHideableCell`. Optional `ICellContextActions` (`Editor/Registry/ICellContextActions.cs`) adds right-click actions.
- **Colors**: a `ColorPaletteAsset` (`Editor/ColorPalette/ColorPaletteAsset.cs`) listing `ColorEntry { Id, DisplayName, Color }`. Subclass + override `ResolveEntries()` to source colors elsewhere (YAK does this).
- **Validation**: `ValidationRuleBase` subclass (`Runtime/Validation/ValidationRuleBase.cs`): `Scope` + `Evaluate(ValidationContext) → IEnumerable<ValidationEntry>`.
- **Export**: `LevelExporterAsset` subclass (`Editor/Exporters/LevelExporterAsset.cs`): `Export(LevelDocument, CellTypeRegistry, jsonFilePath) → bool`, plus optional Summary-panel hooks.

Registration is **data, not code**: drop the assets onto the `GameProfile`. Cell-type JSON discrimination is handled by a registry (`CellTypeRegistry` built from `_cellTypes`, keyed on `TypeId`).

---

## 4. How the CURRENT game (YarnTwist) implements Layer 2

- **Cells** (`Assets/YarnTwist/Runtime/` + `Editor/Cells/`): `YarnBoxCell` (`yt.box`, implements `IColoredCell` + `IHideableCell`, also carries `ConnectedDir`), `YarnArrowBoxCell`, `YarnTunnelCell`, `YarnWallCell`, `YarnEmptyCell` — each paired with a `*CellDefinition : CellTypeDefinition` (e.g. `YarnBoxCellDefinition`, which also implements `ICellContextActions`). `CellTypeId` is a hardcoded string per class; that string is the JSON discriminator.
- **Colors**: a `ColorPaletteAsset` + a `StringIntMapping` asset (`YarnColorMapping`) translating colorId→int at export.
- **Top section** (the spool columns above the grid): `YarnTopSectionPanel : TopSectionPanel`, assigned to `GameProfile._topSectionScript` as a `MonoScript`; data in `YarnTopSectionData` (serialized into `LevelDocument.TopSection` as raw `JObject`).
- **Validation**: `YarnColorBalanceRule`, `YarnArrowBoxTargetRule`, `YarnTunnelOutputRule`, `YarnConnectedBoxRule`, `YarnConnectedSpoolRule`, `YarnPaletteRule` — all `ValidationRuleBase` assets wired into `_rules`.
- **Export**: `YarnMasterLevelExporter : LevelExporterAsset` writes/upserts a master `level_config.json` (`{ "LevelConfigs": { "<key>": {...} } }`) — a **different schema** from the editor's own `LevelDocument` JSON.
- **Analysis/auto-fill**: `YarnTwistLevelAnalyzer : LevelAnalyzerAsset` (win-path/solvability/difficulty), `YarnTwistSpoolAutofiller : LevelCompleterAsset`, `YarnTwistSpoolAutofillConfig`.
- **Generation**: `YarnTwistLevelGenerator : LevelGeneratorAsset` + `YarnTwistGeneratorConfig`.
- **Overlay**: `YarnPaletteOverlay : CanvasOverlayAsset` (3×3 palette-cover annotation).

All of the above are referenced from `YarnTwistProfile.asset`. **YAK** mirrors the same pattern with fewer pieces (`YAKWoolCell`/`YAKEmptyCell`, `YAKSpoolSectionPanel`, `YAKColorBalanceRule`, `YAKLevelExporter`, plus `YAKLevelImporter`/`YAKImportMenu`); notably its color source is `YAKStaticManagerColorSource : ColorPaletteAsset`, which derives entries from the game's `YAKStaticManagerScriptableObject` and adds string-enum↔int↔Color mapping (`GetInt`/`GetColorId`).

---

## 5. Locations of specific subsystems

| Subsystem | File · main type | Layer |
|---|---|---|
| **Editor level data model / save schema** | `Packages/.../Runtime/Data/LevelDocument.cs` · `LevelDocument` (+ `GridData<TCell>`, `LevelMetadata`). Cell JSON is a discriminated union keyed on a `"type"` field, handled in `Runtime/Serialization/JsonLevelSerializer.cs` · `CellDataConverter`. `TopSection` & `GameData` are opaque `JObject` pass-throughs. | **L1** |
| **Game runtime save schema** (what the game loads) | Produced by Layer 2 exporters — **not** the same as `LevelDocument`. e.g. `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs`, `Assets/YAK/Editor/YAKLevelExporter.cs` (flat `PixelColors` int[]). | **L2** |
| **Color palette + enum→Color mapping** | **Layer 1 has NO enum** — colors are *string IDs* (`Runtime/Data/ColorId.cs` · `ColorId`) + `Editor/ColorPalette/ColorPaletteAsset.cs` (string→`Color`). The int/enum mapping is **per game**: `Editor/Mapping/StringIntMapping.cs` (generic string→int SO) used by `YarnColorMapping`; YAK's `YAKColorType` enum + `YAKStaticManagerColorSource.cs` (enum-name↔int↔Color). | L1 (palette) / **L2** (enum + int map) |
| **Image / brush quantizer** | **Does not exist.** No `Texture2D`/`GetPixel`/image-to-grid code anywhere in the editor. Closest concepts: the painting **brush template** (`LevelEditorSession.BrushTemplate` / `CloneBrushTemplate()`) and the export-time color→int `StringIntMapping`. A new image→grid pipeline is greenfield. | — |
| **Validation logic** | `Runtime/Validation/` — `IValidationRule`, `ValidationRuleBase`, `ValidationContext`, `ValidationReport`, `ValidationSeverity`; registry in `Editor/Registry/ValidationRuleRegistry.cs`; generic rules in `Runtime/Validation/Rules/` (`ColorBalanceRule`, `GridNonEmptyRule`, `PaletteColorsExistRule`). Game rules in `Assets/*/Editor/Validation/`. | L1 (engine) / L2 (game rules) |
| **Save / Load** | `Runtime/Serialization/JsonLevelSerializer.cs` (`Load`/`Save`); orchestrated + undo/redo in `Editor/LevelEditorSession.cs`; window I/O in `Editor/Window/LevelEditorWindow.cs`. | **L1** |
| **Export** | Contract `Editor/Exporters/ILevelExporter.cs` + `LevelExporterAsset`; generic impls `JsonExporter`, `ScriptableObjectExporter`; per-game impls in L2. Runs on Save + the "Export ▸" toolbar button. | L1 (contract) / L2 (impl) |
| **Grid dimensions & per-game config** | `GameProfile.GridWidth/GridHeight` (defaults); per-level override via `Editor/Window/NewLevelDialog.cs` + `LevelEditorSession.CreateEmpty(profile, w, h)`. Arbitrary per-game config via typed `GameProfile` slots or `_extensions` + `GetExtension<T>()`. | **L1** |

---

## 6. Existing extension points a new generic pipeline can reuse

Two of the three planned systems **already have generic Layer 1 contracts** — extend these, don't fork:

- **Automated level generation → already exists.** `Editor/Generator/ILevelGenerator.cs` + `LevelGeneratorAsset` (abstract SO on `GameProfile._levelGenerator`), driven by `LevelGeneratorRequest` (`Difficulty`, `TargetAPS?`, `Seed`, `AdvancedConfig`) → `LevelGeneratorResult` (carries a `LevelDocument` + diagnostics). UI: `Editor/Generator/GeneratorModePanel.cs` (✨ Generate toolbar toggle). Shared candidate-vs-rules helper: `Editor/Generator/LevelGeneratorRunner.cs`. Handoff into the editor uses the panel's `OnUseLevel` event → the window's normal document-load path.
- **Simulator / difficulty-scorer → partially exists.** `Editor/Analysis/ILevelAnalyzer.cs` + `LevelAnalyzerAsset` returns `LevelAnalysisResult` (solvability, win-path counts, difficulty); `ILevelCompleter` + `LevelCompleterAsset` does auto-fill; both surface in `Editor/Analysis/AutofillPanel.cs` (Spool Analysis side panel). A generic difficulty scorer should generalize `LevelAnalysisResult`/`AnalysisRequest`.
- **Image→grid conversion → no contract yet, but a clean template exists.** Mirror the generator pattern: a new abstract `*Asset : ScriptableObject` on a new `GameProfile` slot, output a `LevelDocument`, reuse the window's document-load handoff. The natural quantization target is the palette: `IColorPalette.ColorIds` / `TryGetColor(id, out Color)` (map each pixel to the nearest `ColorEntry.Color`, emit cells whose `IColoredCell.ColorId` = the matched id). The "empty" cell-type convention (`CellTypes[0]`) gives you the transparent/background mapping for free.

Other reusable hooks: `CanvasOverlayAsset` (draw over the grid), `EditorPanelAsset`/`IEditorPanel` (extra toolbar tab), `GameProfile.GetExtension<T>()` (attach arbitrary per-profile config without a new typed field), and `StringIntMapping` (reusable string→int table).

---

## 7. Caveats / leaks (read before designing the new systems)

- **Layer 1 is "generic in principle, grid-shaped in practice."** `GridData<TCell>` is hard-baked into `LevelDocument` and every panel; the whole framework assumes a 2D rectangular grid. Fine for image→grid and grid generation, but it's not abstract over non-grid level shapes.
- **APS leaked upward.** `LevelMetadata.Aps` sits in Layer 1 (`LevelDocument.cs`), and `LevelGeneratorRequest.TargetAPS`/`Difficulty (1..10)` are in Layer 1 — these are YarnTwist gameplay concepts that hardened into the generic contracts. A generic difficulty-scorer should treat "difficulty/APS" as a game-defined metric, not assume this scale.
- **"TopSection" naming is spool-flavored.** `LevelDocument.TopSection` is a generic opaque `JObject`, but the panel base is named `TopSectionPanel` and its only real users are spool panels. Don't assume a "top section" semantically — it's just a game-owned blob.
- **Two distinct JSON schemas.** The editor's working file (`LevelDocument`) ≠ the game's runtime config (exporter output). A new pipeline emitting levels should produce a `LevelDocument` and let the existing exporter chain do the game-specific translation — don't write game JSON directly.
- **Color model is string-keyed in Layer 1, int/enum-keyed per game.** Any generic image quantizer must operate on palette **string IDs** (`ColorEntry`), and leave int/enum conversion to the Layer 2 exporter. Don't bake a color enum into Layer 1.
- **`ILevelCompleter.MechanicToggles`** is a generic-looking `IReadOnlyList<string>` but was shaped by spool mechanics (Hidden/Connected) — treat as a loose convention.
- **Single active profile.** The window drives one `GameProfile` at a time (EditorPrefs GUID). Pipelines should operate against the active profile, not assume multi-profile context.
- **Reference implementation available**: `Packages/.../Samples~/DemoColorGridGame` is the cleanest, least game-coupled Layer 2 example to copy when building/testing a new generic system.
