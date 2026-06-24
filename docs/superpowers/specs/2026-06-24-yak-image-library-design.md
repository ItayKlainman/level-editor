# YAK Image Library — AI Source-Image Auto-fill — Design Spec

**Date:** 2026-06-24
**Status:** Draft, pending user review

---

## Overview

The YAK level-automation pipeline already converts a source image into a playable level
(`YAKImageToGrid` → spool auto-fill → analyzer gate → batch). Today the **source images are
supplied by hand** — a human finds or draws pictures and drops them into the generator's
`SourceImageFolder`. This is the last manual step at the front of the pipe ("Gap A").

This feature adds an **offline Editor tool** that fills that folder automatically. The designer
keeps a plain-text list of level ideas (one per line: `monkey eating banana`, `dolphin`,
`popsicle`, …). The tool reads the list, asks OpenAI's Images API to draw each idea as a flat,
grid-friendly picture in the game's own wool colors, and writes the resulting PNGs into
`SourceImageFolder`. The existing generator then consumes them **unchanged**.

The tool is a pure **producer** into the image folder. Nothing downstream changes.

---

## Goals

1. **Auto-fill `SourceImageFolder`** from a text list of ideas, one API-generated image per idea.
2. **Grid-friendly output** — the constant prompt forces a flat sticker/vector style (single
   centered subject, bold solid colors, plain background, no gradients/text/shadows) and injects
   the game's **wool colors** so the image already sits near the palette before quantization.
3. **Cheap re-runs** — only generate images for ideas that don't yet have one (gap-fill).
4. **Safe to run** — never freezes the Editor, never blows the budget unannounced, survives a
   mid-run crash, and handles rate-limits.
5. **No pipeline changes** — `YAKLevelGenerator`, `YAKImageToGrid`, `YAKBatchHarness` untouched.

## Non-goals (v1)

- **Inline / on-demand generation.** v1 is offline pre-fill only. No network during generation
  or batch runs. (An `IImageSource` runtime interface is explicitly *not* introduced — nothing
  else would implement it; the folder is the seam.)
- **Rich per-idea entries** (per-idea color count, palette bias, background hints). v1 ideas are
  plain strings. The schema can grow later if some subjects convert poorly.
- **JSON ideas format.** Plain text, one idea per line, `#` for comment lines. (YAGNI.)
- **Image *editing*/refinement passes, upscaling, or A/B variant selection.** One image per idea.
- **Calibration of the analyzer (Gap B) or in-game verification (Gap C).** Separate initiatives.

---

## Decisions log

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Offline pre-fill into the folder; generator consumes unchanged | Cheapest, deterministic, reviewable; the existing `PickSourceImage(folder, seed)` seam is already built for it. |
| D2 | Unity **Editor window** in C# (`Window ▸ Hoppa ▸ YAK ▸ Image Library`) | Matches the rest of the pipeline (all Editor C#); ideas/prompt/folder reviewable in one place. Mirrors `BatchReviewWindow`. |
| D3 | Ideas = **plain text asset**, one idea per line, `#` comments | Simplest to author/maintain; auto-tracking by derived filename. |
| D4 | Auto-track via **filename = slug(idea)**; only generate missing files | Re-runs fill gaps, never regenerate → cost control + idempotent resume. |
| D5 | Constant prompt injects the **wool colors only** (not background neutrals) | Bold subject in real game colors; the converter fills the background neutral itself. |
| D6 | **Model name is a config field** (default `gpt-image-1`) | `gpt-image-1` deprecates 2026-10-23; must be swappable without a code change. |
| D7 | API key from **env var `OPENAI_API_KEY`** then **EditorPrefs** fallback; never committed | Project rule: no secrets/absolute paths in committed assets. |
| D8 | **Pure core** (slug / parse / gap-diff / prompt) split from I/O (HTTP / file / UI) | Unit-testable decisions; thin untested edges. Matches the project's pure-core convention. |
| D9 | **No `IImageSource` interface** in v1 | Speculative — nothing else implements it. The folder is the integration boundary. |

---

## Architecture

The tool is five small types. Network and disk live at the edges; all decisions live in a pure,
testable core.

```
ideas.txt ──► YAKImageLibraryCore (PURE) ──► YAKImageLibraryWindow ──► OpenAI ──► PNGs ──► SourceImageFolder
              · ParseIdeas                    (orchestrates,                              │
              · IdeaToFileName (slug)           progress, cancel)                          ▼
              · FindMissing (gap-diff)                │                          [ existing pipeline,
              · BuildPrompt (+ wool palette)          ├─ YAKImageApiKey            unchanged ]
                                                       ├─ YAKOpenAIImageClient
                                                       └─ YAKImageLibraryConfig
```

| Type | Kind | Owns | I/O |
|------|------|------|-----|
| `YAKImageLibraryCore` | pure static | `ParseIdeas`, `IdeaToFileName` (collision-safe slug), `FindMissing`, `BuildPrompt` | none |
| `YAKImageLibraryConfig` | ScriptableObject | ideas `TextAsset`, output folder (project-relative, default = generator's `SourceImageFolder`), model name, image size, style preamble, palette source, `MaxImagesPerRun` | none |
| `YAKImageApiKey` | static | resolve key: env `OPENAI_API_KEY` → `EditorPrefs`; set/clear helper | EditorPrefs |
| `YAKOpenAIImageClient` | class | one `UnityWebRequest` POST → PNG `byte[]` or typed error; tolerates `b64_json` **and** `url` responses | network |
| `YAKImageLibraryWindow` | EditorWindow | UI, run loop (update-pump), progress bar, cancel; calls the others | EditorPrefs, file write, AssetDatabase |

Located under `Assets/YAK/Editor/ImageLibrary/` (existing `YAK.Editor` asmdef — no new asmdef).
Tests under `Assets/YAK/Tests/Editor/` (existing test asmdef).

---

## Data flow

1. Designer edits the ideas text asset (one idea per line).
2. **Scan Gaps** — `ParseIdeas` → `IdeaToFileName` per idea → `FindMissing` against PNGs already
   in the output folder. Window lists each idea as *have* / *missing*.
3. **Generate Missing** — confirm dialog shows count + estimated $ (count × per-image price for
   the chosen size/quality), capped at `MaxImagesPerRun`. On confirm:
   - For each missing idea (concurrency 1): `BuildPrompt(idea, woolPalette, stylePreamble)` →
     `YAKOpenAIImageClient` → PNG bytes → **atomic write** (temp file → rename) into the folder →
     `AssetDatabase.ImportAsset`. Per-idea status row updates; progress bar is cancelable.
4. The existing `YAKLevelGenerator.PickSourceImage(folder, seed)` now finds real images and runs
   the rest of the pipeline as before.

---

## Prompt construction

`BuildPrompt(idea, woolPalette, stylePreamble)` composes, in order:

1. **Style preamble** (config-editable default): *"A single centered {idea}, flat vector sticker
   style, bold solid fill colors, thick clean shapes, plain solid background. No gradients, no
   shading, no text, no drop shadows, no photorealism."*
2. **Palette constraint:** *"Use only these flat colors: {comma-joined wool color names / hexes}."*
   Wool colors are read from the profile's `ColorPalette` (via the public accessor — not by
   reaching into `YAKStaticManagerColorSource`), excluding the background neutrals listed in
   `YAKImageToGrid.BackgroundNeutrals`.

Palette injection is a **quality lever, not a correctness requirement** — `YAKImageToGrid` always
re-quantizes to the palette, so a mismatched image degrades gracefully rather than breaking.

---

## Failure modes & safety rails

| Risk | Handling |
|------|----------|
| **Editor freeze** | Requests driven off an `EditorApplication.update` / coroutine pump, one in-flight at a time, with a cancelable `EditorUtility.DisplayCancelableProgressBar`. |
| **Cost runaway** | Gap-diff never regenerates existing files; `MaxImagesPerRun` cap; confirm dialog with count + estimated spend before the first call. |
| **Partial failure / corrupt write** | Temp-file → atomic rename; "done" = a valid PNG exists, so a crashed run resumes by re-scanning gaps. One idea's failure is recorded in its status row, never aborts the batch. |
| **Filename collision** | Slug is deterministic and total (lowercase, strip non-alphanumerics); on collision append a short stable hash of the full idea string. Pure → unit-tested. |
| **Rate-limit (429) / 5xx** | Exponential backoff with jitter, capped retries, then mark the idea failed-retryable. Concurrency stays 1 — parallelizing trips limits and multiplies cost. |
| **Secret leakage** | Key never stored in the config SO or any committed asset; env var first, per-machine EditorPrefs fallback. |
| **Absolute paths in assets** | Output folder stored project-relative so `PickSourceImage`'s AssetDatabase lookup works across checkouts. |

---

## Testing

**EditMode unit tests** (`YakImageLibraryTests.cs`) on the pure core:

- `IdeaToFileName` — stability, sanitization, collision→hash branch.
- `ParseIdeas` — one-per-line, trimming, de-dupe, `#` comment skip.
- `FindMissing` — ideas minus existing slugs → correct gap list.
- `BuildPrompt` — output contains the idea, the style constraints, and each wool color; excludes
  background neutrals.

**Not unit-tested (manual / editor-verified):** the HTTP client, file writes, AssetDatabase
import, and window UI. Smoke test: a 3-idea list end-to-end against the live API producing 3 PNGs
in the folder, then a generator run consuming one.

---

## Out-of-scope follow-ups (noted, not built)

- AI image *source* variants beyond OpenAI (other providers behind the same client shape).
- Rich per-idea hints (color count / palette bias) if some subjects convert poorly.
- On-demand inline generation, if the offline flywheel ever proves too slow.
