# YAK Image Library — Art Narrative + Boss-Brief Idea Set

**Date:** 2026-07-14
**Status:** Implemented (409→408 EditMode green)

## Goal

The boss supplied five prompts for the image generator. They are **full prompts** — each mixes a
*subject category* (cute animals / everyday objects / hybrids / fantasy creatures / funny characters)
with the *visual style*. Read literally they'd be five one-off image prompts. What they actually
encode is one art direction plus five buckets of subjects.

So we split them along that seam:

- **Style → `prompts.txt`** — one shared art narrative, applied to every image.
- **Subjects → `ideas.txt`** — the five buckets, expanded into a concrete idea list.

## The art narrative

Every one of the boss's five prompts lands on the same look: *"premium/collectible **pixel art**
icon"* — cute, oversized eyes, big readable emotion, playful personality, instantly recognizable
silhouette, wholesome and funny. That is the style; it is now the single preamble for the whole set,
so the levels read as one collectible family rather than five unrelated looks.

**The pixel-art / outline conflict.** Classic pixel-art icons carry a thick dark outline — which is
exactly what wrecked the shark conversion (the outline smears into a spread dark ring when downscaled
to the level grid). Resolution (lead's call): **adopt the pixel-art look, keep the hard no-outline
rule.** Chunky blocky shapes, crisp edges, flat fills — no stroke, shadow, or halo. Two clauses in the
narrative are load-bearing for the converter and must survive any future edit:

1. no outline / border / stroke / drop shadow / halo;
2. one flat uniform solid background color, contrasting, no gradient or texture.

## Design

**`prompts.txt`** holds the narrative with an `{idea}` placeholder. It is the designer-editable source
of truth: editing it changes the look of every image, with no code change.

**`YAKImageLibraryCore.ParseStylePrompt(raw)`** strips `#` comments and rejoins hard-wrapped lines into
one paragraph. Returns `""` when the asset carries no style text, so callers fall back cleanly.

**Config:** `StylePromptAsset` (TextAsset). When assigned and non-empty it overrides the `StylePreamble`
string field; `DefaultStylePreamble` (now the pixel-art narrative) is the final fallback. The window
shows an "Art style" row naming the active source, so a designer can see at a glance that their
`prompts.txt` edit is the style actually being sent.

**Generation:** unchanged in shape — tick ideas, Generate Selected. Each job is a
`GenJob { Prompt, FileName, StatusKey }`, so prompt-building sits outside the pump and the
OpenAI/retry/atomic-save path stays single. "Batching" is simply ticking many ideas.

**`ideas.txt`** gains a `# Boss brief` section: ~110 subjects across his five categories (the lead's
three supplied lists, plus a few seeded ones).

## Testing

`ParseStylePrompt` (comment strip + line rejoin; empty → fallback), the asset text actually driving the
built prompt with `{idea}` substituted, and a guard that `DefaultStylePreamble` is the pixel-art
narrative **and still bans outlines**. The IMGUI/network path has no unit test (verified by compile +
a live generate).

## Superseded

An earlier draft of this spec read the five prompts as *themes* — "theme → N images, AI picks the
subject" — and shipped a theme-batch UI (`ParsePrompts`, `BuildThemePrompt`, `ThemeToFileName`,
`ImagesPerPrompt`, a Prompts checkbox panel). That premise was wrong: the prompts are full prompts, not
themes. The theme path was removed rather than left as a dead, confusing second way to generate. The
`GenJob` queue refactor from that work was kept — it is a genuine simplification.
