# How to integrate a new game with the Level Editor

This guide covers everything needed to wire a new game into the editor so that
levels created here can be loaded directly by the game project. No changes to
the core framework (`com.hoppa.leveleditor.core`) are required.

---

## Overview

The editor has two output formats:

| Format | File | Produced by | Used by |
|--------|------|-------------|---------|
| Raw level document | `level_001.json` | Built-in `JsonExporter` | Editor (reload, version control) |
| Game-ready config | e.g. `level_config.json` | Your `LevelExporterAsset` | Game project at runtime |

The `Export ▸` button in the toolbar (and auto-export on Save) runs every
`LevelExporterAsset` in the active `GameProfile`. Swapping which exporter runs
is just a matter of swapping the asset in the profile — no code changes needed.

---

## Step-by-step: adding an exporter for a new game

### 1. Create the exporter class

In your game layer (e.g. `Assets/YourGame/Editor/`), subclass `LevelExporterAsset`:

```csharp
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace YourGame.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Your Game/Master Level Exporter")]
    public sealed class YourGameMasterLevelExporter : LevelExporterAsset
    {
        [SerializeField] private string _outputPath;
        [SerializeField] private StringIntMapping _colorMapping;
        [SerializeField] private StringIntMapping _cellTypeMapping;

        public override string Name => "YourGameMasterLevelConfig";

        public override bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            // Transform document into the format your game's runtime expects.
            // Write to _outputPath.
            // Return true on success, false on failure (or throw on hard errors).
            return true;
        }
    }
}
```

Key rules:
- `Name` is used in the Export dialog and logs — make it unique.
- `Export` must be idempotent and safe to call on every Save.
- Write the game-ready file to a configurable `_outputPath` (not hardcoded).
- Use `StringIntMapping` ScriptableObjects for any string→int translations
  (cell types, colors) so the mapping is editable without code changes.

### 2. Create the ScriptableObject assets

In the Unity Editor, right-click in the Project window:

- `Hoppa / Your Game / Master Level Exporter` → `YourGameMasterLevelExporter.asset`
- `Hoppa / Level Editor / String Int Mapping` → color mapping asset
- `Hoppa / Level Editor / String Int Mapping` → cell type mapping asset

Fill in the mappings to match your game's enum values exactly.

Set `_outputPath` to the absolute path of the file your game loads at runtime
(e.g. `E:/Projects/YourGame/Assets/Configs/Resources/level_config.json`).

### 3. Assign the exporter to the GameProfile

Open your game's `GameProfile` asset. In the **Exporters** list, add your new
`YourGameMasterLevelExporter.asset`.

That's it. From now on:
- **Save** in the editor → game-ready file updated automatically.
- **Export ▸** button → same exporters run on demand, with a save prompt if
  there are unsaved changes.

---

## Switching between games

Each `GameProfile` carries its own `Exporters` list. Switching profiles in the
editor (on the startup screen) automatically switches which exporter runs. No
code toggle needed.

If you need to temporarily disable an exporter without removing it, remove it
from the `Exporters` list in the Inspector and re-add it when ready.

---

## Verifying the output

Before shipping levels to a new game, verify:

1. **Integer mappings** — the `ColorType` and `BottomType` (or equivalent)
   integer values in your mapping assets match the game's enum ordinals exactly.
   A mismatch causes wrong colors / wrong cell types at runtime with no error.

2. **Position convention** — confirm whether `x`/`y` are lowercase or
   uppercase in the game's deserialization model. The editor emits whatever your
   exporter writes; the game's JSON library may or may not be case-sensitive.

3. **Dictionary key format** — if the game uses `Dictionary<int, T>`, keys in
   JSON must be string-form integers (`"1"`, `"2"`, …), not zero-padded.

4. **Enum format** — Newtonsoft.Json serializes enums as strings by default;
   `JsonUtility` serializes them as integers. Match your exporter's output to
   what the game expects.

---

## Reference: Yarn Twist integration (the first game)

| Artifact | Location |
|----------|----------|
| Exporter class | `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs` |
| Exporter asset | `Assets/YarnTwist/Data/Config/Exporters/YarnMasterLevelExporter.asset` |
| Color mapping | `Assets/YarnTwist/Data/Config/Exporters/YarnColorMapping.asset` |
| Cell type mapping | `Assets/YarnTwist/Data/Config/Exporters/YarnCellTypeMapping.asset` |
| Output path | `E:/Projects/Hoppa/YarnTwist/Assets/_YAT/Configs/Resources/Configs/level_config.json` |
| Game reads it via | `YATConfigManager` → `Resources.LoadAll<TextAsset>("Configs")` |
