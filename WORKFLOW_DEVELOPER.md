# Developer Integration Guide

How to wire a new game into the Hoppa Level Editor framework.
One game = one Editor assembly + one Runtime assembly.

---

## Step 0 — Assembly setup

Create two `.asmdef` files (one per folder):

**Runtime** (`Assets/MyGame/Runtime/MyGame.Runtime.asmdef`)
```json
{
  "name": "MyGame.Runtime",
  "references": ["Hoppa.LevelEditor.Core.Runtime"],
  "autoReferenced": true
}
```

**Editor** (`Assets/MyGame/Editor/MyGame.Editor.asmdef`)
```json
{
  "name": "MyGame.Editor",
  "references": [
    "Hoppa.LevelEditor.Core.Runtime",
    "Hoppa.LevelEditor.Core.Editor",
    "MyGame.Runtime"
  ],
  "includePlatforms": ["Editor"]
}
```

---

## Step 1 — Define cell types (Runtime)

One class per cell type. Implement `ICellData`. Use `[JsonProperty]` for every serialized field.

```csharp
// Runtime/MyBoxCell.cs
using Newtonsoft.Json;
using Hoppa.LevelEditor.Core;

public sealed class MyBoxCell : IColoredCell
{
    public string CellTypeId => "mg.box";   // stable string ID — never rename once levels are saved

    [JsonProperty("colorId")]
    public string ColorId { get; set; } = "red";
}
```

Required minimum: an **Empty** cell (used for erase + new-level fill) and a **Wall** cell.

---

## Step 2 — Define cell definitions (Editor)

One `CellTypeDefinition` subclass per cell type. Controls palette appearance and in-canvas inspector.

```csharp
// Editor/Cells/MyBoxCellDefinition.cs
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(menuName = "MyGame/Cells/Box")]
public sealed class MyBoxCellDefinition : CellTypeDefinition
{
    [SerializeField] private ColorPaletteAsset _palette;

    public override ICellData CreateDefault() => new MyBoxCell();

    public override void DrawCell(Rect rect, ICellData data)
    {
        if (data is not MyBoxCell box) return;
        Color c = Color.gray;
        if (_palette != null) _palette.TryGetColor(box.ColorId, out c);
        EditorGUI.DrawRect(rect, c);
    }

    public override void DrawInspector(Rect rect, ref ICellData data)
    {
        if (data is not MyBoxCell box) return;
        // draw color dropdown, toggles, etc. using EditorGUI
    }
}
```

---

## Step 3 — Define validation rules (Runtime or Editor)

Authoring-time checks only. These catch designer mistakes in the Level Editor.
**They do NOT run in the shipped game.**

```csharp
// Editor/Validation/MyBalanceRule.cs
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

[CreateAssetMenu(menuName = "MyGame/Rules/Balance")]
public sealed class MyBalanceRule : ValidationRuleBase
{
    [SerializeField] private int _valuePerCell = 9;

    public override ValidationScope Scope => ValidationScope.Color;

    public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
    {
        if (ctx.Grid?.Cells == null) yield break;
        // count sources/sinks, yield ValidationEntry on mismatch
        // severity: ValidationSeverity.Error / Warning / Info
    }
}
```

The `Id` field (from `ValidationRuleBase`) is the rule's stable identifier — set it in the Inspector on the `.asset`.

---

## Step 4 — Define the exporter (Editor)

Runs automatically on every Save. Produces a `.asset` file next to the `.json` for runtime loading.

```csharp
// Editor/MyLevelAsset.cs
using Hoppa.LevelEditor.Core.Editor;
public sealed class MyLevelAsset : LevelAsset { }  // nothing else needed

// Editor/MyExporter.cs
using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

[CreateAssetMenu(menuName = "MyGame/My Exporter")]
public sealed class MyExporter : ScriptableObjectExporter
{
    protected override LevelAsset CreateLevelAssetInstance()
        => CreateInstance<MyLevelAsset>();
}
```

---

## Step 5 — (Optional) Top section panel (Editor)

For game-specific UI above the grid (e.g., Yarn Twist's spool columns).

```csharp
// Editor/MyTopSectionPanel.cs
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class MyTopSectionPanel : TopSectionPanel
{
    public override float PreferredHeight => 80f;

    public override void OnGUI(Rect rect, LevelEditorSession session)
    {
        // Read:  session.Document.TopSection?.ToObject<MyTopData>()
        // Write: session.Document.TopSection = JObject.FromObject(myData)
        //        inside EditorGUI.EndChangeCheck() block
        // Undo:  session.PushUndoSnapshot() before mutations
    }
}
```

---

## Step 6 — Create config assets in Unity Inspector

Do this once per game. Create assets in `Assets/MyGame/Data/Config/`:

| Asset | Right-click → Create menu path |
|---|---|
| `MyPalette.asset` | Hoppa / Level Editor / Color Palette |
| `MyEmptyCellDef.asset` | MyGame / Cells / Empty |
| `MyBoxCellDef.asset` | MyGame / Cells / Box |
| `MyBalanceRule.asset` | MyGame / Rules / Balance |
| `MyExporter.asset` | MyGame / My Exporter |
| `MyGameProfile.asset` | Hoppa / Level Editor / Game Profile |

**Wire `MyGameProfile.asset` in the Inspector:**

| Field | Value |
|---|---|
| Schema Id | `"mygame.v1"` |
| Color Palette | `MyPalette` |
| Cell Types | `[MyEmptyCellDef, MyBoxCellDef, …]` — **Empty must be index 0** |
| Rules | `[MyBalanceRule, …]` |
| Exporters | `[MyExporter]` |
| Top Section Script | `MyTopSectionPanel` (drag the MonoScript) |

---

## Step 7 — Open the Level Editor

`Window → Hoppa → Level Editor` → **Open Profile** → select `MyGameProfile.asset`.

---

## Step 8 — Runtime level loading (in the game)

`LevelAsset` is the only framework type the game repo needs to reference.

```csharp
// In your game's level loader (Runtime assembly)
using Hoppa.LevelEditor.Core;

public void LoadLevel(MyLevelAsset asset)
{
    var registry = new CellTypeRegistry();
    registry.Register("mg.empty", typeof(MyEmptyCell));
    registry.Register("mg.wall",  typeof(MyWallCell));
    registry.Register("mg.box",   typeof(MyBoxCell));

    LevelDocument doc = new JsonLevelSerializer().Load(asset.LevelJson, registry);

    // doc.Grid.Get(x, y)  → ICellData (cast to concrete type as needed)
    // doc.TopSection      → JObject, parse with .ToObject<MyTopData>()
    // doc.GameData        → JObject, use for move limits, win params, etc.
}
```

---

## Step 9 — Win / lose logic

Win/lose is **not** part of the framework — it lives in your game code.

**Simple approach:**
```csharp
if (gameState.RemainingBalls == 0)  TriggerWin();
if (gameState.AvailableMoves == 0)  TriggerLose();
```

**Configurable approach — ScriptableObject (same pattern as `ValidationRuleBase` but runtime):**
```csharp
// Runtime assembly
public abstract class WinConditionBase : ScriptableObject
{
    public abstract bool Evaluate(GameState state);
}

[CreateAssetMenu(menuName = "MyGame/Win Condition/All Sorted")]
public sealed class AllSortedWin : WinConditionBase
{
    public override bool Evaluate(GameState state) => state.RemainingBalls == 0;
}
```

> `GameProfile` is Editor-only and cannot be used in game builds.
> Win conditions that vary per level can also be stored in `LevelDocument.GameData` (JObject) and read at runtime.

---

## Checklist

- [ ] Two `.asmdef` files (Runtime + Editor)
- [ ] Cell type C# classes — `ICellData` subclasses (Runtime)
- [ ] Cell definition SOs — `CellTypeDefinition` subclasses (Editor)
- [ ] Validation rule SOs — `ValidationRuleBase` subclasses (Editor)
- [ ] `LevelAsset` subclass + `ScriptableObjectExporter` subclass (Editor)
- [ ] `TopSectionPanel` subclass if the game has a top section (Editor)
- [ ] Config `.asset` files created and wired in `GameProfile.asset`
- [ ] Runtime level loader reading `LevelJson`
- [ ] Win/lose logic in game code
