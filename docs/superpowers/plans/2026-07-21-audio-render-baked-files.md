# Audio Render — Gain-Baked Files Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render every analyzed clip as a 24-bit WAV with its solved gain already applied, into a timestamped hand-off folder outside the Unity asset pipeline, accompanied by a plain-text manifest.

**Architecture:** Two new pure-logic classes under `Editor/Render/` plus one toolbar button. `WavWriter` encodes 24-bit RIFF; `RenderedAudioWriter` orchestrates read → scale → write and builds the manifest. Both follow the existing `GainTableWriter` pattern: all logic lives in a static class the window merely calls, so it is unit-testable without an editor window. The DSP already exists (`ClipSampleReader.TryRead`, `PreviewClipFactory.Scale`) and is not reimplemented.

**Tech Stack:** Unity 6000.3.8f1, C#, NUnit EditMode tests, `com.hoppa.audiobalance` package.

**Spec:** `docs/superpowers/specs/2026-07-21-audio-render-baked-files-design.md`

## Global Constraints

- Target package: `Packages/com.hoppa.audiobalance/`. Namespace `Hoppa.AudioBalance.Editor`; tests `Hoppa.AudioBalance.Editor.Tests`.
- Baseline is **688/688 EditMode green**. Every task ends green; a red suite is a stop condition, not something to work around.
- Output bit depth is **24-bit PCM**, always. Sample rate and channel count pass through from the source clip unchanged — only amplitude changes.
- Output root is `StagingExports/AudioBalance/<yyyy-MM-dd_HHmm>/`, at **repo root, outside `Assets/`**. This placement is load-bearing: it is what makes re-render gain-stacking structurally impossible. Never write renders under `Assets/` or `Packages/`.
- The skip guard is **`row.Gain.Clip == null`**, never `row.Gain.Status == ClipStatus.Ok`. `ClipStatus.Ok` is `0`, so an unsolved `default(GainResult)` reports `Ok` and a status check is vacuous by construction. This has already produced a test that passed against a broken build in this package.
- No source asset is ever opened for write.
- Tests use `AudioClip.Create` for pure-logic cases and `AudioAssetFixture` when a real asset path is required. `AudioAssetFixture` writes into `Assets/` and cleans up in `Dispose()`; always use it in a `try`/`finally` or `[TearDown]`.
- Assertions on float dB values use a tolerance (`1e-4f`), matching `GainTableWriterTests`.

---

## File Structure

| File | Responsibility |
|---|---|
| `Editor/Render/WavWriter.cs` (create) | 24-bit PCM RIFF encoding. Pure; no `UnityEditor` dependency. |
| `Editor/Render/RenderPaths.cs` (create) | Source-path → mirrored-output-path derivation, lossy-format detection, timestamped root. Pure string logic. |
| `Editor/Render/RenderedAudioWriter.cs` (create) | Orchestration: skip guard, read, scale, write, report. Manifest text. |
| `Editor/Window/AudioBalanceWindow.cs` (modify, `DrawToolbar` ~line 511-520) | One `Render Files...` button on toolbar row 1. |
| `.gitignore` (modify) | Ignore `StagingExports/AudioBalance/`. |
| `Tests/Editor/WavWriterTests.cs` (create) | Header fields, sample encoding, round-trip. |
| `Tests/Editor/RenderPathsTests.cs` (create) | Mirroring incl. the real `MainMenu` collision, lossy detection, timestamp format. |
| `Tests/Editor/RenderedAudioWriterTests.cs` (create) | Skip semantics, batch resilience, manifest content, end-to-end. |

---

## Task 1: `WavWriter` — 24-bit PCM RIFF encoding

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Render/WavWriter.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/WavWriterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `WavWriter.Write24(string absolutePath, float[] interleaved, int channels, int sampleRate)` → `void`. Also `WavWriter.MaxAmplitude` (`const int` = 8388607).

**Background:** `Tests/Editor/AudioAssetFixture.cs:114` already writes a 16-bit RIFF file. **Do not move or retarget it** — it is a test tool producing importable fixtures, and coupling it to production output format is explicitly rejected by the spec (§4.1). Model the header layout on it; write new code.

24-bit differs from 16-bit in four places: `bitsPerSample` (24), `blockAlign` (`channels * 3`), `byteRate` (`sampleRate * channels * 3`), and `dataSize` (`samples * 3`). Samples are signed little-endian 3-byte values.

- [ ] **Step 1: Write the failing test**

Create `Packages/com.hoppa.audiobalance/Tests/Editor/WavWriterTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class WavWriterTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(),
                "HoppaWavWriterTests_" + System.Guid.NewGuid().ToString("N") + ".wav");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [Test]
        public void Write24_HeaderDeclares24BitPcmWithMatchingDerivedFields()
        {
            // 4 frames of stereo => 8 interleaved samples.
            WavWriter.Write24(_path, new float[8], channels: 2, sampleRate: 48000);

            var bytes = File.ReadAllBytes(_path);

            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.AreEqual("fmt ", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));

            Assert.AreEqual(1, System.BitConverter.ToInt16(bytes, 20), "PCM format tag");
            Assert.AreEqual(2, System.BitConverter.ToInt16(bytes, 22), "channels");
            Assert.AreEqual(48000, System.BitConverter.ToInt32(bytes, 24), "sample rate");
            Assert.AreEqual(48000 * 2 * 3, System.BitConverter.ToInt32(bytes, 28), "byte rate");
            Assert.AreEqual(2 * 3, System.BitConverter.ToInt16(bytes, 32), "block align");
            Assert.AreEqual(24, System.BitConverter.ToInt16(bytes, 34), "bits per sample");

            Assert.AreEqual("data", System.Text.Encoding.ASCII.GetString(bytes, 36, 4));
            Assert.AreEqual(8 * 3, System.BitConverter.ToInt32(bytes, 40), "data size");
            Assert.AreEqual(44 + 8 * 3, bytes.Length, "total file length");
            Assert.AreEqual(36 + 8 * 3, System.BitConverter.ToInt32(bytes, 4), "RIFF chunk size");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run the EditMode suite filtered to this class (via the Unity MCP `tests-run` tool, `testMode: EditMode`, filter `Hoppa.AudioBalance.Editor.Tests.WavWriterTests`).
Expected: FAIL to compile — `WavWriter` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Packages/com.hoppa.audiobalance/Editor/Render/WavWriter.cs`:

```csharp
using System.IO;
using System.Text;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Writes 24-bit PCM RIFF WAV files.
    ///
    /// <para>
    /// 24-bit rather than 16-bit because every gain this package solves is ATTENUATION (at or
    /// below 0 dB -- see the headroom pass in <see cref="GainSolver"/>). Attenuating by 20 dB
    /// costs roughly 3.3 bits, which in 16 bits leaves an effective ~12.7-bit signal before the
    /// downstream OGG encode. 24 bits absorbs that with room to spare.
    /// </para>
    ///
    /// <para>
    /// <c>AudioAssetFixture.WriteWav</c> in the test assembly writes a similar 16-bit file. That
    /// is deliberately NOT shared: it exists to produce importable test fixtures, and pointing it
    /// at this format would couple fixture convenience to production output. The RIFF layout is
    /// the shared idea; the code is not.
    /// </para>
    /// </summary>
    public static class WavWriter
    {
        /// <summary>Largest magnitude a 24-bit signed sample can carry.</summary>
        public const int MaxAmplitude = 8388607;

        private const int BytesPerSample = 3;

        public static void Write24(string absolutePath, float[] interleaved, int channels, int sampleRate)
        {
            var samples = interleaved ?? new float[0];

            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dataSize = samples.Length * BytesPerSample;
            var byteRate = sampleRate * channels * BytesPerSample;
            var blockAlign = (short)(channels * BytesPerSample);

            using (var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16); // PCM fmt chunk size
                writer.Write((short)1); // PCM format tag
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write((short)(BytesPerSample * 8));

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);

                foreach (var sample in samples)
                {
                    var clamped = Mathf.Clamp(sample, -1f, 1f);
                    var value = Mathf.RoundToInt(clamped * MaxAmplitude);

                    writer.Write((byte)(value & 0xFF));
                    writer.Write((byte)((value >> 8) & 0xFF));
                    writer.Write((byte)((value >> 16) & 0xFF));
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run the same filter. Expected: PASS (1 test).

- [ ] **Step 5: Add the sample-encoding and round-trip tests**

Append to `WavWriterTests`:

```csharp
        private static int ReadSample24(byte[] bytes, int index)
        {
            var offset = 44 + index * 3;
            var value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);

            // Sign-extend the 24-bit value into an int.
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            return value;
        }

        [Test]
        public void Write24_EncodesFullScaleSilenceAndNegativeFullScale()
        {
            WavWriter.Write24(_path, new[] { 1f, 0f, -1f }, channels: 1, sampleRate: 48000);

            var bytes = File.ReadAllBytes(_path);

            Assert.AreEqual(WavWriter.MaxAmplitude, ReadSample24(bytes, 0), "positive full scale");
            Assert.AreEqual(0, ReadSample24(bytes, 1), "silence");
            Assert.AreEqual(-WavWriter.MaxAmplitude, ReadSample24(bytes, 2), "negative full scale");
        }

        [Test]
        public void Write24_ClampsSamplesBeyondFullScale()
        {
            WavWriter.Write24(_path, new[] { 2f, -2f }, channels: 1, sampleRate: 48000);

            var bytes = File.ReadAllBytes(_path);

            Assert.AreEqual(WavWriter.MaxAmplitude, ReadSample24(bytes, 0));
            Assert.AreEqual(-WavWriter.MaxAmplitude, ReadSample24(bytes, 1));
        }

        [Test]
        public void Write24_RoundTripsAHalfScaleSampleWithinOneQuantumOfItsInput()
        {
            WavWriter.Write24(_path, new[] { 0.5f }, channels: 1, sampleRate: 48000);

            var bytes = File.ReadAllBytes(_path);
            var decoded = ReadSample24(bytes, 0) / (float)WavWriter.MaxAmplitude;

            // One quantum at 24 bits; far tighter than 16-bit could manage, which is the point.
            Assert.AreEqual(0.5f, decoded, 1f / WavWriter.MaxAmplitude);
        }

        [Test]
        public void Write24_CreatesMissingDirectories()
        {
            var nested = Path.Combine(Path.GetDirectoryName(_path),
                "HoppaWavWriterNested_" + System.Guid.NewGuid().ToString("N"), "out.wav");

            try
            {
                WavWriter.Write24(nested, new float[4], channels: 1, sampleRate: 48000);
                Assert.IsTrue(File.Exists(nested));
            }
            finally
            {
                var dir = Path.GetDirectoryName(nested);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
```

- [ ] **Step 6: Run tests to verify they pass**

Run the `WavWriterTests` filter. Expected: PASS (5 tests).

- [ ] **Step 7: Run the full EditMode suite**

Expected: 693/693 PASS (688 baseline + 5 new), 0 failures.

- [ ] **Step 8: Commit**

```bash
git add Packages/com.hoppa.audiobalance/Editor/Render/WavWriter.cs \
        Packages/com.hoppa.audiobalance/Editor/Render/WavWriter.cs.meta \
        Packages/com.hoppa.audiobalance/Tests/Editor/WavWriterTests.cs \
        Packages/com.hoppa.audiobalance/Tests/Editor/WavWriterTests.cs.meta
git commit -m "feat(audio): 24-bit PCM WAV writer for rendered output"
```

---

## Task 2: `RenderPaths` — mirrored output paths, lossy detection, timestamped root

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Render/RenderPaths.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/RenderPathsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `RenderPaths.MirroredRelativePath(string sourceAssetPath)` → `string`
  - `RenderPaths.IsLossySource(string sourceAssetPath)` → `bool`
  - `RenderPaths.TimestampedRoot(string stagingRoot, System.DateTime now)` → `string`
  - `RenderPaths.StagingRoot()` → `string` (absolute `<project>/StagingExports/AudioBalance`)

**Background:** Flat output is unsafe against real data. Bus Buddies has `MainMenu.ogg` at **both** `Assets/_BUB/Audio/Music/` and `Assets/_BUB/Resources/Audio/Music/`, and `Generic1.wav` likewise — a flat folder silently overwrites one with the other. Mirroring source structure makes the collision impossible and shows where each file belongs.

- [ ] **Step 1: Write the failing test**

Create `Packages/com.hoppa.audiobalance/Tests/Editor/RenderPathsTests.cs`:

```csharp
using System;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class RenderPathsTests
    {
        [Test]
        public void MirroredRelativePath_StripsAssetsPrefixAndForcesWavExtension()
        {
            Assert.AreEqual("_BUB/Resources/Audio/UI/FlyCoins.wav",
                RenderPaths.MirroredRelativePath("Assets/_BUB/Resources/Audio/UI/FlyCoins.wav"));

            Assert.AreEqual("_BUB/Resources/Audio/UI/CarEngineStart.wav",
                RenderPaths.MirroredRelativePath("Assets/_BUB/Resources/Audio/UI/CarEngineStart.mp3"));
        }

        [Test]
        public void MirroredRelativePath_KeepsSameNamedClipsInDifferentFoldersApart()
        {
            // The real Bus Buddies collision: MainMenu.ogg exists in BOTH of these folders.
            // A flat output folder would silently overwrite one with the other.
            var a = RenderPaths.MirroredRelativePath("Assets/_BUB/Audio/Music/MainMenu.ogg");
            var b = RenderPaths.MirroredRelativePath("Assets/_BUB/Resources/Audio/Music/MainMenu.ogg");

            Assert.AreEqual("_BUB/Audio/Music/MainMenu.wav", a);
            Assert.AreEqual("_BUB/Resources/Audio/Music/MainMenu.wav", b);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void MirroredRelativePath_KeepsNonAssetsRootsIntactSoPackageClipsStayDistinct()
        {
            Assert.AreEqual("Packages/com.example.audio/Sfx/Beep.wav",
                RenderPaths.MirroredRelativePath("Packages/com.example.audio/Sfx/Beep.wav"));
        }

        [Test]
        public void IsLossySource_FlagsCompressedFormatsCaseInsensitively()
        {
            Assert.IsTrue(RenderPaths.IsLossySource("Assets/A/x.mp3"));
            Assert.IsTrue(RenderPaths.IsLossySource("Assets/A/x.ogg"));
            Assert.IsTrue(RenderPaths.IsLossySource("Assets/A/x.OGG"));
            Assert.IsFalse(RenderPaths.IsLossySource("Assets/A/x.wav"));
            Assert.IsFalse(RenderPaths.IsLossySource("Assets/A/x.aiff"));
        }

        [Test]
        public void TimestampedRoot_UsesMinutePrecisionSoSameDayRendersDoNotCollide()
        {
            var morning = RenderPaths.TimestampedRoot("/out", new DateTime(2026, 7, 21, 9, 5, 0));
            var afternoon = RenderPaths.TimestampedRoot("/out", new DateTime(2026, 7, 21, 14, 32, 0));

            StringAssert.EndsWith("2026-07-21_0905", morning);
            StringAssert.EndsWith("2026-07-21_1432", afternoon);
            Assert.AreNotEqual(morning, afternoon);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Filter: `Hoppa.AudioBalance.Editor.Tests.RenderPathsTests`.
Expected: FAIL to compile — `RenderPaths` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Packages/com.hoppa.audiobalance/Editor/Render/RenderPaths.cs`:

```csharp
using System;
using System.IO;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Path derivation for rendered audio output. Pure string logic, deliberately free of
    /// AssetDatabase calls so it is testable without importing assets.
    /// </summary>
    public static class RenderPaths
    {
        private const string AssetsPrefix = "Assets/";

        /// <summary>Folder name under the project root. OUTSIDE Assets/ -- see the class remarks
        /// on <see cref="StagingRoot"/>.</summary>
        private const string StagingFolder = "StagingExports/AudioBalance";

        /// <summary>
        /// Maps a source asset path to its path under the export root, preserving folder
        /// structure and forcing a <c>.wav</c> extension.
        ///
        /// <para>
        /// Structure is preserved because flat output is unsafe against real projects: Bus
        /// Buddies carries <c>MainMenu.ogg</c> in two folders and <c>Generic1.wav</c> in two
        /// more, and a flat folder would silently overwrite one with the other. Mirroring makes
        /// the collision unrepresentable and shows where each file came from.
        /// </para>
        ///
        /// <para>
        /// The <c>Assets/</c> prefix is stripped because it is noise every path shares. Other
        /// roots (notably <c>Packages/</c>) are kept intact, so a package clip cannot collide
        /// with a project clip of the same relative path.
        /// </para>
        /// </summary>
        public static string MirroredRelativePath(string sourceAssetPath)
        {
            if (string.IsNullOrEmpty(sourceAssetPath))
            {
                return string.Empty;
            }

            var normalised = sourceAssetPath.Replace('\\', '/');

            if (normalised.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalised = normalised.Substring(AssetsPrefix.Length);
            }

            var directory = Path.GetDirectoryName(normalised);
            var name = Path.GetFileNameWithoutExtension(normalised);

            return string.IsNullOrEmpty(directory)
                ? name + ".wav"
                : directory.Replace('\\', '/') + "/" + name + ".wav";
        }

        /// <summary>
        /// True when the source is a lossy-compressed format, i.e. a generation is already lost
        /// before we read it. <see cref="ClipSampleReader"/> reads the DECODED clip, so for these
        /// the decode has happened and the downstream OGG encode will add another generation.
        /// Rendering them anyway is the right trade -- an unbalanced clip is a worse problem than
        /// a re-encoded one -- but the loss must be visible, so the manifest reports it.
        /// </summary>
        public static bool IsLossySource(string sourceAssetPath)
        {
            if (string.IsNullOrEmpty(sourceAssetPath))
            {
                return false;
            }

            var extension = Path.GetExtension(sourceAssetPath).ToLowerInvariant();

            return extension == ".mp3"
                || extension == ".ogg"
                || extension == ".aac"
                || extension == ".m4a";
        }

        /// <summary>
        /// Minute precision, not date. Iterating on category offsets means several renders in one
        /// afternoon, and a date-only folder would overwrite the export already sitting in the
        /// audio engineer's hands.
        /// </summary>
        public static string TimestampedRoot(string stagingRoot, DateTime now)
        {
            return Path.Combine(stagingRoot, now.ToString("yyyy-MM-dd_HHmm")).Replace('\\', '/');
        }

        /// <summary>
        /// Absolute path to <c>&lt;project&gt;/StagingExports/AudioBalance</c>.
        ///
        /// <para>
        /// Being outside <c>Assets/</c> is LOAD-BEARING, not tidiness.
        /// <c>LoudnessAnalyzer.FindClips</c> discovers clips through
        /// <c>AssetDatabase.IsValidFolder</c>, which only resolves folders Unity imports. Because
        /// Unity never imports this location, rendered output is invisible to the panel and can
        /// never be re-analyzed or re-rendered -- so the attenuation baked into a rendered file
        /// can never compound. Moving this under Assets/ would silently reintroduce that bug.
        /// </para>
        /// </summary>
        public static string StagingRoot()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.Combine(projectRoot, StagingFolder).Replace('\\', '/');
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Filter `RenderPathsTests`. Expected: PASS (5 tests).

- [ ] **Step 5: Run the full EditMode suite**

Expected: 698/698 PASS, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.audiobalance/Editor/Render/RenderPaths.cs \
        Packages/com.hoppa.audiobalance/Editor/Render/RenderPaths.cs.meta \
        Packages/com.hoppa.audiobalance/Tests/Editor/RenderPathsTests.cs \
        Packages/com.hoppa.audiobalance/Tests/Editor/RenderPathsTests.cs.meta
git commit -m "feat(audio): mirrored export paths + lossy-source detection"
```

---

## Task 3: `RenderedAudioWriter` — orchestration, skip semantics, report

**Files:**
- Create: `Packages/com.hoppa.audiobalance/Editor/Render/RenderedAudioWriter.cs`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/RenderedAudioWriterTests.cs`

**Interfaces:**
- Consumes: `WavWriter.Write24` (Task 1); `RenderPaths.MirroredRelativePath` / `IsLossySource` (Task 2); existing `ClipSampleReader.TryRead(AudioClip, out float[], out string)` and `PreviewClipFactory.Scale(float[], float)`.
- Produces:
  - `RenderedFile` struct: `SourcePath` (string), `OutputPath` (string), `GainDb` (float), `IsLossySource` (bool), `SkipReason` (string), `Skipped` (bool property, `SkipReason != null`).
  - `RenderReport` class: `OutputRoot` (string), `Files` (`List<RenderedFile>`, initialised non-null so Task 4's tests can use a collection initialiser), `WrittenCount` (int), `SkippedCount` (int).
  - `RenderedAudioWriter.Render(IReadOnlyList<AudioBalanceRow> rows, string outputRoot)` → `RenderReport`.

**Background — the skip guard:** use `row.Gain.Clip == null`, **never** `row.Gain.Status == ClipStatus.Ok`. `ClipStatus.Ok` is `0`, so a `default(GainResult)` on an unsolved row reports `Ok`. `GainTableWriter.cs:27-37` carries this same guard with the same reasoning. A status check here would be vacuous by construction and would pass its own tests while writing garbage.

- [ ] **Step 1: Write the failing test**

Create `Packages/com.hoppa.audiobalance/Tests/Editor/RenderedAudioWriterTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class RenderedAudioWriterTests
    {
        private string _root;
        private AudioAssetFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(),
                "HoppaRenderTests_" + System.Guid.NewGuid().ToString("N")).Replace('\\', '/');
            _fixture = new AudioAssetFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _fixture.Dispose();

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private AudioBalanceRow AssetRow(string name, float gainDb)
        {
            var clip = _fixture.CreateTone(name, peakDbfs: -6.0, seconds: 0.05);
            return new AudioBalanceRow
            {
                Clip = clip,
                Analysis = ClipAnalysis.Ok(clip, -20f, -6f),
                Gain = new GainResult(clip, ClipStatus.Ok, gainDb, gainDb, false)
            };
        }

        [Test]
        public void Render_WritesOneWavPerSolvedRowUnderTheMirroredSourcePath()
        {
            var row = AssetRow("kick", -6f);

            var report = RenderedAudioWriter.Render(new List<AudioBalanceRow> { row }, _root);

            Assert.AreEqual(1, report.WrittenCount);
            Assert.AreEqual(0, report.SkippedCount);

            var written = report.Files.Single();
            Assert.IsFalse(written.Skipped);
            Assert.IsTrue(File.Exists(written.OutputPath), "rendered file should exist on disk");
            StringAssert.EndsWith(".wav", written.OutputPath);
            Assert.AreEqual(-6f, written.GainDb, 1e-4f);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Filter: `Hoppa.AudioBalance.Editor.Tests.RenderedAudioWriterTests`.
Expected: FAIL to compile — `RenderedAudioWriter` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `Packages/com.hoppa.audiobalance/Editor/Render/RenderedAudioWriter.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>One clip's outcome in a render pass.</summary>
    public readonly struct RenderedFile
    {
        public readonly string SourcePath;
        public readonly string OutputPath;
        public readonly float GainDb;
        public readonly bool IsLossySource;
        public readonly string SkipReason;

        public RenderedFile(string sourcePath, string outputPath, float gainDb,
            bool isLossySource, string skipReason = null)
        {
            SourcePath = sourcePath;
            OutputPath = outputPath;
            GainDb = gainDb;
            IsLossySource = isLossySource;
            SkipReason = skipReason;
        }

        public bool Skipped => SkipReason != null;
    }

    /// <summary>The result of one render pass.</summary>
    public sealed class RenderReport
    {
        public string OutputRoot;
        public List<RenderedFile> Files = new List<RenderedFile>();

        public int WrittenCount
        {
            get
            {
                var count = 0;
                foreach (var file in Files)
                {
                    if (!file.Skipped)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int SkippedCount => Files.Count - WrittenCount;
    }

    /// <summary>
    /// Renders solved gains into standalone 24-bit WAV files for hand-off. Source assets are
    /// never opened for write; output lands outside the asset pipeline entirely (see
    /// <see cref="RenderPaths.StagingRoot"/>).
    /// </summary>
    public static class RenderedAudioWriter
    {
        public const string NoGainSolvedReason = "no gain solved for this clip";
        public const string NotAnalyzableReason = "clip is not in an analyzable state";
        public const string MissingAssetPathReason = "clip has no asset path (procedural clip)";

        public static RenderReport Render(IReadOnlyList<AudioBalanceRow> rows, string outputRoot)
        {
            var report = new RenderReport { OutputRoot = outputRoot };
            if (rows == null)
            {
                return report;
            }

            foreach (var row in rows)
            {
                if (row?.Clip == null)
                {
                    continue;
                }

                var sourcePath = AssetDatabase.GetAssetPath(row.Clip);
                var lossy = RenderPaths.IsLossySource(sourcePath);

                if (string.IsNullOrEmpty(sourcePath))
                {
                    report.Files.Add(new RenderedFile(row.Clip.name, null, 0f, false,
                        MissingAssetPathReason));
                    continue;
                }

                if (row.Analysis.Status != ClipStatus.Ok)
                {
                    report.Files.Add(new RenderedFile(sourcePath, null, 0f, lossy,
                        row.Analysis.Reason ?? NotAnalyzableReason));
                    continue;
                }

                // Gain.Clip, NOT Gain.Status: ClipStatus.Ok is 0, so an unsolved
                // default(GainResult) reports Ok and a status check would be vacuous by
                // construction. GainTableWriter carries the same guard for the same reason.
                if (row.Gain.Clip == null)
                {
                    report.Files.Add(new RenderedFile(sourcePath, null, 0f, lossy,
                        NoGainSolvedReason));
                    continue;
                }

                if (!ClipSampleReader.TryRead(row.Clip, out var samples, out var error))
                {
                    // One unreadable clip must not cost the whole hand-off.
                    report.Files.Add(new RenderedFile(sourcePath, null, 0f, lossy, error));
                    continue;
                }

                var scaled = PreviewClipFactory.Scale(samples, row.Gain.FinalGainDb);
                var outputPath = Path.Combine(outputRoot,
                    RenderPaths.MirroredRelativePath(sourcePath)).Replace('\\', '/');

                WavWriter.Write24(outputPath, scaled, row.Clip.channels, row.Clip.frequency);

                report.Files.Add(new RenderedFile(sourcePath, outputPath,
                    row.Gain.FinalGainDb, lossy));
            }

            return report;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Filter `RenderedAudioWriterTests`. Expected: PASS (1 test).

- [ ] **Step 5: Add skip-semantics and resilience tests**

Append to `RenderedAudioWriterTests`:

```csharp
        [Test]
        public void Render_SkipsRowsWhoseGainWasNeverSolved()
        {
            var clip = _fixture.CreateTone("unsolved", peakDbfs: -6.0, seconds: 0.05);
            var row = new AudioBalanceRow
            {
                Clip = clip,
                Analysis = ClipAnalysis.Ok(clip, -20f, -6f),
                Gain = default // Status reads Ok because ClipStatus.Ok == 0.
            };

            var report = RenderedAudioWriter.Render(new List<AudioBalanceRow> { row }, _root);

            Assert.AreEqual(0, report.WrittenCount, "an unsolved row must not be rendered");
            Assert.AreEqual(1, report.SkippedCount);
            Assert.AreEqual(RenderedAudioWriter.NoGainSolvedReason,
                report.Files.Single().SkipReason);
        }

        [Test]
        public void Render_SkipsRowsThatAreNotAnalyzableAndKeepsTheirReason()
        {
            var clip = _fixture.CreateSilence("quiet", seconds: 0.05);
            var row = new AudioBalanceRow
            {
                Clip = clip,
                Analysis = ClipAnalysis.Silent(clip),
                Gain = new GainResult(clip, ClipStatus.Silent, 0f, 0f, false)
            };

            var report = RenderedAudioWriter.Render(new List<AudioBalanceRow> { row }, _root);

            Assert.AreEqual(0, report.WrittenCount);
            Assert.AreEqual(ClipAnalysis.SilentReason, report.Files.Single().SkipReason);
        }

        [Test]
        public void Render_SkipsProceduralClipsThatHaveNoAssetPath()
        {
            var clip = AudioClip.Create("procedural", 128, 1, 44100, false);
            var row = new AudioBalanceRow
            {
                Clip = clip,
                Analysis = ClipAnalysis.Ok(clip, -20f, -6f),
                Gain = new GainResult(clip, ClipStatus.Ok, -3f, -3f, false)
            };

            var report = RenderedAudioWriter.Render(new List<AudioBalanceRow> { row }, _root);

            Assert.AreEqual(0, report.WrittenCount);
            Assert.AreEqual(RenderedAudioWriter.MissingAssetPathReason,
                report.Files.Single().SkipReason);
        }

        [Test]
        public void Render_ContinuesPastASkippedRowSoOneBadClipCannotCostTheHandoff()
        {
            var good = AssetRow("good", -3f);

            var badClip = _fixture.CreateSilence("bad", seconds: 0.05);
            var bad = new AudioBalanceRow
            {
                Clip = badClip,
                Analysis = ClipAnalysis.Silent(badClip),
                Gain = new GainResult(badClip, ClipStatus.Silent, 0f, 0f, false)
            };

            var alsoGood = AssetRow("alsoGood", -9f);

            var report = RenderedAudioWriter.Render(
                new List<AudioBalanceRow> { bad, good, alsoGood }, _root);

            Assert.AreEqual(2, report.WrittenCount, "the two healthy clips must still render");
            Assert.AreEqual(1, report.SkippedCount);
        }

        [Test]
        public void Render_AttenuatesTheRenderedSamplesRatherThanCopyingTheSource()
        {
            // -6 dB halves amplitude; the written file must be quieter than an unrendered copy.
            var loud = RenderedAudioWriter.Render(
                new List<AudioBalanceRow> { AssetRow("flat", 0f) }, _root + "/flat");
            var quiet = RenderedAudioWriter.Render(
                new List<AudioBalanceRow> { AssetRow("cut", -6f) }, _root + "/cut");

            var loudPeak = PeakOf(loud.Files.Single().OutputPath);
            var quietPeak = PeakOf(quiet.Files.Single().OutputPath);

            Assert.Less(quietPeak, loudPeak * 0.75f,
                "a -6 dB render must be audibly quieter than a 0 dB render");
        }

        private static int PeakOf(string wavPath)
        {
            var bytes = File.ReadAllBytes(wavPath);
            var peak = 0;

            for (var offset = 44; offset + 2 < bytes.Length; offset += 3)
            {
                var value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
                if ((value & 0x800000) != 0)
                {
                    value |= unchecked((int)0xFF000000);
                }

                peak = System.Math.Max(peak, System.Math.Abs(value));
            }

            return peak;
        }

        [Test]
        public void Render_HandlesNullAndEmptyRowSets()
        {
            Assert.AreEqual(0, RenderedAudioWriter.Render(null, _root).Files.Count);
            Assert.AreEqual(0,
                RenderedAudioWriter.Render(new List<AudioBalanceRow>(), _root).Files.Count);
        }
```

- [ ] **Step 6: Run tests to verify they pass**

Filter `RenderedAudioWriterTests`. Expected: PASS (7 tests).

- [ ] **Step 7: Prove the skip-guard test is not vacuous**

Temporarily change the guard in `RenderedAudioWriter.cs` from
`if (row.Gain.Clip == null)` to `if (row.Gain.Status != ClipStatus.Ok)`, then re-run
`Render_SkipsRowsWhoseGainWasNeverSolved`.
Expected: **FAIL** — proving the test actually pins the guard. Revert the change and re-run to confirm PASS.

This step is mandatory. The vacuous-`Ok` trap has already produced a green test against a broken build in this package; a guard test that cannot fail is worse than none.

- [ ] **Step 8: Run the full EditMode suite**

Expected: 705/705 PASS, 0 failures.

- [ ] **Step 9: Commit**

```bash
git add Packages/com.hoppa.audiobalance/Editor/Render/RenderedAudioWriter.cs \
        Packages/com.hoppa.audiobalance/Editor/Render/RenderedAudioWriter.cs.meta \
        Packages/com.hoppa.audiobalance/Tests/Editor/RenderedAudioWriterTests.cs \
        Packages/com.hoppa.audiobalance/Tests/Editor/RenderedAudioWriterTests.cs.meta
git commit -m "feat(audio): render solved gains into 24-bit WAV files"
```

---

## Task 4: Manifest

**Files:**
- Modify: `Packages/com.hoppa.audiobalance/Editor/Render/RenderedAudioWriter.cs`
- Modify: `Packages/com.hoppa.audiobalance/Tests/Editor/RenderedAudioWriterTests.cs`

**Interfaces:**
- Consumes: `RenderReport`, `RenderedFile` (Task 3).
- Produces: `RenderedAudioWriter.BuildManifest(RenderReport report)` → `string`; `RenderedAudioWriter.ManifestFileName` (`const string` = `"manifest.txt"`). `Render` now also writes the manifest to `<outputRoot>/manifest.txt`.

**Background:** The manifest is read by a person deciding what to convert, so it is plain text, not CSV or JSON. It must make the lossy-source generation loss **visible** — that is its main job beyond bookkeeping.

- [ ] **Step 1: Write the failing test**

Append to `RenderedAudioWriterTests`:

```csharp
        [Test]
        public void BuildManifest_ListsGainAndFlagsLossySourcesExplicitly()
        {
            var report = new RenderReport
            {
                OutputRoot = "/out",
                Files =
                {
                    new RenderedFile("Assets/A/music.ogg", "/out/A/music.wav", -6.2f, true),
                    new RenderedFile("Assets/A/hit.wav", "/out/A/hit.wav", -1.5f, false)
                }
            };

            var manifest = RenderedAudioWriter.BuildManifest(report);

            StringAssert.Contains("Assets/A/music.ogg", manifest);
            StringAssert.Contains("-6.2", manifest);
            StringAssert.Contains("LOSSY SOURCE", manifest);

            StringAssert.Contains("Assets/A/hit.wav", manifest);
            StringAssert.Contains("-1.5", manifest);
        }

        [Test]
        public void BuildManifest_RecordsSkippedClipsWithTheirReason()
        {
            var report = new RenderReport
            {
                OutputRoot = "/out",
                Files = { new RenderedFile("Assets/A/quiet.wav", null, 0f, false, "silent") }
            };

            var manifest = RenderedAudioWriter.BuildManifest(report);

            StringAssert.Contains("SKIPPED", manifest);
            StringAssert.Contains("silent", manifest);
            StringAssert.Contains("Assets/A/quiet.wav", manifest);
        }

        [Test]
        public void Render_WritesTheManifestBesideTheRenderedFiles()
        {
            RenderedAudioWriter.Render(
                new List<AudioBalanceRow> { AssetRow("kick", -6f) }, _root);

            var manifestPath = Path.Combine(_root, RenderedAudioWriter.ManifestFileName);

            Assert.IsTrue(File.Exists(manifestPath), "manifest.txt should sit at the export root");
            StringAssert.Contains("kick", File.ReadAllText(manifestPath));
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Filter `RenderedAudioWriterTests`.
Expected: FAIL to compile — `BuildManifest` and `ManifestFileName` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `RenderedAudioWriter` (inside the class, after the existing reason constants):

```csharp
        public const string ManifestFileName = "manifest.txt";

        /// <summary>
        /// Plain text, deliberately: a person reads this to decide what to convert, and the
        /// lossy-source flag is the part that must not be missable. Machine-parseable formats
        /// were rejected as unnecessary for a hand-off of a few dozen lines.
        /// </summary>
        public static string BuildManifest(RenderReport report)
        {
            var builder = new System.Text.StringBuilder();

            builder.AppendLine("# Audio Balance render manifest");
            builder.AppendLine("# Gains are ATTENUATION and are already applied to these files.");
            builder.AppendLine("# LOSSY SOURCE = the original was already compressed, so encoding");
            builder.AppendLine("#   these to OGG costs a second generation. Prefer WAV masters.");
            builder.AppendLine();

            if (report == null)
            {
                return builder.ToString();
            }

            builder.AppendLine("Output root: " + report.OutputRoot);
            builder.AppendLine("Rendered: " + report.WrittenCount + "   Skipped: " + report.SkippedCount);
            builder.AppendLine();

            foreach (var file in report.Files)
            {
                if (file.Skipped)
                {
                    builder.AppendLine("SKIPPED  " + file.SourcePath + "  -- " + file.SkipReason);
                    continue;
                }

                var line = file.GainDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    + " dB  " + file.SourcePath;

                if (file.IsLossySource)
                {
                    line += "  [LOSSY SOURCE]";
                }

                builder.AppendLine(line);
            }

            return builder.ToString();
        }
```

Then, at the end of `Render`, immediately before `return report;`:

```csharp
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(Path.Combine(outputRoot, ManifestFileName), BuildManifest(report));

            return report;
```

- [ ] **Step 4: Run tests to verify they pass**

Filter `RenderedAudioWriterTests`. Expected: PASS (10 tests).

- [ ] **Step 5: Run the full EditMode suite**

Expected: 708/708 PASS, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.audiobalance/Editor/Render/RenderedAudioWriter.cs \
        Packages/com.hoppa.audiobalance/Tests/Editor/RenderedAudioWriterTests.cs
git commit -m "feat(audio): render manifest flagging lossy sources"
```

---

## Task 5: `Render Files...` toolbar button + gitignore

**Files:**
- Modify: `Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs` (`DrawToolbar`, around lines 511-520; tooltip constants around line 130)
- Modify: `.gitignore` (repo root)

**Interfaces:**
- Consumes: `RenderedAudioWriter.Render` (Task 3), `RenderPaths.StagingRoot` / `TimestampedRoot` (Task 2).
- Produces: nothing consumed by later tasks.

**Background — layout, and why the button goes on row 1.** The window carries an explicit warning at `AudioBalanceWindow.cs:533`: *"Re-derive both rows before adding any further toolbar control."* This package has already shipped layout defects that pushed features off-screen at the default window size. The derivation:

- `minSize` is **720 wide** (`AudioBalanceWindow.cs:301`); with `Pad` either side, **714 px** is usable.
- **Row 1** (`DrawToolbar`) currently ends at **x = 560** (per the comment at line 515). Remaining: **154 px**.
- **Row 2** (`DrawTableBar`) ends at **x ≈ 672**. Remaining: **42 px** — a 110 px button plus its 6 px gap needs 116 and would end at **788**, past the window edge.

So the button goes on **row 1** at `x = 566`, width **110**, ending at **676** — inside 714 with 38 px to spare. This is also the semantically correct row: rendering needs solved rows, **not** a gain table, so it does not belong beside `Write Table`.

Enable condition is `_session.Rows.Count > 0` — deliberately **not** gated on `_profile.Table`, since rendering never touches the table.

- [ ] **Step 1: Add the tooltip constant**

In `AudioBalanceWindow.cs`, beside the existing `TipWriteTable` constant (around line 132):

```csharp
        private static readonly GUIContent TipRenderFiles = new GUIContent(string.Empty,
            "Render every analyzed clip to a 24-bit WAV with its gain already applied, into a " +
            "timestamped folder under StagingExports/AudioBalance. Your source files are never " +
            "modified. Hand the folder off for OGG conversion.");
```

- [ ] **Step 2: Add the button to row 1**

In `DrawToolbar`, replace the closing of the method (the `? Guide` button block ending at line 520) so the Guide button is followed by:

```csharp
            x += 68f;

            // Row 1 now ends at x = 676 against the 714 available at minSize -- see DrawTableBar's
            // remarks before adding anything further. Deliberately NOT on the table row: rendering
            // needs solved rows, not a gain table, and row 2 has only 42 px left in any case.
            GUI.enabled = _session.Rows.Count > 0;

            if (GUI.Button(new Rect(x, rect.y, 110f, rect.height - 4f), "Render Files..."))
            {
                RenderFiles();
            }

            Tip(new Rect(x, rect.y, 110f, rect.height - 4f), TipRenderFiles);
            GUI.enabled = true;
```

Note the Guide button's own comment at line 515 says *"Row 1 ends at x = 560"*; update it to read *"the Render Files button follows at x = 566"* so the next person re-derives from a true statement.

- [ ] **Step 3: Add the handler**

Add a private method to `AudioBalanceWindow`, next to the other action handlers:

```csharp
        /// <summary>
        /// Renders every solved row to a timestamped hand-off folder. Output lands OUTSIDE the
        /// asset pipeline, which is what makes a re-render unable to stack attenuation on an
        /// already-rendered file -- see <see cref="RenderPaths.StagingRoot"/>.
        /// </summary>
        private void RenderFiles()
        {
            var root = RenderPaths.TimestampedRoot(RenderPaths.StagingRoot(), System.DateTime.Now);

            if (System.IO.Directory.Exists(root))
            {
                // Same minute, second render. Merging into a populated folder would leave a
                // half-overwritten export, which is worse than no export.
                EditorUtility.DisplayDialog("Render Files",
                    "An export already exists at:\n\n" + root +
                    "\n\nWait a minute and render again.", "OK");
                return;
            }

            var report = RenderedAudioWriter.Render(_session.Rows, root);

            EditorUtility.RevealInFinder(root);

            ShowNotification(new GUIContent(
                "Rendered " + report.WrittenCount + ", skipped " + report.SkippedCount));
        }
```

- [ ] **Step 4: Verify the window compiles and the suite is green**

Run the full EditMode suite. Expected: 708/708 PASS, 0 failures.

- [ ] **Step 5: Add the gitignore entry**

Append to `.gitignore` at repo root:

```gitignore
# Audio Balance render hand-offs: multi-MB, timestamped per run, and reproducible
# from the tracked source assets. NOTE: Bus Buddies is a SEPARATE repo, so ignored
# exports do not travel between projects -- if rendered files seem to go missing
# while testing against BB, check this rule first.
StagingExports/AudioBalance/
```

- [ ] **Step 6: Verify the ignore rule works**

```bash
mkdir -p StagingExports/AudioBalance/probe && touch StagingExports/AudioBalance/probe/x.wav
git status --porcelain StagingExports/
```
Expected: **no output** (the path is ignored). Then remove the probe:
```bash
rm -rf StagingExports/AudioBalance/probe
```

- [ ] **Step 7: Commit**

```bash
git add Packages/com.hoppa.audiobalance/Editor/Window/AudioBalanceWindow.cs .gitignore
git commit -m "feat(audio): Render Files button + ignore render hand-offs"
```

---

## Task 6: Guide documentation

**Files:**
- Modify: `Packages/com.hoppa.audiobalance/Documentation~/audio-balance-guide.md`
- Test: `Packages/com.hoppa.audiobalance/Tests/Editor/AudioBalanceGuideTests.cs`

**Interfaces:**
- Consumes: `AudioBalanceGuide.LoadGuideMarkdown()` → `string` (existing).
- Produces: nothing.

**Background:** The package ships an in-editor guide (`? Guide` button → `AudioBalanceGuide.Open()`), and `AudioBalanceGuideTests` already pins guide content. A shipped feature with no guide section is how the anchor's documentation drifted out of sync with its behaviour in the predecessor spec.

- [ ] **Step 1: Write the failing test**

Append to `AudioBalanceGuideTests`, matching the assertion style already in that file
(it reads content via `AudioBalanceGuide.LoadGuideMarkdown()` and null-guards first):

```csharp
        [Test]
        public void Guide_ExplainsRenderingFilesAndThatSourcesAreNeverModified()
        {
            string md = AudioBalanceGuide.LoadGuideMarkdown();

            Assert.IsNotNull(md, "guide markdown not found at " + AudioBalanceGuide.PackageGuidePath);

            StringAssert.Contains("Render Files", md);
            StringAssert.Contains("StagingExports/AudioBalance", md);
            StringAssert.Contains("24-bit", md);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Filter `AudioBalanceGuideTests`. Expected: FAIL — the guide has no Render Files section.

- [ ] **Step 3: Add the guide section**

Add to `Packages/com.hoppa.audiobalance/Documentation~/audio-balance-guide.md`, after the
Write Table section:

```markdown
## Render Files

`Write Table` bakes gains into an asset the game reads at runtime. `Render Files...`
instead produces **standalone audio files with the gain already applied**, for hand-off
to whoever does the final encoding.

Clicking it writes a timestamped folder under `StagingExports/AudioBalance/`, mirroring
your source folder structure, plus a `manifest.txt` listing the gain applied to each clip.

Three things worth knowing:

- **Your source files are never modified.** Rendering only reads them.
- **Output is 24-bit WAV.** Every gain here is attenuation, and 24-bit keeps the quiet
  clips from losing detail before whatever encoding happens downstream.
- **Clips whose source was already compressed** (`.mp3`, `.ogg`) are marked
  `[LOSSY SOURCE]` in the manifest. They still render, but encoding them again costs a
  second generation of quality. WAV masters are better where you can get them.

Skipped clips — silent, unreadable, or never solved — are listed in the manifest with
the reason, and never silently omitted.
```

- [ ] **Step 4: Run tests to verify they pass**

Filter `AudioBalanceGuideTests`. Expected: PASS.

- [ ] **Step 5: Run the full EditMode suite**

Expected: 709/709 PASS, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.audiobalance/Documentation~/audio-balance-guide.md \
        Packages/com.hoppa.audiobalance/Tests/Editor/AudioBalanceGuideTests.cs
git commit -m "docs(audio): guide section for Render Files"
```

---

## Handover checklist (human steps the agent cannot do)

Nothing below is verifiable from an agent session; the coordinator must hand these to the lead.

1. **Open the window and click `Render Files...`.** Confirm the button is visible at the default window size (720 wide) and not clipped — the layout derivation in Task 5 is arithmetic, not observation.
2. **Confirm the export folder opens** and contains mirrored subfolders plus `manifest.txt`.
3. **Listen to a rendered file against its source.** It should be the same audio, quieter. This is the only check that catches a channel/sample-rate mix-up, which no unit test in this plan would notice.
4. **Hand a folder to Eliran** and confirm his OGG conversion accepts 24-bit WAV input.
