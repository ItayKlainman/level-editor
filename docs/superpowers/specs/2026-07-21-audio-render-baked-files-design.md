# Audio Balance — render gain-baked files

**Date:** 2026-07-21
**Package:** `com.hoppa.audiobalance`
**Status:** design approved, plan not yet written
**Predecessor:** `2026-07-19-audio-balance-panel-design.md`

---

## 1. Why

The shipped Audio Balance panel is **non-destructive by design**: it measures loudness, solves
per-clip gains, and bakes them into an `AudioGainTable` asset. Source `.wav` files are never
touched, and nothing is balanced until a game applies the table at runtime.

Eliran (Bus Buddies) asked for something different:

> "Do it without OGG — I'll convert it later. But I want a new file with adjusted decibels."

So the deliverable is **rendered audio files with the gain already applied**, handed off for him to
encode to OGG himself. This supersedes the previously-considered "wire the game to the runtime gain
table" approach, which is now off the table — he wants files in hand, not runtime lookup.

### Non-goal: OGG encoding

Unity exposes no Vorbis encoder to editor scripts. Encoding OGG ourselves would mean a native
libvorbis P/Invoke or a managed encoder — per-platform binaries, licensing review, and its own test
surface, for roughly 3–4 days. **Eliran handles the conversion downstream**, so we render WAV and
stop there.

### Context: this is not a build-size win

Measured on Bus Buddies while scoping this work:

| | Size |
|---|---|
| All shipping audio (9 files in `Resources/`) | 1.76 MB |
| `.png` textures | 330 MB |
| `heart-spritesheet.png` (one file) | 46.5 MB |
| Total `Assets/` | 463 MB |

Audio is ~0.4% of assets. Every audio file — including the `.wav` sources — already ships as
Vorbis (`compressionFormat: 1`, `quality: 1`), because Unity re-encodes at build time regardless of
the container on disk. **This feature is about loudness balance, not about shipping a lighter
game.** If build size is the goal, textures are the lever; that is a separate piece of work.

---

## 2. Deliverable

A timestamped export folder, outside the Unity asset pipeline:

```
StagingExports/AudioBalance/2026-07-21_1432/
    _BUB/Resources/Audio/Music/MainMenu.wav      ← -6.2 dB applied
    _BUB/Resources/Audio/UI/FullBoard.wav        ← -2.1 dB applied
    _BUB/Resources/Audio/UI/FlyCoins.wav         ← -4.8 dB applied
    manifest.txt
```

Workflow: Analyze + solve in the panel (unchanged) → **Render Files…** → hand Eliran the folder →
he converts to OGG and places them in the game.

---

## 3. Decisions

### 3.1 Export folder, not in-place replacement

Rendering never touches `Assets/`. No source asset is opened for write; no GUIDs, importer
settings, or scene references change. A bad render is deleted, not undone.

### 3.2 Gain stacking is structurally impossible

Every solved gain is **attenuation** (≤ 0 dB, a consequence of `AudioSource.volume` capping at
1.0 — see the predecessor spec). Rendering *on top of* an already-rendered file would compound the
attenuation silently, with no way to detect it from the audio.

This cannot happen. `LoudnessAnalyzer.FindClips` discovers clips via
`AssetDatabase.IsValidFolder`, which only resolves folders Unity imports (`Assets/`, `Packages/`).
`StagingExports/` sits at repo root, **outside the asset pipeline** — Unity never imports it, so
rendered output is invisible to the panel and cannot be re-analyzed or re-rendered.

> This package has twice paid for enforcing a contract with documentation (the cache-key
> derivation, the category-name setter). The guarantee here is structural: there is no code path
> that can express the mistake.

### 3.3 24-bit PCM output

Gains are attenuation-only, so quantization loss is a live concern: a clip attenuated −20 dB loses
~3.3 bits. In 16-bit that leaves an effective ~12.7-bit signal *before* Eliran's Vorbis encode.
24-bit absorbs it with room to spare and is universally supported by conversion tools.

32-bit float was considered — zero quantization, simplest code — but Eliran's converter is an
unknown and some tools reject float WAV. 24-bit is the safer intermediate-master convention.

Sample rate and channel count carry through from the source clip unchanged. **Only amplitude
changes.**

### 3.4 Render every analyzed clip; flag lossy sources

Eliran gets one complete folder to convert, with no need to work out which originals to carry over
unchanged.

`ClipSampleReader` reads the **decoded** clip. For `.wav` sources that is lossless — decoded equals
original. For `.mp3` / `.ogg` sources a generation is already lost before we touch them, and the
downstream OGG encode adds another. In the BB shipping set this affects 5 of 9 files.

Rendering them anyway is the right trade: an unbalanced clip is a worse problem than a re-encoded
one. But the loss must be **visible**, so the manifest records each source's original format. The
real fix is obtaining original WAV masters, which is a content-pipeline matter, not a code one.

### 3.5 Mirror the source folder structure

Flat output is unsafe with real data: BB has `MainMenu.ogg` **and** `Generic1.wav` each existing in
two locations (`_BUB/Audio/…` and `_BUB/Resources/Audio/…`). A flat folder would silently overwrite.

Mirroring source paths makes collisions impossible by construction and shows Eliran exactly where
each file belongs when he puts them back.

### 3.6 Gitignore the export root

`StagingExports/AudioBalance/` is added to `.gitignore`. Renders are multi-MB, timestamped, and
accumulate per run, while the pristine source of truth is already tracked.

> **⚠️ Revisit if BB testing gets strange.** Bus Buddies is a **separate repository**
> (`E:/Projects/Hoppa/BusBuddies`). Gitignored artifacts do not travel between the two projects or
> between machines. If rendered files appear to vanish, fail to sync, or are missing when verifying
> against BB, **the gitignore is the first thing to check** — the fix is either committing a
> specific export or copying the folder across manually. Recorded here deliberately so this is not
> re-diagnosed from scratch.

---

## 4. Architecture

Follows the established package pattern: pure logic in focused classes, the window only wires a
button to them. `AudioBalanceWindow` is already 1289 lines and must not absorb this.

```
Editor/Render/WavWriter.cs            24-bit PCM WAV encoding (pure, no editor deps)
Editor/Render/RenderedAudioWriter.cs  orchestration + manifest
```

### Data flow

```
AudioBalanceRow (Clip + Gain.FinalGainDb)
  → ClipSampleReader.TryRead        [exists]  decoded interleaved PCM
  → PreviewClipFactory.Scale        [exists]  applies gain, clamps to [-1, 1]
  → WavWriter.Write24               [new]     RIFF writing modelled on AudioAssetFixture, at 24-bit
  → StagingExports/AudioBalance/<timestamp>/<mirrored path>.wav
```

Three of the four stages already exist and are tested. The DSP is not the work here — the
orchestration, path derivation, and skip semantics are.

### 4.1 `WavWriter`

`Write24(string absolutePath, float[] interleaved, int channels, int sampleRate)`

Same RIFF/`fmt `/`data` structure as `AudioAssetFixture.WriteWav`, with `bitsPerSample: 24`,
`blockAlign = channels * 3`, and samples written as 3-byte little-endian signed values. Pure and
unit-testable with no Unity editor dependency.

`WavWriter` is **new production code**, not a move. `AudioAssetFixture.WriteWav` stays where it is
and keeps writing 16-bit: it is a test tool whose job is producing importable fixtures, and
retargeting it at 24-bit would couple fixture convenience to production output format. The RIFF
header layout is the shared idea; the code is not shared.

### 4.2 `RenderedAudioWriter`

`Render(IReadOnlyList<AudioBalanceRow> rows, string outputRoot) → RenderReport`

Per row: skip guard → read → scale → derive mirrored path → write → record outcome. Returns a
report the window surfaces (written count, skipped count, output path).

### 4.3 Manifest

`manifest.txt` at the export root, one line per clip: source asset path, applied gain in dB, source
format, and skip reason where applicable. Plain text — it is read by a human deciding what to
convert, not parsed by tooling.

---

## 5. Skip semantics and error handling

**The guard is `row.Gain.Clip == null` — never `row.Gain.Status == ClipStatus.Ok`.**
`ClipStatus.Ok` is `0`, so an unsolved `default(GainResult)` reports `Ok` and a status check is
vacuous by construction. This exact trap already produced a test that passed against a broken build
in this package; `GainTableWriter` carries the same guard for the same reason.

Rows are skipped when the clip is null, the analysis status is not `Ok`, or no gain was solved.

**A clip that fails to read does not abort the batch.** Its reason is recorded in the manifest and
the render continues — one bad clip should not cost a 20-file hand-off.
`ClipSampleReader`'s existing `StreamingError` and `LoadPendingError` messages are already
actionable and surface verbatim.

**Output folders are timestamped to the minute** (`yyyy-MM-dd_HHmm`), so a re-render never clobbers
a hand-off already given to Eliran. A date alone is not enough — iterating on category offsets
means several renders in one afternoon, and a date-only folder would overwrite the export already
sitting in Eliran's hands. In the rare case the same minute is hit twice, the render **fails with
the existing path named** rather than merging into a populated folder, since a half-overwritten
export is worse than no export.

---

## 6. Testing

- `WavWriter`: header field correctness, `blockAlign`/`byteRate`/`dataSize` for 24-bit, sample
  encoding at known values (full scale, silence, negative full scale), and a round-trip read-back
- Mirrored-path derivation, **explicitly covering the real `MainMenu.ogg` two-location collision**
- Skip semantics: null `Gain.Clip`, non-`Ok` analysis, unreadable clip, empty row set
- Batch continues past a failing clip
- Manifest content, including lossy-source flagging
- End-to-end through `AudioAssetFixture`, which already writes real WAVs into `Assets/`, imports
  them, and cleans up in teardown

---

## 7. Out of scope

- OGG / Vorbis encoding (Eliran's step)
- Wiring any game to the runtime `AudioGainTable` — superseded by this approach
- Texture / build-size work (see §1; separate initiative)
- Changing any `AudioImporter` settings in Bus Buddies
- Cleaning up BB's 6 MB of unreferenced audio or the dead `CarEngineStartold.mp3` — found while
  scoping, deliberately left alone
