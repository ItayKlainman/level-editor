# YAK Idea Generator — Design Spec (v1)

**Date:** 2026-07-23
**Status:** Approved (design); pending implementation plan
**Owner area:** YAK image tooling (editor-core), surfaced in the Level Editor's Generate tab

---

## 1. Goal

Give designers an in-editor button that **generates deduped, conversion-friendly pixel-art idea
lines** from a rich subject taxonomy, previews them grouped by subject, and — on approval —
appends them to `Assets/YAK/SourceImages/ideas.txt`. This automates the manual curation loop
(subject × modifier combination + quality-rule enforcement) that is currently done by hand and
via the boss's spreadsheets.

The generated idea lines feed the **existing** YAK image pipeline unchanged (image → grid →
level source art). This spec covers idea-line generation only; it does **not** change how images
are generated or consumed.

## 2. Background & constraints

- `ideas.txt` (one idea per line, `#` comments, `# @style:` sections, `# @batch: N` dividers) is
  the source of truth the YAK Image Library turns into images via shared `[rules]` + per-`@style`
  prompts in `prompts.txt`. See the `yak-ideas` curation skill for the quality rules.
- The image converter has known failure modes the idea lines must respect: **Dominant downscale,
  ColorCap, KeepLargestSubjectOnly, Despeckle, BorderRing.** In practice: one concrete subject,
  clear silhouette, small palette, no text/logos/numbers, no detached particles, no two-subject
  or scene phrasing, phrased to slug cleanly (lowercase, no trailing period).
- OpenAI access already exists for image generation via `YAKImageApiKey` (env `OPENAI_API_KEY` /
  EditorPrefs `Hoppa.YAK.OpenAIKey`). The **account billing hard limit** is a live blocker for the
  paid image run; text generation is negligible cost but **also gated by the same account limit**.
- The **Generate tab** today (`GeneratorModePanel`, Layer-1) hosts the level generator
  (Difficulty/APS/Seed → candidate level), used by BB/YarnTwist for difficulty-batch runs. It must
  remain intact.

## 3. Source taxonomy (from the boss's two docs)

### 3.1 Subject libraries (required — one primary subject per concept)
Animals (Forest Mammals, Pets, Farm, Ocean, Birds, Jungle, Desert, Arctic, Reptiles & Insects),
Food & Drinks, Nature, Fantasy, Vehicles, Sports, Sports Equipment, Tools, Technology, Household,
Professions, Hobbies, Music, Architecture, Seasonal & Holidays, Clothing, Toys & Games.
*(Full entry lists per Text 2 are seeded into the Knowledge Base asset — §5.)*

### 3.2 Optional modifiers (Creative Building Blocks — used only when appropriate)
Expression · Accessory · Theme · Action · Companion · Environment Piece.

### 3.3 Composition types
Subject only · Subject with expression · Subject performing an action · Subject wearing/holding an
accessory · Subject interacting with another object · Subject emerging from another object · Hybrid
subject · Mini storytelling scene.

### 3.4 Complexity levels & target distribution
Very Simple 10% · Simple 25% · Medium 30% · Complex 20% · Hybrid 10% · Mini Scene 5%.

### 3.5 Design rules (baked into the generation prompt)
Do not always add a face / emotions / accessories. Prefer variety over consistency. Mix minimalist
and rich concepts. Keep silhouettes clear. Encourage surprising niche ideas. Avoid repetitive
combinations. Validate uniqueness against the existing library.

## 4. UI — new "Idea Generator" mode in the Generate tab

- **Mode toggle** at the top of the Generate tab: `Level Generator | Idea Generator`. Defaults to
  the existing behavior; Idea Generator is an added mode, not a replacement. Only shown for a
  profile that opts in (BB profile in v1).
- **Left column:** `Amount` dropdown (5 / 10 / 20 / 50 / 100 / 200) · **Generate** button ·
  progress + status label · **Export Ideas** button (disabled until a successful generation).
- **Right column, two checkbox tables:**
  - **Subjects** — the 17 libraries (§3.1), all enabled by default; optional expand to sub-lists.
  - **Modifiers** — the 6 building blocks (§3.2); default "let the system decide."
  - **Advanced foldout:** complexity-distribution override (defaults to §3.4) and a batch-label
    field.
- **Output area (center/bottom):** generated lines **grouped by subject library**, each with a
  keep/cut checkbox and a "dupe" flag for any line colliding with the existing library.

## 5. Data — Knowledge Base asset

`Assets/YAK/Data/IdeaKnowledgeBase.json` (Layer-2, plain JSON, editable by hand; "hundreds of
entries later" = append to the JSON). Contents:

- `subjects`: array of libraries, each `{ name, subcategories?: [{name, entries:[...]}] | entries:[...] }`,
  seeded from Text 2.
- `modifiers`: the 6 building blocks with short guidance strings.
- `compositionTypes`: the 8 types.
- `complexityDistribution`: the 6 levels with default percentages.
- `designRules`: the rule strings from §3.5 + the converter quality rules from §2.

The asset is the single source the prompt builder reads; no taxonomy is hard-coded in C#.

## 6. Generation engine (LLM-backed)

Lives in `Assets/YAK`. Pure, testable pieces + one live call:

1. **Prompt builder (pure):** composes a single request from — selected subjects, allowed
   modifiers, requested amount, complexity distribution, composition types, design rules, the
   converter quality rules, and the **current `ideas.txt` contents** (for uniqueness). Instructs
   the model to follow the boss's generation flow (subject → composition → complexity →
   only-necessary modifiers → uniqueness) and to return lines grouped/labeled by subject in a
   parseable format.
2. **API call:** a cheap OpenAI **chat/text** model (model id configurable in the YAK config;
   reuses `YAKImageApiKey`). Text cost is negligible.
3. **Response parser (pure):** parses the model output into `{subject → [lines]}`. Tolerant of
   minor format drift; drops empty/garbage lines.
4. **Dedupe (pure):** case-insensitive, ignoring a leading a/an/the and trailing punctuation —
   against the existing `ideas.txt` set **and** within the new batch. Flags (does not silently
   drop) collisions in the preview.

Nothing is written to disk during generation; results live in memory for review.

## 7. Export

On **Export Ideas**, kept lines are appended to `ideas.txt`:

- Under a new `# @batch: N` divider (auto-incremented; label overridable in Advanced).
- Grouped with per-subject `# <Subject>` comment headers.
- Bound to a new general-purpose **`# @style: collectible`** section, with one matching prompt
  block added to `prompts.txt`, so the existing image pipeline always resolves a prompt for the
  new lines.
- Existing file content, comments, and ordering are preserved (append only).

## 8. Architecture & packaging

- **Layer-1 (com.hoppa.leveleditor.core):** add a generic hook `ProfileGeneratePanel` (mirrors the
  `ProfileLeftPanel`/`ProfileRightPanel` pattern) and a mode toggle in `GeneratorModePanel`. Layer-1
  stays ignorant of YAK/ideas.txt/OpenAI — it only knows "the profile supplied an extra Generate-tab
  panel." **Package bump 0.12.0 → 0.13.0.**
- **Layer-2 (Assets/YAK):** the Idea Generator panel, the generation engine, and the Knowledge Base
  asset. The BB profile references the YAK panel via the new hook.
- **Editor-core-only.** YAK tooling is not mirrored to the BB game project, so there is **no
  BB-game deploy** for this feature. The BB game may re-pin `#v0.13.0` at its leisure (the Layer-1
  hook is additive/backward-compatible and unused there).

## 9. Error handling

- **API / billing-limit / network errors:** surface a clear message (echo the OpenAI error, e.g.
  `billing_hard_limit_reached`), keep any partial results, write nothing.
- **Empty or malformed response:** report how many lines parsed vs requested; keep what parsed.
- **No subjects selected / amount 0:** disable Generate with a hint.
- Export is the only path that writes to disk, and only kept lines.

## 10. Testing

Unit-test the pure pieces with a **faked** API response (no live call in tests):
- Prompt builder includes selected subjects/modifiers, the distribution, and the existing library.
- Response parser handles well-formed and slightly-malformed output; groups by subject.
- Dedupe catches case/article/punctuation variants against the existing set and within-batch.
- Export writes a correct `# @batch: N` + per-subject block, preserves prior content, and adds the
  `@style: collectible` binding.
- Distribution accounting: requested complexity mix is represented in the built prompt.

The live API call and the Unity IMGUI panel are not unit-tested (consistent with the existing
image-gen tooling).

## 11. Out of scope (Future — per the boss's own "Future Expansion" label)

- Per-concept generated image prompts and rarity/tags/compatibility metadata.
- In-UI editing of the Knowledge Base (v1 edits the JSON by hand).
- The actual paid image run (still blocked on the OpenAI account billing limit).
- Mirroring any of this to the BB game project.

## 12. Notes

- The 100 finalized ideas (Cosmic Critters / Prehistoric Pals / Living-Gear Sports / Rockstar
  Instruments / Pirate-Sci-Fi Cove) remain parked. Preferred path: make them the tool's first real
  Generate/Export (dogfood). Alternatively they can be hand-appended at any time.
