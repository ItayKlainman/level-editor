# YAK Image Library — Multi-Prompt (Theme) Generation

**Date:** 2026-07-14
**Status:** Design — drafting (core in progress, window wiring pending review)

## Goal

Let the Image Library generate a batch from **multiple theme prompts**, N images per prompt (the
boss's ask: 5 prompts × 2 images = 10). Each prompt is a *theme*; the model invents a different
subject each time, wrapped in the convert-friendly technical rules so the outputs still make clean
single-subject levels.

## Today

`YAKImageLibraryCore` + `YAKImageLibraryWindow`: one `StylePreamble` × many single-subject **ideas**
(`ideas.txt`), one image per idea. The generation pump (`Pump`) dequeues an *idea string*, builds its
prompt via `BuildPrompt(idea, colors, StylePreamble)`, calls OpenAI, and saves `<ideaSlug>_<hash>.png`
(`IdeaToFileName`). `_current` is used as BOTH the prompt subject and the filename/status key.

## Design

### Prompt source
A new **`TextAsset PromptsAsset`** parallel to `ideas.txt`, parsed into **blocks separated by blank
line(s)** — each block is one theme prompt (multi-sentence). A leading enumerator (`1)`, `2.`, `-`) is
stripped. (Blank-line blocks, not per-line, because the prompts are multi-sentence paragraphs.)

### Theme wrapper (convert-friendly technical rules)
The boss's prompts describe vibe but omit what makes an image convert cleanly. A shared
**`ThemeStylePreamble`** (with a `{theme}` placeholder) supplies the technical rules and appends the
theme. Default:

> A single centered subject, flat bold cartoon illustration, big solid fill colors, thick clean chunky
> shapes, clear readable silhouette. Fill the ENTIRE background with one flat uniform solid color that
> clearly contrasts the subject and is not a color used in the subject — no gradient, vignette, glow,
> or texture. Do NOT draw ANY outline, border, stroke, drop shadow, or halo around the subject — flat
> fills only. No shading, no text, no frame, no photorealism. Exactly ONE subject, centered, filling
> most of the frame. Theme: {theme}

`BuildThemePrompt(theme, colorDescriptors, preamble)` = preamble with `{theme}` replaced (+ optional
color list, same as `BuildPrompt`).

### The 5 adapted prompts (stored in the prompts asset)
Lightly adjusted from the boss's list to guarantee a **single** subject (critical for conversion),
art style preserved; the technical rules live in the wrapper so the boss edits pure theme content.

1. Cute animals with funny facial expressions, oversized eyes, tiny mouths, chubby proportions, playful poses, big clear emotions (smiling, surprised, sleepy, grumpy), wholesome and adorable, one instantly recognizable animal.
2. A single everyday object turned into an adorable living character — cute face, tiny eyes, happy expression, playful personality (smiling food, a friendly household item), charming, wholesome and funny.
3. One whimsical hybrid creature combining two unrelated things into a single adorable design — an animal mixed with a fruit, vegetable, dessert, sea creature, plant, or everyday object; funny, unexpected, charming, one clear subject.
4. A single tiny fantasy creature — a magical forest animal, miniature monster, mythical being, cute ghost, tiny dragon, or little spirit — cozy and magical, playful personality, adorable expression.
5. One funny miniature character with exaggerated emotion and a silly costume or unexpected situation — cute internet-meme energy, a single collectible character that makes people smile instantly (one focal character, not a busy scene).

### Generation flow (unify both modes through one pump)
Replace the pump's `Queue<string>` with a `Queue<GenJob>` where `GenJob { string Prompt; string FileKey; }`:
- **Ideas mode:** `Prompt = BuildPrompt(idea, colors, StylePreamble)`, `FileKey = idea` (→ `IdeaToFileName`).
- **Prompts mode:** for each selected theme, enqueue `ImagesPerPrompt` jobs with
  `Prompt = BuildThemePrompt(theme, colors, ThemeStylePreamble)`, `FileKey = ThemeToFileName(theme, i)`.

`ThemeToFileName(theme, i)` = `<themeSlug>_<hash8>` where `themeSlug` = slug of the theme's first ~4
words and `hash8` = deterministic hash of `theme + "#" + i` (distinct per image, collision-free,
stable for re-runs). `WritePng` already takes a file key; `_status`/progress key on it unchanged.

### Config additions (`YAKImageLibraryConfig`)
- `TextAsset PromptsAsset;`
- `[Min(1)] int ImagesPerPrompt = 2;`
- `string ThemeStylePreamble = YAKImageLibraryCore.DefaultThemeStylePreamble;`

### Window UI (`YAKImageLibraryWindow`)
A **"Prompts"** section beside the ideas list: shows the parsed prompt blocks with checkboxes, an
**Images per prompt** int field, and a **Generate (P × K)** button that enqueues `selectedPrompts ×
ImagesPerPrompt` jobs (respecting `MaxImagesPerRun` + the cost estimate/confirm dialog). Ideas mode
stays as-is.

## Testing (core, headless)
- `ParsePrompts`: blank-line blocks split correctly; leading `N)`/`N.`/`-` stripped; blank input → empty.
- `BuildThemePrompt`: `{theme}` replaced; color list appended when provided; falls back to default preamble when empty.
- `ThemeToFileName`: deterministic; distinct for different `i`; filesystem-safe slug.
- Window pump changes: verified by compile + a manual generate (API cost) — no unit test for the IMGUI/network path.

## Out of scope / notes
- No change to the converter (Task 2 handles conversion quality).
- The theme wrapper deliberately hard-pins ONE subject + no-outline so outputs match the convert
  pipeline; prompt #5 was reworded from "scenes" to a single focal character for the same reason.
- Filenames have no human subject label (the image API returns no caption); the theme slug keeps them
  grouped/identifiable.
