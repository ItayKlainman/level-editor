# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## 🟢 CODE-COMPLETE (2026-07-20) — Audio Balance panel (new package `com.hoppa.audiobalance`)

Branch `feat/audio-balance`, **21+ commits, NOTHING PUSHED**.
**All 13 tasks complete and reviewed. 657/657 EditMode green** (~139 in the new package).
**Nothing has been hand-verified in a live editor by a human yet** — see the handover checklist
at the end of the Task 13 section.

- **Spec:** `docs/superpowers/specs/2026-07-19-audio-balance-panel-design.md`
- **Plan:** `docs/superpowers/plans/2026-07-19-audio-balance-panel.md` (13 TDD tasks; its
  **"Plan self-review" section at the bottom records all 5 deviations** and why)
- **Ledger (GITIGNORED — do not rely on it surviving `git clean -fdx`):** `.superpowers/sdd/progress.md`
- Executed with `superpowers:subagent-driven-development`: fresh implementer per task, opus/sonnet
  reviewer per task, fix-round + re-review when a review finds Critical/Important.

### What it does
Pick one clip as the **anchor** (normally level BG music). Every other clip is measured for
*perceived* loudness (LUFS, ITU-R BS.1770-4 — not the Unity volume field) and assigned a gain
placing it at a deliberate offset from the anchor, via category offsets (Music 0 / SFX +3 / UI −6)
plus a per-clip trim. Gains bake into a runtime `AudioGainTable` asset. **Source .wav files are
never modified.** v1 wires up **no game** — BB adoption is a separate, deliberate step.

### Built (Tasks 1–8)
`AudioGainMath`/`AudioGainTable`/`AudioGainTableExtensions` (Runtime) · `KWeighting` (coefficients
match the published 48 kHz table to 1e-9) · `LufsMeter` integrated loudness + two-stage gating
(**calibration test reads −22.9933 LUFS**) · `MomentaryMax` · `PeakMeter.SamplePeakDb` ·
`AudioBalanceProfile`/`AudioCategory`/`ClipSettings` · `GainSolver` + headroom pass · `ClipSampleReader`.

### Shipped 2026-07-20 (Tasks 9–10)
- **Task 9 `LoudnessCache`** — `a229595`, verdict SHIP. Took **three review rounds**; see the
  cache-key story below. `Library/HoppaAudioBalance/loudness-cache.json`, regenerable, gitignored.
- **Task 10 `LoudnessAnalyzer`** — `7290daf`, verdict SHIP, no Criticals. `Analyze`/`FindClips`/
  `PruneMissingClips`/`ShouldCache`. **`Unanalyzable` results are deliberately NOT cached**:
  `ClipSampleReader.LoadPendingError` is *transient* and its message tells the user to re-run, but
  re-running doesn't change the key — so a cached failure would be served forever and the instructed
  remedy was impossible. Chose that over adding a `Reason` field (also kills a Task-12 bug where a
  missing `Reason` fell through to an *outlier* tooltip — a wrong diagnosis).

### Shipped 2026-07-20 (Task 11) — window shell
**`288d109`**, 565 → **582 EditMode green** (+12 `AudioBalanceSessionTests`, +5 `EditGestureTests`).
`AudioBalanceRow` / `AudioBalanceSession` / `EditGesture` / `AudioBalanceWindow` under
`Editor/Window/`. Implemented from the AMENDED plan, so all six audit corrections are in:
category edits call `Analyze` (not `Resolve`), outliers suppressed when the anchor isn't `Ok`,
computed `CategoryBlockHeight`, real progress+Cancel, ASCII captions, `ExitGUI()` after modals.

- **PLAN DEFECT #7 (mechanical, fixed in place):** the plan's own test block called
  `Assert.AreNotEqual(integrated, momentary, 0.5f, "...")`. **NUnit's `AreNotEqual` has no
  tolerance overload** — `0.5f` binds to `message` → `CS1503`. The plan's Task 11 test file did
  not compile as written. Rewritten as `Assert.Greater(momentary - integrated, 0.5f)`, intent
  preserved. Worth noting the pattern held again: the defect was in the *test*, not the design.
- **Test strengthened beyond the plan (deliberate).** `Analyze_WithNoAnchor_DoesNotFlagEvery
  RowAsAnOutlier` as written only pinned the −23 sentinel, not the suppression: at −20/−22 both
  rows sit within 12 dB of the sentinel anyway, so it passed against a build that swapped the
  constant and suppressed nothing. Added a −60 clip (37 dB from the sentinel). **Mutation-verified:**
  disabling the suppression fails it with `Expected: <empty> But was: < "far" >` — i.e. *only* the
  added clip flags. Without it the test was vacuous.
- **`EditGesture` is an addition beyond the plan's code block**, required by the brief's undo rule
  (the plan still recorded undo inside `EndChangeCheck`, which fires per frame during a
  `FloatField` label-drag). Pure state machine, 5 tests, testable outside `OnGUI`.
- **Anchor-change behaviour is an addition:** picking a new anchor re-runs `RunAnalysis()` **only
  when rows already exist**, so the LUFS readout and outlier flags stay live instead of going
  stale next to a fresh clip. Guarded so it never kicks off an unprompted full-library decode.
- **Smoke-verified via `script-execute`** (not just tests): window opens, `RepaintImmediately`
  drives a real `OnGUI` pass in both the empty-state and a six-category profile, no exceptions,
  block height 170 ≥ 162 needed. Editor left closed/clean.
- **Still lead-only:** whether it *looks* right. See the Task 11 review notes handed back.

#### Review round 1 — BLOCKED, fixed in `fb54b89` (582 → **593 green**)
- **C1 — the gesture machine was unreachable from the window, and the reviewer's prescribed fix
  would not have worked either.** Review said "use `Event.current.rawType`, which is not rewritten
  to `Used` on consumption." **Measured, in live IMGUI with real mouse events: `Use()` rewrites
  BOTH `type` AND `rawType` to `Used`** (`after type:Used/raw:Used` on every frame). `rawType`
  surviving consumption is a widely-repeated belief that is false in this position. Fixed with
  `GUIUtility.hotControl != 0` instead — measured stable at `487` for the whole gesture, `0` after
  release, untouched by `Use()`. **Second correction:** the review's stated mechanism (dragging the
  category `OffsetDb` label) does not occur — `EditorGUI.FloatField(rect, value)` is the *label-less*
  overload with an EMPTY drag hot zone and never streams (`changed:False` on every drag frame). The
  defect was real but latent; it goes live with **Task 12's trim slider**, which the probe confirms
  does stream per frame. New `EditGestureSeamTests` drives real events through a real window —
  reproducing the old behaviour fails it `Expected: 1 But was: 3`.
- **I2/I3/I4 fixed** — `FindSettings` (non-mutating) added to the profile so `AudioBalanceSession`
  never enrols; enrolment is explicit in `RunAnalysis` inside Undo; anchor measured once not twice;
  `RenameCategory` renames AND re-points clips as one unrepresentable-to-get-wrong operation;
  `[SerializeField]` on `_profile`/`_clipScroll`.
- **Three tests that could not fail** were rewritten (null-profile clear, anchor-status staleness,
  cancel-keeps-rows). Note `Gain.Status` is a **vacuous assertion by construction** — `ClipStatus.Ok`
  is `0`, so an unsolved `default(GainResult)` reports `Ok`; assert `Gain.Clip != null` instead.
- All five fixes mutation-verified, not just observed green.

#### Review round 2 — BLOCKED on a regression I introduced; fixed in `fa7da62` (593 → **602 green**)
- **HIGH-1 was my own regression.** Round 1's `RenameCategory(oldName, newName)` resolved the target
  by **first exact name match**. `Add Category` named every new row `"New"`, so two clicks + a rename
  renamed the **wrong row** while that same keystroke's offset/mode landed on the right one — one edit
  split across two categories. Now takes the `AudioCategory` **by reference**. `288d109` was correct
  here; my "structural" fix traded a good object reference for a lookup. **Lesson: a fix that
  introduces an indirection the caller didn't need is a regression risk, not a hardening.**
- **MEDIUM-2** — rename onto an existing name is rejected (was a silent merge that re-pointed clips to
  whichever category came first). **MEDIUM-3** — enforcement made real, not documented:
  `AudioCategory.Name` is now a read-only property (`Categories[i].Name = "x"` is a **compile error**
  outside the package), `SettingsFor` is `internal` + new `InternalsVisibleTo`, and
  `OffsetDbFor`/`TrimDbFor`/`ModeFor` — the actual hazard — are **genuinely non-mutating**, removing
  it at source rather than hiding it. Verified by reflection **from a separate assembly**.
  Residual gap stated in the doc rather than overclaimed: C# has no friend-of-one-type access, so
  in-assembly code can still reach the internal setter.
- **LOW** — `Clear()` sets `IsDirty`; `IsDirty` gained its own suite incl. the load-bearing
  **failed-`Save` keeps it set** case; seam click-test gained a ran-at-all precondition.
- **Documented for Task 12 (accepted, not bugs):** commit is deferred one frame when a button holds
  `hotControl` (can cause a second `RunAnalysis`); mid-gesture edits aren't `SetDirty`-ed, so a domain
  reload mid-drag loses them.
- All four round-2 behaviours mutation-verified, each failing its own test and no other.

### Shipped 2026-07-20 (Task 12) — sortable/filterable clip table + bulk assign
**602 → 623 EditMode green** (+15 `ClipListViewTests`, +2 `TrimSliderSeamTests`, +4 `PendingAnalyzeTests`).
New: `ClipSortMode` / `ClipListView` / `PendingAnalyze` under `Editor/Window/`; `DrawClips` replaced
with header (filter · sort · Asc/Desc · bulk **Set Category (n)**) + per-row
select · name · category · LUFS · gain · **trim slider** · status icon.

- **The trim slider is the first control in this window that genuinely STREAMS** (measured: 5 change
  frames per drag vs the category `FloatField`'s empty drag zone). It is wired to the existing
  `EditGesture` via `hotControl`, **not** the plan's `_trimDragClip` + `Event.type == MouseUp` shape —
  the plan's Task 12 text still used the event-type mechanism that Task 11 disproved. `TrimSliderSeam
  Tests` drives real IMGUI events and asserts `ChangeFrames > 1` as a **precondition**, so it cannot
  pass vacuously the way the label-less-field harness could.
- **Deviations from the plan (all deliberate, see commit body):** `EditGesture` for the trim (above);
  `ClipListView.BuildSettingsLookup` (new, tested, uses `FindSettings`) instead of an untested private
  `SyncSettings` calling the **mutating** `SettingsFor` from `OnGUI`; the per-row category change sets
  `_analyzeRequested` and runs at the END of `OnGUI` rather than firing a modal progress bar from
  inside an open `GUI.BeginScrollView`; category-name array hoisted out of the row loop.
- **Weak test found by mutation and fixed:** the plan's `BuildVisible_SortsByCategory` fixture
  (`"bed"`/`"blip"`) passed against a build that ignored the sort mode and always sorted by NAME.
  Renamed to `"zzz_bed"`/`"aaa_blip"` so name order contradicts category order.
- **Task 11 review nit folded in (lead's instruction):** `_categoryEditNeedsAnalyze` (a bool the
  rejected-rename path assigned `false` to, wiping a pending mode change from an earlier gesture
  frame) is now the `PendingAnalyze` type, which can only accumulate. The rename is applied **before**
  the flag is folded, so a rejection contributes nothing instead of clearing everything. The IMGUI
  interleaving itself is **not** covered by a test — see that file's class doc for why a harness would
  have passed vacuously.
- `[FormerlySerializedAs("Name")]` added to `AudioCategory._name`. Zero profile assets exist anywhere,
  so nothing needs migrating today; it costs one line and makes a stashed profile a non-question.
- All new behaviour **mutation-verified** in 5 rounds; every mutation failed its own test and, except
  where noted, no other.

### Shipped 2026-07-20 (Task 13) — preview player, Write Table, docs — FINAL TASK
**623 → 655 EditMode green** (+9 `GainTableWriterTests`, +8 `PreviewClipFactoryTests`,
+6 `CategoryEditTests`, +9 `ClipListViewTests`, +1 `EditGestureTests`, −1 deleted vacuous test).
New: `GainTableWriter` / `PreviewClipFactory` / `AudioPreviewPlayer` / `CategoryEdit` under
`Editor/Window/`; `README.md` + `Documentation~/audio-balance-guide.md`; window gained a second
toolbar row (Write Table + Table field) and per-row **Play** / **A/B** buttons.

- **The preview player is NOT the design the plan originally described.** `AudioSource.Play()` is
  silent outside Play Mode, so the planned hidden-scene-AudioSource could never have made a sound.
  Per the lead's amended decision it reflects into `UnityEditor.AudioUtil.PlayPreviewClip` and makes
  the gain audible by **pre-scaling into one reusable temp clip** (`HideAndDontSave`, destroyed
  before each new preview and on domain reload / play-mode change / window close — never one per
  click). **Reflection resolution VERIFIED live on this editor** via `script-execute`:
  Unity 6000.3.8f1 exposes `PlayPreviewClip(AudioClip, Int32, Boolean)` and `StopAllPreviewClips`,
  both found by the name-and-first-parameter search. Whether it is *audible* is still lead-only.
- **The reflection boundary is an accepted, documented gap**, in the same house style as
  `ClipSampleReader.StreamingError`. The testable half was extracted into `PreviewClipFactory`
  (`Scale`/`Mix`) and given 8 tests. **The plan's 7 Mix/Scale tests all passed 0 dB for both gains**,
  so every one of them would have survived a `Mix` that ignored its gain arguments entirely — the
  exact failure that makes A/B useless. Added `Mix_AppliesEachSignalsOwnGainRatherThanIgnoringThem`,
  which pins the two gains *separately*; mutation-verified (sharing one gain reads 0.6 vs 0.5).
- **`GainTableWriter.BuildEntries` also skips rows whose gain was never solved** (`Gain.Clip == null`),
  a strengthening beyond the plan. `Analysis` and `Gain` are filled by separate passes, and an
  unsolved `default(GainResult)` is **0 dB** — a plausible-looking value meaning "full volume".
  Per the standing rule, the discriminator is `Gain.Clip != null`, never `Gain.Status` (`Ok == 0`).
- `AssetDatabase.SaveAssets()` lives in the window's button handler, **not** in `Write` — `Write` is
  a pure asset mutation, so a test run cannot flush every dirty asset in the project.
- **Layout verified against `minSize` (720 wide, 708 usable):** table bar ends at x=672; the
  Play/A-B buttons end at x=794 inside the 800-wide `MinRowWidth` content rect, so they are reached
  by the horizontal scrollbar Task 12 introduced. **Do not narrow `MinRowWidth`.**
- **Docs are truthful about the two counterintuitive things** and must not regress: the anchor does
  **not** move the Gain column (it cancels; it is the outlier reference + readout), and it is the
  **quietest** clip relative to target that pins at 0 dB, not the loudest. Also stated plainly:
  source files are never modified, clipping is structurally impossible, nothing reaches the game
  until Write Table, compensate **once** on the master mixer, and the cache no-ops for clips in
  non-embedded packages.
- **Task 13's plan text did NOT carry the disproven `Event.type` gesture mechanism** (Task 12's did).
  Nothing to ignore there; reported for the record.
- Smoke-verified via `script-execute`: window renders at `minSize` in the empty state, with a table
  assigned, and with none assigned (hint path); `Teardown` is safe with nothing ever played. Editor
  left closed and clean.

#### Task 12 review findings, folded in here (reviewer's instruction — same files)
- **MEDIUM-1 `_settingsStamp` was blind to an identity change at constant count.** `rows*397 ^ clips`
  is unchanged by [A,B]→[A,C], so the settings map kept stale B: row C drew with **no category
  dropdown and no trim slider** while showing LUFS/Gain normally, and stale B stayed counted in the
  bulk button while resolving zero targets. (The hash was not injective either.) **Fingerprinting
  removed entirely** — `RebuildSettings()` is called from the three events that can invalidate it
  (`RunAnalysis`, profile switch, undo). Strictly stronger than a better hash.
- **MEDIUM-2 nothing subscribed to `Undo.undoRedoPerformed`.** Now subscribed in `OnEnable` /
  unsubscribed in `OnDisable`; the handler rebuilds the settings map and calls `Resolve` (**not**
  `Analyze` — an undo must not trigger a full-library decode and cannot have changed a measurement).
  This also closes the detached-`ClipSettings` hazard the reviewer flagged as likely-but-unverified.
- **LOW** — gestures now reset on profile switch (new `EditGesture.Reset()`, tested); the orphaned
  category is no longer a dead-end control (`ClipListView.CategoryPopupOptions` appends an explicit
  `(unknown: X)` entry instead of clamping to index 0, which displayed a category the clip was not
  in and could not be corrected by selecting it); the bulk button now reads
  `Set Category (12, 10 hidden)` when the filter hides part of the selection; the overstated
  "a miss should not be reachable at all" doc on `BuildSettingsLookup` now names the real path
  (undo of enrolment) and why read-only is the right response; `PendingAnalyze.Reset()` renamed
  **`ResetForProfileSwitch()`** so misuse reads wrong at the call site.
- **The category-fold sequencing is now testable and tested.** Extracted to `CategoryEdit.Apply`
  — pure sequencing, no IMGUI — with 6 tests. The load-bearing one is
  `Apply_WithARejectedRenameAndAModeChange_StillAsksForAReMeasure`. Mutation-verified: moving the
  `needsAnalyze` computation *after* `category.Mode = newMode` (a very easy real-world edit) fails
  it. The reviewer was right that this beat my "a harness would pass vacuously" reasoning.
- **TESTS** — deleted `BuildVisible_DoesNotMutateTheProfile` (**could not fail**: `BuildVisible` has
  no profile parameter, so the regression it claimed to guard would be a *compile error in the test*,
  not a red test — the 7th tautological test in this initiative). `ClipListViewTests.Lookup` switched
  from the **mutating** `SettingsFor` to `FindSettings`, so the 8 `BuildVisible` tests now exercise
  the same non-mutating shape production uses. Added the 4 requested gap tests.

#### Task 13 review — BLOCKED on the item I flagged for scrutiny; fixed in `3a4a5c4` (655 → **657 green**)
- **HIGH — `OnUndoRedo` called `Resolve` where the undo CAN change a measurement.** My doc claimed
  an undo "cannot have changed any measurement"; **three comments in my own commit contradicted it**
  (`AudioBalanceSession:121-127`, the forward path at `AudioBalanceWindow:762-767`, and
  `ClipListView:219-222`). Four undo entries this window pushes restore a measurement input —
  `"Edit Audio Category"` (a category's `MeasureMode`), `"Change Audio Category"` and
  `"Assign Audio Category"` (a clip's category → a different mode), and `"Scan Audio Folders"`
  (un-enrolment → `Integrated`). **Severity is systemic, not local:** the headroom pass subtracts
  `max(raw)` across the whole set, so bulk-assigning 20 one-shots and pressing Ctrl+Z moved the
  0 dB ceiling for **every clip in the library** — no marker, no warning, `Write Table` still
  enabled. Now drives `_session.Analyze` **directly** (not `RunAnalysis`, which does
  `Undo.RecordObject` + enrolment — unsafe re-entrant work inside an `undoRedoPerformed` callback),
  guarded on `Rows.Count`. My cost objection was wrong: `LoudnessCacheKey` carries the mode, so any
  clip whose mode is unchanged is a cache hit. `Analyze` ends in `Resolve`, so it subsumes the old
  behaviour, and it fixes the undone-enrolment staleness for free.
- **MEDIUM — added `OnFocus() => RebuildSettings()`.** `RebuildSettings` was exhaustive for edits
  originating in this window, but **external** mutation (the Inspector's `Clips` list, an asset
  revert/reimport) recreates the `[Serializable] ClipSettings` elements without firing
  `undoRedoPerformed`, leaving `_settings` holding detached references — a trim slider writes into
  an orphan, the slider moves, the Gain column never responds, the edit never persists. The
  `_settings` doc claim is scoped to "within this window" accordingly.
- **LOW (comments only, no behaviour):** `PreviewClipFactory.Scale`'s clamp rationale was wrong —
  `Scale` is only ever called with `FinalGainDb`, guaranteed ≤ 0 dB, so it cannot overdrive; the
  load-bearing clamp is in `Mix`, where two ≤0 dB signals sum past unity. `DrawTableBar`'s
  justification said the first row was "full after Analyze" (it ends at x=492) — the split is still
  right, the arithmetic quoted for it was not; now states the real figure (the field would land at
  x=658→838 against 708). `AudioPreviewPlayer.StopAll` no longer gates on `Resolve()`'s return
  (which reports whether the PLAY method was found), so a Unity renaming only `PlayPreviewClip`
  cannot make Stop unreachable.
- **The new test got a real RED, not a compile error:** `Expected: greater than 0.5f / But was: 0.0f`
  — `Resolve` leaves the LUFS byte-identical. Mutation-verified by reverting to `Resolve`, which
  reproduces that exact failure and no other. Uses `AudioAssetFixture` + a 0.5 s tone / 3.5 s
  silence one-shot, where Integrated and MomentaryMax diverge ~1.5 dB, and a null cache so both
  passes really measure.

#### Verification standard for this task
The only RED available for new types is a compile error, which is weak — so **all 33 new tests were
mutation-verified in 5 rounds**, each failing with its own message and, where mutations were grouped,
each group chosen so failures were uniquely attributable. Two tests were strengthened *because* a
mutation showed the plan's version could not fail (the Mix-gain one above; the unsolved-gain one).

### 🔴 AUDIT OF TASKS 11–13 (2026-07-20) — 16 findings, 2 design-level

A read-only audit was commissioned after the 5th plan defect. Both design-level findings were
lead-decided; the rest are folded into a plan-amendment pass.

1. **THE ANCHOR IS MATHEMATICALLY INERT.** `raw_i = anchorLufs + offset_i + trim_i − measured_i`,
   then `final_i = raw_i − max_j(raw_j)`. `anchorLufs` is the *same constant in every* `raw_i`, so it
   cancels **exactly**. `FinalGainDb` is provably independent of the anchor's measured loudness; its
   only live effect is `IsOutlier`. **It was not wrong when designed — it became inert when
   downward-only normalization was adopted** (the `AudioSource.volume ≤ 1.0` constraint), and the docs
   never caught up. README/guide still promise clips are "positioned relative to it", so swapping the
   anchor changes nothing in the Gain column and reads as a broken binding.
   Also: the no-anchor fallback `: 0f` is **digital full scale**, so typical −20 LUFS content gives
   `raw ≈ +20` and trips the 12 dB outlier threshold on *every row* — press Analyze before picking an
   anchor and the whole table lights up red on healthy clips.
   **LEAD DECISION: keep the anchor** as the outlier reference + sanity readout, fix every doc claim,
   fix the fallback.
2. **`AudioPreviewPlayer` cannot play.** It builds a hidden scene `AudioSource`, but
   `AudioSource.Play()` produces no audio outside Play Mode. The plan correctly rejected the built-in
   preview (no volume control) but picked the replacement that doesn't work at all — ~110 untested
   lines + both ▶/A-B buttons + a guide section, all discovered dead at Step 9, the *last* checkpoint.
   **LEAD DECISION: reflect into `UnityEditor.AudioUtil.PlayPreviewClip` + pre-scale via a
   gain-applied temp clip; A/B stays.** Resolve the method defensively (signature varies by Unity
   version) and degrade with a clear diagnostic.

Also folded into the amendment: **category change re-solves but never RE-MEASURES** (a category
carries its own `MeasureMode` — Music=Integrated vs SFX/UI=MomentaryMax — so Music→SFX silently keeps
the old-mode LUFS and bakes a wrong gain, worst on short one-shots; the existing test *encodes the bug
as expected behavior*); **"loudest clip lands at 0 dB" is BACKWARDS in 4 places** (it is the
*quietest* that pins at 0; the loudest is attenuated most) and is inherited from shipped `GainSolver`'s
own docstring so it is actively propagating; a **stale 4-arg `ClipAnalysis.Ok`** in Task 12 (deviation
#5 reached Task 13's identical helper but not Task 12's — the exact defect class the audit was
commissioned to find); `BuildVisible` documented as pure while `SettingsFor` **appends to the profile
during `OnGUI`** (un-undoable writes, O(n²) per repaint); a decorative progress bar whose Cancel
doesn't cancel; and **3 layout defects that make both headline Task-13 features invisible at default
window size**.

**Audited clean:** Task 10 in full, every 2022.3 API signature, the DSP assertions (traced by hand),
`AudioBalanceSession`, `GainTableWriter`. The known 2022.3 emoji-in-IMGUI trap did **not** apply —
those glyphs are BMP geometric shapes, not astral-plane emoji.

### ✅ WAV TEST-FIXTURE HELPER CLOSED (2026-07-20, commit `4a3df42`)
`AudioAssetFixture` (Tests/Editor) writes a minimal 16-bit PCM WAV into a uniquely-named temp
folder under `Assets/`, imports it via `AssetDatabase.ImportAsset`, and deletes the whole folder
in `[TearDown]` — asset-backed, nothing committed. Closed all **three** zero-coverage seams: the
cache hit/store path in `LoudnessAnalyzer.Analyze` (incl. a genuine-hit proof — seed the cache
with a deliberately wrong value, assert `Analyze` returns it instead of the real measurement),
`FindClips`' entire positive path (dedupe across overlapping folders, `LoadAssetAtPath`, sort),
and `KeyFor`'s AssetDatabase→absolute-path adapter. TDD: all three bug-fix tests observed RED
first (`Analyze_OnACacheHit_ForASilentClip_PreservesTheReason` — expected `"silent"`, got `null`;
`Analyze_WithACorruptCachedStatus_ReMeasuresInsteadOfCastingOutOfRange` — expected `Ok`, got the
raw out-of-range value `99`; `FindClips_TiesBrokenByAssetPathForEqualNames` — first attempt passed
"by accident" because folders were fed in ascending order and .NET's small-array insertion sort
happens to preserve ties, so it was rewritten to feed folders in descending order before it
produced a real RED). Also fixed while in those files: `Silent`'s `Reason` was dropped on a cache
hit (`CachedLoudness` has no `Reason` field) — cache hits now reconstruct
`ClipAnalysis.SilentReason`; the unchecked `(ClipStatus)cached.Status` cast now range-checks and
falls through to a re-measure on a corrupt/forward-version `Status`; `FindClips`' name-only sort
now tiebreaks on asset path. Documented (not fixed): `KeyFor`'s project-root path resolution
silently no-ops for registry/git-URL packages. 557 → 565 EditMode tests, all green.

### ⏭️ NEXT — all remaining work is the LEAD's

**The initiative is code-complete. Nothing further is agent-actionable without a human in the editor.**

**1. Hands-on editor pass (the initiative's handover checklist).** Create a profile + gain table, add
a folder with at least two clips of clearly different loudness, set an anchor, press Analyze:
- [ ] LUFS appears for every readable clip.
- [ ] **Every gain is ≤ 0 dB and exactly one clip reads 0.0 dB** — and it is the clip *quietest*
      relative to its category target. If the LOUDEST clip sits at 0, the solver's sign is inverted.
- [ ] Two clips in the same category differ by their measured LUFS difference.
- [ ] **Press Play on a row and confirm you actually HEAR it, with the gain applied.** This is the
      one thing no test covers. A quiet clip and a loud one should sound closer together than the
      raw files do. Silence here means the `AudioUtil` reflection failed → check the console for the
      `[AudioBalance]` warning. (Resolution itself is already verified on 6000.3.8f1.)
- [ ] **A/B** plays the clip over the anchor bed, and the bed keeps playing after a short clip ends.
- [ ] **Write Table** populates the asset — check it in the Inspector.
- [ ] The Table field and the Play / A-B buttons are all reachable **without resizing the window**
      (scrolling the table sideways is expected and fine).
- [ ] **Anchor behaviour, the thing most likely to be reported as a bug:** note the Gain column, swap
      the anchor for a much louder/quieter clip, re-run Analyze — **the Gain column must NOT change.**
      Only the `outlier` markers may move.
- [ ] Clear the anchor entirely, re-run Analyze — confirm **no** rows are marked `outlier` (not every
      row). 
- [ ] Undo a trim and a category change; confirm the Gain column updates without pressing Analyze.
- [ ] Console clean apart from intentional `[AudioBalance]` diagnostics.
- [ ] Orphan a category (rename via the Inspector, not the window) and confirm the row's dropdown
      shows `(unknown: X)` and can be corrected.

**2. Decide on adoption.** **No game consumes the runtime `AudioGainTable` yet.** v1 deliberately
wires up nothing — BB adoption is a separate, explicit step (add the package, assign a table, call
`PlayOneShotBalanced`). Until then this ships value to nobody, which is fine and was the plan.

**3. Push.** 21+ commits, nothing pushed. Package is `0.1.0`, untagged.

### Loose ends visible from Task 13 (NOT fixed silently — lead's call)
1. **Task 8's Streaming branch is still uncovered**, and is now known to be *closable* via
   `AudioAssetFixture` + an `AudioImporter.loadType` flip. The XML doc still frames it as an
   accepted gap; the lead chose not to reopen it. Re-word if it is ever revisited.
2. **Suite-wide test hygiene (plan deviation #22):** no test file in this package calls
   `DestroyImmediate` on the `ScriptableObject`s / `AudioClip`s it creates. Consistent, but it does
   leak Unity objects across an EditMode run. Eleven files; a suite-wide decision, not a Task-13 one.
3. **`LoudnessCacheTests` logs two real-looking `[AudioBalance]` warnings** ("Could not write the
   loudness cache…", "Could not read the loudness cache…") during every suite run. They are
   *deliberate* negative tests asserting graceful degradation, but only one of them wraps the log in
   `LogAssert.ignoreFailingMessages`. Harmless; noted because they look alarming in a fresh console
   and cost time to re-diagnose.
4. **`AudioBalanceWindow` is ~1000 lines.** Still thin per-method and everything testable is out of
   `OnGUI`, but it is the largest file in the package and the natural next split if it grows again.

> **Known limitation to document, not fix:** `KeyFor` builds a path under the project root, so the
> cache silently no-ops for clips in **non-embedded** packages (registry/git URL) — those get
> re-decoded on every window open with no warning. Fine for game audio under `Assets/`.

> **Task 8's Streaming gap is now known-closable.** It was accepted on the reasoning that covering it
> meant committing a `.wav` binary — that reasoning is false by the fixture technique above (plus an
> `AudioImporter.defaultSampleSettings.loadType` flip). Lead chose not to reopen it now. It is
> therefore a deliberate choice, **not** an unavoidable limitation — re-word the XML doc if it's ever
> revisited.

### ✅ BOTH TASK-8 FOLLOW-UPS CLOSED (2026-07-19, commit `fd4e7ff`)
- **(A) APPLIED.** `LoadAudioData()` is **async** — a `true` return means *queued*, not loaded, and
  the code was calling `GetData` immediately. Now re-checks `clip.loadState` afterwards and fails with
  `ClipSampleReader.LoadPendingError` if it is not `Loaded`. `GetData` is provably unreachable unless
  `loadState == Loaded`. Single re-check, no polling — editor-time code, so a not-ready clip is a
  reportable condition, not something to block on.
- **(B) LEAD DECIDED: accept the gap, document it.** The Streaming rejection branch has no automated
  coverage and will not get any: `AudioClip.Create` (the only way these tests build clips) always
  yields a fully-resident non-streaming clip, and `Streaming` can only be set on an imported asset via
  its `AudioImporter`. Covering it would need a committed `.wav` fixture with `.meta` pinned to
  Streaming — deliberately rejected to keep binaries out of the package. Reasoning is recorded in the
  XML doc on `ClipSampleReader.StreamingError` so no future maintainer assumes coverage exists.

### ⚠️ FIVE plan errors caught by TESTS/AUDIT, not review (all lead-approved, plan amended + committed)

> **The pattern:** this plan was written end-to-end *before any code existed*, so later sections encode
> assumptions that earlier sections invalidated once real. The anchor (#5 below) is the purest case —
> correct when written, silently made inert by a later approved decision.
1. **T3** — a tolerance that was mathematically impossible to satisfy (block-straddling artifact
   forces a −0.223 dB offset; the plan asserted 0.2).
2. **T4 — the measure mode's rationale was BACKWARDS.** The claim was "integrated gating discards a
   one-shot's decay tail so short SFX under-read." The relative gate *already* excludes the tail —
   that is its job — so the specified 3 s window averaged the attack with silence and read **6 dB
   below** the mode it was meant to beat (−25.8 vs −19.5). Now `MomentaryMax` at **400 ms**
   (−17.99 vs −19.54). Renamed throughout; Tasks 6/10/11 reference the new name.
3. **T5 — `ApproxTruePeakDb` struck entirely.** Linear interpolation yields a convex combination of
   its endpoints, so `|a+(b−a)t| ≤ max(|a|,|b|)`: it can never exceed the sample peak, hence never
   detect an inter-sample peak — the only purpose of a true-peak meter. It also read 2.5 dB *below*
   sample peak on mono `[0,0,1.0]`. `ClipAnalysis`/`CachedLoudness` now carry **one `PeakDb`** field.
4. **T9 — the cache key ignored import settings, and the amendment didn't propagate.** The key read the
   **source `.wav`**, but what's measured is the **decoded `AudioClip`** — a product of the `.meta`
   importer settings. Force To Mono leaves the `.wav` byte-identical, so the cache hit and served a stale
   **stereo** LUFS (~3 dB off) straight into the baked gain table, silently. Fix round 1 amended three
   places but **left a copy-paste-ready `ResolveIdentity` inside Task 10's body** computing it the old
   way — an agent working Task 10 top-to-bottom would have retyped the fixed bug from a plan claiming in
   three other places it was fixed. Its regression test was also **vacuous** (computed `Math.Max` in the
   test file itself, so it asserted `5100 != 5200` and passed unchanged against the broken code). The new
   "crash-safe" save had also regressed to `delete → move`, leaving a window where **neither** file exists.
   **Lesson: a design defect cannot be fixed with documentation** — a contract in an XML doc is only as
   strong as the next agent reading it, and here the same plan handed that agent contradictory code.
   Round 3 made it **structural**: `KeyFor(clip, mode)` is the only place `Ticks` is computed and
   `TryGet`/`Put` take a `LoudnessCacheKey` struct, so hand-rolling an identity is now a **compile
   error**. Measure mode became a real struct field instead of being string-mangled into the guid.
   Took 3 review rounds (budget is 2 — the 3rd was explicit lead direction after escalation).
5. **T11/T13 — the anchor is mathematically inert** (full detail in the audit section above). Found by
   *audit*, not by tests: every test passes and every number looks plausible, because the feature simply
   doesn't do what its own documentation says.

### Design constraint worth re-reading before touching `GainSolver`
`AudioSource.volume` is hard-capped at 1.0, so a clip needing +6 dB **cannot get it**. Gains are
therefore normalised **downward**: `final = raw − max(raw over analyzable clips)`, pinning the
loudest clip at exactly 0 dB. Relative spacing is preserved exactly and clipping becomes structurally
impossible. Accepted cost (lead signed off at design time): overall output is quieter, compensated
once on the master mixer. Silent/unanalyzable clips are excluded from the max so one broken asset
cannot attenuate the whole project.

### Op notes for this initiative
- Unity MCP is bound to **this** checkout (`dataPath` probe confirmed) — that is why the work is on
  a **branch in place, NOT a worktree**: a worktree would be invisible to `tests-run`, which would
  report green on code it never compiled.
- Unity **6000.3.8f1**; package targets `"unity": "2022.3"`; **zero package dependencies** (JsonUtility,
  not Newtonsoft).
- **HAZARD:** a subagent ran MCP `enroll_engine_plugin`, which auto-resolved the Unity MCP plugin to
  "latest" (0.84.3) and broke compatibility. It was reverted and verified (`manifest.json` still pins
  `com.ivanmurzak.unity.mcp: 0.76.0`). **Tell subagents not to run `enroll_engine_plugin`.**
- The lead's pre-existing `Assets/` dirt (designer `BusBuddiesImageToGrid.asset` edit + YAK
  `_prev_prompts` deletions) is **not ours — leave it alone.**

---

## ✅ SHIPPED (2026-07-15) — BB Hidden Pixels · Hidden Buses · Connected Buses

Three hand-authored Bus Buddies mechanics, TDD'd task-by-task on branch
`feat/bb-hidden-connected-mechanics`. Plan:
`docs/superpowers/plans/2026-07-15-bus-buddies-hidden-connected-mechanics.md`.

- **Full EditMode suite: 434/434 green** (413 baseline + 21 new tests, 0 regressions).
- **Package `0.9.0 → 0.10.0`**, tag `v0.10.0` (LOCAL — not pushed; lead's gate).
- **Hidden Pixels (new):** `BBPixelCell.Hidden`; generic Layer-1 flag-paint tool
  (`ICellFlagPainter`/`CellFlagPainterAsset`/`GameProfile.FlagPainter`/`GridEditTool.Hide`,
  driven by `GridCanvasPanel`+`PalettePanel`); `BusBuddiesHiddenPixelPainter` wired into
  `BusBuddiesProfile._flagPainter`; grid overlay; export/import `HiddenPixels` (`x*width+y`).
- **Hidden Buses (verify-only):** round-trip test pins `BusType:1` export + import.
- **Connected Buses (new):** `BusConnection` ops + `ConnectionsDeadlock`; `BBConnectedBusRule`
  appended to `_rules`; export/import `ConnectedBuses` coordinate pairs; connect/disconnect UI
  in `BusBuddiesQueuePanel`.
- **Open items:** (1) LEAD — re-pin BB game `#v0.10.0` + re-mirror BB Layer-2 + compile-check +
  push (agent can't compile-verify the game). (2) GAME TEAM — confirm the `HiddenPixels` `x*width+y`
  vs `PixelColors` `y*width+x` transpose is intended (square grids only) — see SESSION_NOTES flag.
  (3) VERIFIER — eyeball the 4 IMGUI surfaces (✦ Hide tool + overlay; connect UI + badges + soft-lock
  refusal) and one in-game hidden-pixel spot-check.

---

## 🔴 ACTIVE CONSTRAINT (2026-07-14) — 30 playable levels by ~2026-07-19

**The team needs at least 30 playable levels by the end of this week.** The lead has **PARKED the
image→grid automation work** — designers are converting the generated images and **fixing them by
hand**. His words: *"trying to do things automatically will have a limit."*

**Judge any new work by: does this get a designer to 30 playable levels faster?** Friction-removal for
designers is in scope; chasing full automation is not. Do not reopen the parked items unasked.

### The one open thread — BB game compile-check + push

- editor-core: **`master` `68f2e30`, tag `v0.9.0`**, pushed, 413/413 EditMode green.
- BB game (`E:/Projects/Hoppa/BusBuddies`, `main`): commit **`d654732` — COMMITTED, NOT PUSHED**
  (manifest re-pinned `#v0.9.0` + Layer-2 mirror re-synced).
- **Why held:** v0.9.0 is BREAKING — `IImageToGrid.Convert` now takes width/height and
  `ImageToGridAsset.Convert` is abstract, so a stale mirror won't compile. A bad push breaks `main`
  for the designers mid-deadline. The agent **cannot compile-verify BB** (Unity is bound to editor-core).
- **Lead step:** open BB in Unity → confirm compile → push `d654732`.
- A designer's in-progress `BusBuddiesImageToGrid.asset` edit (dark-blue→black remap) is deliberately
  left uncommitted.

### Shipped 2026-07-14 (all on `master`, pushed)

1. **BB auto-fill can no longer emit an unsolvable level** — `BusBuddiesConstructiveArranger` derives the
   bus queue from a simulated border-inward peel and verifies by exact replay. (The old dig-search had no
   real solvability gate above 64 cells; the lead's cupcake had its black outline buried 6 buses deep.)
2. **Bus queue draws HEAD at the top**, matching in-game pull order (display-only; data was never reversed).
3. **Grid-size selector** in the Image→Grid tab (the breaking change behind v0.9.0).
4. **Pixel-art prompt system** — 5 prompts × 20 ideas bound by `# @style:` tags.
   Spec: `docs/superpowers/specs/2026-07-14-yak-multi-prompt-image-generation.md`.

### PARKED — do not reopen without the lead asking

- **The dark outline.** Every generated image still has one. Three escalating rounds of "NO outline" in
  the prompt failed — the pixel-art convention beats the instruction. **The fix belongs in the converter**
  (absorb the dark border ring), not in more prompting.
- **90 of the 100 boss-brief images** are ungenerated ("Select all tagged" runs the rest, ~$6).
- **End-to-end was never verified** — we only ever judged the source PNGs; nobody converted at 40×40 and
  played the result. If automation resumes, start there.
- `1024 ÷ 40 = 25.6` is not a clean divisor — if smearing reappears at 40×40, that is why (32 is exact).

---

## ★ ACTIVE INITIATIVE — Bus Buddies (Food Hunt clone) — as of 2026-06-29

A new game authored through this editor: a re-theme of the live game **Food Hunt**, ~100%
mechanically identical (buses + passengers theme), built as a **YAK-sibling Layer-2** (configured
`BusBuddiesProfile.asset` + Layer-2 assets, NOT a subclass — `GameProfile` is sealed).
- **Backlog:** `docs/BUS-BUDDIES-BACKLOG.md` · **Spec:** `docs/superpowers/specs/2026-06-29-bus-buddies-design.md`
- **Goal:** generate + export **30 solvable, difficulty-graded levels** for a D1 40%+ validation campaign.
- **A companion game IS being built now on the YAK codebase** → exporter stays YAK-shaped (buses≡spools,
  `conveyorCount` key reused for the Active-Bus-Row slot count). The game must implement the SAME
  canonical rules we define (4-way accessibility + nearest-to-hole targeting).

**Core mechanic (canonical contract):** no gravity (removed blocks vanish, nothing moves); a block is
accessible if reachable from the frame edge through empty cells **4-way, outside-the-grid counts as open**
(edge blocks always reachable); tap pulls the top bus of a queue column into a stationary **Active Bus
Row (max 5)** which auto-empties into reachable matching blocks; **passenger targets the matching block
NEAREST the Hole** (bottom-center, squared-Euclidean, tie-break lowest index — makes the solver sound);
WIN = board cleared + all buses empty; LOSE = 5 active slots full + none can release (deadlock).

### SHIPPED to master (all pushed to origin)
- **Task 4** — image→grid options (subject-only Empty + black Outline) — master `2a26059`.
- **Spec** approved — master (`a3c3212`).
- **Sub-phase 1a — engine** — master `ea8ee76`. Pure-C# `Assets/BusBuddies/Runtime/Sim/`
  (BusLevelModel / BusSimState / BusSolver / BusAveragePlayer) + cells + queue data + 23 tests.
- **Sub-phase 1b-i — author & analyze** — master `52bfbd9`. `Hoppa.BusBuddies.Editor` asmdef + BB
  cell brushes + `BusBuddiesPalette.asset` (8 colors: red/blue/green/yellow/orange/purple/pink/cyan)
  + `BBColorBalanceRule` + `BusBuddiesAnalyzer`(+config, wraps 1a onto ILevelAnalyzer) +
  `BusBuddiesProfile.asset` (wires palette/cells/rule/analyzer; 30×30; `_spoolsBelowGrid=true`).
- **Sub-phase 1b-ii — author half (autofiller + queue panel)** — master `74aa848` (2026-07-05).
  `BusBuddiesAutofiller : LevelCompleterAsset` + config (bus-scaled cap 3–12/avg6, ColumnRange 1–5,
  APS-only gating — no complexity axis; `Partition` copied from YAK) + `BusBuddiesQueuePanel :
  TopSectionPanel` (author color/capacity/hidden, add/del/reorder/move, HEAD=Buses[0]; connected-bus
  UI deferred). Wired `_levelCompleter` + `_bottomSectionScript`. 7 TDD tests.
- **Sub-phase 1c — generation pipeline** — master `8d8c53f` (2026-07-05). `BusBuddiesImageToGrid`
  (Empty-bg + outline defaults, emits BB cells, outline=purple/tunable) + `BusBuddiesLevelGenerator`
  + config (analyzer-gated; procedural fallback all-pixel) + `BusBuddiesBatchHarness` + a mirrored
  BB difficulty-curve stack (config/tier-builder[clone-safe]/curve-harness/window) + a seeded default
  30-level 5-tier curve asset. Wired `_imageToGrid` / `_levelGenerator` / `_generatorConfig`. 16 TDD tests.
- **Full EditMode suite: 278/278 green.** Engine + analyzer + autofiller + generator all opus-reviewed.

### ⏭️ TOMORROW — the LEAD's in-editor eyeball (the whole pipeline is now runnable)
Open the Level Editor on `BusBuddiesProfile` and exercise the flows by hand:
1. **Author + auto-fill:** paint BBPixelCells → Bus Queue panel renders below the grid (bottom bus =
   HEAD) → **Auto-fill** → buses appear, `BBColorBalanceRule` green → **Analyze** → Solvable + APS + Band.
2. **Image → grid:** 🖼 Image mode → pick an image → Convert (Empty bg + purple outline) → Use This Level.
3. **Generate:** ✨ Generate → single procedural/image level, analyzer-gated Solvable.
4. **Batch / curve:** run `BusBuddiesBatchHarness` (menu) or the Difficulty Curve window → Batch Review
   window → import the good ones. Tune the default curve asset bands + outline color to taste.

### Deferred / blocked-on-game (not actionable until the BB game exists)
- **Exporter (1c Task 4) NOT built** — BB game doesn't exist yet → no target schema. Build when the
  game defines its level format (or lead provides a provisional schema).
- **APS calibration** — APS is measured but uncalibrated (no Bus Buddies player data; shares YAK's "Gap B").
- **Prove a generated level plays in the real game** (YAK "Gap C" analog).
- **3 mechanics' difficulty modeling** (Hidden cube / Hidden Bus / Connected Bus) — authorable+exportable
  later, accurate scoring deferred. Connected-bus authoring UI also deferred (ConnectedId carried as data).
- **Fast-follows (both inherited from YAK, present in both games):** `HashBlock` int.MinValue →
  negative-modulo IndexOOB (rare); `PickSourceImage` disk-branch Texture2D leak (editor-only); the
  column-remove-frame `ArgumentOutOfRangeException` in `BusBuddiesQueuePanel`/`YAKSpoolSectionPanel`.

### Op notes (Unity MCP, this session)
- Use the **direct `mcp__ai-game-developer__*` tools**, NOT `npx unity-mcp-cli` (CLI 500s on a stale token).
- Adding a new asmdef triggers a Unity **domain reload** → Editor-API calls 500 for ~tens of seconds; retry.
- **`git add` the FOLDER** in every commit so Unity `.meta` sidecars ship (1a missed them; caught at push).
- Team-workflow tracker: `.claude/teams/board.md` (Active table + Journal). Improvements: `/retro`.

---

## (previous initiative) — Automated level-generation tooling for YAK (A–E, phase-gated)

Plan: `C:\Users\itay0\.claude\plans\no-thanks-i-want-velvety-frost.md` · Spec: `docs/level-tooling-megaprompt.md`.
Five systems built as phases A–E; **stop for explicit approval at each gate.** Generic logic →
Layer 1 package (now **v0.6.0**); YAK specifics → `Assets/YAK/`.

### Phase A — YAK difficulty scorer / simulator — ✅ DONE, verified, awaiting gate (2026-06-15)

**NOT committed/pushed yet.** Full EditMode suite **145/145 green** (11 new YAK tests).

- **Layer 1 (v0.6.0, additive/non-breaking):** `AnalysisStatus` enum (Unknown/Solvable/Unsolvable/
  TimedOut/Faulted); `LevelAnalysisResult` gained `Status`, `ApsEstimate`+`ApsCalibrated`, `Band`,
  `WinPath`; `AnalysisRequest` gained `NodeBudget`, `Seed`. `package.json` 0.5.22→0.6.0 + CHANGELOG.
  **Pending: git tag v0.6.0 + game manifest pin bump when this initiative ships** (deferred until a
  Layer-1 consumer needs it; no game currently consumes these new fields).
- **YAK Runtime sim (engine-agnostic plain C#, `Assets/YAK/Runtime/Sim/`):** `YakLevelModel` (interned
  level: grid reduced to per-column bottom→top color sequences since consumption is bottom-row + gravity;
  empties omitted), `YakSimState` (ApplyMove/ResolveBelt lap-model to steady state/IsWin/IsDeadlock/
  HasLegalMove/Key), `YakSolver` (DFS+memo over the DAG; budget hit → BudgetExceeded, never Unsolvable),
  `YakAveragePlayer` (ε-careless myopic lookahead player; APS≈1/win-rate; `Calibrate` grid-search hook).
- **YAK Editor (`Assets/YAK/Editor/Analysis/`):** `YAKLevelAnalyzer : LevelAnalyzerAsset` +
  `YAKAnalyzerConfig`; assets created at `Assets/YAK/Data/Config/Analysis/` and wired into
  `YAKProfile._levelAnalyzer` (via script-execute). Conveyor slots resolved request→GameData["conveyorCount"]→config.
- **Tests:** `Assets/YAK/Tests/Editor/` (`Hoppa.YAK.Editor.Tests.asmdef` + `YakAnalyzerTests.cs`):
  solver trivial/order-sensitive(1-slot deadlock vs 2-slot solve)/budget-hit; player determinism/
  trivial-APS-1/slack-lowers-APS; analyzer solvable+uncalibrated-APS/balance-mismatch-Unsolvable/
  no-spools-Unknown/null-fault; smoke-loads the 2 TestConfigs without faulting.
- **Calibration deferred (decided):** only 2 test levels in repo (one spool-less), no real-player APS.
  ε default 0.1, `ApsCalibrated=false`. Run `YakAveragePlayer.Calibrate` when the 10 levels + APS land.

### Phase B — YAK spool auto-filler — ✅ DONE, verified, awaiting gate (2026-06-15)

**NOT committed/pushed yet.** Full EditMode suite **151/151 green** (6 new autofill tests).

- **YAK Editor (`Assets/YAK/Editor/Analysis/`):** `YAKSpoolAutofiller : LevelCompleterAsset` +
  `YAKSpoolAutofillConfig`; assets at `Assets/YAK/Data/Config/Analysis/`, wired into
  `YAKProfile._levelCompleter` (script-execute). Surfaces in the shared `AutofillPanel` (Auto-fill button).
- **Algorithm:** tally per-color wool (via IColoredCell) → `Partition(total,min,max,avg)` splits each
  color into capacities summing EXACTLY (balance by construction; total<min → one undersized spool) →
  random sweep: assign into a column count in `ColumnRange` (2–5), gate each candidate on
  `YAKLevelAnalyzer` (Status==Solvable + |APS−target|≤tol), accept first in-band, else honest
  best-effort (closest-solvable / "no solvable arrangement" / "target unreachable"). No mechanic toggles.
- **Conveyor precedence fix:** YAK analyzer + autofiller resolve belt slots from
  `GameData["conveyorCount"]` FIRST (authored truth), since the shared AutofillPanel's 24/30 dropdown is
  YarnTwist belt-capacity and would mis-drive YAK. (Known Layer-1 nicety deferred: make the panel's
  conveyor presets profile-configurable — not blocking.)
- **Tests:** `Assets/YAK/Tests/Editor/YakAutofillTests.cs` — Partition sums-exactly+in-range +
  undersized exception; single-color balanced+solvable+caps-in-range; two-color balanced; empty-grid →
  empty top success; no-analyzer fails cleanly.

### Phase C — image→grid converter — ✅ DONE, verified, awaiting gate (2026-06-15)

**NOT committed/pushed yet.** Full EditMode suite **155/155 green** (4 new image tests) + real-profile smoke.

- **Layer 1 (v0.6.0, additive):** `IImageToGrid` + `ImageToGridAsset` (`Editor/ImageToGrid/`);
  `GameProfile._imageToGrid` slot + `ImageToGrid` accessor; `ImageToGridModePanel` (mirrors
  GeneratorModePanel: source-texture field, converter-asset inspector in Advanced, Convert + preview +
  Use-This-Level via OnUseLevel); `ToolbarPanel` `OnImageToggle`/`ImageMode`/`ShowImage` + `🖼 Image`
  button; `LevelEditorWindow` `_inImageMode` (mutually exclusive with Order/Generate; reuses
  `HandleGeneratorUseLevel` load handoff). CHANGELOG 0.6.0 updated.
- **YAK (`Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs`):** pipeline = `ReadablePixels` (blit→RT,
  handles non-readable) → `Downscale` (area average to GridW×H) → `Segment` (BorderRing flood-fill from
  edge of border-dominant color; Alpha and MostSaturated modes; ambiguity → MostSaturated fallback) →
  `NearestId` redmean quantize to palette → background = most luminance-contrasting neutral (excludes
  subject's own id) → `MergeToCap` (least-used → nearest neighbour) → emit all-`YAKWoolCell`
  LevelDocument, **zero empties**, GameData conveyorCount=5. Config: `ColorCap=6`, `BackgroundNeutrals`
  (Grey/GreyLight/GreyDark/DarkGrey/White/Black), `SegmentationMode`. Asset wired into
  `YAKProfile._imageToGrid`.
- **Tests:** `Assets/YAK/Tests/Editor/YakImageToGridTests.cs` (hermetic synthetic palette) — dims+all-wool
  +no-empties; determinism; color-cap respected; subject≠background. Real-profile smoke (script-execute):
  16×16 red-on-grey → 30×30, distinct=2 [White,Red], palette=36.

### Phase D — solution export + in-game viewer — ✅ DONE (editor verified; game viewer UNVERIFIED) (2026-06-15)

**NOT committed (editor-core or game).** Full EditMode suite **157/157 green** (2 new solution tests).

- **Layer 1 (v0.6.0, additive):** `Editor/Solution/LevelSolution` (`schemaVersion`/`levelId`/`steps:int[]`)
  + `SolutionJson` (Serialize/Deserialize/Write). Flat lowercase fields so the SAME file round-trips
  through Newtonsoft (editor) AND JsonUtility (game, zero deps). `AutofillPanel."Save Solution…"` now also
  writes `<levelId>.solution.json` from `LevelAnalysisResult.WinPath` (graceful skip when WinPath null —
  YarnTwist doesn't set it). CHANGELOG 0.6.0 updated.
- **Tests:** `Assets/YAK/Tests/Editor/YakSolutionTests.cs` — export → JSON round-trip → replay steps
  through `YakSimState` → `IsWin` (acceptance: solution replays to a win in the simulator); no-WinPath
  writes nothing.
- **Game viewer (UNVERIFIED):** `E:\Projects\Hoppa\YarnKingdom\Assets\_YAK\Scripts\Gamelogic\Gameplay\YAKSolutionViewer.cs`
  — self-contained MonoBehaviour (UnityEngine/JsonUtility only, namespace `YAK.Gamelogic.Gameplay`):
  loads a `.solution.json` (TextAsset or path), tracks current step, `CurrentColumn` + Next/Prev/Reset +
  auto-advance + `onStepChanged(column)`/`onCompleted` UnityEvents + optional column-anchor gizmo + OnGUI
  controls. Does NOT tap anything itself (decoupled from the game's board). **User must open YarnKingdom in
  Unity 2022.3, let it import (generates the .meta), wire `onStepChanged` to the board highlight, and
  play-test.** Not committed in the game repo.

### Phase E — batch generator + review window — ✅ DONE, verified (2026-06-15)

**Phase E NOT committed yet.** Full EditMode suite **160/160 green** + real batch smoke (kept 2/2,
wrote json+png+stats).

- **Layer 1 (v0.6.0, Editor/Batch):** `LevelThumbnail` (grid→Texture2D/PNG, 1px+/cell from palette);
  `BatchStaging` + `LevelStats` + `BatchCandidate` (pure scan/import helpers); `BatchReviewWindow`
  (`Window▸Hoppa▸Batch Review`: folder scan, thumbnail+stats grid, multi-select, Import Selected → target
  folder). CHANGELOG 0.6.0 updated.
- **YAK (Editor/Generator):** `YAKLevelGenerator : LevelGeneratorAsset` + `YAKGeneratorConfig` — source
  image from a library folder (picked by seed) → `YAKImageToGrid`, procedural fallback when none →
  `YAKSpoolAutofiller` → `YAKLevelAnalyzer` gate (Succeeded iff solvable + APS in band). Wired into
  `YAKProfile._levelGenerator` + `_generatorConfig`. `YAKBatchHarness` ([MenuItem]
  `Window▸Hoppa▸YAK▸Run Batch (20)` + `RunHeadless` for `-batchmode -executeMethod`): loop → dedup
  (grid FNV signature) → filter (solvable + |APS−target|≤tol) → write `<id>.json` + `<id>.png` +
  `<id>.stats.json` to dated `YAK_Batch/<ts>/` (gitignored).
- **Analyzer scale fix (important):** rollout-rescue in `YAKLevelAnalyzer` — the exact solver
  budget-exceeds on 30×30, so a winning Monte-Carlo playout now upgrades `TimedOut → Solvable`
  (WinPath null at that scale; exact Save-Solution remains best for small/medium levels). This is what
  makes full-size generation/auto-fill actually accept levels.
- **Tests:** `YakBatchTests.cs` — thumbnail size/color; staging scan+import round-trip; generator
  procedural end-to-end → solvable autofilled level.
- **Image-AI deferred:** the HTTP image step is not built (no endpoint/auth yet). Source images come
  from the config's library folder (drop images in) or the procedural fallback. A future `IImageSource`
  fetching AI images into that folder drops straight into the same flow.

---

## ✅ INITIATIVE CODE-COMPLETE — all phases A–E done, committed, tagged, PUSHED (2026-06-15)

Editor-core `master`: `b89b78f` (A–C), `1921303` (D), `111cc47` (E) — **tag `v0.6.0`** at E.
All **pushed to origin**. Full EditMode suite **160/160 green**; real batch smoke kept 2/2.
**Caveat: NOTHING has been hand-verified in the editor / in-game by a human yet.**

### ⏭️ TOMORROW — TEST & VERIFY (do this first)

Open the Level Editor on `YAKProfile` and exercise the new flows by hand:
- [ ] **Analyzer:** open a YAK level (or import a TestConfig), press **Analyze** — confirm the
      Spool Analysis panel shows a sensible Status + measured APS (marked *uncalibrated*).
- [ ] **Auto-fill (B):** paint/Import a grid, press **Auto-fill** — confirm spools appear, the level
      validates (YAKColorBalanceRule green) and reads solvable.
- [ ] **Image→grid (C):** 🖼 **Image** toolbar mode → pick a source image → **Convert** → preview →
      **Use This Level**. Confirm a clean blocky 30×30, no empty cells, subject vs background.
- [ ] **Solution (D):** **Save Solution…** on a small/medium solvable level → confirm both `.txt` and
      `.solution.json` are written. (Then in **YarnKingdom**: import `YAKSolutionViewer.cs`, wire
      `onStepChanged` to the board highlight, load the json, play-test that following it WINS.)
- [ ] **Batch (E):** **Window ▸ Hoppa ▸ YAK ▸ Run Batch (20)** → then **Window ▸ Hoppa ▸ Batch Review**,
      point it at the dated `YAK_Batch/<ts>/` folder → confirm thumbnails + stats, multi-select,
      **Import Selected** lands levels in a target folder that open + export normally.

### Deferred (by design — not blocking)
- **Image-gen API** (endpoint + auth) for the batch image step — currently library-folder + procedural
  fallback. A future `IImageSource` drops into `YAKLevelGenerator.PickSourceImage`.
- **APS calibration:** run `YakAveragePlayer.Calibrate` once the 10 real levels + player APS exist
  (ε currently default, `ApsCalibrated=false`).
- **Game manifest pin:** bump the YAK game's `manifest.json` to `#v0.6.0` when it needs the new Layer-1
  contracts (no game consumes them yet).
- **Game viewer** `YAKSolutionViewer.cs` (YarnKingdom) — open in Unity 2022.3, wire + play-test (above).

---

## Latest shipped — APS auto-fill + mechanic toggles + palette solver (2026-06-09) — ✅ SHIPPED

editor-core `master c7edf40` + tag **`v0.5.22`**; game `itay-main 1d9fdd7` (manifest `#v0.5.22`).
**122 EditMode green; UI verified via screenshot.** See memories
`design_yarntw_aps_and_mechanic_toggles` + `design_yarntw_palette_countdown`.
- Auto-fill is **APS 1-6** (replaced Difficulty 1-10); `TargetAPS`, APS curves in config.
- Per-mechanic **bool table** = **Hidden Spools** + **Connected Spools** checkboxes (generic Layer-1
  `MechanicToggles` hook). Auto-fill now GENERATES connected pairs (deadlock-guarded).
- Analyzer **models Palettes** (`PaletteReq`/`_opened`; covered box tappable iff normal-access AND
  global opens ≥ amount — matches game `BoxClicked` countdown, +2 per connected pair) + replay-based
  **labelled solutions** (co-taps, palette reveals, spool unlocks, hidden reveals).
- Summary panel trimmed to **ID / Grid / Coins / Notes**.
- [ ] **PENDING — lead:** confirm the game compiles in Unity 2022.3 (agent can't compile-verify game).

---

## Active phase

**Save Solution / Win-path solver correctness — YarnTwist (2026-06-03) — ✅ VERIFIED WORKING (2026-06-04).**

> **DONE.** User play-tested level_044 (the hardest, zero-slack case) in the real game on
> 2026-06-04 — the generated solution **won perfectly.** The belt model is confirmed correct
> against the live game (see "Model verified equivalent" below). Everything is committed + pushed
> (editor-core `master`, game `itay-main`).
>
> **Logger KEPT (user decision, 2026-06-04):** the temporary `YATSolutionDebug` logger +
> `SolutionVisualizer` are intentionally left in the game project — still useful for validating
> more levels. Do NOT remove them. The removal checklist below is parked, not abandoned.

### Tunnel solver fix — output-cell modeling (2026-06-04) — CODE COMPLETE, awaiting in-game re-test

While validating more levels, level_041 (has tunnels) generated a solution whose tunnel steps
pointed at the wrong tiles. **Root cause:** the analyzer modeled a tunnel's tappable item at the
**tunnel tile**, but in-game the tunnel spawns its queued boxes into the adjacent cell in
`OutputDirection` and the player taps THEM there (`YATTunnelPrefabComponent` +
`YATGameManagerComponent:485-495`: `boxGridPosition = tunnelPos + direction`; unlock via
`IsBoxActive` at that cell). The tunnel tile is never tapped.

**Fix (analyzer-only, Layer-2; 5 edits to `YarnTwistLevelAnalyzer.cs`):**
- `BuildItems`: place the tunnel `Item` at `tunnelPos + OutputDirection` (fallback to tile if
  off-grid); map `idxAt` there.
- `Analyze` grid-open map: treat the tunnel TILE as solid (added `!(cell is YarnTunnelCell)`) so it
  never falsely unlocks neighbours.
- `IsCellCleared` (both SearchContext + RolloutContext): tunnel cell opens only when fully depleted
  (`queueIdx >= Queue.Length`), matching the game's ActivateNeighbors-on-last-box.
- `FormatSolution`: relabel "Tap Tunnel box (x,y) → …"; coordinate now resolves to the output cell.

**Verified:**
- New EditMode fixtures 26 (solution targets output cell) + 27 (accessibility follows output cell;
  dead-ended tunnel = unsolvable). Both went red→green.
- **Full EditMode suite green: 117/117** (incl. pre-existing tunnel fixtures 5 & 15).
- Ran the analyzer on the real `level_041.json` via script-execute: **Solvable, 26 steps**, tunnel
  steps now correctly at output cells — tunnel#1 `(4,3)`, tunnel#2 `(3,2)` player-coords, each
  tapped 3× in queue order.
- Synced to game repo `Assets/_YAT/Scripts/Editor/Analysis/YarnTwistLevelAnalyzer.cs` (identical 5
  edits). Layer-2 only → **no UPM tag bump.** Both repos UNCOMMITTED.

**Resume / next:**
- [ ] **User:** regenerate level_041's solution (Save Solution) in the game and re-test in-play to
      confirm the tunnel steps now highlight the correct (output-cell) boxes and the level wins.
- [ ] **User:** confirm the game project still compiles in Unity 2022.3 (agent can't compile-verify).
- [ ] Commit + push both repos once confirmed (editor-core `master`, game `itay-main`). Analyzer +
      the 2 new test fixtures only.

### Goal
Make the **Save Solution** feature (Spool Analysis panel → "Save Solution…") produce a
tap sequence a player can actually follow to WIN the level. It was producing unsolvable /
nonsensical / belt-jamming solutions. Test level = **level_044** (`E:/Projects/Hoppa/YarnTwist/
Assets/_YAT/Configs/LevelEditor configs/level_044.json`), the hardest current level (369 balls
= 369 spool capacity, zero slack — a brutal calibration case).

### What we fixed today (all committed + synced, in order)
All changes in `Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs` (Layer-2; synced to
game `Assets/_YAT/Scripts/Editor/Analysis/YarnTwistLevelAnalyzer.cs`; no UPM tag — Layer-2 only):

1. **Demand-ordered solution recording** (editor-core `7a51c7e`, refined `03eadd4`) — the DFS
   records taps in *demand-score order* (taps whose color matches current column heads go first)
   so the hint reads naturally instead of grid-scan order. (A strict pre-tap capacity gate was
   tried here and **reverted** — it made recording time out; see "dead ends".)
2. **Player-perspective Y coords** (`e5fdfab`) — solution text + `BuildItems` scan flipped so
   `(x,0)` = TOP row nearest the belt (what the player sees), not data-space bottom row.
   `Model.GridHeight` added; `FormatSolution` flips `displayY = GridHeight-1 - dataY`.
3. **Box-tap unlock mechanic** (`151bbf3`) — only the top row is tappable initially; tapping a
   box unlocks its 4 orthogonal neighbours (walls block; empty cells always open). Modelled via
   `IsAccessible(i)` in both SearchContext + RolloutContext. `Model` gained `GridWidth`,
   `GridItemIdx[]`, `GridCellOpen[]`. See memory `design_yarntw_box_unlock`.
4. **Belt drain order = RIGHTMOST column first** (`1d0421f`) — real belt circulates
   counter-clockwise; rightmost column (highest index) consumes a shared color first.
   `ResolveMatches` iterates `k = Columns-1 → 0`.
5. **Belt jams AT capacity, not above** (`f706493`) — the real lose fires when on-belt count
   == capacity AND nothing matches (full belt of unmatchable balls = permanent jam; queued
   balls can't flow through). DFS/rollout check changed `_bagSum > _capacity` → `>= _capacity`.

### How #4 and #5 were nailed: a real game-log capture
We added a TEMPORARY debug logger to the GAME project (`YATSolutionDebug.cs` under
`Assets/_YAT/Scripts/Gamelogic/Gameplay/`) that writes a full run to
`E:/Projects/Hoppa/YarnTwist/yat_solution_debug.log` (gitignored). The user played level_044;
the log gave ground truth: **capacity = 30, drain order = rightmost-first, lose at belt==30
with no match.** That confirmed #4 and pinpointed #5.

### Current status — level_044 now SOLVABLE & SAFE
Analyzer produces a 41-step solution whose between-taps belt residue peaks at **27/30 — never
jams**. All **115 EditMode tests pass**. Capacity is set via the AutofillPanel dropdown
(**24** for levels 1–15, **30** for 16+; level_044 = 30).

### Model verified equivalent to the real lose-check (2026-06-04)
Read the game's `YATGameManagerComponent.CheckLose()` + `CanAnyBallFillAnyWinder()`. The real
lose fires iff **belt full (`_splineYarnBalls.Count >= MaxRoadBalls`) AND no ball matches any
current non-full winder head.** The analyzer's `_bagSum(settled) >= capacity` is *provably the
same* condition: settled residue contains only balls matching no head, so residue>=cap ⟺
belt-full-and-nothing-matches. **The transient-peak worry is dead** — the game does NOT lose on a
momentarily-full belt if any ball still matches a winder (`CanAnyBallFillAnyWinder` short-circuits
the loss); the 9-ball spike drains. So the model is structurally correct, not just empirically
calibrated. **The only residual sim-vs-real gap is PACING:** the model assumes `ResolveMatches`
settles to fixpoint between taps (winder heads advance instantly); the game advances a head only
when 3 physical balls arrive. Tapping faster than the belt drains can hit a transient
"full + matching head hasn't advanced yet" state the model never represents → loss.

### Resume checklist (do these tomorrow)
- [x] **User play-test (2026-06-04):** level_044 solution played through in the real game and
      **WON.** Pacing guidance confirmed sound: follow the recorded order one tap at a time,
      letting the belt settle between taps (sim == reality under that condition).
- [~] **Logger removal — DEFERRED (user keeping it 2026-06-04).** When eventually removing the
      temporary `YATSolutionDebug` logger, the 5 hook sites are (grep-verified 2026-06-04):
      `YATBoxPrefabComponent.cs:268` (`LogTap` in OnClick), `YATYarnBallPrefabComponent.cs:89`
      (`LogConsume` in OnFindWinder), `YATGameManagerComponent.cs:275` (`Reset` at level start) +
      `:763` (`LogFail` in CheckLose), plus the read-only accessors added for it:
      `YATWinderPrefabComponent.cs:69` (`WinderColumn` getter) and
      `YATWinderColumnPrefabComponent.cs:19` (`ColumnIndex`). Also the `.gitignore` line + delete
      `YATSolutionDebug.cs`. (`SolutionVisualizer.cs` is the play-time path overlay — keep/remove
      independently.)
- [ ] **If a FUTURE level fails:** capture a fresh `yat_solution_debug.log` of the user FOLLOWING
      the generated solution (not free-play) and re-read it. **At the failing tap, compare the
      winder head colors against the belt contents** — if a column whose head *should* have
      advanced (per the model) is still showing its old color, that confirms the
      head-advancement-timing/pacing gap, not a model bug. Only then consider a positional belt sim.
- [ ] **Difficulty drift check (low priority):** the `>=` change slightly tightens the shared
      hot path (win-count/autofill/difficulty). No test regressed, but spot-check that
      auto-fill difficulty bands still feel right on a couple of levels.

### Dead ends (don't re-try)
- **Strict pre-tap gate** (`residue + 9 ≤ capacity`): too pessimistic — made level_044 unsolvable
  / timed out. The real belt drains *while* balls feed in, so the correct bound is post-resolve
  `residue >= capacity`, not pre-tap room-for-9. Reverted.
- **Order-robust (all-24-permutations)**: level_044 has zero slack (peaks at capacity even under
  the single best order), so requiring safety under all orders → no solution. Abandoned in favour
  of matching the real (rightmost-first) order exactly.

### Also shipped today (separate, done)
- **Palette exporter fix** (`d57beff`, game `f9a52c8`): export `PaletteAmount` + move
  `ExtraFeatureBottomType`/`PaletteAmount` after `ColorType`/`Hidden` (Eliran's field order).
- **`scan-project` skill** created (`.claude/skills/scan-project/SKILL.md`) — run `/scan-project`
  at the start of a familiar-project session for a resume briefing.
- **SolutionVisualizer** (game-only, `Assets/_YAT/Scripts/SolutionVisualizer.cs` + Editor):
  drop on a GameObject, assign a `.solution.txt`, click "Show Solution Path" in Play mode →
  white gizmo squares over the boxes to tap (yellow = current). Auto-advances on box click
  (subscribes to `YATBoxPrefabComponent.BoxClicked`). Gizmos are Scene-view only.

---

## Parked — New game mechanics — week of 2026-06-01 — 3 mechanics, one per spec.

Mechanic 1 = **Connected Boxes** (design approved, spec written, NOT yet implemented).
Mechanics 2 & 3 = TBD, to be pulled from the game project as we get to them.

> **Source-of-truth rule for every new mechanic this week:** the YarnTwist GAME
> project (`E:/Projects/Hoppa/YarnTwist`, Layer-2 under `Assets/_YAT/`, branch
> `itay-main`) defines the Layer-2 data structure. Eliran implements game-side first;
> we mirror his `level_config.json` shape EXACTLY (field names, enum ordinals, key
> casing) — do **not** invent an editor-side schema. Workflow per mechanic: dispatch a
> read-only agent into the game project → find the `BottomConfig`/enum/runtime classes
> + a sample `level_config.json` → adopt identifiers → brainstorm editor side → write a
> per-mechanic spec. See memory `feedback_eliran_game_code_is_schema_source_of_truth`.

### Mechanic 1 — Connected Boxes (2026-06-01) — CODE COMPLETE; awaiting user EditMode test run + rollout approval

Spec: `docs/superpowers/specs/2026-06-01-yarntwist-connected-boxes-design.md` (read it first).

**What it is:** two orthogonally-adjacent regular boxes are linked; tap either → both
release balls & clear together. Colors independent. Authored via right-click
Connect/Un-connect, shown with a white outline.

**Game schema (Eliran, commit `56608a5`) — there is NO connection object.** A connected
box is a normal `BottomConfig` with `BottomType = 6` (ConnectedBox) + a PascalCase string
`Direction` pointing at its partner. Pair = two boxes with reciprocal Direction
(`(2,4)"Right"` ↔ `(3,4)"Left"`). Each keeps its own `ColorType`. The game does NOT
validate reverse links → the editor must guarantee reciprocity. Full schema in the spec
+ memory `reference_connected_boxes_game_schema`.

**Implementation checklist (resume here next session):**
- [x] **Layer 1 (UPM → tag `v0.5.20`)** — extended the context-action system:
      `CellActionContext` struct (Cell/Registry/Session/CellRef) + `.meta` (guid
      `7d3f9b2a1c8e4f06b5a09d2e4c6f8a1b`) passed to `GetContextActions`; `CellContextAction`
      gained a free-form `Action<LevelEditorSession>` apply ctor; `GridCellPopup` runs apply
      under one `PushUndoSnapshot`. Updated implementers `YarnBoxCellDefinition` +
      `YarnArrowBoxCellDefinition`. **NOTE: YAK does NOT implement `ICellContextActions`
      (verified by grep) → no YarnKingdom sync needed for this signature change.**
- [x] **Data model** — `YarnBoxCell.ConnectedDir` (`YarnDirection?`, null = unconnected),
      `[JsonProperty("connectedDir", NullValueHandling=Ignore)]`. Done.
- [x] **Authoring** — `YarnBoxCellDefinition`: `Connect Pair: <Dir>` actions filtered to
      valid unconnected-box neighbors; `Un-connect` when connected; both mutate BOTH boxes
      reciprocally in one undo step (`Connect`/`Disconnect` helpers). White border +
      shared-edge seam via `DrawConnectionOutline`. Convert-to-arrowbox now gated to
      unconnected boxes only (avoids orphaning a partner). Done.
- [x] **Validation** — `YarnConnectedBoxRule` (Error on out-of-bounds / non-box / non-
      reciprocal). Created `.cs` (guid `c1a2b3d4e5f60718293a4b5c6d7e8f90`) + `.asset`
      (guid `d2b3c4e5f6071829304a5b6c7d8e9f01`, `_id: yt.connected_box`); wired into
      `YarnTwistProfile.asset` `_rules`. Done.
- [x] **Export** — `YarnMasterLevelExporter.BuildBottomConfigs`: connected box overrides
      `BottomType → 6` + adds `Direction` string (keeps own ColorType/Hidden). Added
      `yt.connectedbox → 6` to `YarnCellTypeMapping.asset`. Done.
- [x] **Analyzer** — `YarnTwistLevelAnalyzer`: added `int[] Partner` to `Model`; `Item.PartnerIndex`
      + `BuildItems` Pass 3 links reciprocal pairs (degrades to independent box if non-reciprocal);
      `Partner` in intern + StructHash; `IsTappable` Box case skips the non-canonical (higher-index)
      member; `ApplyTap` co-taps partner (both colors' balls, both tapped, one ResolveMatches) in
      BOTH SearchContext + RolloutContext; DFS undo restores the co-tapped partner; rollout
      `FeedDemand` scores the canonical member by summed demand of both colors. Done.
- [x] **Auto-fill** — confirmed **correct-by-delegation**; no structural change. Added the
      inventory comment (connected box still = 9 balls = 3 spools of its color; analyzer owns the
      clear-together effect). Connected-box autofiller test still to add (see Tests).
      Tuning watch: difficulty curves were tuned connection-free; revisit retune later, not now.
- [x] **Tests (EditMode)** — COMPLETE:
      - [x] analyzer (`YarnTwistLevelAnalyzerTests` fixtures 18–21): same-color pair = 1 path,
            distinct-color pair = 1 path, arrow-prereq satisfied by co-tap, non-reciprocal =
            2 independent paths.
      - [x] exporter (`YarnMasterLevelExporterTests` +3): connected → BottomType 6 + Direction
            string + own ColorType; reciprocal pair; unconnected box has no Direction key.
      - [x] validation + context actions (new `YarnConnectedBoxTests.cs`, +meta guid
            `e3c4d5f60718293a4b5c6d7e8f901a2b`): rule errors on dangling/non-box/out-of-bounds
            & passes reciprocal; connect sets reciprocal dirs; direction filter; neighbour-already-
            connected excluded; connected box offers Un-connect only; un-connect clears both;
            connect→undo restores (also covers ConnectedDir serialization round-trip).
      - [x] autofiller (`YarnTwistSpoolAutofillerTests` fixture 10): connected pair grid →
            balanced (6 pink spools) + solvable.
- [x] **Tests verified** — user ran EditMode tests in Unity: all pass.
- [x] **Committed + tagged locally** — commit `bfc0752` on `master` (25 files; Connected Boxes
      only, no unrelated working-tree changes); lightweight tag `v0.5.20` points at it.
- [x] **Rollout — SHIPPED (2026-06-01).** editor-core: `master` `7af843b` + tag `v0.5.20`
      (`bfc0752`) pushed to `origin`. YarnTwist game (`itay-main`, commit `e8a727f`, repo
      `hoppa-cloppa/YAT---Yarn-Twist`): manifest pinned `#v0.5.20` + Layer-2 sync (YarnBoxCell,
      Yarn{Box,ArrowBox}CellDefinition, YarnMasterLevelExporter, YarnTwistLevelAnalyzer, new
      YarnConnectedBoxRule). **User confirmed the game compiles clean in Unity 2022.3.** YAK sync
      NOT needed. The v0.5.20 `ICellContextActions` signature change was breaking — BOTH game cell
      defs (box + arrowbox) had to sync in lockstep with the pin bump (lesson for future Layer-1
      interface changes).
      - **Deferred game-side follow-ups (optional, non-blocking):** the game's embedded editor has
        connected-box authoring (ConnectedDir + Connect/Un-connect UI + outline) and export emits
        `BottomType 6` via the exporter's `Get(...)` default, BUT (a) `YarnConnectedBoxRule` is NOT
        wired into the game's `GameProfile` asset (no `.asset` created there) → no validation in the
        game's editor; (b) `yt.connectedbox→6` not added to the game's cell-type-mapping asset
        (harmless). Wire these in the game project if its embedded editor needs the full safety net.
        Primary authoring is in editor-core where everything is wired + tested.
      Watch-if-tests-fail: (a) hand-written `.meta` GUIDs for `CellActionContext`,
      `YarnConnectedBoxRule` (.cs+.asset), `YarnConnectedBoxTests` must be unique & importable;
      (b) the `CellContextAction` Func/Action ctor overload resolves by the named `create:`/
      `apply:` argument at every call site.

### Mechanic 2 — Connected Spools (2026-06-02) — CODE COMPLETE; awaiting user EditMode test run + rollout approval

Spec/plan: `C:\Users\itay0\.claude\plans\let-s-talk-about-the-stateless-meerkat.md`.

**What it is:** two spools in *adjacent* columns are linked; each stays locked until BOTH
reach their column's bottom active row, then the chain breaks and both activate. Authored in
the spool panel via right-click Add Connect / Disable Connect; shown with a number badge +
chain line.

**Game schema (Eliran) — NO connection object.** A connected spool is a `WinderConfig` with
`WinderType = ConnectedWinders` + `ConnectedColumnIndex` + `ConnectedWinderIndex` pointing at its
partner (mirrors Connected Boxes). Source: `YarnTwist/.../YATLevelManager.cs` (`WinderConfig`,
`enum YATWinderType { None, ConnectedWinders }`). Editor stores a stable `ConnectionId` on the
spool; the exporter translates to the partner's `(column, index)`.

**User decisions (all the ambitious option):** (1) commit-on-first-click — the first spool sticks
and an unfinished pair is flagged incomplete by validation; (2) draw the actual chain line across
columns; (3) analyzer models the lock now; (4) auto-fill stays analyzer-accurate (it still wipes
connections — connect AFTER auto-filling).

**Implementation checklist:**
- [x] **Data model** — `YarnSpoolData.ConnectionId` (`int?`, `[JsonProperty("connId", Ignore)]`).
- [x] **Authoring** — `YarnTopSectionPanel`: row right-click `GenericMenu` (Change Color… moved into
      the menu; Add/Complete/Disable/Cancel Connect); number badge (amber while pending) + cross-column
      chain line drawn after the scroll views; deferred color-picker (col,idx) so it reopens inside the
      scroll view. Connection ops live in new **`YarnSpoolConnection`** (UI-agnostic, tested):
      `BuildConnInfo`/`DisplayNumber`/`AllocId`/`CanComplete`/`Connect`/`DisconnectGroup`.
- [x] **Validation** — `YarnConnectedSpoolRule` (Scope=Level): incomplete (size 1) → *"Connected
      Spool Pair N is incomplete…"*; size>2; same/non-adjacent columns. `.cs` guid
      `c2a3b4d5e6f70819203a4b5c6d7e8f91`, `.asset` guid `d3b4c5e6f7081920304a5b6c7d8e9f02`
      (`_id: yt.connected_spool`); wired into `YarnTwistProfile.asset` `_rules`.
- [x] **Soft-lock prevention (2026-06-02 follow-up)** — user hit a mutual deadlock by authoring two
      *crossing* pairs (pair links can't cross between a column-pair). `YarnSpoolConnection.ConnectionsDeadlock`
      = color-blind head-advance simulation (infinite yarn; a connected spool clears only when its partner
      is simultaneously at its head). Enforced two ways: (a) the connect menu disables a completion that
      `CompletingDeadlocks` → *"would soft-lock — links can't cross"*; (b) `YarnConnectedSpoolRule` emits a
      soft-lock Error (safety net for reorder/move/hand-edit; blocks export). Tests added (+4) in
      `YarnConnectedSpoolTests`.
- [x] **Export** — `YarnMasterLevelExporter.BuildTopConfigs`: complete pair → both winders get
      `WinderType:"ConnectedWinders"` (string, Direction precedent) + reciprocal `ConnectedColumnIndex`/
      `ConnectedWinderIndex`; unconnected/incomplete spools unchanged.
- [x] **Analyzer (lock)** — `YarnTwistLevelAnalyzer`: `Column` gains `PartnerCol`/`PartnerPos`,
      `ParseTopSection` resolves complete pairs (size≠2 → independent), `Model.ColPartnerCol/Pos` +
      `Intern` + `ComputeStructHash`. **Key:** shared `ResolveMatches` gates a connected head spool with
      the pure latching check `spoolHead[partnerCol] < partnerPos` — memo-safe, alloc-free, covers DFS
      + rollouts. Caveat understood: belt capacity is checked at node entry (post-resolve), so a
      one-way lock only reduces win paths when balls stay stuck across a node; mutual locks deadlock →
      unsolvable.
- [x] **Auto-fill** — no behavior change; inventory comment noting it clears connections.
- [x] **Tests (EditMode)** — exporter (+3: reciprocal pointers / no WinderType when unconnected /
      incomplete exports unconnected); analyzer (+2: connected lock removes orderings vs unconnected
      baseline [relational, cap 12]; mutual lock → unsolvable); new `YarnConnectedSpoolTests.cs`
      (.meta `e4c5d6f70819203a4b5c6d7e8f901a2c`): validation + authoring (two-step connect, adjacency
      gate, disable clears both, connect→undo round-trip, pending detection).
- [x] **Tests verified** — user ran EditMode tests in Unity: all green (incl. soft-lock suite).
- [x] **Rollout — editor-core SHIPPED (2026-06-02).** `master` commit `ebaddc8` pushed to `origin`
      (18 files, Connected Spools only; spec at `docs/superpowers/specs/2026-06-02-yarntwist-connected-spools-design.md`).
      Layer-2 only → **no UPM tag bump** (manifest stays `#v0.5.20`).
- [x] **Rollout — game-repo sync SHIPPED (2026-06-02).** YarnTwist game (`itay-main`, commit `d711bcb`,
      repo `hoppa-cloppa/YAT---Yarn-Twist`) pushed. 9 files mirrored under `Assets/_YAT/Scripts/`:
      `Runtime/YarnTopSectionData.cs`, `Editor/TopSection/YarnTopSectionPanel.cs` + new
      `Editor/TopSection/YarnSpoolConnection.cs`, `Editor/YarnMasterLevelExporter.cs`,
      `Editor/Analysis/YarnTwistLevelAnalyzer.cs`, `Editor/Analysis/YarnTwistSpoolAutofiller.cs`, new
      `Editor/Validation/YarnConnectedSpoolRule.cs` (+metas, reusing editor-core GUIDs). Pre-sync divergence
      was only a missing comment in the autofiller (applied my hunk, didn't overwrite). Rule `.asset` NOT
      created / NOT wired into game `GameProfile` — deferred, mirroring `YarnConnectedBoxRule`. Manifest pin
      unchanged (`#v0.5.20`). **User to confirm the game compiles in Unity 2022.3** (agent can't compile-verify).

### Mechanic 3 — Palette (2026-06-02) — SHIPPED; exporter follow-up patched 2026-06-03

Spec: `docs/superpowers/specs/2026-06-02-yarntwist-palette-design.md`.

**What it is:** a 3×3 cover over boxes that hides them in-game and reveals them after the player
opens enough boxes elsewhere (a countdown). Editor authors it; gameplay is Eliran's parallel task.

**Exporter follow-up (2026-06-03):** `PaletteAmount` was missing from export + `ExtraFeatureBottomType`
was emitted before `ColorType`/`Hidden`. Fixed in editor-core `d57beff` + game-repo `f9a52c8`.
Layer-2 only — manifest stays `#v0.5.21`.

**Game schema:** only the stub exists — `YATBottomType.Palette = 7` + `BottomConfig.ExtraFeatureBottomType`.
**User decisions:** no ID (center = identity); covered always 3×3 (derived); amount editable+stored
editor-side; `PaletteAmount` exported; export marks **center box only** with
`ExtraFeatureBottomType="Palette"`; placement via **right-click context actions** (not a button);
analyzer/difficulty **deferred**.

**Layer 1 (UPM → tag `v0.5.21`):** new `CanvasOverlayAsset` (abstract SO) + `GameProfile.CanvasOverlay`;
`GridCanvasPanel` calls `CanvasOverlay?.DrawOverlay(session, cellRect)` after cells (inside scroll view).
Additive/optional → YAK/YarnKingdom unaffected. `.cs` guid `f4a1c2e3b5d60718293a4b5c6d7e8f10`.
package.json bumped 0.1.0→0.5.21 + CHANGELOG entry.

**Layer 2 (YarnTwist):**
- [x] **Data** — `YarnPalettes` (guid `a5b6c7d8e9f0a1b2c3d4e5f60718293a`): palettes in
      `GameData["palettes"]` (`[{center{x,y},amount}]`); `All/Write/CanPlace/CoveredCells/TryPaletteAt/
      Add/Remove/SetAmount/IsBox`. "Box" = `yt.box`/`yt.arrowbox`.
- [x] **Authoring** — `YarnBoxCellDefinition.GetContextActions`: Add Palette (3×3 here) when `CanPlace`;
      on a covered box, Set Palette Requirement (inline IntField via `drawOptions`) + Remove Palette.
- [x] **Overlay** — `YarnPaletteOverlay : CanvasOverlayAsset` (.cs guid `b6c7d8e9f0a1b2c3d4e5f60718293a4b`,
      .asset guid `c7d8e9f0a1b2c3d4e5f60718293a4b5c`): red 3×3 outline + tint + amount badge. Wired into
      `YarnTwistProfile._canvasOverlay`.
- [x] **Validation** — `YarnPaletteRule` (Scope=Level; .cs guid `d8e9f0a1b2c3d4e5f60718293a4b5c6d`,
      .asset guid `e9f0a1b2c3d4e5f60718293a4b5c6d7e`, `_id: yt.palette`): in-bounds / all-boxes / no-overlap.
      Wired into `_rules`.
- [x] **Export** — `YarnMasterLevelExporter.BuildBottomConfigs(grid, document)`: center cell gets
      `ExtraFeatureBottomType:"Palette"`. (Confirm center-vs-all-9 with Eliran when spawner lands.)
- [x] **Tests** — exporter (+2: center flagged / no-palette no key); new `YarnPaletteTests` (.meta
      `f0a1b2c3d4e5f60718293a4b5c6d7e8f`): helper (CanPlace valid/edge/non-box/overlap, CoveredCells,
      TryPaletteAt), authoring (add offered for valid center only, covered-box offers remove+requirement,
      add stores default 5, remove clears, add→undo GameData round-trip), validation (valid / non-box /
      overlap / off-grid).
- [x] **Tests verified** — ran EditMode via MCP (the unity-mcp 401 is resolved): 113 tests, 0 failures,
      all Palette tests green. Also fixed 2 **pre-existing stale** exporter reward tests
      (`Export_NewLevel_StubsRewardEntry`, renamed `Export_ExistingReward_IsPreserved` →
      `Export_AlwaysWritesReward_OverwritingExisting`) — they asserted old "preserve reward" behavior /
      depended on EditorPrefs; updated to the canonical always-write-reward behavior, deterministic via
      `GameData["coinReward"]`. Not Palette regressions.
- [x] **Rollout — SHIPPED (2026-06-02).** Layer-1 change → **UPM tag `v0.5.21`**. editor-core commit +
      tag + push to `origin`; game (`itay-main`) manifest pin `#v0.5.21` + Layer-2 mirror pushed.

---

### Awaiting verification — Spool auto-fill v2 + follow-ups (2026-05-31) — CODE COMPLETE, shipped as `v0.5.19`

Tagged & pushed (`v0.5.19`); still **pending the user's EditMode test run** (agent can't —
unity-mcp-cli 401). Summary kept for context:
- **Alloc-free DFS** + **Monte-Carlo WinRate** (hidden spools lower measured difficulty) in
  `YarnTwistLevelAnalyzer.cs`; **parallel sweep + win-rate-band acceptance + guided
  hill-climbing** in `YarnTwistSpoolAutofiller.cs`; raised caps + new knobs in
  `YarnTwistSpoolAutofillConfig`; AutofillPanel win-rate line.
- Follow-ups: **unsolvable red banner** in `AutofillPanel`; **exportable solution steps**
  (`RecordSolution` → `SolutionSteps`, "Save Solution…" button writing `<levelId>.solution.txt`).
- Watch points if a test fails: (1) `Span`/`stackalloc` in target Unity; (2) noise-tolerant
  `hidden ≤ visible + 0.05` assertion; (3) probabilistic fixtures (`CapacityOverride`,
  `DifficultyOne_HasHigherAveragePathCount`) under the candidate RNG.

> **Connected Boxes touches the same analyzer/autofiller files** — when implementing it,
> fold its changes in carefully so as not to disturb the v0.5.19 alloc-free hot path.

---

### Parked — Per-level Difficulty Type — YarnTwist (2026-05-28) — FEATURE DONE (game-project editor blocker since resolved, see below)

Small Layer-2 feature. The game's `LevelConfig` gained `YATLevelType LevelType`
(`None` / `Hard` / `SuperHard`). Editor now lets the designer pick a difficulty
per level via a dropdown in the Summary panel (second row, under Coins), stored
as `doc.GameData["levelType"]` and exported as `LevelConfigs[key].LevelType`
(enum-name string, matching how `Direction` is written).

Feature status: **complete, committed, pushed, deployed.**
- Spec: `docs/superpowers/specs/2026-05-28-yarntwist-level-difficulty-type-design.md`
- Code: `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs` (`ExtraSummaryRowCount`
  1→2, dropdown in `DrawExtraSummaryRows`, `LevelType` in `Export`).
- Tests: `Assets/YarnTwist/Tests/Editor/YarnMasterLevelExporterTests.cs` (+2;
  user confirmed they pass).
- editor-core commit `e9136c1` pushed to `origin/master`; game repo commit
  `5fb4707` pushed to `origin/itay-main`. Layer-2 only → no UPM tag bump.
- **Verified working in editor-core** (Unity 6): dropdown shows "None" under
  Coins in the Summary panel.

### RESOLVED — Level Editor in the YarnTwist GAME project works fine (user-confirmed 2026-06-02)

The "blocker" below is **no longer reproducing**. User confirms the Level Editor
opens and works correctly in the YarnTwist game project. Most likely fixed by the
`v0.5.20` rollout (manifest pin bump from `#v0.5.18` → `#v0.5.20` + the Layer-2 sync
that came with Connected Boxes, which the user verified compiles clean in Unity
2022.3). Do not chase this next session — kept here only as historical context.

<details><summary>Original blocker writeup (historical — left for context)</summary>

Was: opening any level NRE'd in `LevelEditorWindow.AutoActivateCellType()` at
`LevelEditorWindow.cs:534` (`def.CreateDefault()` where `def = _profile.CellTypes[1]`
was **null**), then an `EndLayoutGroup`/`Stack empty` IMGUI cascade; palette empty,
Summary showed no Coins/Difficulty rows. Game was then pinned at `#v0.5.18`. Disk
checks (cell-def GUIDs, `m_Script` bindings, no duplicate types) all came back clean;
adding two game-side asmdefs (`Hoppa.YarnTwist.Runtime`/`Editor`) did not fix it at
the time. The version bump to v0.5.20 appears to have resolved it.

</details>

---

**Spool auto-fill + win-path analysis — YarnTwist v1 (2026-05-26)**

New authoring flow on top of the editor: the designer paints the grid by
hand, then the Spool Analysis side panel auto-completes the top section
and reports exact win-path count so she has a concrete difficulty signal.

Architecture (Layer 1): generic `ILevelAnalyzer` + `ILevelCompleter`
contracts under `Packages/.../Editor/Analysis/`; `GameProfile` gains two
typed fields (`_levelAnalyzer`, `_levelCompleter`) and a generic
`_extensions: List<ScriptableObject>` slot for future per-profile data.
`AutofillPanel` IMGUI side panel shown in the right column when an
analyzer is wired.

Layer 2 (YarnTwist): `YarnTwistLevelAnalyzer` (memoized DFS over tap
orderings; Box / ArrowBox / Tunnel rules; multiset-on-belt
abstraction; configurable conveyor capacity), `YarnTwistSpoolAutofiller`
(reroll loop targeting a Difficulty-keyed win-path band),
`YarnTwistSpoolAutofillConfig` (curves + caps + timeouts owned by the
autofiller asset, NOT by the profile).

Tests: `YarnTwistLevelAnalyzerTests` (9 fixtures) +
`YarnTwistSpoolAutofillerTests` (7 fixtures), all green.

Manual verification (must run in Unity) — see "Manual verification" below.

---

**Level generator framework — YarnTwist v1 (2026-05-25)**

New initiative on top of the existing editor: a parameter-driven level
generator that produces candidate `LevelDocument`s the operator previews,
then hands off to the normal save/export flow. Architecture is Layer 1
(generic generator framework — `ILevelGenerator`, `LevelGeneratorRequest/
Result`, `LevelGeneratorRunner`, `GeneratorModePanel`, abstract
`LevelGeneratorAsset` base, `GameProfile._levelGenerator` + `_generatorConfig`
fields, ✨ Generate toolbar button) + Layer 2 YarnTwist implementation
(`YarnTwistLevelGenerator`, `YarnTwistGeneratorConfig` with AnimationCurve
knobs driven by Difficulty). v1 ships **YarnTwist only**; YAK gets its own
generator later. Reuses the profile's existing validation rules as the
sanity gate — no new playability-check interface.

This unparks YarnTwist for generator work. The prior "YAK only focus"
memory (`project_yak_only_focus.md`) is now scoped: it still applies to
non-generator YarnTwist work (existing tests, refactors), but generator
work is YarnTwist-first by user direction (2026-05-25). YAK Layer 2
onboarding (below, "Added 2026-05-18") remains in flight in parallel.

**YAK Layer 2 onboarding (2026-05-18)** — still in progress, manual
verification pending (see Open items).

YarnTwist's editor work was previously complete at tag `v0.5.14`. The
framework supports multi-profile coexistence
(`LevelEditorWindow.DrawProfileSelector`); YAK lives alongside YarnTwist
at `Assets/YAK/`. Switching games = drag a different `GameProfile` asset
into the editor window field.

### Added this session (2026-05-18)

**YAK Layer 2 — initial scaffold (`Assets/YAK/`)**

Runtime (`Hoppa.YAK.Runtime`):
- `YAKEmptyCell` (`ICellData`, `yak.empty`) — no-wool sentinel.
- `YAKWoolCell` (`IColoredCell`, `yak.wool`) — holds `ColorId`.
- `YAKSpoolEntry` (color + capacity + hidden), `YAKSpoolColumn`, `YAKTopSectionData`.

Editor (`Hoppa.YAK.Editor`):
- `YAKEmptyCellDefinition` — checker pattern in canvas, empty inspector.
- `YAKWoolCellDefinition` — palette-coloured fill, `ColorSwatchDrawer` in inspector.
- `YAKSpoolSectionPanel` — adapted from `YarnTopSectionPanel`. Variable column
  count (2–5) via `+ / −` header buttons; per-spool capacity int field next to
  the color swatch; per-spool `Hidden` toggle; drag-reorder + per-row delete +
  cross-column move + column swap (same UX as Yarn).
- `YAKColorBalanceRule` — counts wool tiles per color vs spool capacity sum per
  color (including hidden); Info row when balanced, Error when not.
- `YAKLevelExporter` — writes one YAK `LevelConfig` JSON per level. Color values
  serialised as ints via `YAKColorMapping`. `PixelColors` is a flat int array
  of length `Width*Height` in row-major bottom-up (`index = y*Width + x`), with
  `0` = empty cell. Configurable `_outputDir` field on the asset (left empty
  until the game repo is connected). Adds Columns / Spools / Wool to the
  Summary panel, plus a Conveyor int field stored in `doc.GameData["conveyorCount"]`.
- `YAKLevelImporter` — parses a YAK LevelConfig JSON back into a `LevelDocument`
  using reverse-lookup on `YAKColorMapping`. Validates `PixelColors.Length == Width*Height`.
- `YAKImportMenu` — `Tools › Hoppa Level Editor › Import YAK Level…`. Picks a
  source YAK JSON, asks where to save the editor's working file, writes the
  `LevelDocument` JSON, and auto-opens it in the Level Editor window.

Framework (Layer 1) additions:
- `NewLevelDialog` (modal) — pops on **New Level** with Width/Height fields
  pre-filled from profile defaults. Lets YAK levels start at any size (30×30,
  40×40, …) without changing the profile.
- `LevelEditorWindow.Profile` — public getter so Layer 2 tooling can read the
  active profile.
- `LevelEditorWindow.OpenLevelFile(path)` — public hook used by `YAKImportMenu`
  to auto-load the freshly imported file.
- `LevelEditorSession.CreateEmpty(profile, width, height)` — overload accepting
  per-level dimensions.

Data assets (`Assets/YAK/Data/Config/`):
- `YAKProfile.asset` — schema `yak`, default grid 30×30, wires empty+wool cell
  defs, color balance rule, exporter, and the spool section panel script. No
  order panel (one file per level, no master config to renumber).
- `YAKPalette.asset` — 13 colors copied from `YarnTwistPalette` as starting set.
- `YAKEmptyCellDef.asset`, `YAKWoolCellDef.asset`, `YAKColorBalanceRule.asset`.
- `YAKColorMapping.asset` — 13 entries (`blue→1`, `cyan→2`, …, `purplebright→13`).
  Reserves `0` for empty cells.
- `YAKLevelExporter.asset` — color mapping wired; output dir empty pending game repo.

---

## Open items / known gaps

### Manual verification — spool auto-fill (must run in Unity)

- [ ] Open Level Editor, select `YarnTwistProfile`; confirm the Spool
      Analysis panel appears in the right column (Validation 40% /
      Summary 30% / Analysis 30%).
- [ ] Open `Assets/YarnTwist/Data/Levels/YT_001.json`; press Analyze;
      confirm win-path count + elapsed time appear in the result block.
- [ ] Paint a small 3-box grid; press Auto-fill; confirm the top section
      populates with spool columns satisfying YarnColorBalanceRule.
- [ ] Sweep Difficulty 1 / 5 / 10; confirm the auto-filled hidden-spool
      count visibly tracks Difficulty (D=1: 0 hidden; D=10: ~40% hidden).
- [ ] Lock seed 🔒 + press Auto-fill twice; confirm identical top section.
- [ ] Press Ctrl-Z after Auto-fill; confirm prior top section restored.
- [ ] Switch active profile to `YAKProfile`; confirm Spool Analysis panel
      disappears (YAK has no analyzer wired — regression check).

### Generator framework — added this session (2026-05-25)

**Layer 1 (`Packages/com.hoppa.leveleditor.core/Editor/Generator/`):**
- `ILevelGenerator` — contract; one method `Generate(request, profile)`.
- `LevelGeneratorAsset` — abstract `ScriptableObject, ILevelGenerator` base
  (mirrors `LevelExporterAsset` / `EditorPanelAsset` pattern). Profiles
  serialize the field as this type so the inspector type-filters.
- `LevelGeneratorRequest` — `Difficulty` (1–10), `TargetAPS?`, `Seed`
  (0 = random), `AdvancedConfig` (ScriptableObject blob the game uses).
- `LevelGeneratorResult` — `Document`, `Succeeded`, `SeedUsed`,
  `CandidatesTried`, `RuleRejectCounts`, `ElapsedMs`. `ToString()` formats
  the diagnostics line.
- `LevelGeneratorRunner.Evaluate(doc, profile)` — runs `profile.Rules`
  against a candidate and returns the report + per-rule Error counts.
  Layer 2 generators own the reroll loop; this helper exists so every
  game produces consistent rejection diagnostics.
- `GeneratorModePanel` — IMGUI panel; params header (Difficulty slider,
  Target APS, Seed + 🎲 + 🔒) + Advanced foldout rendering
  `Editor.CreateEditor(profile.GeneratorConfig).OnInspectorGUI()` for
  game-specific tuning, + preview using `GridCanvasPanel` and the
  profile's top-section panel, + Regenerate / Use This Level / diagnostics.
  "Use This Level" hands off to `LevelEditorWindow` via `OnUseLevel` event;
  the document loads as unsaved-new in normal edit view.

**Layer 1 modifications:**
- `GameProfile` — two new optional fields: `_levelGenerator`
  (`LevelGeneratorAsset`) + `_generatorConfig` (`ScriptableObject`).
- `ToolbarPanel` — `OnGenerateToggle` event, `GenerateMode` + `ShowGenerate`
  flags, ✨ Generate toggle button (visible only when profile has a generator).
- `LevelEditorWindow` — `_inGeneratorMode` state, `_generator` panel,
  `HandleGenerateToggle` (exits Order mode if active), `HandleGeneratorUseLevel`
  (calls existing document-load path with the candidate doc).

**Layer 2 — YarnTwist (`Assets/YarnTwist/Editor/Generator/`):**
- `YarnTwistGeneratorConfig` — `ScriptableObject` with 9 `AnimationCurve`
  knobs (GridWidth, GridHeight, WallDensity, BoxRatio, ArrowBoxRatio,
  TunnelCount, ColorCount, HiddenSpoolRatio, CoinReward), `MaxRerollAttempts`
  (50), `MaxTunnelQueueLength` (3), and 3 overrides (GridWidthOverride,
  GridHeightOverride, ColorCountOverride; 0 = use curve). `OnEnable`
  defensively populates default linear curves if the asset YAML loads
  them empty.
- `YarnTwistLevelGenerator : LevelGeneratorAsset` — Option A from the
  design: layout-first (walls → tunnels → boxes/arrowboxes), then
  derive spool distribution from per-color grid totals (each colored grid
  cell = 9 balls = exactly 3 spools, so balance is exact by construction).
  Arrow-direction repair pass demotes unfixable arrows to plain boxes.
  Wraps each candidate in a `LevelGeneratorRunner.Evaluate` call and rerolls
  with a derived sub-seed on `Error`-severity rule failures, capped at
  `MaxRerollAttempts`.

**Assets written (with .meta GUIDs so teammates pick them up):**
- `Assets/YarnTwist/Data/Config/YarnTwistGeneratorConfig.asset`
- `Assets/YarnTwist/Data/Config/YarnTwistLevelGenerator.asset`
- `Assets/YarnTwist/Data/Config/YarnTwistProfile.asset` updated with
  `_levelGenerator` + `_generatorConfig` references.

**Tests (`Assets/YarnTwist/Tests/Editor/YarnTwistLevelGeneratorTests.cs`):**
- Determinism: same seed + config → identical structural signature
  (timestamps/levelId stripped).
- Difficulty sweep (1, 3, 5, 8, 10) × parameterized seeds — each must
  produce a `Succeeded == true` document that re-passes
  `LevelGeneratorRunner.Evaluate`.
- `GridWidthOverride` propagates to output `Grid.Width`.
- Diagnostics fields populated (`SeedUsed`, `CandidatesTried`, `ElapsedMs`,
  `RuleRejectCounts`).
- `TargetAPS` recorded on `Metadata.Aps`.

### Manual verification still pending (must run in Unity)

**Generator framework:**
- [ ] Recompile scripts; confirm no compile errors in
      `Hoppa.LevelEditor.Core.Editor` / `Hoppa.YarnTwist.Editor` /
      `Hoppa.YarnTwist.Editor.Tests`.
- [ ] Run `YarnTwistLevelGeneratorTests` from the Test Runner (Edit Mode);
      confirm all pass.
- [ ] Open Level Editor window, select `YarnTwistProfile`; confirm
      ✨ Generate button appears in toolbar.
- [ ] Click Generate; confirm generator mode renders params panel +
      preview canvas + top-section preview.
- [ ] Sweep Difficulty 1 / 5 / 10; click Regenerate a few times per
      Difficulty; confirm grids vary and the diagnostics line populates
      (e.g. `Generated in 14 ms · 3 candidate(s) · seed 1745032`).
- [ ] Lock a seed (🔒) and regenerate twice; confirm preview is identical.
- [ ] Expand Advanced; set `GridWidthOverride = 8`; regenerate; confirm
      the preview grid is 8 wide.
- [ ] Click Use This Level → confirm exit to normal edit view with
      unsaved-modified indicator, the generated grid loaded in the canvas,
      spool columns rendering in the top section.
- [ ] Save As `Assets/YarnTwist/Data/Levels/YT_GEN_001.json`, then Export.
      Confirm `YarnMasterLevelExporter` produces a new entry in the master
      `level_config.json`.
- [ ] Open ⇅ Order panel; confirm the new level appears in the list
      with a key.
- [ ] **Regression** — switch profile back to `YAKProfile`; confirm
      ✨ Generate button is hidden (YAK has no generator wired yet).

**YAK Layer 2 (carried over from 2026-05-18):**
- [ ] Recompile scripts; confirm no compile errors in `Hoppa.YAK.Runtime` /
      `Hoppa.YAK.Editor` / `Hoppa.LevelEditor.Core.Editor`.
- [ ] Switch active profile to `YAKProfile` in `LevelEditorWindow`; confirm
      grid renders, palette shows Empty + Wool, spool section panel renders
      with two empty columns.
- [ ] Click **New Level** → confirm the size dialog pops and 30×30 default is
      pre-filled; create a level and paint a few wool cells.
- [ ] Add spool columns / spools / capacities; confirm the Color Balance rule
      shows one row per color and turns Error on mismatch.
- [ ] Save level (LevelDocument JSON). Open it again via Open → round-trip works.
- [ ] Set `_outputDir` on `YAKLevelExporter.asset` to a real path; click Export;
      confirm `level_NNN.json` is written with int color values + correct
      `Width`/`Height`/`ConveyorCount`/`SpoolColumnConfigs`/`PixelColors`.
- [ ] **Regression** — switch profile back to `YarnTwistProfile`; confirm Yarn
      flows (open existing `YT_*.json`, edit, save, export) still work.
- [ ] **Importer** — once Eliran shares his `level_001.json`, run
      `Tools › Hoppa Level Editor › Import YAK Level…` and round-trip it.

### Decisions still pending input
- [ ] Game repo output path for `YAKLevelExporter._outputDir`
- [ ] Confirm `YAKColorType` int values once the game's enum is settled (current
      mapping copied from YarnTwist's order: blue=1 … purplebright=13).
- [ ] Whether YAK needs an Order panel later (currently filesystem-driven order;
      can add later if a master config is introduced).

---

## Done (completed phases)

| Phase | Status |
|-------|--------|
| Planning | ✅ Complete |
| Phase 0 — Package skeleton | ✅ Complete |
| Phase 1 — Core data + serialization | ✅ Complete |
| Phase 2 — Validation engine | ✅ Complete |
| Phase 3 — EditorWindow + grid canvas | ✅ Complete |
| Phase 4 — Top-section abstraction + export | ✅ Complete |
| Phase 4.5 — UI/UX polish | ✅ Complete |
| Phase 5 — Data pipeline integration | ✅ Complete |
| YarnTwist Jira UI gaps | ✅ Complete |
| YarnTwist brush template + color swatches | ✅ Complete |
| YarnTwist export key fix + Level Order Manager | ✅ Complete |
| YarnTwist save/open directory memory | ✅ Complete |
| YarnTwist spool columns drag-reorder | ✅ Complete |
| YarnTwist multi-select + profile persistence | ✅ Complete |
| YarnTwist summary panel enhancements + coin reward (v0.5.14) | ✅ Complete |
| Phase 6 — Second-game onboarding | 🔵 **In progress (YAK)** |

---

## Key design notes (carry forward)

- `GridData` is bottomUp: y=0 = bottom row in data, drawn at bottom of canvas.
  YAK's `PixelColors` follows the same convention so export needs no inversion.
- YAK reserves `0` in its color mapping for the empty/no-wool sentinel; wool
  colors start at `1`.
- First cell type in `GameProfile.CellTypes` must be the "empty" type (erase + fill).
- `PushUndoSnapshot()` must be called BEFORE mutations (not after).
- `CloneBrushTemplate()` is the correct way to get a fresh painted cell — never
  store `BrushTemplate` reference directly in the grid.
- `DrawInspector` must use absolute `GUI.*` / `EditorGUI.*` calls — never
  `GUILayout.BeginArea` (causes render leaks outside panel bounds).
- `LevelEditorSession.MarkDirty()` is the safe way to flag unsaved state from panels.
- Game switching = drag a different `GameProfile` asset into the
  `LevelEditorWindow`'s profile selector. The selector remembers the choice
  per project via `EditorPrefs` (`Hoppa.LevelEditor.ProfileGuid`).
- Every new `.cs` file in the package or under `Assets/` **must** have a
  committed `.meta` file with a stable GUID, or Unity silently excludes it from
  compilation in package consumers.
- Layer 1 UPM changes deploy via git tag + consumer `manifest.json` bump
  (currently `v0.5.16`). Tag history:
  - `v0.5.15` — `NewLevelDialog`, public `Profile`/`OpenLevelFile`,
    `CreateEmpty(profile, w, h)` overload, framework bottom-section support,
    YAK single-source color refactor.
  - `v0.5.16` — `GridCanvasPanel` mouse hit-test now follows the scroll
    offset; previously, once the canvas scrolled (e.g. when the YAK bottom
    spool panel shrunk the canvas below the grid's required height), the
    bottom `_scroll.y` pixels of the visible viewport silently rejected
    clicks. Layer 2: `YAKProfile.asset` rewired from the legacy
    `YAKPalette.asset` to `YAKStaticManagerColorSource.asset` (single
    source of truth per `design_yak_colors_single_source.md`) — fixes the
    empty color picker popup when right-clicking spool swatches.
