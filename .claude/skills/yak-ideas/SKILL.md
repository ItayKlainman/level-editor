---
name: yak-ideas
description: Curate the YAK Image Library ideas list. Use when the user wants to add ideas (by pasting reference level images for a genre, or naming a genre/theme) or remove ideas from the ideas.txt that the YAK image generator consumes. Proposes deduped, conversion-friendly, single-subject idea lines and only writes after the user approves.
---

# YAK Ideas Curation

Curate the plain-text **ideas list** that the YAK Image Library tool turns into source images
(one image per line). You are the curator: you propose, the user approves, then you write.

**Canonical file:** `Assets/YAK/SourceImages/ideas.txt`
(If it doesn't exist yet, create it by copying the header + lines from `ideas.sample.txt` in the
same folder, then continue.) The user may override the path — honor it if they do.

**File format:** plain text, UTF-8, one idea per line, `#` starts a comment line.

There are two modes: **add** (default) and **remove**. Pick based on what the user asked.

---

## Add mode

Trigger: the user pasted reference level images and/or named a genre/theme and wants more ideas.

Create a todo per step and do them in order:

1. **Read the current file.** Load the ideas file (create from `ideas.sample.txt` if missing).
   Parse every non-comment, non-blank line; trim; build a **lowercased set** of existing ideas for
   dedupe. Note the count.

2. **Establish the genre + vocabulary.**
   - If reference images were pasted: look at them and infer the genre's **subject vocabulary**
     (the recurring kinds of objects) and visual style (how iconic / simple the subjects are).
   - If only a genre/theme label was given: use it directly.
   - If neither is clear, ask the user for the genre before proceeding — one question, then continue.

3. **Generate candidates.** Produce the target number of idea lines (default ~30 if unspecified),
   as a mix of:
   - `[from refs]` — subjects that resemble what the references show, and
   - `[new on-genre]` — fresh subjects that fit the same genre but aren't in the references.
   Every line MUST obey the **Idea-Quality Rules** below.

4. **Dedupe.** Drop any candidate whose lowercased form is already in the existing set, and drop
   near-duplicates within the candidate set. Treat these as the same: ignore a leading "a"/"an"/"the",
   ignore trailing punctuation, compare case-insensitively (so "popsicle" ≈ "a popsicle").

5. **Present for approval.** Show the surviving candidates as a numbered list, each tagged
   `[from refs]` or `[new on-genre]`. State how many you generated and how many you dropped as
   duplicates. Ask the user to approve, edit, or cut lines. **Do not write yet.**

6. **Write on approval.** Append the approved lines to the file, preserving the existing `#`
   comments and line order (append at the end). Report: how many added, how many skipped as
   duplicates, and the new total. Do not touch any other file.

---

## Remove mode

Trigger: the user wants to remove ideas (names subjects, or a substring/pattern).

1. Read the file and find every line matching what the user named (case-insensitive substring
   match unless they specify exact).
2. Show the matching lines as a numbered list. If none match, say so and stop.
3. On the user's confirmation, remove those lines (leave comments and all other lines untouched)
   and report how many were removed and the new total.

Never remove lines without showing the matches and getting confirmation first.

---

## Idea-Quality Rules (non-negotiable — this is the point of the skill)

The YAK image→grid converter needs a clean, iconic subject. Every idea line you propose MUST be:

- **A single, concrete subject** — "a slice of watermelon", never a scene or multiple objects
  ("a picnic with friends", "a busy kitchen").
- **Recognizable by silhouette** — so a flat sticker of it reads clearly at grid resolution.
- **Colorable with a small wool palette** — avoid subjects defined by fine gradients, photographic
  detail, or text.
- **Free of text, logos, numbers, and abstract concepts** ("freedom", "Tuesday").
- **Phrased to slug cleanly** — the Image Library derives the PNG filename from the line by
  lowercasing and turning non-alphanumerics into dashes, so keep lines short and plain
  (no quotes, slashes, or emoji).

Good: `red apple`, `dolphin jumping over a wave`, `a slice of watermelon`, `smiling sun`.
Bad: `a cozy beach scene at sunset` (scene), `the word LOVE` (text), `something fun` (vague).

---

## Guardrails

- **Never write before the user approves** the proposed list (add) or confirms the matches (remove).
- **Only edit the ideas file.** This skill does not generate images, touch C#, or run Unity — image
  generation is the Image Library tool's job (`Window ▸ Hoppa ▸ YAK ▸ Image Library`).
- **Preserve the file's comments and ordering.** Append new ideas; don't reorder or rewrite existing
  lines.
- If the user pasted no references and gave no genre, ask for the genre — don't invent one silently.
