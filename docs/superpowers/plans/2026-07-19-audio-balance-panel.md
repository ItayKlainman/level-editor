# Audio Balance Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a new `com.hoppa.audiobalance` UPM package whose editor window measures every audio clip's perceived loudness (LUFS), balances it against one anchor clip, and bakes per-clip gains into a runtime table asset.

**Architecture:** Pure-C# DSP (K-weighting biquads → gated block loudness) lives in the Editor assembly with no `UnityEngine` dependency, so it unit-tests in milliseconds against generated signals. An editor-only `AudioBalanceProfile` holds authoring state (folders, anchor, categories); the runtime assembly ships only the baked `AudioGainTable` and its lookup extensions.

**Tech Stack:** Unity 2022.3, C#, IMGUI (absolute `GUI.*` rects), NUnit EditMode tests, `JsonUtility` for the cache (keeps the package dependency-free).

**Spec:** `docs/superpowers/specs/2026-07-19-audio-balance-panel-design.md`

## Global Constraints

- Package name `com.hoppa.audiobalance`, version `0.1.0`, `"unity": "2022.3"`.
- **Zero package dependencies.** Use `JsonUtility`, not Newtonsoft.
- Assembly names: `Hoppa.AudioBalance.Runtime`, `Hoppa.AudioBalance.Editor`, `Hoppa.AudioBalance.Editor.Tests`. Namespaces: `Hoppa.AudioBalance`, `Hoppa.AudioBalance.Editor`, `Hoppa.AudioBalance.Editor.Tests`.
- **Commit `.meta` files** for every new file and folder. A missing `.meta` breaks the package for every other checkout.
- **No absolute paths in committed assets.** Folder references are stored project-relative (`Assets/...`).
- All UI uses absolute `GUI.*` rects, never `GUILayout`. (`GUILayout` islands that open modals caused a layout-stack corruption crash in `LevelEditorWindow`.)
- No game is wired up in v1. Adoption by Bus Buddies is a separate follow-up.
- Run tests with the `tests-run` MCP tool: `testMode: EditMode`, `assemblyNames: ["Hoppa.AudioBalance.Editor.Tests"]`. Call `assets-refresh` first when new `.cs` files were added outside Unity.

---

### Task 1: Package scaffold + runtime gain table

**Files:**
- Create: `Packages/com.hoppa.audiobalance/package.json`
- Create: `Packages/com.hoppa.audiobalance/Runtime/Hoppa.AudioBalance.Runtime.asmdef`
- Create: `Packages/com.hoppa.audiobalance/Runtime/AudioGainMath.cs`
- Create: `Packages/com.hoppa.audiobalance/Runtime/AudioGainTable.cs`
- Create: `Packages/com.hoppa.audiobalance/Runtime/AudioGainTableExtensions.cs`
- Create: `Packages/com.hoppa.audiobalance/Tests/Editor/Hoppa.AudioBalance.Editor.Tests.asmdef`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/AudioGainTableTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AudioGainMath.LinearFromDb(float) -> float`, `AudioGainMath.DbFromLinear(float) -> float`, `AudioGainTable.GetGainDb(AudioClip) -> float`, `AudioGainTable.GetGain(AudioClip) -> float`, `AudioGainTable.SetEntries(IEnumerable<AudioGainTable.Entry>)`, `AudioGainTable.Entry { AudioClip Clip; float GainDb; }`.

- [ ] **Step 1: Create the package manifest**

`Packages/com.hoppa.audiobalance/package.json`:

```json
{
  "name": "com.hoppa.audiobalance",
  "version": "0.1.0",
  "displayName": "Hoppa Audio Balance",
  "description": "Anchor-relative loudness balancing for Unity audio. Measures perceived loudness (LUFS, ITU-R BS.1770-4) of every clip, balances them against one anchor clip via category offsets, and bakes per-clip gains into a runtime table asset. Source audio files are never modified.",
  "unity": "2022.3",
  "keywords": [
    "audio",
    "loudness",
    "lufs",
    "mixing",
    "hoppa"
  ],
  "author": {
    "name": "Hoppa"
  }
}
```

- [ ] **Step 2: Create the two assembly definitions**

`Packages/com.hoppa.audiobalance/Runtime/Hoppa.AudioBalance.Runtime.asmdef`:

```json
{
    "name": "Hoppa.AudioBalance.Runtime",
    "rootNamespace": "Hoppa.AudioBalance",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Packages/com.hoppa.audiobalance/Tests/Editor/Hoppa.AudioBalance.Editor.Tests.asmdef`:

```json
{
    "name": "Hoppa.AudioBalance.Editor.Tests",
    "rootNamespace": "Hoppa.AudioBalance.Editor.Tests",
    "references": [
        "Hoppa.AudioBalance.Runtime",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

> The test asmdef gains a `Hoppa.AudioBalance.Editor` reference in Task 2. It is omitted now because that assembly does not exist yet and Unity errors on unresolved references.

- [ ] **Step 3: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/AudioGainTableTests.cs`:

```csharp
using System.Collections.Generic;
using Hoppa.AudioBalance;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class AudioGainTableTests
    {
        private static AudioClip MakeClip(string name)
        {
            return AudioClip.Create(name, 128, 1, 44100, false);
        }

        [Test]
        public void LinearFromDb_IsCorrectAtKnownPoints()
        {
            Assert.AreEqual(1.0f, AudioGainMath.LinearFromDb(0f), 1e-4f);
            Assert.AreEqual(0.5012f, AudioGainMath.LinearFromDb(-6f), 1e-3f);
            Assert.AreEqual(0.1f, AudioGainMath.LinearFromDb(-20f), 1e-4f);
        }

        [Test]
        public void DbFromLinear_RoundTripsWithLinearFromDb()
        {
            Assert.AreEqual(-13.5f, AudioGainMath.DbFromLinear(AudioGainMath.LinearFromDb(-13.5f)), 1e-3f);
        }

        [Test]
        public void DbFromLinear_ClampsAtZeroInsteadOfReturningNegativeInfinity()
        {
            Assert.AreEqual(AudioGainMath.MinDb, AudioGainMath.DbFromLinear(0f), 1e-4f);
        }

        [Test]
        public void GetGainDb_ReturnsStoredValue()
        {
            var clip = MakeClip("stored");
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = clip, GainDb = -7.5f }
            });

            Assert.AreEqual(-7.5f, table.GetGainDb(clip), 1e-4f);
        }

        [Test]
        public void GetGain_ReturnsUnityGainForUnknownClip()
        {
            var known = MakeClip("known");
            var unknown = MakeClip("unknown");
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = known, GainDb = -12f }
            });

            Assert.AreEqual(1f, table.GetGain(unknown), 1e-4f,
                "An unknown clip must never be silenced by a missing table entry.");
        }

        [Test]
        public void GetGainDb_HandlesNullClipWithoutThrowing()
        {
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            Assert.AreEqual(0f, table.GetGainDb(null), 1e-4f);
        }

        [Test]
        public void SetEntries_ReplacesPreviousLookup()
        {
            var clip = MakeClip("replaced");
            var table = ScriptableObject.CreateInstance<AudioGainTable>();
            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = clip, GainDb = -3f }
            });
            Assert.AreEqual(-3f, table.GetGainDb(clip), 1e-4f);

            table.SetEntries(new List<AudioGainTable.Entry>
            {
                new AudioGainTable.Entry { Clip = clip, GainDb = -9f }
            });
            Assert.AreEqual(-9f, table.GetGainDb(clip), 1e-4f,
                "The cached lookup must be invalidated when entries are replaced.");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run` with `testMode: EditMode`, `assemblyNames: ["Hoppa.AudioBalance.Editor.Tests"]`.
Expected: compile errors — `AudioGainMath` and `AudioGainTable` do not exist.

- [ ] **Step 5: Implement `AudioGainMath`**

`Packages/com.hoppa.audiobalance/Runtime/AudioGainMath.cs`:

```csharp
using UnityEngine;

namespace Hoppa.AudioBalance
{
    /// <summary>Decibel/linear conversions shared by the editor tooling and the runtime lookup.</summary>
    public static class AudioGainMath
    {
        /// <summary>Floor reported instead of negative infinity for a zero-amplitude signal.</summary>
        public const float MinDb = -80f;

        public static float LinearFromDb(float db)
        {
            return Mathf.Pow(10f, db / 20f);
        }

        public static float DbFromLinear(float linear)
        {
            return linear <= 0f ? MinDb : 20f * Mathf.Log10(linear);
        }
    }
}
```

- [ ] **Step 6: Implement `AudioGainTable`**

`Packages/com.hoppa.audiobalance/Runtime/AudioGainTable.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.AudioBalance
{
    /// <summary>
    /// Baked output of the Audio Balance window: a per-clip gain in decibels, relative to
    /// the anchor clip. Every gain is at or below 0 dB (see the headroom pass in the editor
    /// solver), so applying one can never push a source past AudioSource.volume's 1.0 cap.
    /// </summary>
    [CreateAssetMenu(menuName = "Hoppa/Audio/Audio Gain Table", fileName = "AudioGainTable")]
    public sealed class AudioGainTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public AudioClip Clip;
            public float GainDb;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        private Dictionary<AudioClip, float> _lookup;

        public IReadOnlyList<Entry> Entries => _entries;

        public void SetEntries(IEnumerable<Entry> entries)
        {
            _entries = entries == null ? Array.Empty<Entry>() : new List<Entry>(entries).ToArray();
            _lookup = null;
        }

        /// <summary>Gain in dB for the clip, or 0 dB (unity) when the clip is not in the table.</summary>
        public float GetGainDb(AudioClip clip)
        {
            if (clip == null)
            {
                return 0f;
            }

            EnsureLookup();
            return _lookup.TryGetValue(clip, out var db) ? db : 0f;
        }

        /// <summary>Linear multiplier for the clip, or 1.0 when the clip is not in the table.</summary>
        public float GetGain(AudioClip clip)
        {
            return AudioGainMath.LinearFromDb(GetGainDb(clip));
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<AudioClip, float>(_entries.Length);
            foreach (var entry in _entries)
            {
                if (entry.Clip != null)
                {
                    _lookup[entry.Clip] = entry.GainDb;
                }
            }
        }

        private void OnEnable()
        {
            _lookup = null;
        }

        private void OnValidate()
        {
            _lookup = null;
        }
    }
}
```

- [ ] **Step 7: Implement `AudioGainTableExtensions`**

`Packages/com.hoppa.audiobalance/Runtime/AudioGainTableExtensions.cs`:

```csharp
using UnityEngine;

namespace Hoppa.AudioBalance
{
    /// <summary>Play-sound helpers that fold the baked gain into AudioSource.volume.</summary>
    public static class AudioGainTableExtensions
    {
        public static void PlayBalanced(this AudioSource source, AudioClip clip,
            AudioGainTable table, float userVolume = 1f)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.clip = clip;
            source.volume = Resolve(table, clip, userVolume);
            source.Play();
        }

        public static void PlayOneShotBalanced(this AudioSource source, AudioClip clip,
            AudioGainTable table, float userVolume = 1f)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.PlayOneShot(clip, Resolve(table, clip, userVolume));
        }

        private static float Resolve(AudioGainTable table, AudioClip clip, float userVolume)
        {
            var gain = table != null ? table.GetGain(clip) : 1f;
            return Mathf.Clamp01(gain * userVolume);
        }
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run` with `testMode: EditMode`, `assemblyNames: ["Hoppa.AudioBalance.Editor.Tests"]`.
Expected: 7/7 PASS.

- [ ] **Step 9: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): scaffold com.hoppa.audiobalance + runtime gain table

New sibling UPM package. Runtime ships only the baked table and its
lookup extensions; an unknown clip resolves to unity gain so a missing
entry can never silence a sound.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: K-weighting filter

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Hoppa.AudioBalance.Editor.asmdef`
- Create: `Packages/com.hoppa.audiobalance/Editor/Dsp/BiquadCoefficients.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Dsp/KWeighting.cs`
- Modify: `Packages/com.hoppa.audiobalance/Tests/Editor/Hoppa.AudioBalance.Editor.Tests.asmdef` (add the Editor reference)
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/KWeightingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `BiquadCoefficients { double B0, B1, B2, A1, A2; }` (constructor takes them in that order), `KWeighting.HighShelf(int sampleRate) -> BiquadCoefficients`, `KWeighting.HighPass(int sampleRate) -> BiquadCoefficients`, `KWeighting.ApplyInPlace(double[] samples, BiquadCoefficients c)`.

- [ ] **Step 1: Add the Editor assembly definition**

`Packages/com.hoppa.audiobalance/Editor/Hoppa.AudioBalance.Editor.asmdef`:

```json
{
    "name": "Hoppa.AudioBalance.Editor",
    "rootNamespace": "Hoppa.AudioBalance.Editor",
    "references": [
        "Hoppa.AudioBalance.Runtime"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Add the Editor reference to the test assembly**

In `Packages/com.hoppa.audiobalance/Tests/Editor/Hoppa.AudioBalance.Editor.Tests.asmdef`, change the `references` array to:

```json
    "references": [
        "Hoppa.AudioBalance.Runtime",
        "Hoppa.AudioBalance.Editor",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
```

- [ ] **Step 3: Write the failing test**

The published BS.1770-4 coefficient table is defined at 48 kHz. Deriving parametrically and asserting it reproduces those constants proves the derivation, which is what lets us measure 44.1 kHz clips without resampling.

`Packages/com.hoppa.audiobalance/Tests/Editor/KWeightingTests.cs`:

```csharp
using System;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class KWeightingTests
    {
        private const double Tolerance = 1e-9;

        [Test]
        public void HighShelf_At48kHz_MatchesPublishedConstants()
        {
            var c = KWeighting.HighShelf(48000);

            Assert.AreEqual(1.53512485958697, c.B0, Tolerance);
            Assert.AreEqual(-2.69169618940638, c.B1, Tolerance);
            Assert.AreEqual(1.19839281085285, c.B2, Tolerance);
            Assert.AreEqual(-1.69065929318241, c.A1, Tolerance);
            Assert.AreEqual(0.73248077421585, c.A2, Tolerance);
        }

        [Test]
        public void HighPass_At48kHz_MatchesPublishedConstants()
        {
            var c = KWeighting.HighPass(48000);

            Assert.AreEqual(1.0, c.B0, Tolerance);
            Assert.AreEqual(-2.0, c.B1, Tolerance);
            Assert.AreEqual(1.0, c.B2, Tolerance);
            Assert.AreEqual(-1.99004745483398, c.A1, Tolerance);
            Assert.AreEqual(0.99007225036621, c.A2, Tolerance);
        }

        [TestCase(44100)]
        [TestCase(22050)]
        [TestCase(96000)]
        public void Coefficients_AtOtherRates_AreFinite(int sampleRate)
        {
            var shelf = KWeighting.HighShelf(sampleRate);
            var pass = KWeighting.HighPass(sampleRate);

            foreach (var v in new[] { shelf.B0, shelf.B1, shelf.B2, shelf.A1, shelf.A2,
                                      pass.B0, pass.B1, pass.B2, pass.A1, pass.A2 })
            {
                Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v));
            }
        }

        [Test]
        public void ApplyInPlace_GainAt1kHz_IsAboutPlusPoint691Db()
        {
            // The -0.691 offset in the loudness formula exists to cancel K-weighting's
            // gain at 1 kHz. Proving that gain here is what makes the LUFS calibration
            // test in LufsMeterTests meaningful.
            const int sampleRate = 48000;
            const int frames = sampleRate * 2;

            var signal = new double[frames];
            for (var i = 0; i < frames; i++)
            {
                signal[i] = Math.Sin(2.0 * Math.PI * 1000.0 * i / sampleRate);
            }

            var filtered = (double[])signal.Clone();
            KWeighting.ApplyInPlace(filtered, KWeighting.HighShelf(sampleRate));
            KWeighting.ApplyInPlace(filtered, KWeighting.HighPass(sampleRate));

            // Skip the first 0.5 s so the filters' transient response is excluded.
            var start = sampleRate / 2;
            var gainDb = 10.0 * Math.Log10(MeanSquare(filtered, start) / MeanSquare(signal, start));

            Assert.AreEqual(0.691, gainDb, 0.02);
        }

        private static double MeanSquare(double[] values, int start)
        {
            var sum = 0.0;
            for (var i = start; i < values.Length; i++)
            {
                sum += values[i] * values[i];
            }

            return sum / (values.Length - start);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `KWeighting` does not exist.

- [ ] **Step 5: Implement `BiquadCoefficients`**

`Packages/com.hoppa.audiobalance/Editor/Dsp/BiquadCoefficients.cs`:

```csharp
namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Normalised direct-form-I biquad coefficients (a0 folded into the rest).</summary>
    public readonly struct BiquadCoefficients
    {
        public readonly double B0;
        public readonly double B1;
        public readonly double B2;
        public readonly double A1;
        public readonly double A2;

        public BiquadCoefficients(double b0, double b1, double b2, double a1, double a2)
        {
            B0 = b0;
            B1 = b1;
            B2 = b2;
            A1 = a1;
            A2 = a2;
        }
    }
}
```

- [ ] **Step 6: Implement `KWeighting`**

`Packages/com.hoppa.audiobalance/Editor/Dsp/KWeighting.cs`:

```csharp
using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// ITU-R BS.1770-4 K-weighting: a high-shelf stage followed by an RLB high-pass stage.
    /// Coefficients are derived from the sample rate rather than resampling the audio, so a
    /// 44.1 kHz clip is measured natively. Deriving at 48 kHz reproduces the standard's
    /// published table (asserted in KWeightingTests).
    /// </summary>
    public static class KWeighting
    {
        public static BiquadCoefficients HighShelf(int sampleRate)
        {
            const double f0 = 1681.974450955533;
            const double gainDb = 3.999843853973347;
            const double q = 0.7071752369554196;

            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var vh = Math.Pow(10.0, gainDb / 20.0);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1.0 + k / q + k * k;

            return new BiquadCoefficients(
                (vh + vb * k / q + k * k) / a0,
                2.0 * (k * k - vh) / a0,
                (vh - vb * k / q + k * k) / a0,
                2.0 * (k * k - 1.0) / a0,
                (1.0 - k / q + k * k) / a0);
        }

        public static BiquadCoefficients HighPass(int sampleRate)
        {
            const double f0 = 38.13547087602444;
            const double q = 0.5003270373238773;

            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var denominator = 1.0 + k / q + k * k;

            return new BiquadCoefficients(
                1.0,
                -2.0,
                1.0,
                2.0 * (k * k - 1.0) / denominator,
                (1.0 - k / q + k * k) / denominator);
        }

        /// <summary>Runs a single-channel signal through the biquad in place.</summary>
        public static void ApplyInPlace(double[] samples, BiquadCoefficients c)
        {
            if (samples == null)
            {
                return;
            }

            double x1 = 0.0, x2 = 0.0, y1 = 0.0, y2 = 0.0;

            for (var i = 0; i < samples.Length; i++)
            {
                var x0 = samples[i];
                var y0 = c.B0 * x0 + c.B1 * x1 + c.B2 * x2 - c.A1 * y1 - c.A2 * y2;

                x2 = x1;
                x1 = x0;
                y2 = y1;
                y1 = y0;

                samples[i] = y0;
            }
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: 13/13 PASS (7 from Task 1 + 6 here).

- [ ] **Step 8: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): K-weighting biquads derived from sample rate

Deriving parametrically (rather than resampling to 48 kHz) lets 44.1 kHz
clips be measured natively. Test asserts the 48 kHz derivation reproduces
the BS.1770-4 published table to 1e-9, and that the filter's 1 kHz gain is
+0.691 dB -- the value the loudness formula's offset cancels.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Integrated loudness (blocks + gating)

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Dsp/LoudnessResult.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Dsp/LufsMeter.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/LufsMeterTests.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/SignalFactory.cs`

**Interfaces:**
- Consumes: `KWeighting.HighShelf`, `KWeighting.HighPass`, `KWeighting.ApplyInPlace`.
- Produces: `LoudnessResult { bool IsSilent; float Lufs; }` with `LoudnessResult.Silent` and `LoudnessResult.At(float)`; `LufsMeter.MeasureIntegrated(float[] interleaved, int channels, int sampleRate) -> LoudnessResult`; constants `LufsMeter.AbsoluteGateLufs = -70f`, `LufsMeter.RelativeGateLu = -10f`.

- [ ] **Step 1: Write the test signal factory**

`Packages/com.hoppa.audiobalance/Tests/Editor/SignalFactory.cs`:

```csharp
using System;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Generates interleaved test signals so the DSP tests need no committed audio fixtures.
    /// </summary>
    public static class SignalFactory
    {
        /// <summary>
        /// A 1 kHz sine whose PEAK amplitude is the given dBFS value, written to every channel.
        /// A stereo sine built this way measures exactly that dBFS value in LUFS: the sine's
        /// -3.01 dB crest factor and the +3.01 dB of summing two equal channels cancel.
        /// </summary>
        public static float[] Sine(double peakDbfs, double seconds, int channels, int sampleRate,
            double frequency = 1000.0)
        {
            var amplitude = Math.Pow(10.0, peakDbfs / 20.0);
            var frames = (int)Math.Round(seconds * sampleRate);
            var data = new float[frames * channels];

            for (var frame = 0; frame < frames; frame++)
            {
                var value = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * frame / sampleRate));
                for (var ch = 0; ch < channels; ch++)
                {
                    data[frame * channels + ch] = value;
                }
            }

            return data;
        }

        public static float[] Silence(double seconds, int channels, int sampleRate)
        {
            return new float[(int)Math.Round(seconds * sampleRate) * channels];
        }

        public static float[] Concat(params float[][] parts)
        {
            var length = 0;
            foreach (var part in parts)
            {
                length += part.Length;
            }

            var result = new float[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Array.Copy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }
    }
}
```

- [ ] **Step 2: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/LufsMeterTests.cs`:

```csharp
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class LufsMeterTests
    {
        private const int Rate = 48000;

        [Test]
        public void Integrated_StereoSineAtMinus23_ReadsMinus23Lufs()
        {
            var signal = SignalFactory.Sine(-23.0, 5.0, 2, Rate);

            var result = LufsMeter.MeasureIntegrated(signal, 2, Rate);

            Assert.IsFalse(result.IsSilent);
            Assert.AreEqual(-23.0f, result.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_StereoSineAtMinus20_ReadsMinus20Lufs()
        {
            var signal = SignalFactory.Sine(-20.0, 5.0, 2, Rate);

            var result = LufsMeter.MeasureIntegrated(signal, 2, Rate);

            Assert.AreEqual(-20.0f, result.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_MonoSine_ReadsThreeDbBelowTheStereoEquivalent()
        {
            var mono = LufsMeter.MeasureIntegrated(SignalFactory.Sine(-23.0, 5.0, 1, Rate), 1, Rate);

            Assert.AreEqual(-26.01f, mono.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_AtOtherSampleRates_MatchesThe48kResult()
        {
            var at441 = LufsMeter.MeasureIntegrated(SignalFactory.Sine(-23.0, 5.0, 2, 44100), 2, 44100);

            Assert.AreEqual(-23.0f, at441.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_AllZeroSignal_IsSilentNotNegativeInfinity()
        {
            var result = LufsMeter.MeasureIntegrated(SignalFactory.Silence(5.0, 2, Rate), 2, Rate);

            Assert.IsTrue(result.IsSilent);
            Assert.IsFalse(float.IsNegativeInfinity(result.Lufs));
            Assert.IsFalse(float.IsNaN(result.Lufs));
        }

        [Test]
        public void Integrated_AbsoluteGate_ExcludesAVeryQuietPassage()
        {
            // 3 s at -23 dBFS then 3 s at -85 dBFS. The quiet half is below the -70 LUFS
            // absolute gate, so it must not drag the result down.
            var loudOnly = LufsMeter.MeasureIntegrated(
                SignalFactory.Sine(-23.0, 3.0, 2, Rate), 2, Rate);

            var mixed = LufsMeter.MeasureIntegrated(
                SignalFactory.Concat(
                    SignalFactory.Sine(-23.0, 3.0, 2, Rate),
                    SignalFactory.Sine(-85.0, 3.0, 2, Rate)),
                2, Rate);

            Assert.AreEqual(loudOnly.Lufs, mixed.Lufs, 0.2f);
        }

        [Test]
        public void Integrated_RelativeGate_ExcludesAPassageMoreThan10LuDown()
        {
            // -23 dBFS then -45 dBFS: the quiet half clears the absolute gate but sits
            // more than 10 LU below the ungated loudness, so the relative gate drops it.
            var loudOnly = LufsMeter.MeasureIntegrated(
                SignalFactory.Sine(-23.0, 3.0, 2, Rate), 2, Rate);

            var mixed = LufsMeter.MeasureIntegrated(
                SignalFactory.Concat(
                    SignalFactory.Sine(-23.0, 3.0, 2, Rate),
                    SignalFactory.Sine(-45.0, 3.0, 2, Rate)),
                2, Rate);

            Assert.AreEqual(loudOnly.Lufs, mixed.Lufs, 0.5f);
        }

        [Test]
        public void Integrated_ClipShorterThanOneBlock_ReturnsAFiniteValue()
        {
            // 200 ms is shorter than the 400 ms block, so the block loop produces nothing
            // and a naive implementation returns -Infinity, which becomes NaN downstream.
            var signal = SignalFactory.Sine(-23.0, 0.2, 2, Rate);

            var result = LufsMeter.MeasureIntegrated(signal, 2, Rate);

            Assert.IsFalse(result.IsSilent);
            Assert.IsFalse(float.IsInfinity(result.Lufs) || float.IsNaN(result.Lufs));
            Assert.AreEqual(-23.0f, result.Lufs, 0.5f);
        }

        [Test]
        public void Integrated_EmptySignal_IsSilent()
        {
            Assert.IsTrue(LufsMeter.MeasureIntegrated(new float[0], 2, Rate).IsSilent);
        }

        [Test]
        public void Integrated_NullSignal_IsSilent()
        {
            Assert.IsTrue(LufsMeter.MeasureIntegrated(null, 2, Rate).IsSilent);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `LufsMeter` does not exist.

- [ ] **Step 4: Implement `LoudnessResult`**

`Packages/com.hoppa.audiobalance/Editor/Dsp/LoudnessResult.cs`:

```csharp
namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// A loudness measurement. Silence has no defined loudness, so it is reported as a
    /// distinct state rather than as -Infinity, which would become NaN once a gain is solved.
    /// </summary>
    public readonly struct LoudnessResult
    {
        public readonly bool IsSilent;
        public readonly float Lufs;

        private LoudnessResult(bool isSilent, float lufs)
        {
            IsSilent = isSilent;
            Lufs = lufs;
        }

        public static LoudnessResult Silent => new LoudnessResult(true, 0f);

        public static LoudnessResult At(float lufs) => new LoudnessResult(false, lufs);
    }
}
```

- [ ] **Step 5: Implement `LufsMeter.MeasureIntegrated`**

`Packages/com.hoppa.audiobalance/Editor/Dsp/LufsMeter.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// ITU-R BS.1770-4 loudness measurement. Pure C# with no UnityEngine dependency so it
    /// runs in unit tests against generated signals.
    /// </summary>
    public static class LufsMeter
    {
        public const float AbsoluteGateLufs = -70f;
        public const float RelativeGateLu = -10f;

        private const double LoudnessOffset = -0.691;
        private const double BlockSeconds = 0.4;
        private const double StepSeconds = 0.1;

        public static LoudnessResult MeasureIntegrated(float[] interleaved, int channels, int sampleRate)
        {
            var blocks = ComputeBlockPowers(interleaved, channels, sampleRate, BlockSeconds, StepSeconds);
            if (blocks.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var weights = ChannelWeights(channels);

            var aboveAbsolute = new List<double[]>();
            foreach (var block in blocks)
            {
                if (BlockLoudness(block, weights) > AbsoluteGateLufs)
                {
                    aboveAbsolute.Add(block);
                }
            }

            if (aboveAbsolute.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var relativeGate = BlockLoudness(MeanPerChannel(aboveAbsolute, channels), weights) + RelativeGateLu;

            var kept = new List<double[]>();
            foreach (var block in aboveAbsolute)
            {
                if (BlockLoudness(block, weights) > relativeGate)
                {
                    kept.Add(block);
                }
            }

            if (kept.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var loudness = BlockLoudness(MeanPerChannel(kept, channels), weights);
            return double.IsNegativeInfinity(loudness)
                ? LoudnessResult.Silent
                : LoudnessResult.At((float)loudness);
        }

        /// <summary>
        /// K-weights each channel, then returns the per-channel mean square of every block.
        /// A signal shorter than one block yields a single block spanning the whole signal --
        /// without this, short SFX produce no blocks at all.
        /// </summary>
        internal static List<double[]> ComputeBlockPowers(float[] interleaved, int channels,
            int sampleRate, double blockSeconds, double stepSeconds)
        {
            var blocks = new List<double[]>();
            if (interleaved == null || interleaved.Length == 0 || channels <= 0 || sampleRate <= 0)
            {
                return blocks;
            }

            var frames = interleaved.Length / channels;
            if (frames == 0)
            {
                return blocks;
            }

            var filtered = new double[channels][];
            var shelf = KWeighting.HighShelf(sampleRate);
            var pass = KWeighting.HighPass(sampleRate);

            for (var ch = 0; ch < channels; ch++)
            {
                var channelData = new double[frames];
                for (var frame = 0; frame < frames; frame++)
                {
                    channelData[frame] = interleaved[frame * channels + ch];
                }

                KWeighting.ApplyInPlace(channelData, shelf);
                KWeighting.ApplyInPlace(channelData, pass);
                filtered[ch] = channelData;
            }

            var blockFrames = (int)Math.Round(blockSeconds * sampleRate);
            var stepFrames = Math.Max(1, (int)Math.Round(stepSeconds * sampleRate));

            if (frames < blockFrames)
            {
                blocks.Add(MeanSquarePerChannel(filtered, 0, frames));
                return blocks;
            }

            for (var start = 0; start + blockFrames <= frames; start += stepFrames)
            {
                blocks.Add(MeanSquarePerChannel(filtered, start, blockFrames));
            }

            return blocks;
        }

        internal static double BlockLoudness(double[] meanSquares, double[] weights)
        {
            var sum = 0.0;
            for (var ch = 0; ch < meanSquares.Length; ch++)
            {
                sum += weights[ch] * meanSquares[ch];
            }

            return sum <= 0.0 ? double.NegativeInfinity : LoudnessOffset + 10.0 * Math.Log10(sum);
        }

        /// <summary>L/R/C weigh 1.0; surround channels weigh 1.41 per the standard.</summary>
        internal static double[] ChannelWeights(int channels)
        {
            var weights = new double[channels];
            for (var ch = 0; ch < channels; ch++)
            {
                weights[ch] = ch >= 3 ? 1.41 : 1.0;
            }

            return weights;
        }

        private static double[] MeanSquarePerChannel(double[][] filtered, int start, int count)
        {
            var result = new double[filtered.Length];
            for (var ch = 0; ch < filtered.Length; ch++)
            {
                var sum = 0.0;
                var data = filtered[ch];
                for (var i = start; i < start + count; i++)
                {
                    sum += data[i] * data[i];
                }

                result[ch] = sum / count;
            }

            return result;
        }

        private static double[] MeanPerChannel(List<double[]> blocks, int channels)
        {
            var result = new double[channels];
            foreach (var block in blocks)
            {
                for (var ch = 0; ch < channels; ch++)
                {
                    result[ch] += block[ch];
                }
            }

            for (var ch = 0; ch < channels; ch++)
            {
                result[ch] /= blocks.Count;
            }

            return result;
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 10 new ones from this task.

- [ ] **Step 7: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): BS.1770-4 integrated loudness with two-stage gating

Calibration is pinned by test: a stereo 1 kHz sine at -23 dBFS peak reads
-23.0 LUFS. Two edge cases that silently produce NaN downstream are
handled explicitly -- an all-zero clip reports Silent rather than
-Infinity, and a clip shorter than one 400 ms block is measured as a
single block instead of producing none.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Momentary-max measure mode

**Files:**
- Modify: `Packages/com.hoppa.audiobalance/Editor/Dsp/LufsMeter.cs` (add `MeasureMomentaryMax`)
- Create: `Packages/com.hoppa.audiobalance/Editor/Dsp/MeasureMode.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/MomentaryMaxTests.cs`

**Interfaces:**
- Consumes: `LufsMeter.ComputeBlockPowers`, `LufsMeter.BlockLoudness`, `LufsMeter.ChannelWeights`, `LoudnessResult`.
- Produces: `enum MeasureMode { Integrated, MomentaryMax }`; `LufsMeter.MeasureMomentaryMax(float[] interleaved, int channels, int sampleRate) -> LoudnessResult`; `LufsMeter.Measure(float[] interleaved, int channels, int sampleRate, MeasureMode mode) -> LoudnessResult`.

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/MomentaryMaxTests.cs`:

```csharp
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class MomentaryMaxTests
    {
        private const int Rate = 48000;

        [Test]
        public void MomentaryMax_OnClipShorterThanOneWindow_MeasuresTheWholeClip()
        {
            // 200 ms is shorter than the 400 ms window, so ComputeBlockPowers collapses to a
            // single block spanning the clip -- the behaviour short SFX depend on.
            var signal = SignalFactory.Sine(-23.0, 0.2, 2, Rate);

            var result = LufsMeter.MeasureMomentaryMax(signal, 2, Rate);

            Assert.IsFalse(result.IsSilent);
            Assert.AreEqual(-23.0f, result.Lufs, 0.5f);
        }

        [Test]
        public void MomentaryMax_OnSteadyTone_AgreesWithIntegrated()
        {
            // On steady material the two modes must not diverge: every 400 ms window looks
            // like every other, and the gating has nothing to exclude.
            var signal = SignalFactory.Sine(-23.0, 5.0, 2, Rate);

            var integrated = LufsMeter.MeasureIntegrated(signal, 2, Rate);
            var momentary = LufsMeter.MeasureMomentaryMax(signal, 2, Rate);

            Assert.AreEqual(integrated.Lufs, momentary.Lufs, 0.1f);
        }

        [Test]
        public void MomentaryMax_ExceedsIntegrated_ForAOneShotWithALongQuietTail()
        {
            // 0.5 s at -18 dBFS then 4 s at -50 dBFS -- a percussive one-shot decaying out.
            // The 400 ms window lands entirely inside the attack (~-18), while integrated
            // gating keeps the attack blocks AND the three that straddle the transition,
            // pulling its answer below the peak (~-19.5).
            //
            // A 3 s window would FAIL this: it would be forced to average 0.5 s of attack
            // with 2.5 s of near-silence (~-25.8), landing well BELOW integrated. That is
            // why this mode measures 400 ms, not 3 s.
            var signal = SignalFactory.Concat(
                SignalFactory.Sine(-18.0, 0.5, 2, Rate),
                SignalFactory.Sine(-50.0, 4.0, 2, Rate));

            var integrated = LufsMeter.MeasureIntegrated(signal, 2, Rate);
            var momentary = LufsMeter.MeasureMomentaryMax(signal, 2, Rate);

            Assert.Greater(momentary.Lufs, integrated.Lufs,
                "Momentary max must track the loudest moment, not the gated average.");
        }

        [Test]
        public void MomentaryMax_OnSilence_IsSilent()
        {
            var result = LufsMeter.MeasureMomentaryMax(SignalFactory.Silence(4.0, 2, Rate), 2, Rate);

            Assert.IsTrue(result.IsSilent);
        }

        [Test]
        public void MomentaryMax_OnVeryQuietSignalBelowAbsoluteGate_IsSilent()
        {
            var result = LufsMeter.MeasureMomentaryMax(SignalFactory.Sine(-90.0, 4.0, 2, Rate), 2, Rate);

            Assert.IsTrue(result.IsSilent);
        }

        [Test]
        public void Measure_DispatchesOnMode()
        {
            var signal = SignalFactory.Sine(-23.0, 5.0, 2, Rate);

            var viaIntegrated = LufsMeter.Measure(signal, 2, Rate, MeasureMode.Integrated);
            var viaMomentary = LufsMeter.Measure(signal, 2, Rate, MeasureMode.MomentaryMax);

            Assert.AreEqual(LufsMeter.MeasureIntegrated(signal, 2, Rate).Lufs, viaIntegrated.Lufs, 1e-4f);
            Assert.AreEqual(LufsMeter.MeasureMomentaryMax(signal, 2, Rate).Lufs, viaMomentary.Lufs, 1e-4f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `MeasureMode` and `MeasureMomentaryMax` do not exist.

- [ ] **Step 3: Implement `MeasureMode`**

`Packages/com.hoppa.audiobalance/Editor/Dsp/MeasureMode.cs`:

```csharp
namespace Hoppa.AudioBalance.Editor
{
    /// <summary>How a category's clips are measured.</summary>
    public enum MeasureMode
    {
        /// <summary>Full gated BS.1770 integrated loudness. Correct for music beds.</summary>
        Integrated = 0,

        /// <summary>
        /// Loudest 400 ms window, ungated -- the standard's "momentary" loudness. Correct
        /// for short one-shots: it lands on the attack, where integrated loudness is pulled
        /// down by the blocks straddling the decay into silence.
        /// </summary>
        MomentaryMax = 1
    }
}
```

- [ ] **Step 4: Add `MeasureMomentaryMax` and `Measure` to `LufsMeter`**

In `Packages/com.hoppa.audiobalance/Editor/Dsp/LufsMeter.cs`, add the constant beside the existing ones:

```csharp
        private const double MomentarySeconds = 0.4;
```

and add these two methods directly after `MeasureIntegrated`:

```csharp
        public static LoudnessResult Measure(float[] interleaved, int channels, int sampleRate,
            MeasureMode mode)
        {
            return mode == MeasureMode.MomentaryMax
                ? MeasureMomentaryMax(interleaved, channels, sampleRate)
                : MeasureIntegrated(interleaved, channels, sampleRate);
        }

        /// <summary>
        /// Loudest 400 ms window, ungated. For a clip shorter than the window,
        /// ComputeBlockPowers collapses to a single block over the whole clip -- exactly the
        /// desired behaviour for the short SFX this mode exists to serve.
        ///
        /// The window is 400 ms, not 3 s, for a measured reason: on a one-shot with a long
        /// quiet tail a 3 s window averages the attack with the silence and reads BELOW
        /// integrated loudness, which is the opposite of this mode's purpose.
        /// </summary>
        public static LoudnessResult MeasureMomentaryMax(float[] interleaved, int channels, int sampleRate)
        {
            var blocks = ComputeBlockPowers(interleaved, channels, sampleRate, MomentarySeconds, StepSeconds);
            if (blocks.Count == 0)
            {
                return LoudnessResult.Silent;
            }

            var weights = ChannelWeights(channels);
            var max = double.NegativeInfinity;

            foreach (var block in blocks)
            {
                var loudness = BlockLoudness(block, weights);
                if (loudness > max)
                {
                    max = loudness;
                }
            }

            return double.IsNegativeInfinity(max) || max <= AbsoluteGateLufs
                ? LoudnessResult.Silent
                : LoudnessResult.At((float)max);
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 6 new ones from this task.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): short-term-max measure mode for one-shot SFX

Integrated gating discards a one-shot's decay tail, making short SFX
under-read against a music bed. MomentaryMax takes the loudest sliding
400 ms window ungated (the standard's "momentary" loudness); for a clip
shorter than the window that collapses to the whole clip.

Measured, not assumed: a 3 s window reads BELOW integrated on a one-shot
with a long tail (-25.8 vs -19.5), because it averages the attack with the
silence. 400 ms lands on the attack, which is the whole point.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Peak meter

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Dsp/PeakMeter.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/PeakMeterTests.cs`

**Interfaces:**
- Consumes: `AudioGainMath.DbFromLinear`.
- Produces: `PeakMeter.SamplePeakDb(float[] interleaved) -> float`.

> Diagnostic only. Because the headroom pass in Task 7 guarantees every gain is ≤ 0 dB, applied gain cannot create clipping — this number exists to spot assets that were already clipped or are near full scale before we touch them.
>
> **No true-peak meter.** An earlier revision specified an `ApproxTruePeakDb` that oversampled 4× by linear interpolation. That was struck after review: linear interpolation produces a convex combination of its two endpoints, so `|a + (b−a)t| ≤ max(|a|,|b|)` for every `t ∈ [0,1]` — it can *never* exceed the sample peak, and therefore can never detect an inter-sample peak, which is the only thing a true-peak meter is for. It also read *below* the sample peak whenever the loudest sample fell on a buffer's final frame. Real true-peak detection needs polyphase FIR upsampling (BS.1770-4 Annex 2); that is a follow-up if it is ever needed, not a diagnostic readout.

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/PeakMeterTests.cs`:

```csharp
using Hoppa.AudioBalance;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class PeakMeterTests
    {
        [Test]
        public void SamplePeakDb_OfFullScaleSignal_IsZeroDb()
        {
            var signal = new[] { 0.1f, -1.0f, 0.5f, 0.2f };

            Assert.AreEqual(0f, PeakMeter.SamplePeakDb(signal), 1e-3f);
        }

        [Test]
        public void SamplePeakDb_OfHalfScaleSignal_IsAboutMinusSixDb()
        {
            var signal = new[] { 0.1f, 0.5f, -0.25f };

            Assert.AreEqual(-6.02f, PeakMeter.SamplePeakDb(signal), 0.05f);
        }

        [Test]
        public void SamplePeakDb_OfSilence_IsTheFloorNotNegativeInfinity()
        {
            Assert.AreEqual(AudioGainMath.MinDb, PeakMeter.SamplePeakDb(new float[16]), 1e-3f);
        }

        [Test]
        public void SamplePeakDb_OfNullOrEmpty_IsTheFloor()
        {
            Assert.AreEqual(AudioGainMath.MinDb, PeakMeter.SamplePeakDb(null), 1e-3f);
            Assert.AreEqual(AudioGainMath.MinDb, PeakMeter.SamplePeakDb(new float[0]), 1e-3f);
        }

        [Test]
        public void SamplePeakDb_FindsThePeakOnTheFinalFrame()
        {
            // Guards the boundary an earlier interpolating implementation got wrong:
            // the loudest sample sits on the very last frame and must still be found.
            Assert.AreEqual(0f, PeakMeter.SamplePeakDb(new[] { 0f, 0f, 1f }), 1e-3f);
        }

        [Test]
        public void SamplePeakDb_ScansEveryChannelOfAnInterleavedBuffer()
        {
            // Quiet left, full-scale right. A meter that only scanned channel 0 would
            // report -20 dB and miss a clipped right channel entirely.
            var stereo = new[] { 0.1f, 0.5f, 0.1f, -1f };

            Assert.AreEqual(0f, PeakMeter.SamplePeakDb(stereo), 1e-3f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `PeakMeter` does not exist.

- [ ] **Step 3: Implement `PeakMeter`**

`Packages/com.hoppa.audiobalance/Editor/Dsp/PeakMeter.cs`:

```csharp
using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Peak diagnostics. Reported in the window to flag assets that arrived already clipped
    /// or hard against full scale -- applied gain can never cause clipping, because the
    /// headroom pass keeps every gain at or below 0 dB.
    /// </summary>
    public static class PeakMeter
    {
        public static float SamplePeakDb(float[] interleaved)
        {
            if (interleaved == null || interleaved.Length == 0)
            {
                return AudioGainMath.MinDb;
            }

            var peak = 0f;
            foreach (var sample in interleaved)
            {
                var magnitude = Math.Abs(sample);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            return AudioGainMath.DbFromLinear(peak);
        }

    }
}
```

`SamplePeakDb` scans the interleaved buffer flat, so it covers every channel and every
frame including the last. It needs no `channels` parameter — the maximum absolute sample
is the same regardless of how the buffer is grouped into frames.

- [ ] **Step 4: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 6 new ones from this task.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): sample-peak diagnostic

Flags assets that arrived already clipped or hard against full scale.
Applied gain can never cause clipping itself, because the headroom pass
keeps every gain at or below 0 dB.

No true-peak meter: linear interpolation yields a convex combination of
its endpoints, so it can never exceed the sample peak and therefore can
never find an inter-sample peak. Real true-peak needs polyphase FIR
upsampling -- a follow-up if ever needed, not a diagnostic readout.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Profile asset + categories

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Data/AudioCategory.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Data/ClipSettings.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Data/AudioBalanceProfile.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/AudioBalanceProfileTests.cs`

**Interfaces:**
- Consumes: `MeasureMode`, `AudioGainTable`.
- Produces:
  - `AudioCategory { string Name; float OffsetDb; MeasureMode Mode; }`
  - `ClipSettings { AudioClip Clip; string Category; float TrimDb; }`
  - `AudioBalanceProfile` with fields `List<string> Folders`, `AudioClip Anchor`, `List<AudioCategory> Categories`, `List<ClipSettings> Clips`, `AudioGainTable Table`
  - `AudioBalanceProfile.ResetToDefaultCategories()`
  - `AudioBalanceProfile.FindCategory(string name) -> AudioCategory` (falls back to the first category, never null when any exist)
  - `AudioBalanceProfile.OffsetDbFor(AudioClip) -> float`
  - `AudioBalanceProfile.TrimDbFor(AudioClip) -> float`
  - `AudioBalanceProfile.ModeFor(AudioClip) -> MeasureMode`
  - `AudioBalanceProfile.SettingsFor(AudioClip) -> ClipSettings` (creates and stores one on first call)

> The profile is editor-only on purpose: the baked table carries final numbers, so nothing at runtime needs to know what a category is.

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/AudioBalanceProfileTests.cs`:

```csharp
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class AudioBalanceProfileTests
    {
        private static AudioClip MakeClip(string name)
        {
            return AudioClip.Create(name, 128, 1, 44100, false);
        }

        private static AudioBalanceProfile MakeProfile()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            return profile;
        }

        [Test]
        public void ResetToDefaultCategories_SeedsMusicSfxAndUi()
        {
            var profile = MakeProfile();

            Assert.AreEqual(3, profile.Categories.Count);
            Assert.AreEqual("Music", profile.Categories[0].Name);
            Assert.AreEqual(0f, profile.Categories[0].OffsetDb, 1e-4f);
            Assert.AreEqual(MeasureMode.Integrated, profile.Categories[0].Mode);

            Assert.AreEqual("SFX", profile.Categories[1].Name);
            Assert.AreEqual(3f, profile.Categories[1].OffsetDb, 1e-4f);
            Assert.AreEqual(MeasureMode.MomentaryMax, profile.Categories[1].Mode);

            Assert.AreEqual("UI", profile.Categories[2].Name);
            Assert.AreEqual(-6f, profile.Categories[2].OffsetDb, 1e-4f);
            Assert.AreEqual(MeasureMode.MomentaryMax, profile.Categories[2].Mode);
        }

        [Test]
        public void SettingsFor_CreatesAnEntryOnFirstCallAndReusesItAfter()
        {
            var profile = MakeProfile();
            var clip = MakeClip("kick");

            var first = profile.SettingsFor(clip);
            var second = profile.SettingsFor(clip);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, profile.Clips.Count);
        }

        [Test]
        public void OffsetDbFor_UsesTheClipsAssignedCategory()
        {
            var profile = MakeProfile();
            var clip = MakeClip("blip");
            profile.SettingsFor(clip).Category = "UI";

            Assert.AreEqual(-6f, profile.OffsetDbFor(clip), 1e-4f);
        }

        [Test]
        public void ModeFor_UsesTheClipsAssignedCategory()
        {
            var profile = MakeProfile();
            var music = MakeClip("loop");
            profile.SettingsFor(music).Category = "Music";

            Assert.AreEqual(MeasureMode.Integrated, profile.ModeFor(music));
        }

        [Test]
        public void TrimDbFor_StacksOnTopOfTheCategoryOffset()
        {
            var profile = MakeProfile();
            var clip = MakeClip("trimmed");
            var settings = profile.SettingsFor(clip);
            settings.Category = "SFX";
            settings.TrimDb = -2.5f;

            Assert.AreEqual(3f, profile.OffsetDbFor(clip), 1e-4f);
            Assert.AreEqual(-2.5f, profile.TrimDbFor(clip), 1e-4f);
        }

        [Test]
        public void FindCategory_FallsBackToTheFirstCategoryForAnUnknownName()
        {
            var profile = MakeProfile();

            var found = profile.FindCategory("DoesNotExist");

            Assert.IsNotNull(found);
            Assert.AreEqual("Music", found.Name);
        }

        [Test]
        public void FindCategory_ReturnsNullWhenNoCategoriesExist()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.Categories.Clear();

            Assert.IsNull(profile.FindCategory("Music"));
        }

        [Test]
        public void OffsetDbFor_IsZeroWhenNoCategoriesExist()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.Categories.Clear();

            Assert.AreEqual(0f, profile.OffsetDbFor(MakeClip("orphan")), 1e-4f);
        }

        [Test]
        public void SettingsFor_HandlesNullClipWithoutThrowing()
        {
            var profile = MakeProfile();

            Assert.IsNull(profile.SettingsFor(null));
            Assert.AreEqual(0f, profile.OffsetDbFor(null), 1e-4f);
            Assert.AreEqual(0f, profile.TrimDbFor(null), 1e-4f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `AudioBalanceProfile` does not exist.

- [ ] **Step 3: Implement `AudioCategory` and `ClipSettings`**

`Packages/com.hoppa.audiobalance/Editor/Data/AudioCategory.cs`:

```csharp
using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// A group of clips that share an intended level relative to the anchor. The offset is
    /// what stops everything collapsing to the same loudness: SFX are meant to sit above the
    /// music bed, UI blips below it.
    /// </summary>
    [Serializable]
    public sealed class AudioCategory
    {
        public string Name = "SFX";
        public float OffsetDb;
        public MeasureMode Mode = MeasureMode.MomentaryMax;
    }
}
```

`Packages/com.hoppa.audiobalance/Editor/Data/ClipSettings.cs`:

```csharp
using System;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Per-clip authoring state: which category it belongs to, plus a manual trim.</summary>
    [Serializable]
    public sealed class ClipSettings
    {
        public AudioClip Clip;
        public string Category = "SFX";

        /// <summary>Manual dB adjustment stacked on top of the category offset.</summary>
        public float TrimDb;
    }
}
```

- [ ] **Step 4: Implement `AudioBalanceProfile`**

`Packages/com.hoppa.audiobalance/Editor/Data/AudioBalanceProfile.cs`:

```csharp
using System.Collections.Generic;
using Hoppa.AudioBalance;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Authoring state for the Audio Balance window. Editor-only by design: the baked
    /// AudioGainTable carries final gains, so the runtime never needs categories.
    /// </summary>
    [CreateAssetMenu(menuName = "Hoppa/Audio/Audio Balance Profile", fileName = "AudioBalanceProfile")]
    public sealed class AudioBalanceProfile : ScriptableObject
    {
        /// <summary>Project-relative folders to scan, e.g. "Assets/BusBuddies/Audio".</summary>
        public List<string> Folders = new List<string>();

        /// <summary>The reference clip -- usually the background music that runs during levels.</summary>
        public AudioClip Anchor;

        public List<AudioCategory> Categories = new List<AudioCategory>();

        public List<ClipSettings> Clips = new List<ClipSettings>();

        /// <summary>Destination asset for the baked gains.</summary>
        public AudioGainTable Table;

        public void ResetToDefaultCategories()
        {
            Categories = new List<AudioCategory>
            {
                new AudioCategory { Name = "Music", OffsetDb = 0f, Mode = MeasureMode.Integrated },
                new AudioCategory { Name = "SFX", OffsetDb = 3f, Mode = MeasureMode.MomentaryMax },
                new AudioCategory { Name = "UI", OffsetDb = -6f, Mode = MeasureMode.MomentaryMax }
            };
        }

        /// <summary>
        /// The named category, or the first one as a fallback so a renamed category never
        /// silently drops a clip's offset to zero. Null only when no categories exist at all.
        /// </summary>
        public AudioCategory FindCategory(string name)
        {
            if (Categories == null || Categories.Count == 0)
            {
                return null;
            }

            foreach (var category in Categories)
            {
                if (category != null && category.Name == name)
                {
                    return category;
                }
            }

            return Categories[0];
        }

        /// <summary>Returns the clip's settings, creating and storing them on first access.</summary>
        public ClipSettings SettingsFor(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            foreach (var settings in Clips)
            {
                if (settings != null && settings.Clip == clip)
                {
                    return settings;
                }
            }

            var created = new ClipSettings
            {
                Clip = clip,
                Category = Categories != null && Categories.Count > 0 ? Categories[0].Name : "SFX"
            };

            Clips.Add(created);
            return created;
        }

        public float OffsetDbFor(AudioClip clip)
        {
            var settings = SettingsFor(clip);
            if (settings == null)
            {
                return 0f;
            }

            var category = FindCategory(settings.Category);
            return category?.OffsetDb ?? 0f;
        }

        public float TrimDbFor(AudioClip clip)
        {
            return SettingsFor(clip)?.TrimDb ?? 0f;
        }

        public MeasureMode ModeFor(AudioClip clip)
        {
            var settings = SettingsFor(clip);
            if (settings == null)
            {
                return MeasureMode.Integrated;
            }

            var category = FindCategory(settings.Category);
            return category?.Mode ?? MeasureMode.Integrated;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 9 new ones from this task.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): editor-only balance profile with seeded categories

Categories default to Music 0 / SFX +3 / UI -6. FindCategory falls back to
the first category rather than returning null, so renaming a category
cannot silently zero a clip's offset.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Gain solver + headroom pass

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Solve/ClipStatus.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Solve/ClipAnalysis.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Solve/GainResult.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Solve/GainSolver.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/GainSolverTests.cs`

**Interfaces:**
- Consumes: nothing beyond `UnityEngine.AudioClip`.
- Produces:
  - `enum ClipStatus { Ok, Silent, Unanalyzable }`
  - `ClipAnalysis` — readonly struct, constructor `ClipAnalysis(AudioClip clip, ClipStatus status, float lufs, float peakDb, string reason = null)`, plus statics `ClipAnalysis.Ok(AudioClip, float lufs, float peakDb)`, `ClipAnalysis.Silent(AudioClip)`, `ClipAnalysis.Unanalyzable(AudioClip, string reason)`; fields `Clip`, `Status`, `Lufs`, `PeakDb`, `Reason`
  - `GainResult` — readonly struct with fields `Clip`, `Status`, `RawGainDb`, `FinalGainDb`, `IsOutlier`
  - `GainSolver.Solve(IReadOnlyList<ClipAnalysis> analyses, float anchorLufs, Func<AudioClip,float> categoryOffsetDb, Func<AudioClip,float> trimDb) -> IReadOnlyList<GainResult>`
  - `GainSolver.OutlierThresholdDb = 12f`

> This is where §6 of the spec lives. `AudioSource.volume` caps at 1.0, so a clip needing +6 dB simply cannot get it — the request silently does nothing and the table lies about the balance. Subtracting the maximum raw gain from every raw gain pins the clip that needed the *most* gain — the quietest one relative to its target — at exactly 0 dB, attenuates every other clip, preserves relative spacing exactly, and makes clipping structurally impossible. The clip that is *loudest* relative to its target is the one attenuated most. (Corrected by deviation #9 — the original wording here said "pins the loudest clip at 0 dB", which is backwards.)

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/GainSolverTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class GainSolverTests
    {
        private static AudioClip MakeClip(string name)
        {
            return AudioClip.Create(name, 128, 1, 44100, false);
        }

        private static IReadOnlyList<GainResult> Solve(
            IReadOnlyList<ClipAnalysis> analyses,
            float anchorLufs,
            Dictionary<AudioClip, float> offsets = null,
            Dictionary<AudioClip, float> trims = null)
        {
            return GainSolver.Solve(
                analyses,
                anchorLufs,
                clip => offsets != null && offsets.TryGetValue(clip, out var o) ? o : 0f,
                clip => trims != null && trims.TryGetValue(clip, out var t) ? t : 0f);
        }

        [Test]
        public void Anchor_InAZeroOffsetCategoryWithNoTrim_ResolvesToZeroRawGain()
        {
            var anchor = MakeClip("anchor");
            var results = Solve(new[] { ClipAnalysis.Ok(anchor, -18f, -1f) }, -18f);

            Assert.AreEqual(0f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void CategoryOffset_ShiftsGainByExactlyThatManyDb()
        {
            var clip = MakeClip("sfx");
            var offsets = new Dictionary<AudioClip, float> { { clip, 3f } };

            var results = Solve(new[] { ClipAnalysis.Ok(clip, -18f, -1f) }, -18f, offsets);

            Assert.AreEqual(3f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void Trim_StacksAdditivelyOnTopOfTheCategoryOffset()
        {
            var clip = MakeClip("sfx");
            var offsets = new Dictionary<AudioClip, float> { { clip, 3f } };
            var trims = new Dictionary<AudioClip, float> { { clip, -1.5f } };

            var results = Solve(new[] { ClipAnalysis.Ok(clip, -18f, -1f) }, -18f, offsets, trims);

            Assert.AreEqual(1.5f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void QuieterClipThanAnchor_NeedsPositiveRawGain()
        {
            var clip = MakeClip("quiet");
            var results = Solve(new[] { ClipAnalysis.Ok(clip, -30f, -12f) }, -18f);

            Assert.AreEqual(12f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void AfterNormalization_TheMaximumFinalGainIsExactlyZeroDb()
        {
            var loud = MakeClip("loud");
            var quiet = MakeClip("quiet");
            var results = Solve(new[]
            {
                ClipAnalysis.Ok(loud, -12f, -1f),
                ClipAnalysis.Ok(quiet, -30f, -14f)
            }, -18f);

            Assert.AreEqual(0f, results.Max(r => r.FinalGainDb), 1e-4f);
        }

        [Test]
        public void AfterNormalization_EveryFinalGainIsAtOrBelowZeroDb()
        {
            var results = Solve(new[]
            {
                ClipAnalysis.Ok(MakeClip("a"), -30f, -14f),
                ClipAnalysis.Ok(MakeClip("b"), -26f, -10f),
                ClipAnalysis.Ok(MakeClip("c"), -40f, -20f)
            }, -18f);

            foreach (var result in results)
            {
                Assert.LessOrEqual(result.FinalGainDb, 1e-4f,
                    "AudioSource.volume caps at 1.0, so a positive gain is unachievable.");
            }
        }

        [Test]
        public void Normalization_PreservesRelativeSpacingExactly()
        {
            var a = MakeClip("a");
            var b = MakeClip("b");
            var results = Solve(new[]
            {
                ClipAnalysis.Ok(a, -30f, -14f),
                ClipAnalysis.Ok(b, -22f, -6f)
            }, -18f);

            var rawSpacing = results[0].RawGainDb - results[1].RawGainDb;
            var finalSpacing = results[0].FinalGainDb - results[1].FinalGainDb;

            Assert.AreEqual(rawSpacing, finalSpacing, 1e-4f);
        }

        [Test]
        public void SilentAndUnanalyzableClips_AreExcludedFromTheMaxGainCalculation()
        {
            var ok = MakeClip("ok");
            var silent = MakeClip("silent");
            var broken = MakeClip("broken");

            var results = Solve(new[]
            {
                ClipAnalysis.Ok(ok, -22f, -6f),
                ClipAnalysis.Silent(silent),
                ClipAnalysis.Unanalyzable(broken, "streaming")
            }, -18f);

            var okResult = results.First(r => r.Clip == ok);
            Assert.AreEqual(0f, okResult.FinalGainDb, 1e-4f,
                "The single analyzable clip should define the 0 dB ceiling.");
        }

        [Test]
        public void SilentAndUnanalyzableClips_GetZeroGainAndKeepTheirStatus()
        {
            var silent = MakeClip("silent");
            var broken = MakeClip("broken");

            var results = Solve(new[]
            {
                ClipAnalysis.Ok(MakeClip("ok"), -22f, -6f),
                ClipAnalysis.Silent(silent),
                ClipAnalysis.Unanalyzable(broken, "streaming")
            }, -18f);

            var silentResult = results.First(r => r.Clip == silent);
            Assert.AreEqual(ClipStatus.Silent, silentResult.Status);
            Assert.AreEqual(0f, silentResult.FinalGainDb, 1e-4f);

            var brokenResult = results.First(r => r.Clip == broken);
            Assert.AreEqual(ClipStatus.Unanalyzable, brokenResult.Status);
            Assert.AreEqual(0f, brokenResult.FinalGainDb, 1e-4f);
        }

        [Test]
        public void OutlierFlag_TriggersAboveTwelveDbAndNotBelow()
        {
            var inside = MakeClip("inside");
            var outside = MakeClip("outside");

            var results = Solve(new[]
            {
                ClipAnalysis.Ok(inside, -29f, -12f),   // raw = +11
                ClipAnalysis.Ok(outside, -35f, -20f)   // raw = +17
            }, -18f);

            Assert.IsFalse(results.First(r => r.Clip == inside).IsOutlier);
            Assert.IsTrue(results.First(r => r.Clip == outside).IsOutlier);
        }

        [Test]
        public void OutlierFlag_TriggersOnLargeNegativeRawGainToo()
        {
            var clip = MakeClip("blaring");
            var results = Solve(new[] { ClipAnalysis.Ok(clip, -2f, -0.1f) }, -18f);

            Assert.AreEqual(-16f, results[0].RawGainDb, 1e-4f);
            Assert.IsTrue(results[0].IsOutlier);
        }

        [Test]
        public void Solve_WithNoAnalyzableClips_ReturnsZeroGainsWithoutThrowing()
        {
            var results = Solve(new[]
            {
                ClipAnalysis.Silent(MakeClip("s1")),
                ClipAnalysis.Unanalyzable(MakeClip("s2"), "streaming")
            }, -18f);

            Assert.AreEqual(2, results.Count);
            foreach (var result in results)
            {
                Assert.AreEqual(0f, result.FinalGainDb, 1e-4f);
            }
        }

        [Test]
        public void Solve_WithEmptyInput_ReturnsAnEmptyList()
        {
            Assert.AreEqual(0, Solve(new ClipAnalysis[0], -18f).Count);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `GainSolver` does not exist.

- [ ] **Step 3: Implement `ClipStatus`, `ClipAnalysis`, `GainResult`**

`Packages/com.hoppa.audiobalance/Editor/Solve/ClipStatus.cs`:

```csharp
namespace Hoppa.AudioBalance.Editor
{
    public enum ClipStatus
    {
        /// <summary>Measured successfully.</summary>
        Ok = 0,

        /// <summary>All-zero or below the absolute gate -- has no defined loudness.</summary>
        Silent = 1,

        /// <summary>Could not be read, e.g. a Streaming clip whose GetData returns silence.</summary>
        Unanalyzable = 2
    }
}
```

`Packages/com.hoppa.audiobalance/Editor/Solve/ClipAnalysis.cs`:

```csharp
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>The measured facts about one clip, before any balancing decision is made.</summary>
    public readonly struct ClipAnalysis
    {
        public readonly AudioClip Clip;
        public readonly ClipStatus Status;
        public readonly float Lufs;
        public readonly float PeakDb;
        public readonly string Reason;

        public ClipAnalysis(AudioClip clip, ClipStatus status, float lufs,
            float peakDb, string reason = null)
        {
            Clip = clip;
            Status = status;
            Lufs = lufs;
            PeakDb = peakDb;
            Reason = reason;
        }

        public static ClipAnalysis Ok(AudioClip clip, float lufs, float peakDb)
        {
            return new ClipAnalysis(clip, ClipStatus.Ok, lufs, peakDb);
        }

        public static ClipAnalysis Silent(AudioClip clip)
        {
            return new ClipAnalysis(clip, ClipStatus.Silent, 0f, AudioGainMath.MinDb, "silent");
        }

        public static ClipAnalysis Unanalyzable(AudioClip clip, string reason)
        {
            return new ClipAnalysis(clip, ClipStatus.Unanalyzable, 0f, AudioGainMath.MinDb, reason);
        }
    }
}
```

`Packages/com.hoppa.audiobalance/Editor/Solve/GainResult.cs`:

```csharp
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>One clip's solved gain. FinalGainDb is what gets baked into the table.</summary>
    public readonly struct GainResult
    {
        public readonly AudioClip Clip;
        public readonly ClipStatus Status;

        /// <summary>Gain before the headroom pass. May be positive.</summary>
        public readonly float RawGainDb;

        /// <summary>Gain after the headroom pass. Always at or below 0 dB.</summary>
        public readonly float FinalGainDb;

        public readonly bool IsOutlier;

        public GainResult(AudioClip clip, ClipStatus status, float rawGainDb,
            float finalGainDb, bool isOutlier)
        {
            Clip = clip;
            Status = status;
            RawGainDb = rawGainDb;
            FinalGainDb = finalGainDb;
            IsOutlier = isOutlier;
        }
    }
}
```

- [ ] **Step 4: Implement `GainSolver`**

`Packages/com.hoppa.audiobalance/Editor/Solve/GainSolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Turns measurements into bakeable gains.
    ///
    ///   raw   = (anchorLufs + categoryOffset + trim) - measuredLufs
    ///   final = raw - max(raw over analyzable clips)
    ///
    /// The second line is the headroom pass. AudioSource.volume is hard-capped at 1.0, so a
    /// clip needing +6 dB simply cannot receive it -- the request silently does nothing and
    /// the table no longer describes what you hear. Subtracting the maximum pins the clip
    /// that needed the MOST gain -- the quietest one relative to its target -- at exactly
    /// 0 dB; every other clip is attenuated, and the clip loudest relative to its target is
    /// attenuated most. Relative spacing is preserved exactly and clipping becomes
    /// structurally impossible rather than merely warned about. The cost is that overall
    /// output is quieter, which is compensated once on the master mixer.
    ///
    /// Note that <c>anchorLufs</c> appears in every raw gain and therefore
    /// cancels exactly in the subtraction: FinalGainDb is provably independent of the
    /// anchor's measured loudness. Relative placement between clips comes from the category
    /// offsets alone. The anchor's only live effect here is on IsOutlier, which is computed
    /// from the raw (pre-headroom) gain.
    /// </summary>
    public static class GainSolver
    {
        /// <summary>Beyond this, a clip is almost always broken rather than genuinely quiet.</summary>
        public const float OutlierThresholdDb = 12f;

        public static IReadOnlyList<GainResult> Solve(
            IReadOnlyList<ClipAnalysis> analyses,
            float anchorLufs,
            Func<AudioClip, float> categoryOffsetDb,
            Func<AudioClip, float> trimDb)
        {
            var results = new List<GainResult>();
            if (analyses == null || analyses.Count == 0)
            {
                return results;
            }

            var raw = new float[analyses.Count];
            var maxRaw = float.NegativeInfinity;

            for (var i = 0; i < analyses.Count; i++)
            {
                var analysis = analyses[i];
                if (analysis.Status != ClipStatus.Ok)
                {
                    continue;
                }

                var offset = categoryOffsetDb?.Invoke(analysis.Clip) ?? 0f;
                var trim = trimDb?.Invoke(analysis.Clip) ?? 0f;

                raw[i] = anchorLufs + offset + trim - analysis.Lufs;

                if (raw[i] > maxRaw)
                {
                    maxRaw = raw[i];
                }
            }

            // No analyzable clip means nothing defines the ceiling; leave every gain at unity.
            var headroomOffset = float.IsNegativeInfinity(maxRaw) ? 0f : maxRaw;

            for (var i = 0; i < analyses.Count; i++)
            {
                var analysis = analyses[i];

                if (analysis.Status != ClipStatus.Ok)
                {
                    results.Add(new GainResult(analysis.Clip, analysis.Status, 0f, 0f, false));
                    continue;
                }

                results.Add(new GainResult(
                    analysis.Clip,
                    analysis.Status,
                    raw[i],
                    raw[i] - headroomOffset,
                    Mathf.Abs(raw[i]) > OutlierThresholdDb));
            }

            return results;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 13 new ones from this task.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): gain solver with downward-only headroom normalization

AudioSource.volume caps at 1.0, so positive gains are unachievable.
Subtracting the max raw gain pins the loudest clip at 0 dB and preserves
relative spacing exactly -- tested. Silent and unanalyzable clips are
excluded from the ceiling calculation so one broken asset cannot
attenuate the whole project.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

> **Note (deviation #9):** this commit message is reproduced as it was actually committed. Its "pins the loudest clip at 0 dB" phrasing is backwards — the clip pinned at 0 dB is the *quietest* relative to its target. Left verbatim because it is a historical record of a shipped commit; the class docstring it described has since been corrected in the source file, and the rationale prose above has been corrected too.

---

### Task 8: Clip sample reader

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Analysis/ClipSampleReader.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/ClipSampleReaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ClipSampleReader.TryRead(AudioClip clip, out float[] interleaved, out string error) -> bool`; `ClipSampleReader.StreamingError` and `ClipSampleReader.LoadPendingError` constants.

> Streaming clips return silence from `GetData`. Rather than mutating the project's import settings behind the user's back, those are reported with an actionable message.
>
> **`LoadAudioData()` is asynchronous.** A `true` return means the load was *queued*, not that decoding finished — so `loadState` is re-checked afterwards and `GetData` never runs unless it reads `Loaded`. A single re-check with an honest error, not polling: this is editor-time code, and a clip that is not ready is a reportable condition rather than something to block on.
>
> **Known, accepted test gap:** the Streaming rejection branch has no automated coverage. `AudioClip.Create` — the only way these tests build clips — always produces a fully-resident non-streaming clip; `Streaming` can only be set on an imported asset via its `AudioImporter`. Covering it would need a committed `.wav` fixture with its `.meta` pinned to Streaming. The lead chose to accept the gap and document it (2026-07-19) rather than put a binary asset in the package; the reasoning is recorded in the XML doc on `StreamingError` so a maintainer does not assume coverage exists.

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/ClipSampleReaderTests.cs`:

```csharp
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class ClipSampleReaderTests
    {
        [Test]
        public void TryRead_OnAProceduralClip_ReturnsInterleavedSamples()
        {
            var clip = AudioClip.Create("tone", 256, 2, 48000, false);
            var data = SignalFactory.Sine(-6.0, 256 / 48000.0, 2, 48000);
            clip.SetData(data, 0);

            var ok = ClipSampleReader.TryRead(clip, out var samples, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(error);
            Assert.AreEqual(256 * 2, samples.Length);
        }

        [Test]
        public void TryRead_OnNullClip_FailsWithAnError()
        {
            var ok = ClipSampleReader.TryRead(null, out var samples, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(samples);
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryRead_RoundTripsSampleValues()
        {
            var clip = AudioClip.Create("ramp", 4, 1, 48000, false);
            clip.SetData(new[] { 0.25f, -0.5f, 0.75f, -1f }, 0);

            Assert.IsTrue(ClipSampleReader.TryRead(clip, out var samples, out _));

            Assert.AreEqual(0.25f, samples[0], 1e-4f);
            Assert.AreEqual(-0.5f, samples[1], 1e-4f);
            Assert.AreEqual(0.75f, samples[2], 1e-4f);
            Assert.AreEqual(-1f, samples[3], 1e-4f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `ClipSampleReader` does not exist.

- [ ] **Step 3: Implement `ClipSampleReader`**

`Packages/com.hoppa.audiobalance/Editor/Analysis/ClipSampleReader.cs`:

```csharp
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Reads decoded PCM out of an AudioClip for analysis.</summary>
    public static class ClipSampleReader
    {
        /// <summary>
        /// Streaming clips return silence from GetData. We report this rather than flipping
        /// the importer's load type, because silently rewriting someone's import settings is
        /// a worse surprise than an actionable message.
        /// </summary>
        public const string StreamingError = "set Load Type to Decompress On Load";

        public static bool TryRead(AudioClip clip, out float[] interleaved, out string error)
        {
            interleaved = null;
            error = null;

            if (clip == null)
            {
                error = "clip is null";
                return false;
            }

            if (clip.loadType == AudioClipLoadType.Streaming)
            {
                error = StreamingError;
                return false;
            }

            // LoadAudioData() returning true only means the load was QUEUED, not that decoding
            // finished. GetData must never run before loadState is actually Loaded.
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                if (!clip.LoadAudioData())
                {
                    error = "failed to load audio data";
                    return false;
                }

                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    error = LoadPendingError;
                    return false;
                }
            }

            var samples = clip.samples * clip.channels;
            if (samples <= 0)
            {
                error = "clip contains no samples";
                return false;
            }

            var data = new float[samples];
            if (!clip.GetData(data, 0))
            {
                error = "GetData failed";
                return false;
            }

            interleaved = data;
            return true;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 3 new ones from this task.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): clip sample reader with actionable streaming diagnostic

Streaming clips return silence from GetData. Reported as an actionable
message rather than silently rewriting the asset's import settings.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Loudness cache

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Analysis/CachedLoudness.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Analysis/LoudnessCache.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/LoudnessCacheTests.cs`

**Interfaces:**
- Consumes: `ClipStatus`, `MeasureMode`.
- Produces:
  - `CachedLoudness` — serializable class with fields `int Status`, `float Lufs`, `float PeakDb`
  - `LoudnessCacheKey` — readonly struct with fields `string Guid`, `long Length`, `long Ticks`, `MeasureMode Mode`, and `bool IsValid` (false when `Guid` is null/empty — e.g. a procedural clip with no asset path). Public constructor for tests to build synthetic keys.
  - `LoudnessCache.Load(string path = null) -> LoudnessCache`
  - `LoudnessCache.KeyFor(AudioClip clip, MeasureMode mode) -> LoudnessCacheKey` — **the single place that derives `Ticks`**, as `Math.Max(assetLastWriteTicks, metaLastWriteTicks)`. Production callers MUST use this (or `KeyForPaths`) instead of hand-assembling a key — see rationale below.
  - `LoudnessCache.KeyForPaths(string guid, string assetPath, string metaPath, MeasureMode mode) -> LoudnessCacheKey` — the pure file-path form `KeyFor` delegates to; exists so the `.meta`-aware timestamp logic is directly testable against real temp files without an AssetDatabase-imported clip.
  - `LoudnessCache.TryGet(LoudnessCacheKey key, out CachedLoudness value) -> bool` — returns a copy; mutating it cannot leak into the cache.
  - `LoudnessCache.Put(LoudnessCacheKey key, CachedLoudness value)` — a null `value` or an invalid `key` is ignored rather than stored; the value is copied on the way in.
  - `LoudnessCache.Save()`
  - `LoudnessCache.Clear()` — also deletes the on-disk file (and any orphan `.tmp`), not just the in-memory entries.
  - `LoudnessCache.DefaultPath` = `"Library/HoppaAudioBalance/loudness-cache.json"`

> `Library/` because the cache is regenerable and must never be committed. Keying on `(guid, fileLength, ticks, mode)` rather than a content hash trades a rare unnecessary re-analysis (a content-preserving touch) for not hashing megabytes on every window open.
>
> **Key derivation is structural, not a documented caller contract — amended mid-execution, 2026-07-20, lead-approved (round 2; supersedes the round-1 amendment below).** `Ticks` is the combined max of the source asset's AND its `.meta`'s last-write ticks, because what is actually measured is the decoded `AudioClip`, which is a product of `.meta` importer settings (Force To Mono, Quality, Sample Rate Override, ...), not just the source bytes. Round 1 fixed this as an XML-doc contract on the caller (Task 10) — but `LoudnessCache` already reads files throughout (`File.Exists`/`ReadAllText`/`WriteAllText`/`Delete`) and its asmdef is Editor-only, so `AssetDatabase` was always available here; there was no reason the derivation had to live outside this class, and a documented-but-unenforced contract left this plan free to also hand Task 10 a complete, wrong, copy-paste-ready derivation a few hundred lines later (see deviation #7 in Plan self-review) — which it did. Fix: `KeyFor`/`KeyForPaths` are the only places that compute `Ticks`; `TryGet`/`Put` take a `LoudnessCacheKey` and can no longer be handed loose, possibly-wrong `(guid, length, ticks)` primitives at all. The measure mode also moved onto the key struct as a real field (superseding deviation #2's mangled-guid-string approach — see deviation #7).

> ⚠️ **The Steps 1/3/4 code blocks below are the ORIGINAL as-planned version and are SUPERSEDED.** They predate both round-1 and round-2 amendments above and use the old `(string guid, long length, long ticks)` API. Do not copy-paste them. The actually-shipped implementation uses `LoudnessCacheKey` / `LoudnessCache.KeyFor` / `KeyForPaths` as described in the interface block above — read the current source at `Packages/com.hoppa.audiobalance/Editor/Analysis/LoudnessCache.cs` and `LoudnessCacheKey.cs` for the real API. Kept here only as a historical record of the task's starting point.

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/LoudnessCacheTests.cs`:

```csharp
using System.IO;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class LoudnessCacheTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "hoppa-audiobalance-cache-test.json");
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static CachedLoudness Sample()
        {
            return new CachedLoudness
            {
                Status = (int)ClipStatus.Ok,
                Lufs = -21.5f,
                PeakDb = -3f
            };
        }

        [Test]
        public void TryGet_HitsOnUnchangedFileIdentity()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());

            Assert.IsTrue(cache.TryGet("guid-a", 1000, 5555, out var value));
            Assert.AreEqual(-21.5f, value.Lufs, 1e-4f);
        }

        [Test]
        public void TryGet_MissesWhenFileLengthChanges()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());

            Assert.IsFalse(cache.TryGet("guid-a", 2000, 5555, out _));
        }

        [Test]
        public void TryGet_MissesWhenModifiedTimeChanges()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 9999, out _));
        }

        [Test]
        public void TryGet_MissesForAnUnknownGuid()
        {
            var cache = LoudnessCache.Load(_path);

            Assert.IsFalse(cache.TryGet("never-seen", 1, 1, out _));
        }

        [Test]
        public void SaveThenLoad_RoundTripsEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Save();

            var reloaded = LoudnessCache.Load(_path);

            Assert.IsTrue(reloaded.TryGet("guid-a", 1000, 5555, out var value));
            Assert.AreEqual(-21.5f, value.Lufs, 1e-4f);
            Assert.AreEqual((int)ClipStatus.Ok, value.Status);
        }

        [Test]
        public void Put_OverwritesAnExistingEntryForTheSameGuid()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Put("guid-a", 1000, 6666, new CachedLoudness { Lufs = -9f });

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 5555, out _),
                "The stale identity must no longer hit.");
            Assert.IsTrue(cache.TryGet("guid-a", 1000, 6666, out var value));
            Assert.AreEqual(-9f, value.Lufs, 1e-4f);
        }

        [Test]
        public void Load_OnCorruptFile_DegradesToAnEmptyCacheWithoutThrowing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ this is not valid json ][");

            LoudnessCache cache = null;
            Assert.DoesNotThrow(() => cache = LoudnessCache.Load(_path));
            Assert.IsFalse(cache.TryGet("anything", 1, 1, out _));
        }

        [Test]
        public void Load_OnMissingFile_ReturnsAnEmptyCache()
        {
            var cache = LoudnessCache.Load(_path);

            Assert.IsFalse(cache.TryGet("anything", 1, 1, out _));
        }

        [Test]
        public void Clear_DropsAllEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Clear();

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 5555, out _));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `LoudnessCache` does not exist.

- [ ] **Step 3: Implement `CachedLoudness`**

`Packages/com.hoppa.audiobalance/Editor/Analysis/CachedLoudness.cs`:

```csharp
using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// One cached measurement. Status is stored as an int because JsonUtility handles enums
    /// inconsistently across Unity versions.
    /// </summary>
    [Serializable]
    public sealed class CachedLoudness
    {
        public int Status;
        public float Lufs;
        public float PeakDb;
    }
}
```

- [ ] **Step 4: Implement `LoudnessCache`**

`Packages/com.hoppa.audiobalance/Editor/Analysis/LoudnessCache.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Persists measurements so reopening the window is instant and only changed clips are
    /// re-analyzed. Lives under Library/ because it is regenerable and must not be committed.
    /// </summary>
    public sealed class LoudnessCache
    {
        public const string DefaultPath = "Library/HoppaAudioBalance/loudness-cache.json";

        [Serializable]
        private sealed class Entry
        {
            public string Guid;
            public long Length;
            public long Ticks;
            public CachedLoudness Value;
        }

        [Serializable]
        private sealed class Store
        {
            public List<Entry> Entries = new List<Entry>();
        }

        private readonly string _path;
        private readonly Dictionary<string, Entry> _byGuid = new Dictionary<string, Entry>();

        private LoudnessCache(string path)
        {
            _path = path;
        }

        public static LoudnessCache Load(string path = null)
        {
            var cache = new LoudnessCache(string.IsNullOrEmpty(path) ? DefaultPath : path);

            try
            {
                if (!File.Exists(cache._path))
                {
                    return cache;
                }

                var store = JsonUtility.FromJson<Store>(File.ReadAllText(cache._path));
                if (store?.Entries == null)
                {
                    return cache;
                }

                foreach (var entry in store.Entries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Guid))
                    {
                        cache._byGuid[entry.Guid] = entry;
                    }
                }
            }
            catch (Exception)
            {
                // A corrupt or unreadable cache is not worth failing over -- it is
                // regenerable by definition. Fall back to a full re-analysis.
                cache._byGuid.Clear();
            }

            return cache;
        }

        public bool TryGet(string guid, long length, long ticks, out CachedLoudness value)
        {
            value = null;

            if (string.IsNullOrEmpty(guid) || !_byGuid.TryGetValue(guid, out var entry))
            {
                return false;
            }

            if (entry.Length != length || entry.Ticks != ticks)
            {
                return false;
            }

            value = entry.Value;
            return value != null;
        }

        public void Put(string guid, long length, long ticks, CachedLoudness value)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            _byGuid[guid] = new Entry
            {
                Guid = guid,
                Length = length,
                Ticks = ticks,
                Value = value
            };
        }

        public void Clear()
        {
            _byGuid.Clear();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var store = new Store();
                store.Entries.AddRange(_byGuid.Values);

                File.WriteAllText(_path, JsonUtility.ToJson(store));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AudioBalance] Could not write the loudness cache: {exception.Message}");
            }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 9 new ones from this task.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): incremental loudness cache under Library/

Keyed on (guid, file length, mtime) rather than a content hash -- trades a
rare needless re-analysis for not hashing megabytes on every window open.
A corrupt cache degrades to a full re-analysis instead of throwing.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Analyzer orchestration

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Analysis/LoudnessAnalyzer.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/LoudnessAnalyzerTests.cs`

**Interfaces:**
- Consumes: `ClipSampleReader.TryRead`, `LufsMeter.Measure`, `PeakMeter.SamplePeakDb`, `LoudnessCache`, `LoudnessCache.KeyFor`, `LoudnessCacheKey`, `ClipAnalysis`, `MeasureMode`.
- Produces:
  - `LoudnessAnalyzer.Analyze(AudioClip clip, MeasureMode mode, LoudnessCache cache) -> ClipAnalysis` — internally calls `LoudnessCache.KeyFor(clip, mode)` for the cache key. Do **not** re-derive a guid/length/ticks identity by hand here — that duplication is exactly what produced the round-2 defect (see deviation #7 in Plan self-review). `LoudnessCache` owns key derivation entirely.
  - `LoudnessAnalyzer.FindClips(IEnumerable<string> projectRelativeFolders) -> List<AudioClip>`

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/LoudnessAnalyzerTests.cs`:

```csharp
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class LoudnessAnalyzerTests
    {
        private const int Rate = 48000;

        private static AudioClip MakeToneClip(string name, double peakDbfs, double seconds)
        {
            var frames = (int)(seconds * Rate);
            var clip = AudioClip.Create(name, frames, 2, Rate, false);
            clip.SetData(SignalFactory.Sine(peakDbfs, seconds, 2, Rate), 0);
            return clip;
        }

        [Test]
        public void Analyze_OnAToneClip_ReportsOkWithTheExpectedLoudness()
        {
            var clip = MakeToneClip("tone", -23.0, 4.0);

            var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, null);

            Assert.AreEqual(ClipStatus.Ok, analysis.Status);
            Assert.AreEqual(-23f, analysis.Lufs, 0.2f);
            Assert.AreSame(clip, analysis.Clip);
        }

        [Test]
        public void Analyze_PopulatesPeakDiagnostics()
        {
            var clip = MakeToneClip("tone", -6.0, 1.0);

            var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.MomentaryMax, null);

            Assert.AreEqual(-6f, analysis.PeakDb, 0.2f);
        }

        [Test]
        public void Analyze_OnASilentClip_ReportsSilent()
        {
            var clip = AudioClip.Create("quiet", Rate * 2, 2, Rate, false);

            var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, null);

            Assert.AreEqual(ClipStatus.Silent, analysis.Status);
        }

        [Test]
        public void Analyze_OnNullClip_ReportsUnanalyzable()
        {
            var analysis = LoudnessAnalyzer.Analyze(null, MeasureMode.Integrated, null);

            Assert.AreEqual(ClipStatus.Unanalyzable, analysis.Status);
        }

        [Test]
        public void Analyze_HonoursTheMeasureMode()
        {
            // Loud burst then a long quiet tail: the two modes must disagree.
            var seconds = 4.5;
            var frames = (int)(seconds * Rate);
            var clip = AudioClip.Create("oneshot", frames, 2, Rate, false);
            clip.SetData(SignalFactory.Concat(
                SignalFactory.Sine(-18.0, 0.5, 2, Rate),
                SignalFactory.Sine(-50.0, 4.0, 2, Rate)), 0);

            var integrated = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, null);
            var momentary = LoudnessAnalyzer.Analyze(clip, MeasureMode.MomentaryMax, null);

            Assert.Greater(momentary.Lufs, integrated.Lufs);
        }

        [Test]
        public void FindClips_WithNullOrEmptyFolders_ReturnsAnEmptyList()
        {
            Assert.AreEqual(0, LoudnessAnalyzer.FindClips(null).Count);
            Assert.AreEqual(0, LoudnessAnalyzer.FindClips(new string[0]).Count);
        }

        [Test]
        public void FindClips_SkipsFoldersThatDoNotExist()
        {
            var clips = LoudnessAnalyzer.FindClips(new[] { "Assets/ThisFolderDoesNotExist" });

            Assert.AreEqual(0, clips.Count);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `LoudnessAnalyzer` does not exist.

- [ ] **Step 3: Implement `LoudnessAnalyzer`**

`Packages/com.hoppa.audiobalance/Editor/Analysis/LoudnessAnalyzer.cs`:

> **Round-2 amendment (2026-07-20, lead-approved):** this code block previously re-derived a
> cache identity by hand (a private `ResolveIdentity`/`AssetIdentity` pair stat'ing only the
> asset file, mirrored below in the corrected form). That was a plan-authoring-time defect: it
> silently dropped the `.meta` timestamp that Task 9's `LoudnessCache` fix (see deviation #7 in
> Plan self-review) exists specifically to fold in, and — because Task 10 hadn't been implemented
> yet when the defect was caught — it was purely a documentation bug, but a live one: the *next*
> implementer to work this section top-to-bottom would have retyped it verbatim. `LoudnessCache`
> now owns identity derivation completely (`KeyFor`/`KeyForPaths`); `LoudnessAnalyzer` must not
> duplicate it.

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Decode -> measure -> cache. The single entry point the window calls per clip.</summary>
    public static class LoudnessAnalyzer
    {
        public static ClipAnalysis Analyze(AudioClip clip, MeasureMode mode, LoudnessCache cache)
        {
            if (clip == null)
            {
                return ClipAnalysis.Unanalyzable(null, "clip is null");
            }

            // LoudnessCache.KeyFor is the ONLY place that derives the cache identity (guid,
            // length, ticks, mode) -- it folds in the .meta timestamp as well as the asset's.
            // Do not re-stat the asset file here; that duplication is exactly what produced the
            // round-2 plan defect this section once contained.
            var key = LoudnessCache.KeyFor(clip, mode);

            if (cache != null && key.IsValid && cache.TryGet(key, out var cached))
            {
                return new ClipAnalysis(clip, (ClipStatus)cached.Status, cached.Lufs, cached.PeakDb);
            }

            var analysis = Measure(clip, mode);

            if (cache != null && key.IsValid)
            {
                cache.Put(key, new CachedLoudness
                {
                    Status = (int)analysis.Status,
                    Lufs = analysis.Lufs,
                    PeakDb = analysis.PeakDb
                });
            }

            return analysis;
        }

        public static List<AudioClip> FindClips(IEnumerable<string> projectRelativeFolders)
        {
            var clips = new List<AudioClip>();
            if (projectRelativeFolders == null)
            {
                return clips;
            }

            var valid = new List<string>();
            foreach (var folder in projectRelativeFolders)
            {
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                {
                    valid.Add(folder);
                }
            }

            if (valid.Count == 0)
            {
                return clips;
            }

            var seen = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", valid.ToArray()))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path))
                {
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        private static ClipAnalysis Measure(AudioClip clip, MeasureMode mode)
        {
            if (!ClipSampleReader.TryRead(clip, out var samples, out var error))
            {
                return ClipAnalysis.Unanalyzable(clip, error);
            }

            var loudness = LufsMeter.Measure(samples, clip.channels, clip.frequency, mode);
            if (loudness.IsSilent)
            {
                return ClipAnalysis.Silent(clip);
            }

            return ClipAnalysis.Ok(
                clip,
                loudness.Lufs,
                PeakMeter.SamplePeakDb(samples));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 7 new ones from this task.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): analyzer orchestration (decode -> measure -> cache)

Cache identity comes entirely from LoudnessCache.KeyFor(clip, mode) -- the
measure mode is a real field on the key struct (the same clip measured
Integrated and MomentaryMax are two different answers), and Ticks already
folds in the .meta timestamp, not just the asset's. LoudnessAnalyzer does
not re-derive any part of the identity itself. Procedural clips with no
asset path get an invalid key and bypass the cache rather than colliding on
an empty guid.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Window shell — toolbar, categories, Analyze

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceRow.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceSession.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/AudioBalanceSessionTests.cs`

**Interfaces:**
- Consumes: `AudioBalanceProfile`, `LoudnessAnalyzer`, `LoudnessCache`, `GainSolver`, `ClipAnalysis`, `GainResult`.
- Produces:
  - `AudioBalanceRow` — class with fields `AudioClip Clip`, `ClipAnalysis Analysis`, `GainResult Gain`
  - `AudioBalanceSession` — `Rows` (`IReadOnlyList<AudioBalanceRow>`), `AnchorLufs` (float), `AnchorStatus` (`ClipStatus`), `bool Analyze(AudioBalanceProfile profile, LoudnessCache cache, Func<AudioClip, int, int, bool> onProgress = null)`, `Resolve(AudioBalanceProfile profile)`
  - `AudioBalanceWindow.Open()` (menu `Window ▸ Hoppa ▸ Audio Balance`)

> Analysis lives in `AudioBalanceSession`, not in the window, precisely so it can be tested without opening an `EditorWindow`. The window is a thin renderer over it.

> **`Analyze` takes a progress callback (deviation #14).** `onProgress(clip, index, total)` is invoked *before* each clip is measured and returns `true` to cancel. `Analyze` returns `false` if it was cancelled, `true` otherwise. This is an API change against the original plan, which had `Analyze` return `void` — it is required because the original window drew a progress bar in a loop that did no work and then called a single blocking `Analyze`, so the bar swept to 100% instantly and Cancel could not stop anything. The callback is the only way the window can report real progress or honour a cancel.

> **When to call `Analyze` vs `Resolve` (deviation #10).** `Resolve` re-runs `GainSolver` over the *existing* measurements. That is correct **only** for the trim slider. A category carries its own `MeasureMode` (shipped defaults: Music = `Integrated`, SFX/UI = `MomentaryMax`), so changing a clip's category, editing a category's mode, or bulk-assigning changes *how the clip must be measured* — `Resolve` cannot change `Analysis.Lufs` and would silently keep a measurement taken under the old mode. Every category or mode edit must call `Analyze(profile, cache)`. That is nearly free: `Mode` is a field on `LoudnessCacheKey`, so every clip whose effective mode did not change is a straight cache hit and is never re-decoded.

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/AudioBalanceSessionTests.cs`:

```csharp
using System.Linq;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class AudioBalanceSessionTests
    {
        private const int Rate = 48000;

        private static AudioClip Tone(string name, double peakDbfs, double seconds = 4.0)
        {
            var frames = (int)(seconds * Rate);
            var clip = AudioClip.Create(name, frames, 2, Rate, false);
            clip.SetData(SignalFactory.Sine(peakDbfs, seconds, 2, Rate), 0);
            return clip;
        }

        private static AudioBalanceProfile Profile(AudioClip anchor, params AudioClip[] others)
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            profile.Anchor = anchor;

            profile.SettingsFor(anchor).Category = "Music";
            foreach (var clip in others)
            {
                profile.SettingsFor(clip).Category = "Music";
            }

            return profile;
        }

        [Test]
        public void Analyze_MeasuresTheAnchorAndExposesItsLoudness()
        {
            var anchor = Tone("anchor", -23.0);
            var session = new AudioBalanceSession();

            session.Analyze(Profile(anchor), null);

            Assert.AreEqual(ClipStatus.Ok, session.AnchorStatus);
            Assert.AreEqual(-23f, session.AnchorLufs, 0.2f);
        }

        [Test]
        public void Analyze_ProducesOneRowPerProfileClip()
        {
            var anchor = Tone("anchor", -23.0);
            var other = Tone("other", -30.0);
            var session = new AudioBalanceSession();

            session.Analyze(Profile(anchor, other), null);

            Assert.AreEqual(2, session.Rows.Count);
            CollectionAssert.AreEquivalent(
                new[] { "anchor", "other" },
                session.Rows.Select(r => r.Clip.name).ToArray());
        }

        [Test]
        public void Analyze_PinsTheQuietestClipAtZeroDbAndAttenuatesTheLouderOne()
        {
            // Both clips are in "Music" (offset 0), so the only thing separating them is
            // their measured loudness. -12 dBFS is 11 dB louder than -23, so once the
            // headroom pass runs the quiet clip must pin at 0 and the loud one must sit
            // ~11 dB below it. Asserting only Max(...) == 0 would be vacuous: GainSolver
            // subtracts the max from every raw gain, so that holds for ANY input.
            var quiet = Tone("anchor", -23.0);
            var loud = Tone("loud", -12.0);
            var session = new AudioBalanceSession();

            session.Analyze(Profile(quiet, loud), null);

            var quietGain = session.Rows.First(r => r.Clip.name == "anchor").Gain.FinalGainDb;
            var loudGain = session.Rows.First(r => r.Clip.name == "loud").Gain.FinalGainDb;

            Assert.AreEqual(0f, quietGain, 0.3f,
                "The clip needing the most gain is the one pinned at 0 dB.");
            Assert.AreEqual(-11f, loudGain, 0.5f,
                "The louder clip is attenuated by exactly the loudness difference.");
        }

        [Test]
        public void Analyze_AQuieterClipEndsUpLouderInGainThanALoudOne()
        {
            var anchor = Tone("anchor", -23.0);
            var quiet = Tone("quiet", -35.0);
            var session = new AudioBalanceSession();

            session.Analyze(Profile(anchor, quiet), null);

            var quietGain = session.Rows.First(r => r.Clip.name == "quiet").Gain.FinalGainDb;
            var anchorGain = session.Rows.First(r => r.Clip.name == "anchor").Gain.FinalGainDb;

            Assert.Greater(quietGain, anchorGain,
                "The quieter source needs relatively more gain to reach the same target.");
        }

        [Test]
        public void Analyze_WithNoAnchor_LeavesTheAnchorStatusUnanalyzableAndDoesNotThrow()
        {
            var clip = Tone("lonely", -23.0);
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            profile.SettingsFor(clip);

            var session = new AudioBalanceSession();

            Assert.DoesNotThrow(() => session.Analyze(profile, null));
            Assert.AreEqual(ClipStatus.Unanalyzable, session.AnchorStatus);
        }

        [Test]
        public void Analyze_WithANullProfile_ClearsRowsWithoutThrowing()
        {
            var session = new AudioBalanceSession();

            Assert.DoesNotThrow(() => session.Analyze(null, null));
            Assert.AreEqual(0, session.Rows.Count);
        }

        [Test]
        public void Resolve_AppliesATrimChangeWithoutReMeasuring()
        {
            var anchor = Tone("anchor", -23.0);
            var other = Tone("other", -23.0);
            var profile = Profile(anchor, other);
            var session = new AudioBalanceSession();
            session.Analyze(profile, null);

            var measuredBefore = session.Rows.First(r => r.Clip.name == "other").Analysis.Lufs;
            var before = session.Rows.First(r => r.Clip.name == "other").Gain.FinalGainDb;

            // A trim is the ONE edit Resolve is correct for: it moves the target only,
            // and cannot change how the clip must be measured.
            profile.SettingsFor(other).TrimDb = -6f;
            session.Resolve(profile);

            var after = session.Rows.First(r => r.Clip.name == "other").Gain.FinalGainDb;
            var measuredAfter = session.Rows.First(r => r.Clip.name == "other").Analysis.Lufs;

            Assert.Less(after, before - 5f, "A -6 dB trim must lower the solved gain.");
            Assert.AreEqual(measuredBefore, measuredAfter, 1e-4f,
                "Resolve must not disturb the measurement.");
        }

        [Test]
        public void ChangingACategoryWithADifferentMeasureMode_ReMeasuresTheClip()
        {
            // "Music" is Integrated; "SFX" is MomentaryMax. A clip whose level is not
            // constant reads differently under the two modes, so moving it between these
            // categories MUST re-measure -- Resolve alone would keep the Integrated
            // number and bake a gain derived from the wrong measurement.
            var anchor = Tone("anchor", -23.0);
            var burst = Burst("burst");
            var profile = Profile(anchor);
            profile.SettingsFor(burst).Category = "Music";

            var session = new AudioBalanceSession();
            session.Analyze(profile, null);

            var integrated = session.Rows.First(r => r.Clip.name == "burst").Analysis.Lufs;

            profile.SettingsFor(burst).Category = "SFX";
            session.Analyze(profile, null);

            var momentary = session.Rows.First(r => r.Clip.name == "burst").Analysis.Lufs;

            Assert.AreNotEqual(integrated, momentary, 0.5f,
                "Switching to a category with a different MeasureMode must re-measure. " +
                "If these are equal, the category edit path is calling Resolve, not Analyze.");
            Assert.Greater(momentary, integrated,
                "MomentaryMax tracks the loud burst; Integrated is dragged down by the tail.");
        }

        [Test]
        public void Resolve_LeavesTheMeasurementUntouchedEvenWhenTheModeWouldHaveChanged()
        {
            // Guards the inverse: Resolve is honest about what it does NOT do. This is the
            // reason the window must not call Resolve on a category edit.
            var anchor = Tone("anchor", -23.0);
            var burst = Burst("burst");
            var profile = Profile(anchor);
            profile.SettingsFor(burst).Category = "Music";

            var session = new AudioBalanceSession();
            session.Analyze(profile, null);

            var before = session.Rows.First(r => r.Clip.name == "burst").Analysis.Lufs;

            profile.SettingsFor(burst).Category = "SFX";
            session.Resolve(profile);

            Assert.AreEqual(before, session.Rows.First(r => r.Clip.name == "burst").Analysis.Lufs,
                1e-4f, "Resolve re-solves; it never re-measures.");
        }

        [Test]
        public void Analyze_WithNoAnchor_DoesNotFlagEveryRowAsAnOutlier()
        {
            // With no anchor there is no reference, so no outlier judgement is meaningful.
            // The naive fallback (anchorLufs = 0, i.e. digital full scale) would give
            // typical -20 LUFS content raw ~= +20 dB and trip the 12 dB threshold on
            // every healthy row -- a wall of markers on a fresh profile.
            var a = Tone("a", -20.0);
            var b = Tone("b", -22.0);
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            profile.SettingsFor(a).Category = "Music";
            profile.SettingsFor(b).Category = "Music";

            var session = new AudioBalanceSession();
            session.Analyze(profile, null);

            Assert.AreEqual(ClipStatus.Unanalyzable, session.AnchorStatus);
            CollectionAssert.IsEmpty(
                session.Rows.Where(r => r.Gain.IsOutlier).Select(r => r.Clip.name).ToArray(),
                "No anchor means no outlier reference, so nothing may be flagged.");
        }

        [Test]
        public void Analyze_WithAnAnchor_StillFlagsAGenuineOutlier()
        {
            // The suppression above must not disable the check when an anchor IS present.
            var anchor = Tone("anchor", -20.0);
            var broken = Tone("broken", -60.0);
            var session = new AudioBalanceSession();

            session.Analyze(Profile(anchor, broken), null);

            Assert.IsTrue(session.Rows.First(r => r.Clip.name == "broken").Gain.IsOutlier,
                "40 dB below the anchor is well past the 12 dB outlier threshold.");
        }

        [Test]
        public void Analyze_ReportsProgressAndHonoursCancel()
        {
            var anchor = Tone("anchor", -23.0);
            var profile = Profile(anchor, Tone("b", -20.0), Tone("c", -20.0));
            var session = new AudioBalanceSession();

            var seen = 0;
            var completed = session.Analyze(profile, null, (clip, index, total) =>
            {
                seen++;
                return true; // cancel at the first clip
            });

            Assert.IsFalse(completed, "Analyze must report that it was cancelled.");
            Assert.AreEqual(1, seen, "Cancelling at the first clip must stop the work.");
        }
    }
}
```

> `Burst` is a second signal helper used by the mode-sensitivity tests — a short loud
> attack followed by a long quiet tail, which is exactly the shape that reads differently
> under `Integrated` (gated, dragged down by the tail) and `MomentaryMax` (tracks the
> attack). Add it beside `Tone`:
>
> ```csharp
>         private static AudioClip Burst(string name)
>         {
>             // 0.5 s at -12 dBFS, then 3.5 s at -50 dBFS. This is the same shape the
>             // Task 4 MomentaryMax tests use, so the expected split is already proven.
>             var all = SignalFactory.Concat(
>                 SignalFactory.Sine(-12.0, 0.5, 2, Rate),
>                 SignalFactory.Sine(-50.0, 3.5, 2, Rate));
>
>             var clip = AudioClip.Create(name, all.Length / 2, 2, Rate, false);
>             clip.SetData(all, 0);
>             return clip;
>         }
> ```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `AudioBalanceSession` does not exist.

- [ ] **Step 3: Implement `AudioBalanceRow`**

`Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceRow.cs`:

```csharp
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>One line in the window: a clip plus its measurement and solved gain.</summary>
    public sealed class AudioBalanceRow
    {
        public AudioClip Clip;
        public ClipAnalysis Analysis;
        public GainResult Gain;
    }
}
```

- [ ] **Step 4: Implement `AudioBalanceSession`**

`Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// The window's model. Analysis and solving live here rather than in the EditorWindow so
    /// they can be tested without opening any UI.
    /// </summary>
    public sealed class AudioBalanceSession
    {
        /// <summary>
        /// Used when no usable anchor exists. The value is arithmetically irrelevant to
        /// FinalGainDb -- the anchor term cancels in GainSolver's headroom subtraction -- but
        /// it does feed RawGainDb, and therefore the outlier check. We suppress the outlier
        /// flag outright in that case (see Resolve), so this constant only keeps RawGainDb in
        /// a sane range for the readout rather than parking it 20 dB off.
        /// </summary>
        private const float NoAnchorReferenceLufs = -23f;

        private readonly List<AudioBalanceRow> _rows = new List<AudioBalanceRow>();

        public IReadOnlyList<AudioBalanceRow> Rows => _rows;

        public float AnchorLufs { get; private set; }

        public ClipStatus AnchorStatus { get; private set; } = ClipStatus.Unanalyzable;

        /// <summary>
        /// Measures the anchor and every profile clip, then solves gains.
        ///
        /// <para>
        /// This is the correct entry point for ANY edit that can change how a clip is
        /// measured -- a category assignment, a bulk assign, or a category's MeasureMode.
        /// It is cheap to call: <see cref="LoudnessCacheKey"/> carries the mode, so a clip
        /// whose effective mode did not change is a cache hit and is never re-decoded.
        /// </para>
        ///
        /// <para>
        /// <paramref name="onProgress"/> is invoked before each clip is measured with
        /// (clip, zero-based index, total) and returns true to cancel. Returns false if the
        /// run was cancelled, true if it completed. On cancel, rows measured so far are kept
        /// and still solved, so the table shows partial-but-consistent results rather than
        /// blanking out.
        /// </para>
        /// </summary>
        public bool Analyze(AudioBalanceProfile profile, LoudnessCache cache,
            Func<AudioClip, int, int, bool> onProgress = null)
        {
            _rows.Clear();
            AnchorLufs = 0f;
            AnchorStatus = ClipStatus.Unanalyzable;

            if (profile == null)
            {
                return true;
            }

            if (profile.Anchor != null)
            {
                var anchorAnalysis = LoudnessAnalyzer.Analyze(
                    profile.Anchor, profile.ModeFor(profile.Anchor), cache);

                AnchorStatus = anchorAnalysis.Status;
                AnchorLufs = anchorAnalysis.Lufs;
            }

            // Snapshot first: profile.ModeFor -> SettingsFor can append to profile.Clips,
            // and mutating the list we are iterating would throw.
            var pending = new List<AudioClip>();
            foreach (var settings in profile.Clips)
            {
                if (settings?.Clip != null)
                {
                    pending.Add(settings.Clip);
                }
            }

            var completed = true;

            for (var i = 0; i < pending.Count; i++)
            {
                var clip = pending[i];

                if (onProgress != null && onProgress(clip, i, pending.Count))
                {
                    completed = false;
                    break;
                }

                _rows.Add(new AudioBalanceRow
                {
                    Clip = clip,
                    Analysis = LoudnessAnalyzer.Analyze(clip, profile.ModeFor(clip), cache)
                });
            }

            Resolve(profile);
            return completed;
        }

        /// <summary>
        /// Re-solves gains from the existing measurements.
        ///
        /// <para>
        /// Correct for the trim slider ONLY. A trim moves the target and cannot change how
        /// the clip must be measured. A category edit is a different animal: a category
        /// carries its own <see cref="MeasureMode"/>, so changing a clip's category (or a
        /// category's mode) changes the measurement itself, which this method cannot do --
        /// it would silently keep the old-mode number and bake a wrong gain. Route those
        /// edits through <see cref="Analyze"/> instead.
        /// </para>
        /// </summary>
        public void Resolve(AudioBalanceProfile profile)
        {
            if (profile == null || _rows.Count == 0)
            {
                return;
            }

            var analyses = new List<ClipAnalysis>(_rows.Count);
            foreach (var row in _rows)
            {
                analyses.Add(row.Analysis);
            }

            var anchorOk = AnchorStatus == ClipStatus.Ok;

            var solved = GainSolver.Solve(
                analyses,
                anchorOk ? AnchorLufs : NoAnchorReferenceLufs,
                profile.OffsetDbFor,
                profile.TrimDbFor);

            for (var i = 0; i < _rows.Count && i < solved.Count; i++)
            {
                var result = solved[i];

                // With no usable anchor there is no reference, so "this clip is 12 dB from
                // its target" is not a judgement we are entitled to make. Suppress rather
                // than mislead: an unexplained wall of outlier markers on a fresh profile
                // reads as a broken tool.
                if (!anchorOk && result.IsOutlier)
                {
                    result = new GainResult(result.Clip, result.Status,
                        result.RawGainDb, result.FinalGainDb, false);
                }

                _rows[i].Gain = result;
            }
        }

        public void Clear()
        {
            _rows.Clear();
            AnchorLufs = 0f;
            AnchorStatus = ClipStatus.Unanalyzable;
        }
    }
}
```

- [ ] **Step 5: Implement the window shell**

`Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Audio Balance panel. Laid out with absolute GUI.* rects rather than GUILayout: a
    /// GUILayout island whose buttons open modal dialogs corrupts the layout stack when the
    /// modal throws ExitGUIException, which is exactly the crash class LevelEditorWindow hit.
    ///
    /// <para>
    /// That immunity is what makes it safe to call EditorUtility.OpenFolderPanel and
    /// DisplayDialog from inside OnGUI here (see AddFolder) -- with no layout stack to
    /// corrupt, an ExitGUIException unwinding through this OnGUI has nothing to leave
    /// inconsistent. The convention still holds for anything added later: after a modal
    /// that mutates state the GUI is about to keep reading, call GUIUtility.ExitGUI() to
    /// abandon the rest of the frame rather than drawing against half-changed state. Any
    /// future GUILayout block in this window would forfeit the immunity entirely.
    /// </para>
    /// </summary>
    public sealed class AudioBalanceWindow : EditorWindow
    {
        private const float ToolbarHeight = 24f;
        private const float RowHeight = 20f;
        private const float Pad = 6f;

        /// <summary>
        /// Height of the categories box, computed rather than fixed. The block is a header
        /// row, one row per category, and the Add Category button; at RowHeight = 20 a fixed
        /// 130f overflowed at just FIVE categories (4 + 20 + 20n + 18 needs 142f at n = 5),
        /// spilling over the clip table with overlapping click rects. The window ships an
        /// "Add Category" button and defaults to three, so that is two clicks away.
        /// </summary>
        private float CategoryBlockHeight =>
            RowHeight * ((_profile?.Categories?.Count ?? 0) + 2) + 10f;

        private AudioBalanceProfile _profile;
        private readonly AudioBalanceSession _session = new AudioBalanceSession();
        private LoudnessCache _cache;
        private Vector2 _clipScroll;

        [MenuItem("Window/Hoppa/Audio Balance")]
        public static void Open()
        {
            var window = GetWindow<AudioBalanceWindow>(false, "Audio Balance", true);
            window.minSize = new Vector2(720f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            _cache = LoudnessCache.Load();
        }

        private void OnDisable()
        {
            _cache?.Save();
        }

        private void OnGUI()
        {
            var y = Pad;

            DrawToolbar(new Rect(Pad, y, position.width - Pad * 2f, ToolbarHeight));
            y += ToolbarHeight + Pad;

            if (_profile == null)
            {
                // ASCII only in IMGUI captions: default-font glyph coverage is not
                // guaranteed, and a blank caption is indistinguishable from a broken one.
                GUI.Label(new Rect(Pad, y, position.width - Pad * 2f, 40f),
                    "Assign an Audio Balance Profile to begin.\n" +
                    "Create one via Assets > Create > Hoppa > Audio > Audio Balance Profile.");
                return;
            }

            DrawAnchor(new Rect(Pad, y, position.width - Pad * 2f, RowHeight));
            y += RowHeight + Pad;

            DrawCategories(new Rect(Pad, y, position.width - Pad * 2f, CategoryBlockHeight));
            y += CategoryBlockHeight + Pad;

            DrawClips(new Rect(Pad, y, position.width - Pad * 2f, position.height - y - Pad));
        }

        private void DrawToolbar(Rect rect)
        {
            var x = rect.x;

            GUI.Label(new Rect(x, rect.y, 50f, rect.height), "Profile");
            x += 54f;

            var picked = (AudioBalanceProfile)EditorGUI.ObjectField(
                new Rect(x, rect.y, 220f, rect.height - 4f), _profile,
                typeof(AudioBalanceProfile), false);

            if (picked != _profile)
            {
                _profile = picked;
                _session.Clear();

                // Task 12 adds a _selected set here too -- see ClearSelection() in that task.
                // Anything holding onto clips from the old profile MUST be dropped here.
            }

            x += 226f;

            if (GUI.Button(new Rect(x, rect.y, 110f, rect.height - 4f), "Add Folder…"))
            {
                AddFolder();
            }

            x += 116f;

            GUI.enabled = _profile != null;

            if (GUI.Button(new Rect(x, rect.y, 90f, rect.height - 4f), "Analyze"))
            {
                RunAnalysis();
            }

            GUI.enabled = true;
        }

        private void DrawAnchor(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y, 50f, rect.height), "Anchor");

            var picked = (AudioClip)EditorGUI.ObjectField(
                new Rect(rect.x + 54f, rect.y, 220f, rect.height - 2f),
                _profile.Anchor, typeof(AudioClip), false);

            if (picked != _profile.Anchor)
            {
                Undo.RecordObject(_profile, "Set Audio Balance Anchor");
                _profile.Anchor = picked;
                EditorUtility.SetDirty(_profile);
            }

            var summary = _session.AnchorStatus == ClipStatus.Ok
                ? $"{_session.AnchorLufs:0.0} LUFS"
                : "not analyzed";

            GUI.Label(new Rect(rect.x + 282f, rect.y, 240f, rect.height), summary);
        }

        private void DrawCategories(Rect rect)
        {
            GUI.Box(rect, GUIContent.none);

            var y = rect.y + 4f;
            GUI.Label(new Rect(rect.x + 6f, y, 240f, RowHeight), "Categories (offset dB)");
            y += RowHeight;

            for (var i = 0; i < _profile.Categories.Count; i++)
            {
                var category = _profile.Categories[i];
                var x = rect.x + 6f;

                // BeginChangeCheck rather than comparing values every frame: a raw
                // comparison fires Undo.RecordObject on EVERY OnGUI pass where the value
                // differs, so one drag of a field produces dozens of undo entries and
                // re-runs the solver for each. Commit once, when the edit ends.
                EditorGUI.BeginChangeCheck();

                var name = EditorGUI.DelayedTextField(
                    new Rect(x, y, 120f, RowHeight - 2f), category.Name);
                x += 126f;

                var offset = EditorGUI.FloatField(new Rect(x, y, 60f, RowHeight - 2f), category.OffsetDb);
                x += 66f;

                var mode = (MeasureMode)EditorGUI.EnumPopup(
                    new Rect(x, y, 130f, RowHeight - 2f), category.Mode);

                if (EditorGUI.EndChangeCheck())
                {
                    var modeChanged = mode != category.Mode;
                    var nameChanged = name != category.Name;

                    Undo.RecordObject(_profile, "Edit Audio Category");
                    category.Name = name;
                    category.OffsetDb = offset;
                    category.Mode = mode;
                    EditorUtility.SetDirty(_profile);

                    // A mode change changes the MEASUREMENT, which Resolve cannot do. A name
                    // change re-points every clip that referenced the old name, which can
                    // change their effective mode too. Both must re-analyze; the cache makes
                    // it near-free for any clip whose mode did not actually move.
                    if (modeChanged || nameChanged)
                    {
                        RunAnalysis();
                    }
                    else
                    {
                        _session.Resolve(_profile);
                    }
                }

                y += RowHeight;
            }

            if (GUI.Button(new Rect(rect.x + 6f, y, 110f, RowHeight - 2f), "Add Category"))
            {
                Undo.RecordObject(_profile, "Add Audio Category");
                _profile.Categories.Add(new AudioCategory { Name = "New", OffsetDb = 0f });
                EditorUtility.SetDirty(_profile);
            }
        }

        /// <summary>Replaced with the full sortable table in Task 12.</summary>
        private void DrawClips(Rect rect)
        {
            GUI.Box(rect, GUIContent.none);

            var content = new Rect(0f, 0f, rect.width - 20f, _session.Rows.Count * RowHeight + 4f);
            _clipScroll = GUI.BeginScrollView(rect, _clipScroll, content);

            var y = 2f;
            foreach (var row in _session.Rows)
            {
                var label = row.Analysis.Status == ClipStatus.Ok
                    ? $"{row.Clip.name}    {row.Analysis.Lufs:0.0} LUFS    {row.Gain.FinalGainDb:0.0} dB"
                    : $"{row.Clip.name}    {row.Analysis.Reason}";

                GUI.Label(new Rect(4f, y, content.width - 8f, RowHeight), label);
                y += RowHeight;
            }

            GUI.EndScrollView();
        }

        private void AddFolder()
        {
            var absolute = EditorUtility.OpenFolderPanel("Add Audio Folder", "Assets", string.Empty);
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            var relative = ToProjectRelative(absolute);
            if (relative == null)
            {
                EditorUtility.DisplayDialog("Audio Balance",
                    "Pick a folder inside this project's Assets directory.", "OK");
                return;
            }

            if (!_profile.Folders.Contains(relative))
            {
                Undo.RecordObject(_profile, "Add Audio Folder");
                _profile.Folders.Add(relative);
                EditorUtility.SetDirty(_profile);
            }
        }

        /// <summary>
        /// Absolute paths in a committed asset break every other checkout, so folders are
        /// always stored relative to the project root.
        /// </summary>
        private static string ToProjectRelative(string absolutePath)
        {
            var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return null;
            }

            var normalizedRoot = projectRoot.Replace('\\', '/') + "/";
            var normalizedPath = absolutePath.Replace('\\', '/');

            return normalizedPath.StartsWith(normalizedRoot)
                ? normalizedPath.Substring(normalizedRoot.Length)
                : null;
        }

        private void RunAnalysis()
        {
            var discovered = LoudnessAnalyzer.FindClips(_profile.Folders);

            Undo.RecordObject(_profile, "Scan Audio Folders");
            foreach (var clip in discovered)
            {
                _profile.SettingsFor(clip);
            }

            EditorUtility.SetDirty(_profile);

            try
            {
                // The progress bar is driven BY the analysis, not alongside it. An earlier
                // revision ran a display-only loop and then called Analyze once, so the bar
                // swept to 100% instantly, the editor froze with no feedback, and Cancel
                // exited only the display loop while the real work ran to completion.
                var completed = _session.Analyze(_profile, _cache, (clip, index, total) =>
                    EditorUtility.DisplayCancelableProgressBar(
                        "Audio Balance",
                        $"Analyzing {clip.name}  ({index + 1}/{total})",
                        total == 0 ? 0f : index / (float)total));

                if (!completed)
                {
                    ShowNotification(new GUIContent("Analysis cancelled - showing partial results"));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _cache?.Save();
            Repaint();
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 12 new ones from this task.

- [ ] **Step 7: Open the window and confirm it renders**

Open `Window ▸ Hoppa ▸ Audio Balance` in the Unity Editor. Confirm: the profile field renders, the empty-state message appears with no profile assigned, and no console errors or layout exceptions occur.

Then add categories with the **Add Category** button until there are six. Confirm the categories box grows to fit them and never overlaps the clip table below it — a fixed-height box overflowed at five.

- [ ] **Step 8: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): Audio Balance window shell with analysis session

Analysis and gain solving live in AudioBalanceSession rather than the
EditorWindow so they unit-test without opening any UI. Resolve() re-solves
from existing measurements when only a category or trim changed, avoiding
a needless re-decode. Layout uses absolute GUI.* rects throughout.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: Sortable, filterable clip table with bulk assign

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/ClipSortMode.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/ClipListView.cs`
- Modify: `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs` (replace `DrawClips`)
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/ClipListViewTests.cs`

**Interfaces:**
- Consumes: `AudioBalanceRow`, `ClipStatus`, `AudioBalanceProfile`.
- Produces:
  - `enum ClipSortMode { Name, Loudness, Gain, Category }`
  - `ClipListView.BuildVisible(IReadOnlyList<AudioBalanceRow> rows, string filter, ClipSortMode sort, bool ascending, Func<AudioBalanceRow, string> categoryOf) -> List<AudioBalanceRow>`
  - `ClipListView.StatusIcon(AudioBalanceRow row) -> string`
  - `ClipListView.BulkAssignCategory(IEnumerable<AudioBalanceRow> rows, AudioBalanceProfile profile, string category)`

> Filtering and sorting are pure functions over the row list, so they test directly. Only the drawing lives in the window.

> **`BuildVisible` takes a category lookup, not the profile (deviation #11).** The original signature took `AudioBalanceProfile` and called `profile.SettingsFor(row.Clip)` to sort by category. `SettingsFor` **appends a new `ClipSettings` to the asset on a miss** — so a function documented as pure was writing to a `ScriptableObject` from inside `OnGUI`, with no `Undo.RecordObject` and no `EditorUtility.SetDirty`. Those writes are not undoable and not reliably persisted, but they *are* picked up by any `AssetDatabase.SaveAssets()` — which `GainTableWriter.Write` calls. It was also O(n²) per repaint (`SettingsFor` is a linear scan, once per row), roughly 250k comparisons at 500 clips in the sort alone and again in the row drawing. Settings are now resolved **once**, outside the render path, into a dictionary the window rebuilds only when the profile or row set actually changes; `BuildVisible` receives a read-only lookup and genuinely cannot mutate anything.

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/ClipListViewTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class ClipListViewTests
    {
        private static AudioBalanceRow Row(string name, float lufs, float gainDb,
            ClipStatus status = ClipStatus.Ok, bool outlier = false)
        {
            var clip = AudioClip.Create(name, 128, 1, 44100, false);
            return new AudioBalanceRow
            {
                Clip = clip,
                Analysis = status == ClipStatus.Ok
                    ? ClipAnalysis.Ok(clip, lufs, -3f)
                    : new ClipAnalysis(clip, status, 0f, -80f, "reason"),
                Gain = new GainResult(clip, status, gainDb, gainDb, outlier)
            };
        }

        private static AudioBalanceProfile Profile(params AudioBalanceRow[] rows)
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            foreach (var row in rows)
            {
                profile.SettingsFor(row.Clip);
            }

            return profile;
        }

        /// <summary>
        /// The read-only category lookup BuildVisible now takes. Resolving settings up front
        /// is exactly what the window does -- BuildVisible must not be able to touch the
        /// profile at all.
        /// </summary>
        private static Func<AudioBalanceRow, string> Lookup(AudioBalanceProfile profile)
        {
            return row => profile == null || row?.Clip == null
                ? string.Empty
                : profile.SettingsFor(row.Clip)?.Category ?? string.Empty;
        }

        [Test]
        public void BuildVisible_WithNoFilter_ReturnsEveryRow()
        {
            var rows = new List<AudioBalanceRow> { Row("kick", -20f, -3f), Row("snare", -18f, -1f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Name, true, Lookup(Profile(rows.ToArray())));

            Assert.AreEqual(2, visible.Count);
        }

        [Test]
        public void BuildVisible_FiltersByNameCaseInsensitively()
        {
            var rows = new List<AudioBalanceRow> { Row("Kick_Heavy", -20f, -3f), Row("snare", -18f, -1f) };

            var visible = ClipListView.BuildVisible(rows, "kick", ClipSortMode.Name, true, Lookup(Profile(rows.ToArray())));

            Assert.AreEqual(1, visible.Count);
            Assert.AreEqual("Kick_Heavy", visible[0].Clip.name);
        }

        [Test]
        public void BuildVisible_SortsByNameAscendingAndDescending()
        {
            var rows = new List<AudioBalanceRow> { Row("zebra", -20f, -3f), Row("apple", -18f, -1f) };
            var profile = Profile(rows.ToArray());

            var ascending = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Name, true, Lookup(profile));
            var descending = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Name, false, Lookup(profile));

            Assert.AreEqual("apple", ascending[0].Clip.name);
            Assert.AreEqual("zebra", descending[0].Clip.name);
        }

        [Test]
        public void BuildVisible_SortsByLoudness()
        {
            var rows = new List<AudioBalanceRow> { Row("loud", -12f, -6f), Row("quiet", -30f, 0f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Loudness, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("quiet", visible[0].Clip.name, "Ascending loudness puts the quietest first.");
        }

        [Test]
        public void BuildVisible_SortsByGain()
        {
            var rows = new List<AudioBalanceRow> { Row("a", -20f, -1f), Row("b", -20f, -12f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Gain, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("b", visible[0].Clip.name);
        }

        [Test]
        public void BuildVisible_SortsByCategory()
        {
            var music = Row("bed", -20f, -3f);
            var ui = Row("blip", -20f, -3f);
            var profile = Profile(music, ui);
            profile.SettingsFor(music.Clip).Category = "Music";
            profile.SettingsFor(ui.Clip).Category = "UI";

            var visible = ClipListView.BuildVisible(new List<AudioBalanceRow> { ui, music },
                string.Empty, ClipSortMode.Category, true, Lookup(profile));

            Assert.AreEqual("Music", profile.SettingsFor(visible[0].Clip).Category);
        }

        [Test]
        public void BuildVisible_SortIsStableForEqualKeys()
        {
            var rows = new List<AudioBalanceRow> { Row("first", -20f, -3f), Row("second", -20f, -3f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Loudness, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("first", visible[0].Clip.name);
            Assert.AreEqual("second", visible[1].Clip.name);
        }

        [Test]
        public void BuildVisible_SortIsStableForEqualKeysWhenDescendingToo()
        {
            // The regression this pins: an earlier revision sorted ascending and then called
            // List.Reverse() to get descending. Reverse inverts tie GROUPS as well as the
            // overall order, so two rows with equal keys came back in reverse discovery
            // order -- stable ascending, unstable descending. Only the ascending case was
            // ever tested, so it survived review.
            var rows = new List<AudioBalanceRow> { Row("first", -20f, -3f), Row("second", -20f, -3f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Loudness, false,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("first", visible[0].Clip.name,
                "Equal keys must keep discovery order in BOTH directions.");
            Assert.AreEqual("second", visible[1].Clip.name);
        }

        [Test]
        public void BuildVisible_DoesNotMutateTheProfile()
        {
            // BuildVisible is documented as pure. It used to call profile.SettingsFor(),
            // which APPENDS a ClipSettings on a miss -- writing to the asset from inside
            // OnGUI. Passing a lookup instead makes that structurally impossible; this test
            // fails loudly if anyone reintroduces a profile parameter.
            var known = Row("known", -20f, -3f);
            var stranger = Row("stranger", -20f, -3f);
            var profile = Profile(known);
            var countBefore = profile.Clips.Count;

            ClipListView.BuildVisible(
                new List<AudioBalanceRow> { known, stranger },
                string.Empty, ClipSortMode.Category, true,
                row => "Music");

            Assert.AreEqual(countBefore, profile.Clips.Count,
                "Building the visible list must not add settings for unknown clips.");
        }

        [Test]
        public void BuildVisible_HandlesNullRowsWithoutThrowing()
        {
            Assert.AreEqual(0, ClipListView.BuildVisible(null, string.Empty, ClipSortMode.Name, true, null).Count);
        }

        [Test]
        public void StatusIcon_MarksOutliersAndBrokenClipsButNotHealthyOnes()
        {
            Assert.AreEqual(string.Empty, ClipListView.StatusIcon(Row("fine", -20f, -3f)));
            Assert.AreNotEqual(string.Empty, ClipListView.StatusIcon(Row("odd", -20f, -20f, ClipStatus.Ok, true)));
            Assert.AreNotEqual(string.Empty, ClipListView.StatusIcon(Row("mute", 0f, 0f, ClipStatus.Silent)));
            Assert.AreNotEqual(string.Empty,
                ClipListView.StatusIcon(Row("broken", 0f, 0f, ClipStatus.Unanalyzable)));
        }

        [Test]
        public void BulkAssignCategory_SetsTheCategoryOnEverySuppliedRow()
        {
            var a = Row("a", -20f, -3f);
            var b = Row("b", -20f, -3f);
            var profile = Profile(a, b);

            ClipListView.BulkAssignCategory(new[] { a, b }, profile, "UI");

            Assert.AreEqual("UI", profile.SettingsFor(a.Clip).Category);
            Assert.AreEqual("UI", profile.SettingsFor(b.Clip).Category);
        }

        [Test]
        public void BulkAssignCategory_IgnoresNullInputsWithoutThrowing()
        {
            var profile = Profile();

            Assert.DoesNotThrow(() => ClipListView.BulkAssignCategory(null, profile, "UI"));
            Assert.DoesNotThrow(() => ClipListView.BulkAssignCategory(new AudioBalanceRow[0], null, "UI"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `ClipListView` does not exist.

- [ ] **Step 3: Implement `ClipSortMode`**

`Packages/com.hoppa.audiobalance/Editor/Window/ClipSortMode.cs`:

```csharp
namespace Hoppa.AudioBalance.Editor
{
    public enum ClipSortMode
    {
        Name = 0,
        Loudness = 1,
        Gain = 2,
        Category = 3
    }
}
```

- [ ] **Step 4: Implement `ClipListView`**

`Packages/com.hoppa.audiobalance/Editor/Window/ClipListView.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Filtering, sorting and bulk edits for the clip table. Kept as pure functions over the
    /// row list so they test without any UI.
    ///
    /// <para>
    /// <c>BuildVisible</c> takes a <c>categoryOf</c> lookup rather than the profile on
    /// purpose. It runs on every OnGUI event, and <c>AudioBalanceProfile.SettingsFor</c>
    /// appends a new <c>ClipSettings</c> on a miss -- so taking the profile made a
    /// "pure" function write to a ScriptableObject during rendering, without Undo and
    /// without SetDirty. The window resolves settings once, outside the render path, and
    /// passes a read-only lookup here.
    /// </para>
    /// </summary>
    public static class ClipListView
    {
        public static List<AudioBalanceRow> BuildVisible(
            IReadOnlyList<AudioBalanceRow> rows,
            string filter,
            ClipSortMode sort,
            bool ascending,
            Func<AudioBalanceRow, string> categoryOf)
        {
            var visible = new List<AudioBalanceRow>();
            if (rows == null)
            {
                return visible;
            }

            foreach (var row in rows)
            {
                if (row?.Clip == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter) &&
                    row.Clip.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                visible.Add(row);
            }

            // OrderBy/OrderByDescending are both stable, so rows with equal keys keep their
            // discovery order in EITHER direction. Sorting ascending and then calling
            // List.Reverse() would not: Reverse inverts tie groups too, silently destroying
            // stability the moment the user clicks the sort-direction button.
            switch (sort)
            {
                case ClipSortMode.Loudness:
                    return Order(visible, ascending,
                        r => r.Analysis.Status == ClipStatus.Ok ? r.Analysis.Lufs : float.MinValue);
                case ClipSortMode.Gain:
                    return Order(visible, ascending, r => r.Gain.FinalGainDb);
                case ClipSortMode.Category:
                    return Order(visible, ascending,
                        r => categoryOf?.Invoke(r) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                default:
                    return Order(visible, ascending,
                        r => r.Clip.name, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// One key selector, both directions -- so ascending and descending can never drift
        /// apart, and stability holds either way.
        /// </summary>
        private static List<AudioBalanceRow> Order<TKey>(
            List<AudioBalanceRow> rows, bool ascending,
            Func<AudioBalanceRow, TKey> key, IComparer<TKey> comparer = null)
        {
            return (ascending
                ? rows.OrderBy(key, comparer)
                : rows.OrderByDescending(key, comparer)).ToList();
        }

        /// <summary>Empty string for a healthy row -- the table should not be a wall of icons.</summary>
        public static string StatusIcon(AudioBalanceRow row)
        {
            if (row == null)
            {
                return string.Empty;
            }

            switch (row.Analysis.Status)
            {
                case ClipStatus.Silent:
                    return "silent";
                case ClipStatus.Unanalyzable:
                    return "!";
                default:
                    return row.Gain.IsOutlier ? "outlier" : string.Empty;
            }
        }

        public static void BulkAssignCategory(IEnumerable<AudioBalanceRow> rows,
            AudioBalanceProfile profile, string category)
        {
            if (rows == null || profile == null || string.IsNullOrEmpty(category))
            {
                return;
            }

            Undo.RecordObject(profile, "Assign Audio Category");

            foreach (var row in rows)
            {
                if (row?.Clip == null)
                {
                    continue;
                }

                var settings = profile.SettingsFor(row.Clip);
                if (settings != null)
                {
                    settings.Category = category;
                }
            }

            EditorUtility.SetDirty(profile);
        }
    }
}
```

> `BulkAssignCategory` keeps taking the profile — it is an explicit user action, not a render-path call, and it correctly wraps the writes in `Undo.RecordObject` / `EditorUtility.SetDirty`. `SettingsFor` creating a missing entry there is the intended behaviour.

- [ ] **Step 5: Replace `DrawClips` in the window**

In `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs`, add these fields beside the existing ones:

```csharp
        private string _filter = string.Empty;
        private ClipSortMode _sort = ClipSortMode.Name;
        private bool _ascending = true;
        private readonly HashSet<AudioClip> _selected = new HashSet<AudioClip>();

        /// <summary>
        /// Clip settings resolved ONCE per row-set change, never during rendering.
        /// AudioBalanceProfile.SettingsFor appends on a miss, so calling it per row per
        /// OnGUI event both mutated the asset mid-render and made drawing O(n^2).
        /// </summary>
        private readonly Dictionary<AudioClip, ClipSettings> _settings =
            new Dictionary<AudioClip, ClipSettings>();

        private int _settingsStamp = -1;

        /// <summary>
        /// Widest a clip row actually draws, including the preview buttons Task 13 appends
        /// (they end at x = 780 measured from the row origin). The scroll view's content rect
        /// must be at least this wide or the horizontal scrollbar never appears and the
        /// right-hand controls cannot be reached at the window's minimum size.
        /// </summary>
        private const float MinRowWidth = 800f;

        /// <summary>
        /// The clip whose trim slider is mid-drag, so the Undo entry and the SetDirty are
        /// recorded once per gesture rather than once per frame.
        /// </summary>
        private AudioClip _trimDragClip;
```

Also update the profile-switch branch in `DrawToolbar` (added in Task 11) to drop stale selection:

```csharp
            if (picked != _profile)
            {
                _profile = picked;
                _session.Clear();
                ClearSelection();
                _settingsStamp = -1;
            }
```

Then replace the whole `DrawClips` method with:

```csharp
        /// <summary>
        /// Rebuilds the clip-settings map when the row set changes. This is the one place
        /// SettingsFor is allowed to run: outside the render path, wrapped in Undo/SetDirty,
        /// so the entries it creates are undoable and properly persisted.
        /// </summary>
        private void SyncSettings()
        {
            var stamp = _profile == null ? -1 : _session.Rows.Count * 397 ^ _profile.Clips.Count;
            if (stamp == _settingsStamp)
            {
                return;
            }

            _settingsStamp = stamp;
            _settings.Clear();

            if (_profile == null)
            {
                return;
            }

            Undo.RecordObject(_profile, "Resolve Audio Clip Settings");
            var created = false;

            foreach (var row in _session.Rows)
            {
                if (row?.Clip == null || _settings.ContainsKey(row.Clip))
                {
                    continue;
                }

                var before = _profile.Clips.Count;
                _settings[row.Clip] = _profile.SettingsFor(row.Clip);
                created |= _profile.Clips.Count != before;
            }

            if (created)
            {
                EditorUtility.SetDirty(_profile);
            }
        }

        private string CategoryOf(AudioBalanceRow row)
        {
            return row?.Clip != null && _settings.TryGetValue(row.Clip, out var s) && s != null
                ? s.Category
                : string.Empty;
        }

        /// <summary>Drops selection state that no longer belongs to the visible profile.</summary>
        private void ClearSelection()
        {
            _selected.Clear();
        }

        private void PruneSelection()
        {
            if (_selected.Count == 0)
            {
                return;
            }

            // Without this the bulk button reads "Set Category (12)" while the resolved
            // target list is empty -- clicking appears to work and silently does nothing.
            _selected.RemoveWhere(clip =>
                clip == null || !_settings.ContainsKey(clip));
        }

        private void DrawClips(Rect rect)
        {
            SyncSettings();
            PruneSelection();

            var header = new Rect(rect.x, rect.y, rect.width, RowHeight);
            DrawClipHeader(header);

            var body = new Rect(rect.x, rect.y + RowHeight + 2f, rect.width,
                rect.height - RowHeight - 2f);
            GUI.Box(body, GUIContent.none);

            var visible = ClipListView.BuildVisible(_session.Rows, _filter, _sort, _ascending, CategoryOf);

            // Content must be as wide as the widest row actually is, not as wide as the
            // viewport. Hard-coding it to the viewport means the horizontal scrollbar never
            // appears and anything past the right edge -- the preview buttons Task 13 adds --
            // is silently unreachable at the window's default size.
            var content = new Rect(0f, 0f,
                Mathf.Max(body.width - 20f, MinRowWidth), visible.Count * RowHeight + 4f);

            _clipScroll = GUI.BeginScrollView(body, _clipScroll, content);

            var y = 2f;
            foreach (var row in visible)
            {
                DrawClipRow(new Rect(4f, y, content.width - 8f, RowHeight), row);
                y += RowHeight;
            }

            GUI.EndScrollView();
        }

        private void DrawClipHeader(Rect rect)
        {
            var x = rect.x;

            GUI.Label(new Rect(x, rect.y, 40f, rect.height), "Filter");
            x += 44f;

            _filter = EditorGUI.TextField(new Rect(x, rect.y, 140f, rect.height - 2f), _filter);
            x += 146f;

            GUI.Label(new Rect(x, rect.y, 32f, rect.height), "Sort");
            x += 36f;

            _sort = (ClipSortMode)EditorGUI.EnumPopup(
                new Rect(x, rect.y, 100f, rect.height - 2f), _sort);
            x += 106f;

            // ASCII captions: default IMGUI font glyph coverage is not guaranteed, and a
            // blank button is indistinguishable from a broken one.
            if (GUI.Button(new Rect(x, rect.y, 44f, rect.height - 2f), _ascending ? "Asc" : "Desc"))
            {
                _ascending = !_ascending;
            }

            x += 50f;

            GUI.enabled = _selected.Count > 0;

            if (GUI.Button(new Rect(x, rect.y, 130f, rect.height - 2f),
                    $"Set Category ({_selected.Count})"))
            {
                ShowBulkCategoryMenu();
            }

            GUI.enabled = true;
        }

        private void DrawClipRow(Rect rect, AudioBalanceRow row)
        {
            var x = rect.x;

            var wasSelected = _selected.Contains(row.Clip);
            var isSelected = EditorGUI.Toggle(new Rect(x, rect.y, 18f, rect.height), wasSelected);
            if (isSelected != wasSelected)
            {
                if (isSelected)
                {
                    _selected.Add(row.Clip);
                }
                else
                {
                    _selected.Remove(row.Clip);
                }
            }

            x += 22f;

            GUI.Label(new Rect(x, rect.y, 170f, rect.height), row.Clip.name);
            x += 174f;

            // Resolved in SyncSettings, never here -- SettingsFor appends on a miss and this
            // runs on every OnGUI event.
            if (!_settings.TryGetValue(row.Clip, out var settings) || settings == null)
            {
                return;
            }

            var names = _profile.Categories.Select(c => c.Name).ToArray();
            var current = Mathf.Max(0, System.Array.IndexOf(names, settings.Category));

            var picked = EditorGUI.Popup(new Rect(x, rect.y, 90f, rect.height - 2f), current, names);
            if (picked != current && picked >= 0 && picked < names.Length)
            {
                Undo.RecordObject(_profile, "Change Audio Category");
                settings.Category = names[picked];
                EditorUtility.SetDirty(_profile);

                // Categories carry their own MeasureMode, so this changes HOW the clip must
                // be measured -- Resolve cannot do that and would keep the old-mode number.
                // Re-analyze; the cache makes it a hit for every clip whose mode is unchanged.
                RunAnalysis();
            }

            x += 96f;

            GUI.Label(new Rect(x, rect.y, 90f, rect.height),
                row.Analysis.Status == ClipStatus.Ok ? $"{row.Analysis.Lufs:0.0} LUFS" : "—");
            x += 94f;

            GUI.Label(new Rect(x, rect.y, 70f, rect.height),
                row.Analysis.Status == ClipStatus.Ok ? $"{row.Gain.FinalGainDb:0.0} dB" : "—");
            x += 74f;

            // Slider drags fire a change on every OnGUI frame the value differs. Comparing
            // values directly would push one Undo entry per frame -- dozens for a single
            // drag -- and re-run GainSolver over every row each time. Record the undo state
            // once when the drag starts, then commit on mouse-up / drag-exit.
            EditorGUI.BeginChangeCheck();

            var trim = EditorGUI.Slider(new Rect(x, rect.y, 150f, rect.height - 2f),
                settings.TrimDb, -12f, 12f);

            if (EditorGUI.EndChangeCheck())
            {
                if (_trimDragClip != row.Clip)
                {
                    Undo.RecordObject(_profile, "Change Audio Trim");
                    _trimDragClip = row.Clip;
                }

                settings.TrimDb = trim;
                _session.Resolve(_profile);
            }

            var e = Event.current;
            if (_trimDragClip != null &&
                (e.type == EventType.MouseUp || e.type == EventType.DragExited))
            {
                // Commit once, at the end of the gesture.
                EditorUtility.SetDirty(_profile);
                _trimDragClip = null;
            }

            x += 156f;

            var icon = ClipListView.StatusIcon(row);
            if (!string.IsNullOrEmpty(icon))
            {
                GUI.Label(new Rect(x, rect.y, 90f, rect.height),
                    new GUIContent(icon, row.Analysis.Reason ?? "gain is far from the category target"));
            }
        }

        private void ShowBulkCategoryMenu()
        {
            var menu = new GenericMenu();
            var targets = _session.Rows.Where(r => _selected.Contains(r.Clip)).ToArray();

            if (targets.Length == 0)
            {
                // Defensive: PruneSelection should already have made this unreachable. Being
                // told nothing is selected beats a menu that silently does nothing.
                menu.AddDisabledItem(new GUIContent("Nothing selected"));
                menu.ShowAsContext();
                return;
            }

            foreach (var category in _profile.Categories)
            {
                var name = category.Name;
                menu.AddItem(new GUIContent(name), false, () =>
                {
                    ClipListView.BulkAssignCategory(targets, _profile, name);

                    // Bulk assign moves clips between categories, and categories carry their
                    // own MeasureMode -- so this needs a re-measure, not just a re-solve.
                    RunAnalysis();
                    Repaint();
                });
            }

            menu.ShowAsContext();
        }
```

Add `using System.Linq;` to the file's using block.

- [ ] **Step 6: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 13 new ones from this task.

- [ ] **Step 7: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): sortable, filterable clip table with bulk category assign

Filtering and sorting are pure functions over the row list, tested
directly; only drawing lives in the window. Editing a category or trim
re-solves from existing measurements rather than re-decoding.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: Preview player, Write Table, and docs

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/PreviewClipFactory.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/AudioPreviewPlayer.cs`
- Create: `Packages/com.hoppa.audiobalance/Editor/Window/GainTableWriter.cs`
- Create: `Packages/com.hoppa.audiobalance/README.md`
- Create: `Packages/com.hoppa.audiobalance/Documentation~/audio-balance-guide.md`
- Modify: `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs` (preview buttons + Write Table)
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/GainTableWriterTests.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/PreviewClipFactoryTests.cs`

**Interfaces:**
- Consumes: `AudioBalanceRow`, `AudioGainTable`, `ClipStatus`, `AudioBalanceProfile`, `ClipSampleReader`, `AudioGainMath`.
- Produces:
  - `GainTableWriter.BuildEntries(IReadOnlyList<AudioBalanceRow> rows) -> List<AudioGainTable.Entry>`
  - `GainTableWriter.Write(AudioBalanceProfile profile, IReadOnlyList<AudioBalanceRow> rows) -> bool` — a pure asset mutation; it does **not** save
  - `PreviewClipFactory.Scale(float[] samples, float gainDb) -> float[]`
  - `PreviewClipFactory.Mix(float[] a, float aGainDb, float[] b, float bGainDb) -> float[]`
  - `AudioPreviewPlayer.PlayWithGain(AudioClip clip, float gainDb)`
  - `AudioPreviewPlayer.PlayAgainstAnchor(AudioClip clip, float gainDb, AudioClip anchor, float anchorGainDb)`
  - `AudioPreviewPlayer.StopAll()` / `AudioPreviewPlayer.Teardown()`

- [ ] **Step 1: Write the failing test**

`Packages/com.hoppa.audiobalance/Tests/Editor/GainTableWriterTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Hoppa.AudioBalance;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class GainTableWriterTests
    {
        private static AudioBalanceRow Row(string name, float gainDb, ClipStatus status = ClipStatus.Ok)
        {
            var clip = AudioClip.Create(name, 128, 1, 44100, false);
            return new AudioBalanceRow
            {
                Clip = clip,
                Analysis = status == ClipStatus.Ok
                    ? ClipAnalysis.Ok(clip, -20f, -3f)
                    : new ClipAnalysis(clip, status, 0f, -80f, "reason"),
                Gain = new GainResult(clip, status, gainDb, gainDb, false)
            };
        }

        [Test]
        public void BuildEntries_IncludesAnalyzableRowsWithTheirFinalGain()
        {
            var rows = new List<AudioBalanceRow> { Row("kick", -6f), Row("snare", 0f) };

            var entries = GainTableWriter.BuildEntries(rows);

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(-6f, entries.First(e => e.Clip.name == "kick").GainDb, 1e-4f);
            Assert.AreEqual(0f, entries.First(e => e.Clip.name == "snare").GainDb, 1e-4f);
        }

        [Test]
        public void BuildEntries_SkipsSilentAndUnanalyzableRows()
        {
            var rows = new List<AudioBalanceRow>
            {
                Row("good", -3f),
                Row("mute", 0f, ClipStatus.Silent),
                Row("broken", 0f, ClipStatus.Unanalyzable)
            };

            var entries = GainTableWriter.BuildEntries(rows);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("good", entries[0].Clip.name);
        }

        [Test]
        public void BuildEntries_IsDeterministicallyOrderedByClipName()
        {
            var rows = new List<AudioBalanceRow> { Row("zebra", -1f), Row("apple", -2f) };

            var entries = GainTableWriter.BuildEntries(rows);

            Assert.AreEqual("apple", entries[0].Clip.name,
                "A stable order keeps the asset's git diff clean between runs.");
            Assert.AreEqual("zebra", entries[1].Clip.name);
        }

        [Test]
        public void BuildEntries_OnNullOrEmptyInput_ReturnsAnEmptyList()
        {
            Assert.AreEqual(0, GainTableWriter.BuildEntries(null).Count);
            Assert.AreEqual(0, GainTableWriter.BuildEntries(new List<AudioBalanceRow>()).Count);
        }

        [Test]
        public void EntriesFedIntoATable_AreReadableThroughTheRuntimeLookup()
        {
            var rows = new List<AudioBalanceRow> { Row("kick", -6f) };
            var table = ScriptableObject.CreateInstance<AudioGainTable>();

            table.SetEntries(GainTableWriter.BuildEntries(rows));

            Assert.AreEqual(-6f, table.GetGainDb(rows[0].Clip), 1e-4f);
            Assert.AreEqual(0.5012f, table.GetGain(rows[0].Clip), 1e-3f);
        }

        [Test]
        public void Write_WithNoTableAssigned_ReturnsFalseWithoutThrowing()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();

            Assert.IsFalse(GainTableWriter.Write(profile, new List<AudioBalanceRow> { Row("kick", -6f) }));
        }

        [Test]
        public void Write_WithNullProfile_ReturnsFalseWithoutThrowing()
        {
            Assert.IsFalse(GainTableWriter.Write(null, new List<AudioBalanceRow>()));
        }

        [Test]
        public void Write_PopulatesTheAssignedTable()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            profile.Table = ScriptableObject.CreateInstance<AudioGainTable>();

            var rows = new List<AudioBalanceRow> { Row("kick", -6f) };

            Assert.IsTrue(GainTableWriter.Write(profile, rows));
            Assert.AreEqual(-6f, profile.Table.GetGainDb(rows[0].Clip), 1e-4f);
        }
    }
}
```

Also `Packages/com.hoppa.audiobalance/Tests/Editor/PreviewClipFactoryTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Covers the half of the preview path that can be tested. Playback itself reflects into
    /// UnityEditor.AudioUtil and is documented as an accepted, untestable gap on
    /// AudioPreviewPlayer -- but the gain arithmetic it depends on is pinned here.
    /// </summary>
    public class PreviewClipFactoryTests
    {
        [Test]
        public void Scale_MinusSixDbHalvesTheAmplitude()
        {
            var scaled = PreviewClipFactory.Scale(new[] { 1f, -1f, 0.5f }, -6.0206f);

            Assert.AreEqual(0.5f, scaled[0], 1e-3f);
            Assert.AreEqual(-0.5f, scaled[1], 1e-3f);
            Assert.AreEqual(0.25f, scaled[2], 1e-3f);
        }

        [Test]
        public void Scale_ZeroDbIsIdentity()
        {
            var scaled = PreviewClipFactory.Scale(new[] { 0.3f, -0.7f }, 0f);

            Assert.AreEqual(0.3f, scaled[0], 1e-6f);
            Assert.AreEqual(-0.7f, scaled[1], 1e-6f);
        }

        [Test]
        public void Scale_ClampsRatherThanWrappingWhenAPositiveTrimOverdrivesTheSignal()
        {
            // A +12 dB trim on a near-full-scale sample must clip, not wrap to a negative
            // value, and not silently renormalise the whole preview.
            var scaled = PreviewClipFactory.Scale(new[] { 0.9f, -0.9f }, 12f);

            Assert.AreEqual(1f, scaled[0], 1e-6f);
            Assert.AreEqual(-1f, scaled[1], 1e-6f);
        }

        [Test]
        public void Scale_OnNullInput_ReturnsAnEmptyArray()
        {
            Assert.AreEqual(0, PreviewClipFactory.Scale(null, 0f).Length);
        }

        [Test]
        public void Mix_SumsBothSignalsAtTheirRespectiveGains()
        {
            var mixed = PreviewClipFactory.Mix(new[] { 0.5f }, 0f, new[] { 0.25f }, 0f);

            Assert.AreEqual(0.75f, mixed[0], 1e-4f);
        }

        [Test]
        public void Mix_ResultIsAsLongAsTheLongerInput()
        {
            // A short SFX over a long bed must not truncate the bed -- judging it in context
            // is the entire point of A/B.
            var mixed = PreviewClipFactory.Mix(new[] { 0.5f }, 0f, new[] { 0.1f, 0.1f, 0.1f }, 0f);

            Assert.AreEqual(3, mixed.Length);
            Assert.AreEqual(0.6f, mixed[0], 1e-4f);
            Assert.AreEqual(0.1f, mixed[1], 1e-4f);
        }

        [Test]
        public void Mix_ClampsTheSum()
        {
            var mixed = PreviewClipFactory.Mix(new[] { 0.8f }, 0f, new[] { 0.8f }, 0f);

            Assert.AreEqual(1f, mixed[0], 1e-6f);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `assets-refresh`, then `tests-run`.
Expected: compile errors — `GainTableWriter` and `PreviewClipFactory` do not exist.

- [ ] **Step 3: Implement `GainTableWriter`**

`Packages/com.hoppa.audiobalance/Editor/Window/GainTableWriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using Hoppa.AudioBalance;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Bakes solved gains into the profile's AudioGainTable asset.</summary>
    public static class GainTableWriter
    {
        public static List<AudioGainTable.Entry> BuildEntries(IReadOnlyList<AudioBalanceRow> rows)
        {
            var entries = new List<AudioGainTable.Entry>();
            if (rows == null)
            {
                return entries;
            }

            foreach (var row in rows)
            {
                // A silent or unreadable clip has no meaningful gain. Omitting it means the
                // runtime lookup falls through to unity gain rather than baking in a guess.
                if (row?.Clip == null || row.Analysis.Status != ClipStatus.Ok)
                {
                    continue;
                }

                entries.Add(new AudioGainTable.Entry
                {
                    Clip = row.Clip,
                    GainDb = row.Gain.FinalGainDb
                });
            }

            // Stable ordering keeps the asset's git diff clean between runs.
            entries.Sort((a, b) => string.CompareOrdinal(a.Clip.name, b.Clip.name));
            return entries;
        }

        public static bool Write(AudioBalanceProfile profile, IReadOnlyList<AudioBalanceRow> rows)
        {
            if (profile == null || profile.Table == null)
            {
                return false;
            }

            Undo.RecordObject(profile.Table, "Write Audio Gain Table");
            profile.Table.SetEntries(BuildEntries(rows));
            EditorUtility.SetDirty(profile.Table);

            // Deliberately NOT calling AssetDatabase.SaveAssets() here. This method is
            // exercised by unit tests, and SaveAssets flushes EVERY dirty asset in the
            // project -- so running the suite would commit unrelated in-flight edits to disk.
            // Saving is the window button's job; Write stays a pure asset mutation.
            return true;
        }
    }
}
```

- [ ] **Step 4: Implement `AudioPreviewPlayer`**

`Packages/com.hoppa.audiobalance/Editor/Window/AudioPreviewPlayer.cs`:

> **This section was rewritten (deviation #12).** The original design built a hidden scene
> `AudioSource` pair and called `AudioSource.Play()`. **`AudioSource.Play()` produces no audio
> outside Play Mode**, so that design could not work at all — the buttons would have been
> silently dead. The original rationale for rejecting Unity's built-in clip preview (it offers
> no volume control) was correct; the replacement simply did not play.
>
> The approach now is the standard one for editor audio tooling: reflect into the internal
> `UnityEditor.AudioUtil.PlayPreviewClip`, and make the gain audible by **pre-scaling** —
> building a temporary `AudioClip` whose samples already have the gain baked in, and previewing
> that. A/B is the same trick with the anchor mixed in, which also keeps everything on one
> preview channel rather than depending on `AudioUtil` supporting simultaneous previews.

Two files. First, the pure sample math, which is the part that can actually be tested:

`Packages/com.hoppa.audiobalance/Editor/Window/PreviewClipFactory.cs`:

```csharp
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Builds the gain-applied clips the preview plays. Kept separate from
    /// <see cref="AudioPreviewPlayer"/> because this half is deterministic, has no editor
    /// dependencies, and therefore unit-tests -- unlike the reflection-based playback.
    /// </summary>
    public static class PreviewClipFactory
    {
        /// <summary>
        /// Scales every sample by a linear gain, clamping to [-1, 1].
        ///
        /// <para>
        /// Clamping is a real decision, not defensive noise: solved gains are normalised
        /// downward so they are almost always negative, but a per-clip trim of up to +12 dB
        /// can push a peak past full scale. Clipping the preview is honest -- it is what the
        /// runtime would do -- whereas normalising here would make the preview quieter than
        /// the thing it is previewing.
        /// </para>
        /// </summary>
        public static float[] Scale(float[] samples, float gainDb)
        {
            if (samples == null)
            {
                return new float[0];
            }

            var gain = AudioGainMath.LinearFromDb(gainDb);
            var scaled = new float[samples.Length];

            for (var i = 0; i < samples.Length; i++)
            {
                scaled[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            }

            return scaled;
        }

        /// <summary>
        /// Sums two gain-applied signals, aligned at sample 0. The result is as long as the
        /// longer input, so a short SFX over a long music bed keeps the bed audible after the
        /// SFX ends -- which is the whole point of judging it in context.
        /// </summary>
        public static float[] Mix(float[] a, float aGainDb, float[] b, float bGainDb)
        {
            var left = Scale(a, aGainDb);
            var right = Scale(b, bGainDb);
            var mixed = new float[Mathf.Max(left.Length, right.Length)];

            for (var i = 0; i < mixed.Length; i++)
            {
                var sum = 0f;
                if (i < left.Length)
                {
                    sum += left[i];
                }

                if (i < right.Length)
                {
                    sum += right[i];
                }

                mixed[i] = Mathf.Clamp(sum, -1f, 1f);
            }

            return mixed;
        }
    }
}
```

Then the player itself:

`Packages/com.hoppa.audiobalance/Editor/Window/AudioPreviewPlayer.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Auditions clips with their solved gain applied.
    ///
    /// <para>
    /// Playback goes through the internal <c>UnityEditor.AudioUtil</c> because
    /// <see cref="AudioSource.Play"/> is silent outside Play Mode -- a scene AudioSource
    /// simply cannot preview anything from an EditorWindow. Gain is applied by pre-scaling
    /// the samples into a temporary clip rather than by a volume parameter, because
    /// AudioUtil exposes no volume control; hearing the gain is the entire point, and
    /// Unity's own clip preview was rejected for exactly that reason.
    /// </para>
    ///
    /// <para>
    /// <b>Untested boundary.</b> The reflection into <c>AudioUtil</c> has no automated
    /// coverage and cannot meaningfully get any: the type is internal, its method signatures
    /// have changed across Unity versions, and asserting that audio was audibly produced is
    /// not something an EditMode test can do. This is the same accepted-gap situation as
    /// <see cref="ClipSampleReader.StreamingError"/>, and it is handled the same way -- named
    /// here rather than left to be discovered. What *is* tested is
    /// <see cref="PreviewClipFactory"/>, which owns all the sample arithmetic. The reflection
    /// layer degrades to a single actionable warning if the method cannot be found, so a
    /// Unity upgrade that moves it produces a clear diagnostic rather than silence.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class AudioPreviewPlayer
    {
        private static AudioClip _temp;
        private static MethodInfo _play;
        private static MethodInfo _stop;
        private static bool _resolved;
        private static bool _warned;

        static AudioPreviewPlayer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Teardown;
            EditorApplication.playModeStateChanged += _ => Teardown();
        }

        public static void PlayWithGain(AudioClip clip, float gainDb)
        {
            if (clip == null || !TryReadSamples(clip, out var samples))
            {
                return;
            }

            Play(BuildTemp(clip, PreviewClipFactory.Scale(samples, gainDb),
                clip.channels, clip.frequency));
        }

        /// <summary>Plays the clip mixed over the anchor bed, so it is judged in context.</summary>
        public static void PlayAgainstAnchor(AudioClip clip, float gainDb,
            AudioClip anchor, float anchorGainDb)
        {
            if (clip == null || !TryReadSamples(clip, out var clipSamples))
            {
                return;
            }

            if (anchor == null || !TryReadSamples(anchor, out var anchorSamples) ||
                anchor.channels != clip.channels || anchor.frequency != clip.frequency)
            {
                // Mixing mismatched channel counts or sample rates by index would pitch- and
                // pan-shift the result, which is worse than not offering the comparison.
                PlayWithGain(clip, gainDb);
                return;
            }

            Play(BuildTemp(clip,
                PreviewClipFactory.Mix(clipSamples, gainDb, anchorSamples, anchorGainDb),
                clip.channels, clip.frequency));
        }

        public static void StopAll()
        {
            if (!Resolve() || _stop == null)
            {
                return;
            }

            try
            {
                _stop.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioBalance] Could not stop preview playback: {e.Message}");
            }
        }

        /// <summary>Destroys the temporary clip so previews do not leak one per click.</summary>
        public static void Teardown()
        {
            StopAll();
            DestroyTemp();
        }

        private static void DestroyTemp()
        {
            if (_temp != null)
            {
                UnityEngine.Object.DestroyImmediate(_temp);
            }

            _temp = null;
        }

        private static bool TryReadSamples(AudioClip clip, out float[] samples)
        {
            // Reuses the reader that already produces the actionable Streaming diagnostic,
            // so preview and analysis fail the same way for the same reason.
            if (ClipSampleReader.TryRead(clip, out samples, out var error))
            {
                return true;
            }

            Debug.LogWarning($"[AudioBalance] Cannot preview '{clip.name}': {error}");
            samples = null;
            return false;
        }

        /// <summary>
        /// The temp clip is a single static slot, replaced (and its predecessor destroyed) on
        /// every preview, and destroyed again on domain reload / play-mode change. One live
        /// temporary at a time, never one per click.
        /// </summary>
        private static AudioClip BuildTemp(AudioClip source, float[] samples, int channels, int frequency)
        {
            StopAll();
            DestroyTemp();

            if (samples == null || samples.Length == 0 || channels <= 0)
            {
                return null;
            }

            _temp = AudioClip.Create($"~preview_{source.name}",
                samples.Length / channels, channels, frequency, false);
            _temp.hideFlags = HideFlags.HideAndDontSave;
            _temp.SetData(samples, 0);

            return _temp;
        }

        private static void Play(AudioClip clip)
        {
            if (clip == null || !Resolve())
            {
                return;
            }

            try
            {
                // Signatures differ across versions: 2020+ is (AudioClip, int, bool),
                // some builds expose (AudioClip). Fill whatever the resolved overload wants.
                var parameters = _play.GetParameters();
                var args = new object[parameters.Length];
                args[0] = clip;

                for (var i = 1; i < parameters.Length; i++)
                {
                    args[i] = parameters[i].ParameterType == typeof(bool)
                        ? (object)false
                        : Activator.CreateInstance(parameters[i].ParameterType);
                }

                _play.Invoke(null, args);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioBalance] Preview playback failed: {e.Message}");
            }
        }

        /// <summary>
        /// Resolves AudioUtil once. Returns false (with one warning, not one per click) when
        /// the method cannot be found, so a Unity version that renames it degrades to an
        /// explicit diagnostic instead of a dead button.
        /// </summary>
        private static bool Resolve()
        {
            if (_resolved)
            {
                return _play != null;
            }

            _resolved = true;

            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (type != null)
            {
                const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public |
                                           BindingFlags.NonPublic;

                // PlayPreviewClip is current; PlayClip is the pre-2020 name.
                _play = type.GetMethods(Flags).FirstOrDefault(m =>
                    (m.Name == "PlayPreviewClip" || m.Name == "PlayClip") &&
                    m.GetParameters().Length > 0 &&
                    m.GetParameters()[0].ParameterType == typeof(AudioClip));

                _stop = type.GetMethods(Flags).FirstOrDefault(m =>
                    (m.Name == "StopAllPreviewClips" || m.Name == "StopAllClips") &&
                    m.GetParameters().Length == 0);
            }

            if (_play == null && !_warned)
            {
                _warned = true;
                Debug.LogWarning(
                    "[AudioBalance] Could not find UnityEditor.AudioUtil.PlayPreviewClip on " +
                    $"Unity {Application.unityVersion}. Clip preview is disabled; every other " +
                    "part of the window (analysis, gains, Write Table) is unaffected. " +
                    "This is an internal Unity API that moves between versions.");
            }

            return _play != null;
        }
    }
}
```

> **Editor in use vs the package's declared minimum.** `package.json` declares
> `"unity": "2022.3"`, but this repo's editor is 6000.3.8f1. `PlayPreviewClip(AudioClip, int, bool)`
> is present on both, which is why it is tried first — but the resolution is written to search
> rather than to bind a fixed signature precisely because this API is internal and unstable.
> Verify the preview audibly works in Step 9; if the warning above appears, that is the
> diagnostic doing its job, and the fix is to widen the name list, not to silence it.

- [ ] **Step 5: Wire preview buttons and Write Table into the window**

In `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs`:

> **The toolbar is now two rows (deviation #13).** Appending Write Table and the Table
> `ObjectField` to the single existing row put the field at x = 658 running to x = 838, while
> `minSize.x = 720` leaves only 708 px of content — so the field was cut off at the window's
> default size. That is a **dead end, not a cosmetic issue**: Write Table is disabled until
> `Table != null`, and the only control that can assign the table was the one clipped away. A
> fresh user would see a permanently greyed button and no way to fix it short of guessing that
> the window needs widening. The toolbar therefore wraps to a second row, and the second row
> holds the two table controls.

Change `OnGUI` so the toolbar gets two rows:

```csharp
            DrawToolbar(new Rect(Pad, y, position.width - Pad * 2f, ToolbarHeight));
            y += ToolbarHeight + 2f;

            DrawTableBar(new Rect(Pad, y, position.width - Pad * 2f, ToolbarHeight));
            y += ToolbarHeight + Pad;
```

Then add the new row as its own method (leaving `DrawToolbar` ending after Analyze):

```csharp
        /// <summary>
        /// Second toolbar row. These two controls live here rather than appended to the first
        /// row because at minSize (720 px wide, 708 usable) the first row is already full
        /// after Analyze -- and clipping the Table field specifically is unrecoverable, since
        /// Write Table stays disabled until a table is assigned.
        /// </summary>
        private void DrawTableBar(Rect rect)
        {
            var x = rect.x;

            GUI.enabled = _profile != null && _profile.Table != null && _session.Rows.Count > 0;

            if (GUI.Button(new Rect(x, rect.y, 110f, rect.height - 4f), "Write Table"))
            {
                if (GainTableWriter.Write(_profile, _session.Rows))
                {
                    // GainTableWriter.Write is a pure mutation so it stays test-safe; the
                    // explicit user action is what commits it to disk.
                    AssetDatabase.SaveAssets();
                    ShowNotification(new GUIContent("Gain table written"));
                }
            }

            GUI.enabled = true;
            x += 116f;

            GUI.Label(new Rect(x, rect.y, 40f, rect.height), "Table");
            x += 44f;

            if (_profile == null)
            {
                return;
            }

            var table = (AudioGainTable)EditorGUI.ObjectField(
                new Rect(x, rect.y, 180f, rect.height - 4f),
                _profile.Table, typeof(AudioGainTable), false);

            if (table != _profile.Table)
            {
                Undo.RecordObject(_profile, "Set Gain Table");
                _profile.Table = table;
                EditorUtility.SetDirty(_profile);
            }

            x += 186f;

            if (_profile.Table == null)
            {
                GUI.Label(new Rect(x, rect.y, 320f, rect.height),
                    "Assign a gain table to enable Write Table.");
            }
        }
```

Add to the end of `DrawClipRow`, after the status icon block (`x += 94f;` first).

> These buttons end at x = 780 measured from the row origin, past the 688 px of content the
> window has at `minSize`. They are only reachable because Task 12's scroll-view content rect
> is at least `MinRowWidth` (800) wide — if that were still hard-coded to the viewport width,
> no horizontal scrollbar would appear and both of Task 13's headline features would be
> silently unreachable at the default window size. Do not narrow `MinRowWidth`.

```csharp
            // ASCII caption: a glyph the default IMGUI font lacks renders as a blank button,
            // which is indistinguishable from a broken one.
            GUI.enabled = row.Analysis.Status == ClipStatus.Ok;

            if (GUI.Button(new Rect(x, rect.y, 40f, rect.height - 2f),
                    new GUIContent("Play", "Preview with the solved gain applied")))
            {
                AudioPreviewPlayer.PlayWithGain(row.Clip, row.Gain.FinalGainDb);
            }

            x += 44f;

            GUI.enabled = row.Analysis.Status == ClipStatus.Ok && _profile.Anchor != null;

            if (GUI.Button(new Rect(x, rect.y, 36f, rect.height - 2f),
                    new GUIContent("A/B", "Play mixed over the anchor bed")))
            {
                var anchorRow = _session.Rows.FirstOrDefault(r => r.Clip == _profile.Anchor);
                AudioPreviewPlayer.PlayAgainstAnchor(
                    row.Clip, row.Gain.FinalGainDb,
                    _profile.Anchor, anchorRow?.Gain.FinalGainDb ?? 0f);
            }

            GUI.enabled = true;
```

Add to `OnDisable`, before the cache save:

```csharp
            // Teardown, not StopAll: this also destroys the temporary gain-applied clip so
            // closing the window never leaves one behind.
            AudioPreviewPlayer.Teardown();
```

Add `using Hoppa.AudioBalance;` to the file's using block so `AudioGainTable` resolves.

- [ ] **Step 6: Write the package README**

`Packages/com.hoppa.audiobalance/README.md`:

```markdown
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

**Source audio files are never modified.**

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
one quietest relative to its target — lands at exactly 0 dB, and every other clip is
attenuated below it. The clip that is *loudest* relative to its target is attenuated
the most. Relative balance is preserved exactly, and clipping becomes impossible.
Compensate the overall drop once, on your master mixer.

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

## Known limitation

Clips imported with **Streaming** load type return silence from `GetData` and are
reported as unanalyzable rather than having their import settings rewritten. Set
their Load Type to *Decompress On Load* to include them.
```

- [ ] **Step 7: Write the guide**

`Packages/com.hoppa.audiobalance/Documentation~/audio-balance-guide.md`:

```markdown
# Audio Balance — Designer Guide

## What this solves

The `volume` number on an `AudioSource` says nothing about how loud a clip actually
sounds. A dense bass-heavy loop and a thin UI blip can both sit at `1.0` and be 15 dB
apart to the ear. This tool measures what your ears hear and does the arithmetic.

## Workflow

1. **Point it at your audio.** Add one or more folders in the toolbar. They are
   stored project-relative, so they work on every teammate's checkout.
2. **Choose the anchor.** Use the background music that plays during levels. The anchor
   is the reference for the *outlier* check and a sanity readout — it is **not** what
   positions your other sounds, and changing it will not move the Gain column. See
   *Two things worth knowing* below.
3. **Assign categories.** This is what actually sets relative placement. Multi-select
   rows and use *Set Category*. Defaults:

   | Category | Offset | Measure mode | Meaning |
   |---|---|---|---|
   | Music | 0 dB | Integrated | The reference level |
   | SFX | +3 dB | Momentary max | Sits above the music bed |
   | UI | −6 dB | Momentary max | Sits below it |

   Changing a clip's category re-measures it, because each category carries its own
   measure mode. That is fast — unchanged clips come straight from the cache.

4. **Analyze.** Measurements are cached, so later runs only re-measure changed clips.
5. **Listen.** *Play* auditions a clip with its gain applied; *A/B* plays it mixed over
   the anchor bed. Trust your ears over the numbers.
6. **Trim what's still wrong.** The per-clip slider stacks on top of the category
   offset. Reach for it only after the category offset is right.
7. **Write Table.**

## Reading the warnings

| Marker | Meaning |
|---|---|
| `outlier` | Gain is more than 12 dB from the target. Usually a broken, near-silent, or wrong-format asset rather than a genuinely quiet sound. |
| `silent` | No measurable signal. Check the file actually contains audio. |
| `!` | Could not be read. Hover for the reason — most often a Streaming load type. |

## Why the measure modes differ

Integrated loudness gates out quiet passages, which is right for a music loop but
makes a short one-shot under-read — its decay tail gets discarded and the sound
lands too quiet against the bed. Momentary max takes the loudest 400 ms window
instead, so percussive SFX are measured on their impact.

## Three things worth knowing

- **Everything gets quieter, on purpose.** `AudioSource.volume` caps at 1.0, so
  positive gain is unachievable. Whichever clip needed the *most* gain — the one
  quietest against its target — is pinned at 0 dB, and everything else sits below it.
  Raise your master mixer once to compensate.
- **Changing the anchor will not move the Gain column.** This surprises people, so it
  is worth being blunt about: the anchor cancels out of the gain arithmetic entirely.
  Its live effects are the *outlier* marker and the LUFS readout beside it. If you want
  to move sounds relative to each other, change a **category offset** or a **trim** —
  those are the controls that do it. (With no anchor set, outlier marking is off
  completely, since there is no reference to be an outlier from.)
- **Re-run after replacing a sound.** The gain of every *other* clip can shift, because
  the clip needing the most gain defines the 0 dB ceiling for the whole set.
```

- [ ] **Step 8: Run tests to verify they pass**

Run `assets-refresh`, then `tests-run`.
Expected: every test in `Hoppa.AudioBalance.Editor.Tests` passes, including the 15 new ones from this task.

- [ ] **Step 9: Verify the full loop in the Editor**

Create a profile and a gain table, add a folder containing at least two audio clips of clearly different loudness, set an anchor, press **Analyze**. Confirm each of these:

- LUFS values appear for every readable clip.
- **Every gain is ≤ 0 dB, and exactly one clip reads 0.0 dB.** That clip is the one *quietest* relative to its category target — not the loudest. If the loudest clip is the one sitting at 0, the solver's sign is inverted.
- The gains of two clips in the same category differ by their measured LUFS difference.
- **Press Play on a row and confirm you actually hear it, with the gain applied** — a quiet clip and a loud one should sound closer together than the raw files do. Silence here means the `AudioUtil` reflection failed; check the console for the `[AudioBalance]` warning.
- **A/B** plays the clip mixed over the anchor bed, and the bed keeps playing after a short clip ends.
- **Write Table** populates the asset — check it in the Inspector.
- The table `ObjectField` and the **Play** / **A/B** buttons are all reachable **without resizing the window**, scrolling horizontally if needed.

Then verify the anchor's documented behaviour, since this is the thing most likely to be reported as a bug:

- Note the Gain column, then swap the anchor for a noticeably louder or quieter clip and re-run **Analyze**. **The Gain column must not change.** This is correct — the anchor cancels out of the gain arithmetic. What may change is which rows are marked `outlier`.
- Clear the anchor entirely and re-run **Analyze**. Confirm **no** rows are marked `outlier` (with no reference, no outlier judgement is made) rather than every row being flagged.

Finally, confirm the console is clean apart from any intentional `[AudioBalance]` diagnostic.

- [ ] **Step 10: Commit**

```bash
git add Packages/com.hoppa.audiobalance
git commit -m "$(cat <<'EOF'
feat(audio): preview player, gain table writer, and docs

Preview reflects into UnityEditor.AudioUtil.PlayPreviewClip: AudioSource
.Play() is silent outside Play Mode, so a scene AudioSource cannot preview
anything from an EditorWindow. AudioUtil exposes no volume control, so the
gain is made audible by pre-scaling the samples into one reusable temporary
clip. The reflection resolves defensively and degrades to a single warning
if the internal API moves; PreviewClipFactory holds the sample arithmetic
so that half is unit-tested.

GainTableWriter.Write stays a pure asset mutation -- SaveAssets moved to
the button handler, because Write is exercised by tests and SaveAssets
flushes every dirty asset in the project. Table entries are sorted by clip
name so the asset's git diff stays clean between runs, and silent or
unanalyzable clips are omitted so the runtime falls through to unity gain
rather than baking in a guess.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Plan self-review

**Spec coverage** — every spec section maps to a task:

| Spec § | Requirement | Task |
|---|---|---|
| 4 | Package split, assembly names | 1, 2 |
| 5.1 | LUFS meter, K-weighting, gating, sample-rate derivation | 2, 3 |
| 5.1 | Measure modes, short-clip and silence edge cases | 3, 4 |
| 5.2 | True-peak meter | 5 |
| 5.3 | `ClipSampleReader`, streaming limitation | 8 |
| 5.4 | `LoudnessCache` under `Library/` | 9 |
| 5.5 | `GainSolver`, outlier flag | 7 |
| 5.6 | `AudioGainTable` + runtime extensions | 1 |
| 5.7 | Window, toolbar, categories, clip table | 11, 12 |
| 5.8 | `AudioPreviewPlayer`, preview + A/B | 13 |
| 6 | Headroom normalization | 7 |
| 7 | Full test list | 1–13 |
| 8 | Deployment note (no game wired in v1) | Global Constraints |

**Deviations from the spec, deliberate:**

1. **Spec §7 says "1 kHz sine at −23 dBFS reads −23.0 LUFS" without stating the convention.** The plan pins it precisely: *stereo*, *peak* amplitude `10^(-23/20)`. A mono sine built the same way reads −26.0, and an RMS-referenced sine would read −20.7 — so the unstated convention was the difference between a passing and a failing test. Both cases are now tested.
2. **The measure mode is part of the cache key** (Task 10). The spec's cache key is `(guid, size, mtime)`, which would return an `Integrated` measurement for a clip later moved into a `MomentaryMax` category. **SUPERSEDED by deviation #7:** this was originally implemented by string-concatenating `$"{guid}:{(int)mode}"` into the `guid` parameter Task 10 passed to `LoudnessCache`. Round 2 replaced that with `Mode` as a real field on `LoudnessCacheKey` — the mangled-string approach fought against the cache doing its own `AssetDatabase` lookup (a `guid` that isn't actually a guid), which is exactly the seam deviation #7 needed to close.
3. **`AudioBalanceSession.Resolve()`** exists so a category/trim edit re-solves without re-decoding. Not in the spec, but without it every slider drag would re-analyze the whole library.
5. **`ApproxTruePeakDb` was struck entirely** — amended mid-execution, 2026-07-19, lead-approved. Spec §5.2 called for a 4× linear-interpolation true-peak meter. Linear interpolation produces a convex combination of its endpoints, so `|a + (b−a)t| ≤ max(|a|,|b|)` always: it cannot exceed the sample peak, and so cannot detect an inter-sample peak — the only reason true-peak meters exist. Review also produced a counter-example (mono `[0, 0, 1.0]`) where it read 2.5 dB *below* the sample peak, because the loop never evaluated `t = 1` on the final frame. `SamplePeakDb` survives as the single honest peak diagnostic; `ClipAnalysis`/`CachedLoudness` carry one `PeakDb` field instead of two. Real true-peak would need polyphase FIR upsampling (BS.1770-4 Annex 2) — a follow-up if ever needed.
4. **`ShortTermMax` (3 s) became `MomentaryMax` (400 ms)** — amended mid-execution, 2026-07-19, lead-approved. The original rationale in spec §5.1 was simply wrong: integrated loudness' relative gate already excludes a decay tail, so a 3 s window did not rescue short SFX, it *hurt* them. Measured on the plan's own test signal (0.5 s at −18 dBFS + 4 s at −50 dBFS): integrated −19.5, 3 s window −25.8. The 3 s mode read 6 dB **below** the mode it was meant to beat. A 400 ms window lands inside the attack (≈ −18) and delivers the intended behaviour. Task 4's failing test is what surfaced this — the implementer correctly reported BLOCKED rather than adjusting a constant to make it pass.
6. **`LoudnessCache`'s key is `(guid, fileLength, ticks)` where `ticks = max(assetTicks, metaTicks)`, not the asset's `lastWriteTicks` alone** — amended mid-execution, 2026-07-20, lead-approved, caught by opus review of Task 9's implementation (not by a plan-authoring-time test, since `LoudnessCache` itself takes `ticks` as an opaque long — the defect is only observable once a caller passes the asset-only value, which is Task 10). The plan's original rationale (deviation note in Task 9) reasoned only about the safe direction: a content-preserving touch causing a needless re-analysis. It missed the unsafe direction: the cache actually keys the *decoded clip*, which importer settings (Force To Mono, Quality, Sample Rate Override) change without touching the source file's bytes, length, or mtime at all — so a meta-only edit would silently serve a stale, wrong LUFS reading into the gain solver. Fix: `ticks` is now documented (XML docs on `TryGet`/`Put`, and the Task 9 rationale note above) as a caller contract — the combined max of the asset's and its `.meta`'s last-write ticks. Task 10, the only caller, must honor it. **SUPERSEDED by deviation #7:** a documented-but-unenforced caller contract turned out not to hold even within this plan document — Task 10's own body (~200 lines below the fix) still contained a complete, copy-paste-ready `ResolveIdentity` implementation using the asset-only tick, verbatim the defect this note describes fixing. A round-2 review caught it. The contract is now structural, not documented.
7. **Key derivation moved entirely inside `LoudnessCache`** (`KeyFor`/`KeyForPaths` + a real `LoudnessCacheKey` struct with a `Mode` field) — amended mid-execution, 2026-07-20, lead-approved (round 2, supersedes deviations #2 and #6 above). Round 1 (deviation #6) fixed the meta-timestamp defect as an XML-doc contract: callers must pass `ticks = max(assetTicks, metaTicks)`. That held for `LoudnessCache.cs` itself, but this plan document is what a Task 10 implementer actually reads top-to-bottom — and Task 10's own `Step 3` code block, written before the round-1 fix and never revisited, still contained a private `ResolveIdentity` deriving `ticks` from the asset file alone. A documented contract sitting a few hundred lines above a contradicting, complete, ready-to-paste implementation is worse than no contract: an implementer working their own section has no reason to scroll back into a different task's prose, and would have retyped the exact bug from a plan that claims three times over that it's fixed. Round-2 review caught this before Task 10 was ever implemented. Fix: `LoudnessCache.KeyFor(AudioClip, MeasureMode)` (and its pure/testable sibling `KeyForPaths`, for real-file tests that don't need an AssetDatabase-imported clip) is now the *only* place `Ticks` is computed; `TryGet`/`Put` take a `LoudnessCacheKey` struct and can no longer be handed loose `(guid, length, ticks)` primitives that a caller could assemble incorrectly. The measure mode moved onto the key struct as a real `MeasureMode Mode` field, replacing deviation #2's `$"{guid}:{(int)mode}"` string-mangling — Task 10's `LoudnessAnalyzer.Analyze` now calls `LoudnessCache.KeyFor(clip, mode)` directly and contains no identity-deriving code of its own. Both Task 9's and Task 10's plan sections were rewritten to match (the old Task 9 code blocks are flagged superseded rather than deleted, to preserve the historical record without being copy-paste hazards).

8. **The anchor is mathematically inert for `FinalGainDb` — kept, with the docs corrected to say so** — amended 2026-07-20, lead-approved, from an audit of Tasks 10–13 before Task 11 was implemented. `GainSolver` computes `raw_i = anchorLufs + offset_i + trim_i − measured_i` and then `final_i = raw_i − max_j(raw_j)`. `anchorLufs` is the same constant in every `raw_i`, so it cancels exactly in the subtraction: **`FinalGainDb` is provably independent of the anchor's measured loudness.** Its only live effect is on `IsOutlier` (`|raw| > OutlierThresholdDb`). This was not wrong when designed — it became inert when downward-only normalization was adopted (deviation from spec §6, because `AudioSource.volume` caps at 1.0), and the docs never caught up. **Lead's call: keep the anchor as the outlier reference and sanity readout, and fix every claim that it sets relative placement.** Category offsets set relative placement; the anchor does not. The README and the designer guide now say this outright, including the explicit warning that changing the anchor will *not* move the Gain column — without it, the first designer to try it reports a broken binding. Task 13's Step 9 now verifies both directions (swap anchor → gains unchanged; clear anchor → no outliers). **Second half of the same defect:** `Resolve` fell back to `AnchorLufs = 0f` when no anchor was set. 0 LUFS is digital full scale, so typical −20 LUFS content yields `raw ≈ +20` and trips the 12 dB threshold on *every* row — a fresh user pressing Analyze would get a wall of outlier markers on entirely healthy clips. Fixed by **suppressing the outlier flag entirely when `AnchorStatus != Ok`**, which is the honest option: with no anchor there is no reference, so no outlier judgement is meaningful. (A sane sentinel of −23 dB is also used for the raw readout, but suppression is what makes the behaviour correct rather than merely less wrong.) Two tests pin both halves.

9. **"The loudest clip lands at exactly 0 dB" is backwards, in eight places plus shipped code** — amended 2026-07-20. `max_j(raw_j)` is attained by the clip needing the **largest** gain, i.e. the one *quietest* relative to its target. That clip pins at 0 dB; the clip loudest relative to its target is attenuated **most**. The audit reported four locations; grepping `loudest` across the document found **eight**: Task 7's rationale prose, Task 7's `GainSolver` code block, Task 7's commit message, Task 11's test name, README, guide (×2), and Task 13's Step 9. All corrected except the Task 7 commit message, which is left verbatim as a historical record of a shipped commit with a note attached. **This wording originated in shipped code** — `GainSolver`'s class docstring — so the source file was corrected too (docstring only, no behavioural change; the suite stayed green at 557). Task 11's test `Analyze_SolvesGainsSoTheLoudestClipSitsAtZeroDb` was worse than misnamed: it asserted `Rows.Max(FinalGainDb) == 0`, which `GainSolver` guarantees for *any* input by construction, so it pinned nothing at all. Rewritten as `Analyze_PinsTheQuietestClipAtZeroDbAndAttenuatesTheLouderOne`, asserting both which clip lands at 0 and that the other is attenuated by exactly the measured loudness difference.

10. **A category or mode edit must call `Analyze`, not `Resolve`** — amended 2026-07-20. `Resolve`'s docstring claimed category and trim edits "affect the target, not the measurement". That is false for categories: a category carries its own `MeasureMode` (shipped defaults Music = `Integrated`, SFX/UI = `MomentaryMax`). Flipping a category's mode, changing a clip's category, or bulk-assigning all called `Resolve`, which cannot change `Analysis.Lufs` — so the old-mode measurement was silently kept and a wrong gain baked. The error is largest on short one-shots, exactly where the mode matters most. `Analyze` is the correct call and is nearly free: `Mode` is a field on `LoudnessCacheKey`, so every clip whose mode did not change is a cache hit. `Resolve` now survives for the trim slider alone, and its docstring says so. The existing test `Resolve_RecomputesGainsWithoutReMeasuring` **encoded the bug as expected behaviour** — it asserted that a *category* change shifts the gain without a re-measure — and was rewritten to use a trim instead; three new tests pin that a category change with a differing mode does re-measure, that `Resolve` alone does not, and that the two are distinguishable.

11. **`ClipListView.BuildVisible` was documented as pure but mutated the profile during `OnGUI`** — amended 2026-07-20. It took `AudioBalanceProfile` and called `SettingsFor`, which **appends a `ClipSettings` on a miss**. `BuildVisible` runs on every `OnGUI` event, so it wrote to the asset during rendering with no `Undo.RecordObject` and no `EditorUtility.SetDirty` — not undoable, not reliably persisted, but *would* be picked up by any `AssetDatabase.SaveAssets()`, which `GainTableWriter.Write` called (see #16). `DrawClipRow` did the same thing unconditionally, once per row per frame. It was also O(n²) per repaint — `SettingsFor` is a linear scan — roughly 250k comparisons at 500 clips in the sort and again in the row draw. Fixed structurally: the window resolves settings **once** into a dictionary outside the render path (wrapped in Undo/SetDirty, where creating entries is legitimate), and `BuildVisible` now takes a read-only `Func<AudioBalanceRow, string>` lookup so it *cannot* reach the profile. A test asserts no settings are created for unknown clips.

12. **`AudioPreviewPlayer` rebuilt on `UnityEditor.AudioUtil` + pre-scaling** — amended 2026-07-20, lead-approved. The planned design built a hidden scene `AudioSource` pair and called `AudioSource.Play()`. **`AudioSource.Play()` produces no audio outside Play Mode**, so the feature could not have worked — both preview buttons would have been silently dead. The plan's stated reason for rejecting Unity's built-in clip preview (no volume control) was correct; its replacement simply did not play. **Lead's call: reflect into `UnityEditor.AudioUtil.PlayPreviewClip`** — the standard approach for editor audio tooling — **and make the gain audible by pre-scaling**, building a temporary gain-applied `AudioClip` and previewing that. A/B mixes the anchor into the same temporary clip, which also avoids depending on `AudioUtil` supporting simultaneous previews. `AudioUtil` is internal and its signatures have changed across Unity versions (the package declares `"unity": "2022.3"`; this repo's editor is 6000.3.8f1), so resolution searches by name and first-parameter type across `PlayPreviewClip`/`PlayClip`, fills the remaining arguments from the resolved overload's own parameter list, and **degrades to a single actionable warning** rather than throwing or failing silently. Lifetime: one static temp-clip slot, destroyed before each new preview and again on domain reload, play-mode change, and window close — never one per click. **Testing:** there was no test file for `AudioPreviewPlayer` at all, unlike the rest of the package. Reflection-dependent playback cannot be meaningfully unit-tested, so the sample arithmetic was extracted into `PreviewClipFactory` (`Scale`/`Mix`) and given 7 tests, and the untestable reflection boundary is documented on the class in the same form `ClipSampleReader.StreamingError` documents its Streaming gap — the precedent this codebase already established.

13. **Layout defects that would not have survived contact with the editor** — amended 2026-07-20. (a) `CategoryBlockHeight` was a fixed `130f` while the block grows 20 px per category. The block needs `4 + 20 + 20n + 18` px, so it overflows the clip table — with overlapping click rects — at **five** categories, not six as first reported; the window ships an "Add Category" button and defaults to three, so that is two clicks away. Now computed from the category count. (b) Task 13 appended Write Table and the Table `ObjectField` to the single toolbar row, putting the field at x = 658 running to x = 838 against 708 px of usable width at `minSize`. That is a **dead end rather than a cosmetic clip**: Write Table is disabled until `Table != null`, and the control that assigns the table was the one cut off. The toolbar now wraps to a second row. (c) The `Play`/`A/B` buttons end at x = 780 against a content width of 688, and the scroll view's content rect was hard-coded to the viewport width, so **no horizontal scrollbar ever appeared** and both of Task 13's headline features were unreachable at the default size. Content width is now `max(viewport, MinRowWidth)`. *(The audit quoted x = 832 and x = 786 for (b) and (c); re-deriving from the plan's own rect arithmetic gives 658 and 780. The defects are real; the figures here are the corrected ones.)*

14. **The progress bar was decorative and Cancel did nothing** — amended 2026-07-20. `RunAnalysis` looped over the clips calling `DisplayCancelableProgressBar` while doing **no work**, then called `_session.Analyze` once, afterwards, as a single blocking call. The bar swept to 100% instantly and the editor then froze with no feedback; `break` on cancel exited only the display loop while `Analyze` still ran to completion. Fixing it requires an **API change**, recorded here explicitly: `AudioBalanceSession.Analyze` now takes an optional `Func<AudioClip, int, int, bool> onProgress` invoked before each clip (returning `true` to cancel) and returns `bool` for completed-vs-cancelled. On cancel it keeps and solves the rows measured so far, so the table shows partial-but-consistent results instead of blanking. A test pins that cancelling at the first clip stops the work.

15. **`result.Reverse()` destroyed sort stability when descending** — amended 2026-07-20. The comment claimed `OrderBy`'s stability preserved discovery order for equal keys. True ascending; false descending, because the post-hoc `Reverse()` inverts tie groups along with everything else. The test covered `ascending: true` only, which is why it survived review. Now `OrderBy`/`OrderByDescending` share one key selector via a small `Order` helper — stable in both directions, and the two directions cannot drift apart. A descending-stability test was added.

16. **`AssetDatabase.SaveAssets()` moved out of `GainTableWriter.Write`** — amended 2026-07-20. `Write` is exercised by a unit test, so running the suite flushed **every dirty asset in the project** to disk; combined with #11's render-path mutations, that was a live route for unintended writes. `Write` is now a pure asset mutation (`SetEntries` + `SetDirty`) and the window's button handler calls `SaveAssets()` — an explicit user action, which is the only place a project-wide flush belongs.

17. **One Undo entry per frame during drags** — amended 2026-07-20. Both the trim slider and the category fields compared values directly and called `Undo.RecordObject` on every `OnGUI` frame where the value differed, so a single trim drag produced dozens of undo entries and re-ran `GainSolver` over every row for each one; the category `TextField` did the same per keystroke frame. Now `EditorGUI.BeginChangeCheck`/`EndChangeCheck`, with the undo recorded once at the start of a drag gesture and `SetDirty` committed on `MouseUp`/`DragExited`. The category name field became a `DelayedTextField` so it commits on Enter/focus-loss rather than per keystroke.

18. **`_selected` was never pruned** — amended 2026-07-20. Switching profiles called `_session.Clear()` but left `_selected` holding clips from the old profile, so the bulk button read `Set Category (12)` while the resolved target list was empty — clicking appeared to work and silently did nothing. The profile-switch branch now clears the selection and invalidates the settings map, `PruneSelection` drops any clip no longer in the resolved set each frame, and `ShowBulkCategoryMenu` shows a disabled "Nothing selected" item rather than an empty menu if it is ever reached with no targets.

19. **Non-ASCII glyphs replaced in IMGUI captions only** — amended 2026-07-20. `▸` (empty-state label), `▲`/`▼` (sort direction) and `▶` (preview) are BMP geometric shapes, **not** astral-plane emoji, so the known project hazard (emoji rendering blank in Unity 2022.3 IMGUI) does not strictly apply — but default-font glyph coverage is not guaranteed and a blank button is indistinguishable from a broken one. Replaced with `>`, `Asc`/`Desc`, and `Play`. The `▸` characters in the README, the designer guide, and the plan's own prose are **deliberately left alone**: those are rendered by a markdown viewer, not by IMGUI.

20. **Stale 4-argument `ClipAnalysis.Ok` call, and the type-consistency claim it falsified** — amended 2026-07-20. Task 12's test helper called `ClipAnalysis.Ok(clip, lufs, -3f, -2.8f)`; the shipped signature takes **three** arguments. Deviation #5 struck `ApproxTruePeakDb` and collapsed the two peak fields into one `PeakDb`, and that amendment reached Task 13's otherwise-identical helper (which is correct) but not Task 12's — the trailing `-2.8f` is vestigial. Fixed. This falsified the "Type consistency: verified" claim below, which has been re-derived rather than restated — see the note there.

21. **`GUIUtility.ExitGUI()` convention noted** — amended 2026-07-20. The window justifies its absolute-rect layout by the `ExitGUIException` layout-stack crash, then calls `EditorUtility.OpenFolderPanel` and `DisplayDialog` from inside `OnGUI` — the very pattern the rationale warns about. On inspection the rationale is **narrowly correct**: absolute rects genuinely immunize the layout stack, so there is nothing for an unwinding `ExitGUIException` to corrupt here. This is a risk to watch, not a defect, and no code changed. The class docstring now records the convention explicitly — call `ExitGUI()` after a modal that mutates state the GUI is about to keep reading, and note that adding any `GUILayout` block to this window forfeits the immunity entirely.

22. **Test hygiene: the reported divergence does not exist** — checked 2026-07-20, **no change made**, recorded because the audit asked for one. The finding was that Tasks 11–13's tests never `DestroyImmediate` their `ScriptableObject.CreateInstance` objects or `AudioClip.Create` clips, and should "match whatever the shipped test files do". Checking the shipped suite: **no** test file calls `DestroyImmediate` at all. The single `[TearDown]` in the package (`LoudnessCacheTests`) deletes a temp *file*, not Unity objects. So Tasks 11–13 are already consistent with the established convention, and adding cleanup to them alone would make them the outliers. Leaked `ScriptableObject`s in an EditMode run are a real if minor cost, but that is a **suite-wide** decision affecting eleven existing files and is out of scope for a plan amendment — flagged here for the lead rather than half-applied to three files.

**Type consistency — re-derived, not restated (see deviation #20).** The previous blanket "verified" claim was false, so every cross-task type usage was re-checked against the shipped source rather than against the plan. What was checked and what was found:

- `ClipAnalysis.Ok(AudioClip, float, float)` — shipped takes **3** args. All 14 call sites in Task 7 are correct; Task 13's is correct; **Task 12's was 4-arg and is now fixed**.
- `new ClipAnalysis(...)` — shipped takes **5** parameters (`clip, status, lufs, peakDb, reason = null`). Tasks 12 and 13 both pass 5 and are correct. **However, Task 7's "Interfaces" block (line ~1859) documents the constructor as 4 parameters, omitting `reason` entirely** — a second, separate staleness in the same family as #20. Corrected.
- `GainResult(AudioClip, ClipStatus, float, float, bool)` — 5 args; consistent at every appearance, including the new outlier-suppression path in Task 11.
- `ClipStatus`, `MeasureMode` — enum member names consistent throughout.
- `AudioGainTable.Entry { Clip, GainDb }` — consistent.
- `AudioBalanceRow { Clip, Analysis, Gain }` — consistent.
- `LoudnessCacheKey` — carries `Mode`, which is what makes deviation #10's "re-analyze is nearly free" claim true; verified against the shipped struct rather than assumed.
- `ClipListView.BuildVisible` — signature changed by deviation #11; **all 9 call sites in the test block and the 1 in the window were updated**, and the now-unused private `CategoryOf` helper was removed from `ClipListView`.
- `AudioBalanceSession.Analyze` — signature changed by deviation #14; existing call sites still compile because the new parameter is optional and the return value is ignorable.

**Test count:** the original "101 EditMode tests across 13 tasks" was a plan-authoring estimate and is badly out of step with reality — Tasks 1–10 alone ship **557**. Treat it as an estimate, not a target. The amendments above change the per-task counts for the three unimplemented tasks: Task 11 **7 → 12**, Task 12 **11 → 13**, Task 13 **8 → 15** (8 `GainTableWriter` + 7 new `PreviewClipFactory`).
