# Summary Panel Enhancements — Design Spec
**Date:** 2026-05-13  
**Status:** Approved

---

## Overview

Five improvements to the Level Editor's Summary panel and export pipeline:

1. **Level ID from filename** — always reflects the saved filename, not the stale `doc.LevelId`
2. **Order index** — shows the level's key in `level_config.json` if already exported
3. **Layout** — shows the non-wall bounding box of the grid (e.g. `5 × 5`)
4. **APS float field** — editable documentation float stored in level metadata
5. **Per-level Coin Reward** — editable int field; persists last value across levels; always exported to `LevelRewardConfigs`

---

## Architecture

### Layer 1 — `LevelMetadata.cs`

Add one field:
```csharp
[JsonProperty("aps")]
public float Aps { get; set; }
```
Serialised into the `"metadata"` block of every level JSON. Generic enough for any game's level design documentation.

---

### Layer 1 — `LevelExporterAsset.cs`

Add two extension points (both virtual with no-op defaults so existing exporters are unaffected):

```csharp
// Read-only info rows shown in the Summary panel
public virtual IEnumerable<(string label, string value)> GetSummaryExtras(LevelEditorSession session)
    => System.Linq.Enumerable.Empty<(string, string)>();

// Number of editable rows the exporter needs below the info rows
public virtual int ExtraSummaryRowCount => 0;

// Draw editable fields into the reserved rect
public virtual void DrawExtraSummaryRows(Rect rect, LevelEditorSession session) { }
```

---

### Layer 1 — `SummaryPanel.cs`

**ID row:** Replace `doc.LevelId` with `Path.GetFileNameWithoutExtension(session.FilePath)` when `session.FilePath` is non-empty; fall back to `doc.LevelId` for unsaved new levels.

**Exporter info rows:** After the Grid row, iterate `session.Profile.Exporters`, call `GetSummaryExtras(session)` on each, and display each `(label, value)` pair as an accent-coloured row. De-duplicate by label if multiple exporters return the same key.

**APS field:** Positioned between the cell-counts separator and the Notes block. Label `"APS"`, drawn with `EditorGUI.FloatField`. On change: write to `meta.Aps`, call `session.MarkDirty()`.

**Exporter editable rows:** Before the Notes block, compute total height needed:
```
extraH = sum of ExtraSummaryRowCount across all exporters × lh
```
Reserve that space. For each exporter with `ExtraSummaryRowCount > 0`, slice a sub-rect and call `DrawExtraSummaryRows(subRect, session)`.

**Bottom layout (revised):**
```
[Header]
[Schema / ID / Grid / exporter info rows]
[separator]
[cell counts]
[separator]
[APS field]
[exporter editable rows]   ← new
[Notes label + textarea]
```

---

### Layer 2 — `YarnMasterLevelExporter.cs`

#### `GetSummaryExtras`

Returns up to two rows:

**Order:**
- Resolve `_outputPath`. If the file exists, parse `LevelConfigs` and find the key whose `levelId == Path.GetFileNameWithoutExtension(session.FilePath)`.
- Yield `("Order", key)` if found, `("Order", "—")` if not found or file missing.
- Skip entirely when `session.FilePath` is null/empty.

**Layout:**
- Scan `session.Document.Grid`. For each cell where `cell.CellTypeId != "yt.wall"`, track `minX, maxX, minY, maxY`.
- Yield `("Layout", $"{maxX-minX+1} × {maxY-minY+1}")`.
- Yield `("Layout", "—")` if no non-wall cells found.

#### `ExtraSummaryRowCount`

Returns `1`.

#### `DrawExtraSummaryRows`

Draws one row: label `"Coins"`, an `EditorGUI.IntField`.

**Read logic:**
1. Read `doc.GameData?["coinReward"]` as int if present.
2. If absent: read `EditorPrefs.GetInt("Hoppa.YarnTwist.LastCoinReward", _defaultRewardAmount)` as the display default.

**Write logic (on `EditorGUI.EndChangeCheck`):**
1. Ensure `doc.GameData` is non-null (`doc.GameData ??= new JObject()`).
2. Write `doc.GameData["coinReward"] = newValue`.
3. `EditorPrefs.SetInt("Hoppa.YarnTwist.LastCoinReward", newValue)`.
4. `session.MarkDirty()`.

#### `Export` — reward config

Remove the `if (rewardConfigsObj[writeKey] == null)` guard. Always write:
```csharp
int coinReward = doc.GameData?["coinReward"]?.Value<int>()
    ?? EditorPrefs.GetInt("Hoppa.YarnTwist.LastCoinReward", _defaultRewardAmount);

rewardConfigsObj[writeKey] = new JObject
{
    ["WinReward"] = new JArray
    {
        new JObject
        {
            ["ScoreType"]   = _defaultRewardScoreType,
            ["ScoreAmount"] = coinReward
        }
    }
};
```

---

## File Change Summary

| File | Layer | Type of change |
|---|---|---|
| `Runtime/Data/LevelDocument.cs` (LevelMetadata) | 1 | Add `float Aps` |
| `Editor/Exporters/LevelExporterAsset.cs` | 1 | Add 3 virtual members |
| `Editor/Panels/SummaryPanel.cs` | 1 | ID fix, info rows, APS field, exporter editable rows |
| `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs` | 2 | Override hooks; fix reward export |

No new files. No `.meta` files needed (no new assets).

---

## Edge Cases

- **Unsaved level:** ID falls back to `doc.LevelId`; Order row omitted; Coins reads from EditorPrefs default.
- **Level not yet in master JSON:** Order shows `"—"`.
- **Grid all walls:** Layout shows `"—"`.
- **`GameData` null on load:** `DrawExtraSummaryRows` handles null gracefully by reading EditorPrefs.
- **Multiple exporters:** `GetSummaryExtras` is called on each; rows are displayed in exporter list order.
