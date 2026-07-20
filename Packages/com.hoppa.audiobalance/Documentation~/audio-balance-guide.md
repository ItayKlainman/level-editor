# Audio Balance — Designer Guide

## What this solves

The `volume` number on an `AudioSource` says nothing about how loud a clip actually
sounds. A dense bass-heavy loop and a thin UI blip can both sit at `1.0` and be 15 dB
apart to the ear. This tool measures what your ears hear and does the arithmetic.

**It never touches your audio files.** Every `.wav` in your project is read and left
exactly as it was. The only thing that changes is one `AudioGainTable` asset, and only
when you press **Write Table**.

## Workflow

1. **Point it at your audio.** Add one or more folders in the toolbar. They are
   stored project-relative, so they work on every teammate's checkout.
2. **Choose the anchor.** Use the background music that plays during levels. The anchor
   is the reference for the *outlier* check and a sanity readout — it is **not** what
   positions your other sounds, and changing it will not move the Gain column. See
   *Three things worth knowing* below.
3. **Assign categories.** This is what actually sets relative placement. Multi-select
   rows and use *Set Category*. Defaults:

   | Category | Offset | Measure mode | Meaning |
   |---|---|---|---|
   | Music | 0 dB | Integrated | The reference level |
   | SFX | +3 dB | Momentary max | Sits above the music bed |
   | UI | −6 dB | Momentary max | Sits below it |

   Changing a clip's category re-measures it, because each category carries its own
   measure mode. That is fast — unchanged clips come straight from the cache.

   If the button reads *Set Category (12, 10 hidden)*, your filter is hiding part of
   the selection. The assign still applies to all 12. That is deliberate — typing in
   the filter box does not throw away what you already picked — but it is worth
   reading before you click.

4. **Analyze.** Measurements are cached, so later runs only re-measure changed clips.
5. **Listen.** *Play* auditions a clip with its gain applied; *A/B* plays it mixed over
   the anchor bed. Trust your ears over the numbers. (Both buttons sit at the right-hand
   end of the row — scroll the table sideways if the window is narrow.)
6. **Trim what's still wrong.** The per-clip slider stacks on top of the category
   offset. Reach for it only after the category offset is right.
7. **Write Table.** This is the only step that produces anything your game will load.

## Reading the warnings

| Marker | Meaning |
|---|---|
| `outlier` | Gain is more than 12 dB from the target. Usually a broken, near-silent, or wrong-format asset rather than a genuinely quiet sound. |
| `silent` | No measurable signal. Check the file actually contains audio. |
| `!` | Could not be read. Hover for the reason — most often a Streaming load type. |
| `(unknown: X)` in the category dropdown | The clip points at a category `X` that no longer exists. Pick a real one to fix it. |

## Why the measure modes differ

Integrated loudness gates out quiet passages, which is right for a music loop but
makes a short one-shot under-read — its decay tail gets discarded and the sound
lands too quiet against the bed. Momentary max takes the loudest 400 ms window
instead, so percussive SFX are measured on their impact.

## Three things worth knowing

- **Everything gets quieter, on purpose.** `AudioSource.volume` caps at 1.0, so
  positive gain is unachievable. Whichever clip needed the *most* gain — the one
  quietest against its target — is pinned at 0 dB, and everything else sits below it.
  Every gain is therefore ≤ 0 dB, which also means this tool can never introduce
  clipping. Raise your master mixer **once** to compensate for the overall drop.
- **Changing the anchor will not move the Gain column.** This surprises people, so it
  is worth being blunt about: the anchor cancels out of the gain arithmetic entirely.
  Its live effects are the *outlier* marker and the LUFS readout beside it. If you want
  to move sounds relative to each other, change a **category offset** or a **trim** —
  those are the controls that do it. (With no anchor set, outlier marking is off
  completely, since there is no reference to be an outlier from.)
- **Re-run after replacing a sound.** The gain of every *other* clip can shift, because
  the clip needing the most gain defines the 0 dB ceiling for the whole set.
