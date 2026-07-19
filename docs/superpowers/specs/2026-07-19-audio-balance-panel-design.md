# Audio Balance Panel — Design

**Date:** 2026-07-19
**Status:** Approved (design), pending implementation plan
**Package:** `com.hoppa.audiobalance` (new sibling package, v0.1.0)

## 1. Problem

Sound assets arrive at wildly inconsistent levels. The number stored on an
`AudioSource` (or in the clip's import settings) says nothing about how loud the
clip *actually sounds* — a dense bass-heavy loop and a thin UI blip can share a
`volume` of `1.0` and be 15 dB apart to the ear.

Today the only remedy is a human dragging volume sliders until it "feels right",
per clip, per game, with no record of the reasoning and no way to redo it when a
sound is replaced.

## 2. Solution

One clip is designated the **anchor** — normally the background music that runs
during levels. Its measured perceived loudness becomes the reference point. Every
other clip is measured the same way and assigned a gain that places it at a
deliberate, stated offset from that anchor.

The output is a **baked gain table asset** the game reads at runtime. Source audio
files are never modified.

## 3. Scope

### In scope (v1)
- New UPM package with editor window, loudness analysis, and runtime lookup
- LUFS (ITU-R BS.1770-4) perceived-loudness measurement
- Anchor selection, category offsets, per-clip trim
- Preview-with-gain and A/B-against-anchor auditioning
- Outlier warnings and true-peak diagnostics
- Cached incremental re-analysis
- Bulk category assignment, sorting, filtering

### Out of scope (v1)
- Modifying source audio files or import settings
- AudioMixer group automation
- Runtime/adaptive ducking, sidechain, or dynamic mixing
- Multi-anchor or per-scene anchors
- Loudness range (LRA) reporting

## 4. Architecture

```
AudioBalanceProfile  (Editor asset — folders, anchor clip, categories + offsets)
   │
   ├→ LoudnessAnalyzer   decode clip → LufsMeter → LoudnessCache (guid + size + mtime)
   │
   ├→ GainSolver         raw = (anchorLufs + categoryOffset + trim) − measuredLufs
   │
   ├→ HeadroomNormalizer final = raw − max(raw)      ← guarantees every gain ≤ 0 dB
   │
   └→ AudioGainTable    (Runtime asset — clip → gainDb)
          │
          └→ game: source.PlayBalanced(clip, table, userVolume)
```

The profile is **editor-only by design**. The table has final numbers baked into
it, so nothing at runtime needs to know what a category is.

### Assemblies

| Assembly | Contents |
|---|---|
| `Hoppa.AudioBalance.Runtime` | `AudioGainTable`, `AudioGainTableExtensions` |
| `Hoppa.AudioBalance.Editor` | Window, analyzer, DSP, cache, solver, preview |
| `Hoppa.AudioBalance.Editor.Tests` | EditMode tests |

Naming mirrors the existing `Hoppa.LevelEditor.Core.*` convention.

## 5. Components

### 5.1 `LufsMeter` (Editor, pure C#)

No `UnityEngine` dependency — takes `float[] samples, int channels, int sampleRate`
and returns a measurement. This is what makes it testable in milliseconds.

Implements ITU-R BS.1770-4:

1. **K-weighting** — two cascaded biquads per channel: a high-shelf stage and an
   RLB high-pass stage.
2. **Blocking** — 400 ms blocks at 75% overlap (100 ms step).
3. **Block loudness** — `l_j = −0.691 + 10·log10(Σ_ch G_ch · z_ij)` where `z` is
   the mean square of the filtered samples in that block.
4. **Channel weights** — `G = 1.0` for L/R/C, `1.41` for surround channels.
5. **Absolute gate** — discard blocks with `l_j ≤ −70 LUFS`.
6. **Relative gate** — discard blocks below `(loudness of absolute-gated set) − 10 LU`.
7. **Integrated loudness** — computed over blocks surviving both gates.

**Sample-rate handling.** The published coefficient table is defined at 48 kHz.
Coefficients are derived parametrically from the clip's actual sample rate rather
than resampling the audio. A test asserts that deriving at 48 000 Hz reproduces the
published constants to within `1e-9`.

**Measure modes:**

| Mode | Definition | Default for |
|---|---|---|
| `Integrated` | Full gated BS.1770 integrated loudness | Music |
| `ShortTermMax` | Max over sliding 3 s windows (100 ms step), ungated | SFX, UI |

`ShortTermMax` exists because gating discards a one-shot's decay tail, causing
short punchy SFX to under-read. For a clip shorter than 3 s — most SFX — the
window collapses to the whole clip, which is exactly the desired behaviour.

**Short-clip edge case.** A clip shorter than one 400 ms block produces zero
complete blocks, and a naive implementation returns `−Infinity`, which then
propagates into the gain as `NaN`. Clips under one block length are measured as a
single ungated block spanning the full clip.

**Silence.** An all-zero clip has no defined loudness. It is reported as
`Silent` — not a number — and is excluded from analysis and from the headroom
calculation, with a warning row in the UI.

### 5.2 `TruePeakMeter` (Editor, pure C#)

Sample peak plus 4× oversampled true peak, in dBTP. Diagnostic only in v1 — see
§6 for why clipping cannot occur.

### 5.3 `ClipSampleReader` (Editor)

Wraps `AudioClip.LoadAudioData()` + `GetData()`.

Clips imported with **Streaming** load type return silence from `GetData`. Rather
than mutating the project's import settings behind the user's back, such clips are
reported as `Unanalyzable` with the message *"set Load Type to Decompress On Load"*.
Most SFX are `.wav`, so this should be uncommon.

### 5.4 `LoudnessCache` (Editor)

JSON at `Library/HoppaAudioBalance/loudness-cache.json` — `Library/` because the
cache is regenerable and must not be committed.

Key: `(assetGuid, fileLengthBytes, lastWriteTimeUtc.Ticks)`. Cheaper than hashing
file contents; the tradeoff is that a content-preserving touch causes an
unnecessary re-analysis, which is harmless.

### 5.5 `GainSolver` (Editor, pure C#)

```
rawGainDb(clip)   = (anchorLufs + categoryOffsetDb(clip.category) + trimDb(clip))
                    − measuredLufs(clip)
finalGainDb(clip) = rawGainDb(clip) − max(rawGainDb over all analyzed clips)
```

No special case for the anchor: if the anchor sits in a category with offset 0 and
has no trim, the formula yields 0 dB for it naturally.

**Outlier flag:** `|rawGainDb| > 12` — in practice this means a broken, silent, or
wrong-format asset far more often than a genuinely quiet sound.

### 5.6 `AudioGainTable` (Runtime)

```csharp
public sealed class AudioGainTable : ScriptableObject
{
    public float GetGainDb(AudioClip clip);  // 0f if unknown
    public float GetGain(AudioClip clip);    // linear, 1f if unknown
}

public static class AudioGainTableExtensions
{
    public static void PlayBalanced(this AudioSource source, AudioClip clip,
                                    AudioGainTable table, float userVolume = 1f);
    public static void PlayOneShotBalanced(this AudioSource source, AudioClip clip,
                                           AudioGainTable table, float userVolume = 1f);
}
```

Backing store is a serialized array; a `Dictionary` is built lazily on first
lookup. An unknown clip returns unity gain — a missing entry must never silence a
sound — and logs a warning once per clip in the editor only.

### 5.7 `AudioBalanceWindow` (Editor)

`Window ▸ Hoppa ▸ Audio Balance`. IMGUI with absolute `GUI.*` rects, matching
`LevelEditorWindow`.

> Rect-based layout is deliberate. `DrawProfileSelector` in the level editor was
> the lone `GUILayout` island and produced a layout-stack corruption crash when
> its buttons opened modals. Absolute rects avoid that whole class of bug.

| Region | Contents |
|---|---|
| Toolbar | Profile field · Scan Folders · **Analyze** · **Write Table** |
| Categories | Name + offset dB. Seed defaults: Music 0, SFX +3, UI −6 |
| Clips | Name · category dropdown · measured LUFS · final gain dB · trim slider · ▶ preview-with-gain · A/B vs anchor · ⚠ status icon |

Clip list is sortable (name / loudness / gain / category) and filterable by name.
Multi-select supports bulk category assignment.

### 5.8 `AudioPreviewPlayer` (Editor)

Auditions a clip with its computed gain applied, and A/B against the anchor bed so
the user judges how a sound *sits in the mix* rather than trusting a number. Uses
the editor's preview audio path; stops cleanly on window close and on domain
reload.

## 6. Headroom: downward-only normalization

`AudioSource.volume` is hard-capped at `1.0`. A clip that needs **+6 dB simply
cannot receive it** — the request silently does nothing, and the balance the user
sees in the table is not the balance they hear.

Subtracting the maximum raw gain from every raw gain pins the loudest clip at
exactly 0 dB and pushes everything else below it. Relative balance is preserved
exactly, every gain becomes achievable, and — because every gain is now ≤ 1.0
linear and source material is assumed ≤ 0 dBFS — clipping becomes structurally
impossible rather than merely warned about.

**Consequence the user accepted:** overall output is quieter than the raw source
material. This is compensated once, on the master mixer, rather than per clip.

## 7. Testing

DSP is pure C# with generated signals, so no binary audio fixtures are committed.

**`LufsMeter`**
- 1 kHz sine at −23 dBFS reads −23.0 LUFS ±0.1 (BS.1770 reference case)
- 1 kHz sine at −20 dBFS reads −20.0 LUFS ±0.1
- K-weighting coefficients derived at 48 kHz match published constants within `1e-9`
- Coefficients derived at 44.1 kHz and 22.05 kHz produce stable, finite output
- Absolute gate excludes a −80 dBFS passage from the integrated result
- Relative gate excludes a quiet passage sitting more than 10 LU below the loud one
- Stereo surround channels receive the 1.41 weight
- Clip shorter than 400 ms returns a finite value via the single-block path
- All-zero clip reports `Silent`, never `−Infinity` or `NaN`
- `ShortTermMax` on a clip under 3 s equals the ungated whole-clip measurement
- `ShortTermMax` exceeds `Integrated` for a short one-shot with a long decay tail

**`GainSolver`**
- Anchor in a 0-offset category with no trim resolves to 0 dB
- Category offset shifts a clip's gain by exactly that many dB
- Per-clip trim stacks additively on top of the category offset
- After normalization, the maximum final gain is exactly 0 dB
- After normalization, every final gain is ≤ 0 dB
- Relative spacing between any two clips is identical before and after normalization
- Silent and unanalyzable clips are excluded from the max-gain calculation
- Outlier flag triggers above 12 dB and not below

**`AudioGainTable`**
- Round-trips through serialization
- Unknown clip returns unity gain, not zero
- `null` clip is handled without throwing
- dB ↔ linear conversion is correct at 0 / −6 / −20 dB

**`LoudnessCache`**
- Hit on unchanged file; miss when size or mtime changes
- Corrupt or unreadable cache file degrades to a full re-analysis, never throws

## 8. Deployment

New package means a **second manifest pin per consuming game**, alongside the
existing `com.hoppa.leveleditor.core` entry. No game is wired up as part of v1 —
adoption is a separate, deliberate step once the tool has proven itself on a real
sound library.

## 9. Open items

None blocking. Deferred by choice: AudioMixer integration, multi-anchor support,
and LRA reporting — all revisitable once v1 is in real use.
