# Bus Buddies — Backlog & Plan

> **What this is:** Bus Buddies is the next game — a re-theme of the live game **Food Hunt**,
> intended to be ~100% mechanically identical, different theme (buses + passengers instead of
> food/ants). It is a **sibling of YAK** in this editor framework: same Layer-1 editor, a new
> Layer-2 profile with a different core simulator.
>
> Source of truth for mechanics = the **Bus Buddies GDD** (pasted by lead 2026-06-29, summarized
> below). The store/YouTube exploration task was dropped — the GDD supersedes it.

---

## 1. Core mechanic (vs YAK)

| Piece | YAK | **Bus Buddies** |
|---|---|---|
| Gravity | blocks fall after a hit | **none — blocks never move once placed.** "Grid collapses" in the GDD = the image is progressively eaten away, NOT physics gravity (confirmed by lead). |
| Active region | bottom row only | **all 4 frame edges** |
| Accessibility | bottom row + gravity-exposed | a block is accessible if a passenger reaches it **without passing through other blocks** = the block is connected to the grid border through **empty cells** (flood-fill from the edge). As blocks clear, the flood expands inward — peel from all sides. Correctly handles interior pockets / donut shapes (a sealed interior empty pocket isn't reachable until its surrounding ring peels). |
| Spools | wool spools | **Buses** — colored, fixed passenger count shown on the bus |
| Queue | spool columns | **1–5 vertical bus columns; only the top bus in each column is tappable.** Tapping pulls it into the Active Bus Row; the column shifts up. |
| Active slots | conveyor belt (moving) | **Active Bus Row — stationary, max 5 buses.** Active buses auto-release one passenger per accessible matching block. |
| Delivery | — | passenger walks to block → removes it → carries it to the **central Hole** and jumps in. ("Ants"/passengers are pure visual; the model is just "tap color → clear accessible matching blocks.") |
| Overflow / holding | belt capacity + jam | **none.** No belt. |
| **Win** | — | all blocks collected **and** all buses emptied |
| **Lose** | belt jam | **deadlock** — all 5 active slots full **and** none of them can release (no accessible matching block of any active color) |

**Grid sizes:** 20×20, 30×30, 40×40 (square). Each cell = one colored Pixel Block.

**Phase-1 scope (from GDD):** core loop + basic progression + **≥3 features** (Hidden cubes / Hidden
Buses / Connected Buses) + **30 playable levels**. Polished prototype for a **D1 40%+** validation
campaign. NOT in scope: boosters, lives, monetization, meta, live-ops, advanced progression.

The 3 features map ~1:1 onto the editor's existing **Hidden-spool / Connected-spool** concepts
(built for YarnTwist) — re-themed, mostly reusable.

---

## 2. Tasks

### Task 4 — Image→grid options (START FIRST · independent · now load-bearing)
Two options added to the shared image-to-grid layer (benefit YAK today; **required** for Bus Buddies):
- **4a — Subject-only (no background cells):** the converter already segments subject vs. background
  (`isBg[]` in `YAKImageToGrid`). Add a mode that emits **empty cells** for background instead of
  filling with a neutral. Load-bearing for Bus Buddies: the flood-accessibility model needs empty
  background to reach concave parts of the silhouette.
- **4b — Black outline on subject only:** after segmentation, paint background cells that are
  orthogonally adjacent to the subject **black**, producing a silhouette outline (background
  interior stays empty/neutral). Matches the Food Hunt look.

Both are config flags on the image-to-grid asset. Localized; no Layer-1 churn expected.

### Task 1+2 — GDD → spec (replaces "explore Food Hunt")
Write the Bus Buddies mechanics brief + architecture-mapping doc from the GDD. Gates Task 3.
Exploration sub-agent **dropped** (GDD is authoritative). Optional light polish/juice reference pass later.

### Task 3 — Bus Buddies Layer-2 profile (the big one · phased, mirrors YAK)
Reusable (~80%): editor UI, autofiller (re-targeted at new accessibility), batch generator,
exporter, palette, image→grid (+ 4a/4b).
New (~20%, all Layer-2): **4-side flood-accessibility / no-gravity / 5-slot-deadlock simulator**
+ analyzer (difficulty/APS + deadlock risk) + autofiller targeting it + `BusBuddiesProfile` +
exporter + the 3 mechanics (Hidden cube / Hidden Bus / Connected Bus).

---

## 3. Attack order

```
Task 4 (image→grid 4a + 4b) ──► START NOW (independent, fast, hard dependency for Bus Buddies)

GDD → spec ──► Task 3 Bus Buddies profile (phased, gated on the spec)
```

## 4. Status
- 2026-06-29 — backlog created from GDD; Task 4 kicked off through the /daily pipeline.
