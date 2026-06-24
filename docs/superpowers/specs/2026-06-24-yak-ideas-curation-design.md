# YAK Ideas Curation (`/yak-ideas` skill) — Design Spec

**Date:** 2026-06-24
**Status:** Draft, pending user review

---

## Overview

The YAK Image Library tool (see `2026-06-24-yak-image-library-design.md`) generates one source
image per line in an `ideas.txt` file. This spec covers **authoring that ideas file**: a project
skill, `/yak-ideas`, that lets the lead refresh the idea list on demand by pasting reference level
images for a genre and having Claude propose on-genre, conversion-friendly idea lines — deduped and
reviewed before they are written.

This is a **workflow, not runtime software**: the only artifact is one `SKILL.md`. There is no C#,
no test suite. Claude executes the steps; the skill keeps them consistent across sessions.

## Goals

1. **Add ideas from references + genre.** Given reference images pasted in chat and a genre label,
   propose idea lines that match the references *and* new subjects that fit the genre.
2. **Remove ideas on request.** Prune named subjects / matching lines from the file.
3. **Keep ideas convertible.** Every proposed line obeys idea-quality rules so `YAKImageToGrid`
   can turn the generated image into a clean grid.
4. **Never write without review.** Claude proposes; the lead approves; only then is the file edited.
5. **Stay consistent.** The same steps run every time, captured in the skill.

## Non-goals

- Any automated/in-editor idea generation (an LLM-calling Unity tool). Explicitly rejected — the
  interactive, taste-driven curation Claude does is a better fit and needs no code.
- Web-search or references-folder ingestion as the primary path. The chosen input is **images
  pasted in chat + a genre label**. (Claude may still search the web if the lead asks, but it is
  not the default flow.)
- Generating the images themselves — that is the Image Library tool's job. This skill only edits
  the text file.

---

## Decisions log

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Curated by Claude on demand, not an automated tool | Occasional, taste-driven task; zero code; fully flexible. |
| D2 | Captured as a project skill `/yak-ideas`, not ad-hoc chat | Consistent steps across sessions; one-invocation. |
| D3 | Primary input = reference images pasted in chat + a genre label | Lead's stated workflow: "give image refs of a few dozen levels for a genre, generate ideas from those + others that fit." |
| D4 | Two modes: add + remove | Title of the request: "add/remove items from the ideas text file." |
| D5 | Review gate before any write | The lead curates the final list; avoids polluting the file. |
| D6 | Canonical live file: `Assets/YAK/SourceImages/ideas.txt` | Sits beside `ideas.sample.txt`; the Image Library config's `IdeasAsset` points here. Skill seeds it from the sample if missing. |
| D7 | No separate TDD implementation plan | The artifact is one prose `SKILL.md`; a code plan/tests would be meaningless. |

---

## The skill

**Location:** `.claude/skills/yak-ideas/SKILL.md` (project-scoped, checked in).
**Invocation:** `/yak-ideas` (optionally `/yak-ideas remove ...`).

### Inputs
- Reference level images pasted into the conversation (add mode).
- A **genre label** (e.g. "casual puzzles").
- Optional **target count** (default ~30).
- Optional **ideas file path** (default `Assets/YAK/SourceImages/ideas.txt`).

### Add flow
1. Read the ideas file (create from `ideas.sample.txt` if missing). Parse non-comment lines into a
   lowercased set for dedupe.
2. Analyze the pasted references: infer the genre's **subject vocabulary** (recurring object types)
   and visual style (how iconic / simple the subjects are).
3. Generate candidate idea lines: a mix of **subjects resembling the references** and **new
   subjects that fit the genre**, each obeying the Idea-Quality Rules below.
4. Dedupe candidates against the existing file **and** within the candidate set — semantic, not
   just exact (treat "popsicle" and "a popsicle" as the same).
5. Present the candidates to the lead, tagged `[from refs]` vs `[new on-genre]`. The lead
   edits/approves.
6. On approval, append the approved lines to the file (preserve `#` comments and existing order).
   Report how many were added and how many duplicates were skipped.

### Remove flow
1. The lead names subjects or a substring.
2. Show the matching lines.
3. On confirmation, remove them and report the count.

### Idea-Quality Rules (the core value)
A good idea line is:
- A **single, concrete subject** ("a slice of watermelon"), never a scene or multiple objects
  ("a picnic with friends").
- **Recognizable by silhouette**, so a flat sticker reads clearly.
- **Colorable with the wool palette** — avoid subjects that depend on fine gradients or text.
- **No text, no logos, no abstract concepts.**
- **Phrased to slug cleanly**, since the Image Library derives the PNG filename from the line
  (lowercase alphanumerics + dashes).

### File conventions (inherited from the Image Library spec)
Plain text, one idea per line, `#` for comment lines, UTF-8.

---

## Testing / verification

No automated tests (prose skill). Verification is a dry run: paste a handful of reference images +
a genre, confirm Claude proposes deduped, on-genre, single-subject lines, tags ref-derived vs new,
and only writes after approval. Confirm a remove request shows matches before deleting.
