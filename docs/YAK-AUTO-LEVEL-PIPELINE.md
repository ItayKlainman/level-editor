# YAK Auto-Level Pipeline — Runbook

How to turn a one-line idea into a playable YAK (YarnKingdom) level, automatically.

```
ideas.txt line   ─►  palette-injected prompt  ─►  OpenAI image  ─►  PNG in SourceImages
   (/yak-ideas)        (BuildPrompt)              (Image Library)         │
                                                                          ▼
   playable level  ◄─  analyze (solvable? APS)  ◄─  autofill spools  ◄─  image → grid
   (export to game)        (YAKLevelAnalyzer)        (YAKSpoolAutofiller)   (YAKImageToGrid)
```

**Status (2026-06-24):** Everything below is proven working **end-to-end against the live OpenAI API** — a "ladybug" idea produced a Solvable level (APS 2.31). Two gaps remain *after* this pipeline: **Gap B** (the APS difficulty number isn't calibrated to the real game) and **Gap C** (a generated level hasn't been confirmed to load + win in the actual YarnKingdom build). Gap C is the next task.

---

## A. One-time setup

### 1. OpenAI API access — NOT the same as ChatGPT Plus
The image generator uses the **OpenAI Images API**, which is a separate developer account with its own billing — a ChatGPT Plus/Pro subscription gives **$0** of API credit.

1. Sign in / create an account at **platform.openai.com** (separate from chat.openai.com).
2. **Billing → add a payment method → add credit** (even $5 is plenty; pay-as-you-go).
3. **API keys → Create new secret key** → copy it (shown once).
4. If you get a *"model must be verified"* error for `gpt-image-1`: **Settings → Organization → Verify** (a quick ID check). Or switch the config `Model` to `dall-e-3` (the client supports it and it needs no verification).

**Cost per image** (square): low ≈ **$0.02**, medium ≈ **$0.07**, high ≈ **$0.19**. A 200-image pool ≈ $4 (low) to $38 (high).

### 2. The config asset
`Assets/YAK/Data/Config/YAKImageLibraryConfig.asset` (create via `Assets ▸ Create ▸ Hoppa ▸ YAK ▸ Image Library Config` if missing). Fields:

| Field | What it does | Default |
|-------|--------------|---------|
| `Profile` | The YAK `GameProfile` (palette, grid size). | `YAKProfile.asset` |
| `IdeasAsset` | The text file of ideas (one per line). | `SourceImages/ideas.txt` |
| `OutputFolder` | Where generated PNGs are written (project-relative). | `Assets/YAK/SourceImages` |
| `Model` | OpenAI image model. **Configurable** — `gpt-image-1` deprecates 2026-10-23. | `gpt-image-1` |
| `ImageSize` | Requested image size. | `1024x1024` |
| `Quality` | `low` / `medium` / `high` — cost vs. fidelity. | `low` |
| `StylePreamble` | The flat-sticker prompt template (contains `{idea}`). | built-in |
| `ExcludedNeutralIds` | Palette colors NOT injected into the prompt (grey/white/black). | grey/white/black set |
| `MaxImagesPerRun` | Hard cap per Generate run. | `25` |
| `EstimatedUsdPerImage` | Only drives the pre-run cost estimate dialog. | `0.07` |

### 3. Set the API key (local, never committed)
The key is resolved at call time and **never stored on any asset or in git**:
- **Preferred:** environment variable `OPENAI_API_KEY` (works headless; set it before launching Unity).
- **Or:** open the Image Library window (below), type the key into the **password field**, click **Set** — stored per-machine in `EditorPrefs`. Click **Clear** to remove it.

---

## B. Day-to-day workflow

### Step 1 — Curate ideas  (`/yak-ideas` skill)
The idea list is a **plain text file**: `Assets/YAK/SourceImages/ideas.txt`, one idea per line, `#` for comments. Not an enum, not JSON.

Run the **`/yak-ideas`** skill in Claude Code:
- **Add:** paste reference level images + name a genre → it proposes deduped, single-subject, palette-friendly idea lines → you approve → it appends them.
- **Remove:** name subjects → it shows matches → you confirm → it deletes.

Idea-quality rules (enforced by the skill): one concrete subject, recognizable silhouette, no scenes/text/abstract, phrased to slug cleanly (the line becomes both the prompt and the PNG filename).

### Step 2 — Generate images  (Image Library window)
**`Window ▸ Hoppa ▸ YAK ▸ Image Library`**
1. Assign the **Config** asset.
2. Make sure the **API key** row shows a source (`env` or `EditorPrefs`).
3. **Scan Gaps** — lists each idea as *have* / *missing* (an idea is "have" if a matching PNG already exists, so re-runs only fill gaps).
4. **Generate Missing (N)** — confirm the cost dialog. Images write to `OutputFolder`, one per idea, capped at `MaxImagesPerRun`.

Safety built in: cost-confirm + cap, gap-skip on re-runs, crash-safe writes (temp → atomic rename), 429 backoff, editor stays responsive (one request at a time).

### Step 3 — Turn images into levels
Three ways, all running the same chain (**image → grid → autofill → analyze**):
- **Single / interactive:** `Window ▸ Level Editor` → toolbar **🖼 Image** mode → set the source image → **Convert** (preview shows grid + spools) → **Use This Level**.
- **Generate one:** Level Editor → **✨ Generate** mode (picks an image from the folder by seed, runs the full chain, gated on Solvable + APS-in-band).
- **Batch / overnight:** `Window ▸ Hoppa ▸ YAK ▸ Run Batch` (or headless `-executeMethod`) → loops the generator, dedups, writes `json + png + stats` to a dated staging folder.

### Step 4 — Review & import  (Batch Review window)
**`Window ▸ Hoppa ▸ Batch Review`** → point at the batch staging folder → thumbnails + stats (Solvable / APS / colors) → multi-select → **Import Selected** into your levels folder.

### Step 5 — Export to the game
Use the existing master exporter (Export ▸ button) to write the level into the YarnKingdom project. *(Confirming the exported level actually plays in-game is Gap C — pending.)*

---

## C. Free / no-API testing
You don't need the API to exercise the **system** — the OpenAI call just fills the image folder. To test for free:
- Drop any flat, simple PNGs (emoji stickers work great — e.g. Google Noto emoji) into `Assets/YAK/SourceImages/`, then run Step 3.
- This validates image → grid → autofill → analyze without spending anything. (A keyless **Pollinations** provider could be added later to also test the auto-generate path for free.)

---

## D. Known polish items & gotchas
- **Prompt wording:** ideas with a leading article produce "centered **a ladybug**" (double article). Cosmetic; planned fix = strip "a/an" or reword `StylePreamble`.
- **Color list length:** the prompt currently injects ~30 palette colors. Faithful for quantization but more than a "bold sticker" needs; may trim to ~12. (The converter caps output to 6 colors regardless.)
- **APS is uncalibrated (Gap B):** "Band 2 / APS 2.31" is the simulator's verdict, not yet validated against the real game.
- **In-game unverified (Gap C):** the export round-trip into a playable, winnable level in YarnKingdom is the next milestone.
- **Model deprecation:** `gpt-image-1` retires 2026-10-23 — change the `Model` field when migrating.
- **Transparent-background sources** (like emoji) segment fine; the converter fills the background with the most-contrasting neutral.

---

## E. Key paths
| Thing | Path / location |
|-------|-----------------|
| Ideas list | `Assets/YAK/SourceImages/ideas.txt` |
| Generated images | `Assets/YAK/SourceImages/` |
| Library config | `Assets/YAK/Data/Config/YAKImageLibraryConfig.asset` |
| Game profile | `Assets/YAK/Data/Config/YAKProfile.asset` |
| Image Library window | `Window ▸ Hoppa ▸ YAK ▸ Image Library` |
| Level Editor | `Window ▸ Level Editor` (🖼 Image / ✨ Generate modes) |
| Batch run / review | `Window ▸ Hoppa ▸ YAK ▸ Run Batch` · `Window ▸ Hoppa ▸ Batch Review` |
| `/yak-ideas` skill | `.claude/skills/yak-ideas/SKILL.md` |
| Design specs / plan | `docs/superpowers/specs/2026-06-24-yak-image-library-design.md`, `…-yak-ideas-curation-design.md`, `docs/superpowers/plans/2026-06-24-yak-image-library.md` |
| Demo level artifacts | `Assets/YAK/_PipelineDemo/` (local; apple + ladybug level JSON — export candidates for Gap C) |
```
