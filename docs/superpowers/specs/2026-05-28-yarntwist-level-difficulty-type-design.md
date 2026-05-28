# YarnTwist — Per-level Difficulty Type

**Date:** 2026-05-28
**Layer:** 2 (YarnTwist game layer) — no Layer 1 / UPM package changes
**Status:** Approved

## Problem

The YarnTwist game's `LevelConfig` gained a new field:

```csharp
public class LevelConfig
{
    public YATLevelType LevelType;   // NEW
    public BottomConfig[] BottomConfigs;
    public TopConfig[]    TopConfigs;
}

public enum YATLevelType { None, Hard, SuperHard }
```

The level editor has no way to set this per level, so exported `level_config.json`
entries never carry a `LevelType`. The designer needs a dropdown in the editing
panel to pick a difficulty type for each level.

## Solution

Reuse the existing per-level metadata pattern already used for `coinReward`:
store the value on the level document's `GameData`, render an editable control via
the exporter's Summary-panel hooks, and write it into the level entry on export.

### Data storage

- Stored as `doc.GameData["levelType"]` — a string: `"None"` / `"Hard"` / `"SuperHard"`.
- Default `"None"` when absent.
- Because `GameData` is serialized with the level JSON, save / open / undo
  round-trip for free (same as `coinReward`).

### UI — dropdown in the Summary panel

In `YarnMasterLevelExporter.cs`:

- Bump `ExtraSummaryRowCount` from `1` → `2`.
- In `DrawExtraSummaryRows`, draw a second row: a `"Difficulty"` mini-label plus an
  `EditorGUI.Popup` over the option list. On change, write the selected name to
  `doc.GameData["levelType"]` and call `session.MarkDirty()`.
- Option list is a `static readonly string[] LevelTypeOptions = { "None", "Hard", "SuperHard" }`
  mirroring the game's `YATLevelType`. If the game adds values later, this one array
  changes.
- The current selection is resolved by index-of the stored string, defaulting to
  index 0 (`None`) when missing or unrecognized.

### Export

In `Export(...)`, add `LevelType` to the `LevelConfigs[writeKey]` object, alongside
`levelId` / `BottomConfigs` / `TopConfigs`:

```jsonc
"5": {
  "levelId": "YT_005",
  "LevelType": "Hard",
  "BottomConfigs": [ ... ],
  "TopConfigs": [ ... ]
}
```

- Value read from `document.GameData?["levelType"]`, falling back to `"None"`.
- Written as the **enum-name string** (matches how `Direction` is already exported
  via `.ToString()`, and is what the game's Newtonsoft deserializer accepts).

## Out of scope (YAGNI)

- No Layer 1 / UPM package changes.
- No new validation rule for difficulty.
- No generator wiring.
- No separate panel — the existing Summary panel hosts it.

## Files touched

- `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs` — UI row + export line.
- `Assets/YarnTwist/Tests/Editor/YarnMasterLevelExporterTests.cs` — add coverage.

## Verification

**Automated (EditMode tests):**
- Export with `GameData["levelType"] = "Hard"` → entry contains `"LevelType": "Hard"`.
- Export with no `levelType` set → entry contains `"LevelType": "None"`.

**Manual (in Unity):**
- Select a level → pick "Hard" in the Summary "Difficulty" dropdown.
- Save → reopen → dropdown still shows "Hard" (persists via GameData).
- Export → confirm `"LevelType": "Hard"` in that level's entry in `level_config.json`.
- Ctrl-Z after changing the dropdown restores the prior value.
