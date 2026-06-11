# Fix Suggestions — Infinite Loops, Stack Overflow & "Stuck on Load" Audit

> Scope: all C# we authored under `Assets/_YAT/Scripts/`.
> Focus: (1) loops that can spin forever / overflow the stack, and (2) logic that can leave Unity hanging on the loading screen.
>
> **TL;DR — the most likely cause of your intermittent "stuck on loading" is the callback-counting `WaitUntil` pattern used across the entire boot chain (findings #1–#5).** A single thrown exception or one un-fired callback *anywhere* in init makes the loading screen wait forever with no error. Everything else is lower priority.

---

## CRITICAL — "Stuck on load" (matches the reported symptom)

The boot sequence chains three `WaitUntil` gates, none with a **timeout** or a **try/catch**. If any awaited flag never flips, the loading screen hangs silently — no exception surfaces, nothing in the log says "I'm stuck here."

### #1 `YATGamelogicLoaderComponent.LoadCoroutine` — line 64
`Assets/_YAT/Scripts/Gamelogic/YATGamelogicLoaderComponent.cs:64`
```csharp
yield return new WaitUntil(() => _gameLogicLoaded && _finishEnterLogo);
```
- Waits on **two** external flags with no timeout.
- `_finishEnterLogo` is set **only** by `OnFinishEnterLogo()` (lines 49–52) — presumably an animation event on the logo. If that animation/event doesn't fire (disabled object, missing event, interrupted clip), the loader hangs forever.
- `_gameLogicLoaded` depends on the entire chain below (#2–#5).
- **Fix:** add a timeout fallback + log which flag is still false, e.g. wrap in a timed loop that `YATDebug.LogError`s after N seconds and proceeds (or retries). Never `WaitUntil` on a boot flag without an escape hatch.

### #2 `YATGamelogic.InitializeManagersAsync` — line 46
`Assets/_YAT/Scripts/Gamelogic/YATGamelogic.cs:46`
```csharp
ScoreManager = new(() => initializedManagers++);
PlayerAnalyticsManager = new(() => initializedManagers++);
LevelManager = new(() => initializedManagers++);
NewFeatureManager = new(() => initializedManagers++);
yield return new WaitUntil(() => initializedManagers >= numManagers); // numManagers = 4
```
- Counter-based gate. If **any** of the 4 manager constructors throws *before* invoking its `onComplete`, the counter never reaches 4 → permanent hang.
- No `try/catch` around the constructors, so an exception is swallowed by the coroutine machinery and looks identical to "still loading."
- **Fix:** wrap each construction in try/catch (increment + log on failure so the gate can still complete), and add a timeout to the `WaitUntil`.

### #3 `YATManager.InitializeCoreManagersAsync` — lines 69 & 77
`Assets/_YAT/Scripts/Core/YATManager.cs:69` and `:77`
```csharp
ConfigManager = new(() => initializedManagers++);
PoolManager   = new(() => initializedManagers++);
FactoryManager= new(() => initializedManagers++);
yield return new WaitUntil(() => initializedManagers >= numManagers); // phase 1: 3
...
SoundManager   = new(() => initializedManagers++);
SettingManager = new(() => initializedManagers++);
yield return new WaitUntil(() => initializedManagers >= numManagers); // phase 2: 2
```
- Same fragile pattern, now in **two sequential phases**. If phase-1 stalls, phase-2 never starts and `LoadManager`'s `onComplete` never fires.
- **Fix:** same as #2 (try/catch per manager + timeout).

### #4 `YATLevelManager` constructor — lines 17–50 (callback chain)
`Assets/_YAT/Scripts/Gamelogic/Managers/YATLevelManager.cs:17`
- This is one of the 4 managers gated by #2. It nests two async calls: `GetConfigAsync(...)` → `SaveManager.LoadData(...)` → `onComplete()`.
- The `onComplete` (which increments #2's counter) fires **only at the very bottom** (line 48). If `GetConfigAsync` throws (see #5), the inner lambda never runs, the counter never increments, and #2 hangs → #1 hangs → **stuck on load**. This is a concrete, end-to-end path to the bug.
- **Fix:** guard the whole body; always invoke `onComplete` even on failure (in a `finally`).

### #5 `YATConfigManager.GetConfigName` — line 60 (NRE that breaks the chain)
`Assets/_YAT/Scripts/Core/Managers/YATConfigManager.cs:60`
```csharp
var config = _currentConfigs.CurrentConfigs.FirstOrDefault(...);
```
- `_currentConfigs` is assigned from `GetConfigOfflineAsync("current_configs", ...)` in the constructor (lines 16–20). If `current_configs` is **missing or fails to deserialize**, that callback is invoked with `default` → `_currentConfigs` is `null`.
- Later, `GetConfigAsync` → `GetConfigName` dereferences `_currentConfigs.CurrentConfigs` → **NullReferenceException** thrown *inside* the LevelManager init callback (#4) → counter never increments (#2) → hang.
- **Fix:** null-check `_currentConfigs` in `GetConfigName`; if null, log a clear error and return null so the chain can complete with a handled failure instead of hanging.

> **Recommended structural fix for #1–#5:** replace the "increment a shared counter and `WaitUntil`" idiom with a helper that (a) wraps each init in try/catch, (b) always reports completion (success *or* handled failure), and (c) has a hard timeout that logs the name of whatever never reported. That single change converts every silent hang above into a visible, diagnosable error.

---

## HIGH — Runtime exception that aborts level load (not a loop, but kills loading)

### #6 `YATLevelManager.GetLevelConfig` — lines 151–152
`Assets/_YAT/Scripts/Gamelogic/Managers/YATLevelManager.cs:151`
```csharp
int loopRangeSize = maxLevel - _loopLevel + 1;          // _loopLevel = 34
int loopedLevel   = _loopLevel + ((level - _loopLevel) % loopRangeSize);
```
- If the loaded config has **fewer than 34 levels**, `loopRangeSize <= 0`:
  - `loopRangeSize == 0` → `% 0` → **DivideByZeroException**.
  - `loopRangeSize < 0` → negative `loopedLevel` → `KeyNotFound` / wrong lookup.
- Only triggers when a level *beyond* `maxLevel` is requested, but that's exactly the looping path this code exists for.
- **Fix:** guard `if (loopRangeSize <= 0) return _config.LevelConfigs[maxLevel];` before the modulo.

---

## MEDIUM — Coroutines that can spin forever (gameplay soft-lock, they yield so they won't freeze the editor)

### #7 `YATWinderPrefabComponent.MoveLinearCoroutine` — line 265
`Assets/_YAT/Scripts/Gamelogic/Gameplay/Level/YATWinderPrefabComponent.cs:265`
```csharp
while ((transform.localPosition - endLocalPosition).sqrMagnitude > 0.001f)
{
    transform.localPosition = Vector3.MoveTowards(..., _speed * Time.deltaTime);
    yield return null;
}
```
- If `_speed == 0` (serialized/forgotten), `MoveTowards` never advances → the winder never reaches the target → coroutine runs every frame forever. Any sequence that waits on this move completing soft-locks.
- **Fix:** assert/guard `_speed > 0`, or add a max-duration safety break.

### #8 `YATYarnBallPrefabComponent.MoveIntoRoadCoroutine` — line 210
`Assets/_YAT/Scripts/Gamelogic/Gameplay/Level/YATYarnBallPrefabComponent.cs:210`
```csharp
while (true)
{
    _currentTime += speed * Time.deltaTime;  // target point also moves along the spline
    ...
    if (Vector3.Distance(transform.position, targetPos) < 0.01f) { ...; break; }
    yield return null;
}
```
- The ball chases a target that is itself advancing. If `transitionSpeed` can't out-pace the moving target, the `< 0.01f` break condition may never be met → never exits.
- **Fix:** add a frame/time budget, or evaluate the spline at a fixed point until caught up.

### #9 `YATYarnBallPrefabComponent.ForceCoroutine` — line 154
`Assets/_YAT/Scripts/Gamelogic/Gameplay/Level/YATYarnBallPrefabComponent.cs:154`
- `while(true)` "anti-stuck" nudger. Correct (yields on `WaitForFixedUpdate`), but it's an unbounded self-correcting loop — verify it's always stopped when the ball is consumed/pooled, or a pooled ball keeps running it.

---

## LOW — `while(true)` service coroutines (by design; listed for completeness)

All of these yield each frame and are intended to run until explicitly stopped. They are **not** hard hangs, but each relies on its owner calling Stop / being destroyed. Worth a quick check that none keep running on a **pooled** object that gets reused:

| File | Line |
|------|------|
| `Gamelogic/Gameplay/Services/YATClickHandler.cs` (`SpeedFeatureCoroutine`) | 64 |
| `Gamelogic/Gameplay/Services/YATClickHandler.cs` (`HandleClickCoroutine`) | 79 |
| `Gamelogic/Gameplay/YATGameManagerComponent.cs` (`TimeCoroutine`) | 369 |
| `Gamelogic/Gameplay/Level/YATInjectorComponent.cs` (`UpdateCoroutine`) | 67 |
| `Gamelogic/Gameplay/Level/YATConveyorSlotPrefabComponent.cs` (`MovementCoroutine`) | 64 |
| `Gamelogic/Gameplay/Level/YATConnectLineWinderPrefab.cs` | 62 |
| `Gamelogic/Gameplay/UI/Core/YATSpeedUpComponent.cs` | 59 |

---

## LOW — Editor-side (can freeze the Editor / Level Editor window, not the runtime load)

### #10 `YarnTwistLevelAnalyzer.DfsMemo` — recursive search, line 556
`Assets/_YAT/Scripts/Editor/Analysis/YarnTwistLevelAnalyzer.cs:556`
- Recursive DFS. **Time** is bounded (timeout checked at lines 558–559), but **stack depth** is not. Depth grows with item count; a pathologically large grid could `StackOverflowException` (uncatchable, kills the Editor).
- Low risk at normal puzzle sizes. **Fix (optional):** cap grid size before analysis, or convert to an explicit stack.

### #11 Bounded-but-invariant-dependent loops — verify data invariants
- `YarnTwistLevelAnalyzer.ResolveMatches` — `Assets/_YAT/Scripts/Editor/Analysis/YarnTwistLevelAnalyzer.cs:851` and inner `:856`
- `YarnSpoolConnection.ConnectionsDeadlock` — `Assets/_YAT/Scripts/Editor/TopSection/YarnSpoolConnection.cs:104` and inner `:109`
- These terminate via *monotonic progress* (`spoolHead` only increases). They're correct as written, but the termination guarantee depends on partner/position indices being well-formed. If malformed level data ever made progress non-monotonic, they'd loop. **Fix (optional):** add an iteration cap as a safety net.

### #12 `YarnTopSectionPanel.OnGUI` — line 58
`Assets/_YAT/Scripts/Editor/TopSection/YarnTopSectionPanel.cs:58`
```csharp
while (topData.Columns.Count < Columns)
    topData.Columns.Add(new YarnSpoolColumn());
```
- Bounded (terminates), but runs on **every repaint** and allocates. Cosmetic / GC pressure only.

---

## Cleared on review (no action needed)
- `YarnTwistLevelGenerator.Generate` — outer loop is capped by `MaxRerollAttempts` (`:60`). ✔
- `YATConfigManager.GetConfigOfflineAsync` — always invokes `onComplete` (success, catch, and not-found branches). ✔ The hang risk is the *NRE in #5*, not a missing callback here.
- `YATExtension.RemovePartsAndToEnum` (`:449`) and `ShuffleList` (`:478`) — both strictly shrink each iteration; terminate. ✔
- The various `while (elapsed < duration)` tween coroutines in `YATExtension` / `YATRopeVerlet` / `YATCoinsDisplayUIComponent` — bounded by time. ✔

---

## Suggested priority order
1. **#5 + #4 + #2/#3** — add try/catch + always-report + timeout to the boot chain. This is the fix that most likely ends the "stuck on loading."
2. **#1** — timeout + logging on the loader's `WaitUntil` so the *next* time it stalls you immediately see which flag is stuck.
3. **#6** — one-line guard against the divide-by-zero in level looping.
4. **#7 / #8** — safety breaks on the winder/ball move coroutines.
5. Editor items (#10–#12) as time permits.
