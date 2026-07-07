# Bus Buddies — Level Integration Plan (2026-07-07)

Getting editor-generated levels playing in the **Bus Buddies game** (`E:/Projects/Hoppa/BusBuddies`).

## The game's real level schema (reverse-engineered from the project)

Each level is **its own JSON file** at `Assets/_BUB/Resources/Configs/Levels/level_<N>.json`, loaded by
`BUBConfigManager.LoadLocalConfig<LevelConfig>("Levels/level_<N>")` →
`Resources.Load<TextAsset>` + `JsonConvert.DeserializeObject` (**Newtonsoft**, PascalCase, enums as **int**).

```csharp
class LevelConfig {
  int SlotsAmount;                     // active-row slots (editor "conveyorCount"); sample = 5
  int Width, Height;                   // sample = 20 x 20  (⚠ confirm with Eliran — see below)
  BusColumnConfig[] BusColumnConfigs;  // the bus queue columns
  BUBColorType[] PixelColors;          // the board picture; index = y*Width + x
}
class BusColumnConfig { BusConfig[] BusConfigs; }
class BusConfig { BUBColorType ColorType; int Capacity; BusType BusType; }  // BusType None|Hidden
```

- **Colors:** `BUBColorType` is **byte-identical to the YAK palette** — Blue=1, Cyan=2, Yellow=3, Green=4,
  Magenta=5, Orange=6, Pink=7, Purple=8, Red=9, … Black=17, Brown=18, … Gold=29 … BrownVeryDark=36.
  So YAK/editor ColorId → BUB ordinal is a straight name→value map (case-insensitive).
- **Orientation:** `BUBPixelService` lays pixels with `index = y*width + x`, x→right, y→+Z (up). The editor
  grid `Cells[]` is already row-major bottom-up, so **no inversion needed** (same mapping proven for YAK).
- **Buses:** `BusColumnConfigs[i].BusConfigs[j]` = `{ColorType, Capacity, BusType}`. `BusType` omitted → `None`.
- Sample `level_1.json` has hand-authored buses but **empty `PixelColors`**; `level_2/3.json` are 35-byte stubs.

## ✅ Done this session
- **`BusBuddiesGameLevelExporter`** (`Assets/BusBuddies/Editor/Exporters/`) — new exporter emitting the
  schema above, **one `level_<N>.json` per level**, full 36-color map, `index=y*Width+x`, `BusType` written
  only when hidden. **6 unit tests, all green** (`BusBuddiesGameLevelExporterTests`). Writes to a configurable
  output dir (default `StagingExports/BusBuddies/Levels`, project-relative — copy into the game).
- (The older `BusBuddiesLevelExporter` still emits the deprecated single-file `{LevelConfigs:{…}}` YAK-shaped
  master; kept for reference. The new one is what matches the current game.)

## 🚧 Blocked on Eliran (decisions for tomorrow)

### 1. Geometry — 20×20 or 30×30?
`level_1.json` says **20×20**, but the lead expects **30×30**. The exporter is size-agnostic (reads dims from
the level), so this is purely a generation choice. **Confirm with Eliran**, then we generate at that size.
Plan: produce the demo levels at **both** sizes so Eliran can eyeball.

### 2. The game doesn't render `PixelColors` yet ← the real blocker
In `BUBPixelService.InitPixels` the picture read is **commented out** and replaced with random colors:
```csharp
int index = y * width + x;
// var colorType = levelConfig.PixelColors[index];   ← commented
#if UNITY_EDITOR
var colorType = _gameManager.ColorTypes.GetRandomFromArray();  ← random
#endif
```
Until this reads `PixelColors`, exported pictures show as random boards. **Suggested backward-compatible change
for Eliran** (keeps random fallback for levels with empty PixelColors):
```csharp
int index = y * width + x;
BUBColorType colorType;
if (levelConfig.PixelColors != null && index < levelConfig.PixelColors.Length
    && levelConfig.PixelColors[index] != BUBColorType.None)
    colorType = levelConfig.PixelColors[index];
else
#if UNITY_EDITOR
    colorType = _gameManager.ColorTypes.GetRandomFromArray();
#else
    colorType = BUBColorType.None;
#endif
```
(Note: as written, `colorType` is only assigned under `#if UNITY_EDITOR`, so a non-editor build wouldn't
compile — the branch above also fixes that.)

## Next steps (once Eliran confirms 1 & 2)
1. Bring **`BusBuddiesImageToGrid`** up to parity with the YAK converter's cleanup (halo-absorb / keep-largest /
   despeckle + full palette) so BB boards look as clean as today's YAK set — *or* generate the board via the
   polished YAK converter and feed it through the BB autofiller for buses.
2. Generate 2–3 good levels (e.g. penguin, cupcake, pizza) at the agreed size, export via
   `BusBuddiesGameLevelExporter`, copy `level_1/2/3.json` into `Assets/_BUB/Resources/Configs/Levels/`
   (back up the existing files first).
3. Eliran enables `PixelColors` → verify the pictures render + play.
