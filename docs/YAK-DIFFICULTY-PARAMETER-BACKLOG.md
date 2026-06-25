# YAK Difficulty Parameters — Backlog from Boss's Claude Spec

**Captured:** 2026-06-25
**Source:** the boss's earlier Claude chat, "Yarn-Kingdom Level Building Rules" (his own attempt to automate this workflow).
**Purpose:** the boss asked us to mine that spec for difficulty parameters to add to the
difficulty-curve system we shipped (`master 83a90c9`, Window ▸ Hoppa ▸ YAK ▸ Difficulty Curve).
This doc lists **only the difficulty-affecting parameters/rules we do NOT already have** — the
next-session TODO set.

> Already in our system (deliberately excluded here): spool capacity as a difficulty lever
> (≈ his *Difficulty-Amount*), conveyor=5, columns 2–5, grid size, hidden-spool %, solvability
> verification by simulation (our APS gate + up-to-N seed retry), color palette / detection /
> output schema / PixelColors bottom-up ordering. Those are covered by existing
> `TierPreset` knobs, `YAKLevelAnalyzer`, `YAKImageToGrid`, and the YAK exporter.

---

## 1. Difficulty-COMPLEXITY — a second, independent difficulty axis (HEADLINE — entirely missing)

Our system models spool-**size** difficulty and **measures** APS, but has **no control over
click-pattern complexity** — how varied / non-obvious the optimal solve sequence is. The boss
treats this as a separate 1–10 dial (`Difficulty-Complexity`) alongside `Difficulty-Amount`.

Current reality in our code: `YAKSpoolAutofiller.BuildCandidate` shuffles the flat spool list
then deals it into columns round-robin (`columns[i % n]`), so the resulting click pattern is
**uncontrolled and frequently trivial** — the opposite of what his spec demands.

What to adopt (his rules R25–R31):
- **Anti-round-robin principle (R25, CRITICAL):** the optimal click sequence must NOT be pure
  round-robin (1,2,3,1,2,3…) — *even at Complexity = 1*.
- **Max consecutive clicks on the same column (R27):** `max_repeat = max(2, min(5, 1 + complexity // 2))`
  → C1–2 ⇒ 2, C3–5 ⇒ 3, C6–8 ⇒ 4, C9–10 ⇒ 5.
- **Pattern-first construction (R26):** build the click pattern FIRST (with the right variation),
  THEN assign spools to columns based on pattern position — inverts our current
  partition → shuffle → `i % columns` flow.
- **Complexity-scaled column selection (R28–R31):** low (1–2) = mostly-different columns with
  short 2-runs; mid (3–5) = weighted-random by remaining per-column quota; high (6–10) = extended
  runs + unpredictable jumps. Each column gets ≈ `total_spools / num_columns` (±1) clicks (R28).

**TODO:** add `Complexity` (1–10) to `TierPreset`; implement pattern-first column assignment in
the autofiller; gate generated levels so their optimal solve matches the target complexity.
*This is the single biggest missing lever — it adds a whole difficulty dimension orthogonal to size.*
Note: his Amount/Complexity are generation **inputs**; our APS is a measured **output** — they
compose (add Complexity as an input, keep APS as the verifier).

---

## 2. Spool-shaping / variety rules (we don't enforce)

Our `Partition` just makes capacities in `[min,max]` summing exactly per color, then shuffles.
His spec adds shape/fairness constraints that affect difficulty feel:

- **Multiples of 5 + range 20–80, hard ceiling 80 (R11/R12).** We use raw ints with defaults
  min=10/max=30/avg=20 — reconcile bands and add the mult-of-5 + ceiling constraint.
- **Preferred sizes = multiples of 20, mixing in 25/30/35/… for variety; never too-uniform (R13).**
- **Two-spool rule (R14/R15):** any color with ≥ 11 px must split into ≥ 2 spools; for 20–39 px
  the min spool size drops to 10 (so a 20-px color can be 10+10).
- **Anti-uniformity across colors (R17):** avoid ~2× size-ratio uniformity (e.g. all Red=80 and
  all Black=40 reads as too uniform).
- **Intra-color variety (R18):** prefer 2+ distinct spool sizes per color when there are 3+ spools.

**TODO:** extend the autofiller's partition/assignment to honor mult-of-5, the 80 ceiling, the
two-spool rule, and the variety/anti-uniformity heuristics.

---

## 3. Color-quantity rules affecting difficulty / solvability (we don't enforce)

We cap the number of colors (`ColorCap`) but don't guarantee per-color quantities:

- **Min ≥ 20 px per color (R5);** merge colors < 10 px to nearest neighbor (R6); expand 10–19 px
  colors to ≥ 20 by converting adjacent pixels (R7).
- **Every color's pixel count divisible by 5 (R8);** total stays = W×H (R9); adjustments net to
  zero (R10). Needed for clean multiple-of-5 spool partitioning (ties to §2).

**TODO:** add a min-pixels-per-color + divisible-by-5 pass (partly `YAKImageToGrid`, partly the
autofiller). Prevents tiny annoying color fragments and enables clean spool sizing.

---

## 4. Difficulty-Amount → size formula (optional refinement)

He derives target average spool size from a single 1–10 dial:
`target_size = 75 - (Amount - 1) * (75 - 20) / 9` (≈ 75 at Amount 1 → 20 at Amount 10), with
fewer/larger spools at low Amount and more/smaller at high (R23/R24).

We set `AvgCapacity` directly per tier — equivalent, but adopting the formula would let one
`Amount` dial drive capacity. Note there is already an **unused** `LevelGeneratorRequest.Difficulty`
(1–10) field in the core that YAK ignores — a natural place to wire `Amount` (and `Complexity`).

---

## Explicitly considered and NOT added (already covered or not a difficulty parameter)
- Detection rules R1–R4 (inner-50% sampling, mode-voting, dark-bg→Black) → `YAKImageToGrid` concern.
- Output schema R34–R39 (ColorType ints, IsHidden default, top-spool-first, bottom-up PixelColors)
  → our exporter/runtime already match (proven in Gap C).
- Simulation/solvability R32–R33 → our `YAKLevelAnalyzer` + APS gate + seed retry already do this.
- Conveyor=5, columns 2–5, hidden spools, grid size → existing `TierPreset` knobs.

## Suggested next-session order
1. **Complexity axis (§1)** — highest value, new dimension. Likely its own brainstorm → spec → plan.
2. **Spool variety + mult-of-5/two-spool (§2)** — pairs naturally with the pattern-first rewrite.
3. **Color min/divisibility (§3)** — supports §2's clean sizing.
4. **Amount→size formula (§4)** — small, do alongside §1 when wiring the `Difficulty` field.
