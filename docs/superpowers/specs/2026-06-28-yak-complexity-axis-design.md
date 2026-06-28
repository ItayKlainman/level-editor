# YAK Complexity Axis — Design

**Date:** 2026-06-28
**Source backlog:** `docs/YAK-DIFFICULTY-PARAMETER-BACKLOG.md` §1 (boss's spec, rules R25–R31)
**Scope decision (locked):** Complexity axis **only**. Spool-variety (§2) and per-color min/divisibility (§3) are explicitly deferred to a later slice. Spool *sizes* are unchanged by this work.

---

## 1. Problem

YAK's difficulty system controls spool **size** (≈ taps to clear) and **measures** APS by simulating an average player, gating generated levels on the result. It has **no control over click-pattern complexity** — how varied / non-round-robin the solve sequence is.

A "click" in YAK = tap a column → its top spool drops onto the conveyor and consumes matching bottom-row wool (see `YakSimState.ApplyMove`). The **click pattern** the boss cares about is the *sequence of which columns the player taps*. Today `YAKSpoolAutofiller.BuildCandidate` partitions each color into spools, shuffles the flat list, and deals it `columns[i % n]` (round-robin). The realized optimal solve is therefore **uncontrolled and frequently trivially round-robin** — the exact thing R25 forbids.

The boss treats `Difficulty-Complexity` as a 1–10 dial **orthogonal** to spool size, with concrete rules:

- **R25 (anti-round-robin, CRITICAL):** the optimal click sequence must never be pure round-robin (1,2,3,1,2,3…), *even at Complexity 1*.
- **R27 (max consecutive same-column run):** `maxRepeat = max(2, min(5, 1 + complexity / 2))` → C1–2 ⇒ 2, C3–5 ⇒ 3, C6–8 ⇒ 4, C9–10 ⇒ 5.
- **R26 (pattern-first construction):** build the click pattern first, then assign spools by pattern position — inverts the current partition → shuffle → `i % cols` flow.
- **R28–R31 (complexity-scaled selection):** each column gets ≈ `total ÷ columns` (±1) taps; low C = mostly-different columns with short 2-runs; mid C = weighted-random by remaining quota; high C = extended runs + unpredictable jumps.

## 2. Approach (decided)

**Outcome + verify (hybrid).** Construct the layout biased toward the boss's pattern rules (the "build" half), then **simulate play and gate on the realized complexity** the same way we gate on APS (the "verify" half). The construction is not trusted blind — because the *player* chooses tap order, not us, a constructed-complex layout can still have a trivial optimal solve. The gate keeps us honest: a level only ships at a target Complexity if it actually plays that way.

**Complexity is measured on the average-player playouts** — the same Monte-Carlo simulated player that already produces APS. We score the click-pattern variety of its *winning* runs and average. This is consistent with APS (same player model, same pass), cheap (no extra search), and reflects realistic play. Tie-break noise is smoothed by averaging over many winning runs.

Generation **inputs** (Complexity, like Amount) compose with the measured **output** (APS): both are independent targets a candidate must satisfy.

## 3. Components

### 3.1 The dial — `Complexity` (1–10)
- Add `int Complexity = 3` to `TierPreset` (`YAKDifficultyCurveConfig.cs`), with a tooltip and `[Range(1,10)]`.
- Add `int? TargetComplexity` to `CompletionRequest` (parallel to `float? TargetAPS`).
- Add to `YAKSpoolAutofillConfig`: `DefaultComplexity` (1–10, default 3) and `ComplexityTolerance` (default ≈ 1.0, in 1–10 units).
- Thread Complexity through the curve window (one int field per tier), `YAKTierProfileBuilder` (reflection set on the cloned config / request), and `YAKCurveBatchHarness`. Existing wiring for `TargetAps` is the template to follow.

### 3.2 Pattern builder (new pure helper)
A new engine-agnostic, independently testable static class `YakClickPattern` in `Assets/YAK/Runtime/Sim/` that, given `(numColumns, numSpools, complexity, rng)`, returns an `int[]` tap pattern of length `numSpools`:

- Per-column quota = `numSpools ÷ numColumns`, distributing the remainder (±1) — R28.
- `maxRepeat = max(2, min(5, 1 + complexity / 2))` — R27.
- **Selection policy by C** — R28–R31:
  - **Low (1–2):** prefer a column different from the previous tap; allow runs up to maxRepeat (=2) only occasionally.
  - **Mid (3–5):** weighted-random by remaining per-column quota; runs up to maxRepeat (=3).
  - **High (6–10):** bias toward extended runs (up to maxRepeat 4–5) and unpredictable jumps between distant columns.
- **R25 guard:** the produced sequence must not be the pure round-robin cycle `0,1,…,K-1,0,1,…` — if construction ever lands exactly on it, perturb (swap two non-adjacent positions) before returning. Holds even at C=1.
- **Invariants (enforced + tested):** exact length `numSpools`; each column appears exactly its quota times; no run longer than `maxRepeat`.

### 3.3 Spool→column assignment (rewrite `BuildCandidate`)
- Keep `Partition` and the per-color spool list exactly as today (sizes unchanged).
- Replace `Shuffle` + `i % columns` with: shuffle the flat spool list (for color mixing), then **walk the tap pattern** — the k-th entry says "append the next spool to that column." Column `c`'s spools, in send order, are the spools landing on `c`'s pattern positions. This makes the constructed varied sequence a real, consistent solve line.
- Hidden-spool marking (`HiddenRatio`) is unchanged and applied after assignment.

### 3.4 Complexity measurement (in the simulator + analyzer)
- **`YakAveragePlayer`:** extend `Config`/`Result` so a playout can record its tap sequence on a **win**, score it via a new pure `ClickPatternComplexity(IReadOnlyList<int> taps, int numColumns)`, and the estimator returns the **mean complexity over winning runs** (`ComplexityEstimate`, plus the number of winning samples). Lost runs contribute nothing. Computed only when requested, so APS-only callers pay nothing extra.
- **`ClickPatternComplexity` metric** (pure, unit-tested) → effective 1–10, blended from two normalized sub-signals:
  1. **Run signal** — longest (or mean) consecutive same-column run, mapped through the inverse of R27's `maxRepeat` (run ≤2 → low, 5+ → high).
  2. **Round-robin-deviation signal** — fraction of steps where the tap is *not* the pure-round-robin successor `(prev+1) mod K`; pure round-robin → 0 → minimal complexity.
  Blend + normalize to 1–10. Exact coefficients are tuned in implementation against property tests (§5); the *shape* (these two signals, blended, 1–10) is fixed here.
- **`LevelAnalysisResult`:** add `float ComplexityEstimate;` (0 = not computed) and surface it in `ToString()` alongside APS.
- **`AnalysisRequest`:** add a `bool MeasureComplexity` flag (default false) so only the autofiller's gating calls pay for it.
- **`YAKLevelAnalyzer.Analyze`:** when `req.MeasureComplexity` and a win is possible, pass the flag into the player config and copy `ComplexityEstimate` onto the result.

### 3.5 The gate (autofiller acceptance)
`YAKSpoolAutofiller.Complete` gains a complexity target alongside the APS target:
- Resolve `targetComplexity` from `req.TargetComplexity ?? cfg.DefaultComplexity`.
- Build candidate using the pattern for `targetComplexity`; analyze with `MeasureComplexity = true`.
- **Accept** a candidate iff `Solvable` AND `|APS − targetAps| ≤ ApsTolerance` AND `|complexity − targetComplexity| ≤ ComplexityTolerance`.
- Otherwise track the best-effort closest by a **combined normalized distance** (APS distance + complexity distance, each scaled to its tolerance) and return it with an honest "couldn't hit target complexity/APS" message — mirroring today's best-effort path. No silent acceptance of off-target levels.

### 3.6 Reporting
- `BatchCandidate` / stats / `BatchReviewWindow`: show measured complexity next to APS/Band (and the off-target flag already shown for APS extends to complexity). The curve harness writes it into the per-level `.stats`.

## 4. Data flow

```
TierPreset.Complexity ─┐
                       ├─► CompletionRequest.TargetComplexity ─► YAKSpoolAutofiller.Complete
DefaultComplexity ─────┘                                              │
                                                                      ├─ build tap pattern (R25–R31)
                                                                      ├─ assign spools by pattern → candidate
                                                                      ├─ analyze (MeasureComplexity=true)
                                                                      │     └─ YakAveragePlayer: score winning playouts
                                                                      │           └─ ClickPatternComplexity → mean
                                                                      └─ gate: solvable & APS-in-band & complexity-in-band
                                                                            └─ accept / best-effort + honest reason
```

## 5. Testing

- **`ClickPatternComplexity` (pure):** pure round-robin → ~1 (minimum); a maximally-varied/long-run sequence → ~10; monotonic-ish on a constructed ramp; symmetric to column count.
- **Pattern builder (pure):** exact length & per-column quota; no run exceeds `maxRepeat(C)`; never the pure round-robin cycle even at C=1; `maxRepeat` matches R27 across C=1–10.
- **Autofiller integration:** on a fixed grid, sweeping `targetComplexity` 1→10 yields candidates whose **measured** complexity trends upward (allowing tolerance/noise); accepted candidates stay solvable and APS-in-band.
- **Regression:** all existing **185** EditMode tests stay green; APS-only callers (no `MeasureComplexity`) produce byte-identical behavior to today.

## 6. Out of scope (this slice)

- §2 spool-variety (mult-of-5, 80 ceiling, two-spool, anti-uniformity) and §3 per-color min/divisibility — next slice.
- Calibrating Complexity to real player data (parallels the still-open APS calibration / Gap B). `ComplexityEstimate` is reported as a measured-but-uncalibrated signal, same honesty stance as APS.
- Wiring the unused core `LevelGeneratorRequest.Difficulty` field (§4 of the backlog) — separate refinement.

## 7. Risks / notes

- **Construction ≠ optimal play.** A constructed-complex layout may still be beatable round-robin; the gate is what guarantees the shipped complexity. If high-C targets prove hard to hit on small grids (few spools ⇒ little room for variety), the best-effort path surfaces it honestly rather than faking the number.
- **Tie-break noise** in the average player can inflate apparent variety on trivial levels; averaging over winning runs + the gate's tolerance absorb it. If noise dominates at low C, increasing winning-sample count is the lever.
- **Few-spool levels** can't express high complexity (a 3-spool level has no room for C=9). Expected; the gate reports best-effort.
