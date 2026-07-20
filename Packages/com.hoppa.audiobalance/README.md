# Hoppa Audio Balance

Anchor-relative loudness balancing for Unity audio.

Every clip is measured with the same perceived-loudness metric (LUFS, ITU-R BS.1770-4)
and assigned a gain that places it at a deliberate offset from the other clips. Those
offsets come from **categories** (Music / SFX / UI), plus an optional per-clip trim.
The result is baked into an `AudioGainTable` asset.

You also pick one clip as the **anchor** — normally the background music that runs
during levels. The anchor is the reference for the *outlier* check and a sanity
readout of where your material sits. It does **not** set relative placement, and
**changing it will not move the Gain column** — see below.

**Source audio files are never modified.** The window only ever reads them. Nothing
reaches your game until you press **Write Table**, and that writes exactly one asset.

## Quick start

1. `Assets ▸ Create ▸ Hoppa ▸ Audio ▸ Audio Balance Profile`
2. `Assets ▸ Create ▸ Hoppa ▸ Audio ▸ Audio Gain Table`
3. `Window ▸ Hoppa ▸ Audio Balance` — assign both, then **Add Folder…**
4. Set the **Anchor** clip, press **Analyze**, then **Write Table**

## Using the table at runtime

```csharp
[SerializeField] private AudioGainTable _gains;
[SerializeField] private AudioSource _source;

_source.PlayOneShotBalanced(clip, _gains, userVolume: _sfxSlider.value);
```

An unknown clip resolves to unity gain, so a missing entry never silences a sound.

## Why everything gets quieter

`AudioSource.volume` is capped at 1.0, so a clip needing +6 dB cannot receive it.
Gains are therefore normalised downward: whichever clip needed the *most* gain — the
one **quietest** relative to its target — lands at exactly 0 dB, and every other clip
is attenuated below it. The clip that is *loudest* relative to its target is attenuated
the most.

Two consequences worth stating plainly:

- **Every gain is at or below 0 dB.** Clipping is therefore structurally impossible —
  the tool cannot make anything louder than the source file already is.
- **The whole mix ends up quieter.** Relative balance is preserved exactly, so the fix
  is to raise your master mixer **once** to compensate. Do not compensate per-clip;
  that is what this tool just finished undoing.

## What the anchor does, and does not, do

The anchor is the reference for the **outlier** marker and a sanity readout. It does
**not** determine relative placement — category offsets do that.

This follows from the arithmetic. Each clip's raw gain is
`anchor + categoryOffset + trim − measured`, and the final gain subtracts the largest
raw gain from every raw gain. The anchor term appears in both, so it cancels exactly:
the Gain column is provably independent of the anchor's measured loudness.

So if you swap the anchor for a louder or quieter track, **the Gain column will not
move**. That is correct behaviour, not a broken binding. What *will* change is which
clips get flagged as outliers, since that check measures distance from the anchor.
To actually re-balance, change the category offsets or a per-clip trim.

If no anchor is set, outlier marking is switched off entirely — with no reference,
there is nothing to be an outlier from.

## Known limitations

- **Streaming clips cannot be measured.** Clips imported with the **Streaming** load
  type return silence from `GetData` and are reported as unanalyzable rather than
  having their import settings rewritten behind your back. Set their Load Type to
  *Decompress On Load* to include them.
- **The loudness cache silently no-ops for clips in non-embedded packages.** The cache
  key is built from a path under the project root, so clips living in a package
  resolved from a registry or a git URL never produce a valid key. They are re-decoded
  on every window open, with no warning and no incorrect results — just slower. Audio
  under `Assets/`, which is where game audio lives, is unaffected.
- **Clip preview depends on an internal Unity API.** Playback reflects into
  `UnityEditor.AudioUtil`, because `AudioSource.Play()` produces no audio outside Play
  Mode. If a future Unity version moves that API, preview degrades to a single
  `[AudioBalance]` console warning and everything else — analysis, gains, Write Table —
  keeps working.
