using System.Linq;
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

        /// <summary>
        /// A short loud attack followed by a long quiet tail -- the one shape that reads
        /// differently under Integrated (gated, dragged down by the tail) and MomentaryMax
        /// (tracks the attack). Same shape the Task 4 MomentaryMax tests use, so the expected
        /// split is already proven.
        /// </summary>
        private static AudioClip Burst(string name)
        {
            var all = SignalFactory.Concat(
                SignalFactory.Sine(-12.0, 0.5, 2, Rate),
                SignalFactory.Sine(-50.0, 3.5, 2, Rate));

            var clip = AudioClip.Create(name, all.Length / 2, 2, Rate, false);
            clip.SetData(all, 0);
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
        public void Analyze_AfterTheAnchorIsCleared_ResetsTheAnchorStatusInsteadOfKeepingAStaleOk()
        {
            // The real bug class is STALENESS, not the initial value: AnchorStatus's field
            // initialiser is already Unanalyzable, so a session that never assigns it passes
            // the test above. This one analyzes with a valid anchor FIRST, so a stale Ok is
            // what an implementation that fails to reset would carry forward. It matters
            // because AnchorStatus gates outlier suppression -- a stale Ok flags rows against
            // a reference that no longer exists.
            var anchor = Tone("anchor", -20.0);
            var broken = Tone("broken", -60.0);
            var profile = Profile(anchor, broken);
            var session = new AudioBalanceSession();

            session.Analyze(profile, null);
            Assert.AreEqual(ClipStatus.Ok, session.AnchorStatus, "Precondition: the anchor measured fine.");
            Assert.IsTrue(session.Rows.First(r => r.Clip.name == "broken").Gain.IsOutlier,
                "Precondition: with an anchor, the -60 clip is a genuine outlier.");

            profile.Anchor = null;
            session.Analyze(profile, null);

            Assert.AreEqual(ClipStatus.Unanalyzable, session.AnchorStatus,
                "Clearing the anchor must reset the status, not leave the previous Ok in place.");
            Assert.IsFalse(session.Rows.First(r => r.Clip.name == "broken").Gain.IsOutlier,
                "A stale Ok would keep suppression off and flag rows against a gone reference.");
        }

        [Test]
        public void Analyze_WithANullProfile_ClearsRowsWithoutThrowing()
        {
            // Analyze a real profile FIRST: on a fresh session Rows is already empty, so an
            // implementation that returns early WITHOUT clearing passes a bare null call.
            var session = new AudioBalanceSession();
            session.Analyze(Profile(Tone("anchor", -23.0), Tone("other", -20.0)), null);
            Assert.AreEqual(2, session.Rows.Count, "Precondition: there are rows to clear.");

            Assert.DoesNotThrow(() => session.Analyze(null, null));
            Assert.AreEqual(0, session.Rows.Count);
            Assert.AreEqual(ClipStatus.Unanalyzable, session.AnchorStatus);
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

            // NUnit's Assert.AreNotEqual has no tolerance overload -- the plan wrote
            // AreNotEqual(integrated, momentary, 0.5f), where 0.5f binds to the `message`
            // parameter and does not compile. The "must differ by more than 0.5 dB" intent
            // is expressed directly instead.
            Assert.Greater(momentary, integrated,
                "MomentaryMax tracks the loud burst; Integrated is dragged down by the tail.");
            Assert.Greater(momentary - integrated, 0.5f,
                "Switching to a category with a different MeasureMode must re-measure. " +
                "If these are equal, the category edit path is calling Resolve, not Analyze.");
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
            //
            // "far" is here deliberately: at -60 it sits 37 dB from the -23 sentinel, so it
            // would still be flagged under the sentinel alone. Without it this test passes
            // against an implementation that only swapped the fallback constant and never
            // suppressed anything -- it would pin the sentinel, not the suppression.
            var a = Tone("a", -20.0);
            var b = Tone("b", -22.0);
            var far = Tone("far", -60.0);
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            profile.SettingsFor(a).Category = "Music";
            profile.SettingsFor(b).Category = "Music";
            profile.SettingsFor(far).Category = "Music";

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
        public void Analyze_ThroughARealCache_ServesTheSecondPassFromTheCache()
        {
            // Every other session test passes cache: null, so deviation #10's load-bearing
            // claim -- "re-analysis is near-free because Mode is a field on LoudnessCacheKey"
            // -- was never exercised through Analyze at all. This needs asset-backed clips:
            // AudioClip.Create has no asset path, so KeyFor returns an invalid key and the
            // cache is structurally bypassed.
            using (var fixture = new AudioAssetFixture())
            {
                var anchor = fixture.CreateTone("anchor", -23.0, 2.0);
                var other = fixture.CreateTone("other", -18.0, 2.0);

                var cachePath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "hoppa-session-cache-" + System.Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    var cache = LoudnessCache.Load(cachePath);
                    var profile = Profile(anchor, other);

                    var session = new AudioBalanceSession();
                    session.Analyze(profile, cache);

                    var measured = session.Rows.First(r => r.Clip == other).Analysis.Lufs;
                    Assert.AreEqual(ClipStatus.Ok, session.Rows.First(r => r.Clip == other).Analysis.Status);
                    Assert.IsTrue(cache.IsDirty, "The first pass must have stored measurements.");

                    // Poison the cache entry under the SAME key the second pass will look up.
                    // If the second pass returns this impossible value, the hit was genuine;
                    // if it returns the real measurement, Analyze silently bypassed the cache
                    // and the "near-free" claim is false.
                    var key = LoudnessCache.KeyFor(other, MeasureMode.Integrated);
                    Assert.IsTrue(key.IsValid, "Precondition: an asset-backed clip has a valid cache key.");
                    cache.Put(key, new CachedLoudness { Status = (int)ClipStatus.Ok, Lufs = -99f, PeakDb = -1f });

                    session.Analyze(profile, cache);

                    Assert.AreEqual(-99f, session.Rows.First(r => r.Clip == other).Analysis.Lufs, 1e-3f,
                        "The second pass must be served from the cache, not re-measured.");
                    Assert.AreNotEqual(-99f, measured,
                        "Sanity: the poisoned value is not what the clip actually measures.");
                }
                finally
                {
                    if (System.IO.File.Exists(cachePath))
                    {
                        System.IO.File.Delete(cachePath);
                    }
                }
            }
        }

        [Test]
        public void Analyze_AfterACategoryModeChange_MissesTheCacheForThatClipOnly()
        {
            // The other half of the same claim: the mode is part of the key, so a clip moved
            // to a differently-measured category must NOT be served the old-mode value.
            using (var fixture = new AudioAssetFixture())
            {
                var anchor = fixture.CreateTone("anchor", -23.0, 2.0);
                var burst = fixture.CreateClip("burst", SignalFactory.Concat(
                        SignalFactory.Sine(-12.0, 0.5, 2, Rate),
                        SignalFactory.Sine(-50.0, 2.0, 2, Rate)),
                    2, Rate);

                var cachePath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "hoppa-session-mode-" + System.Guid.NewGuid().ToString("N") + ".json");

                try
                {
                    var cache = LoudnessCache.Load(cachePath);
                    var profile = Profile(anchor);
                    profile.SettingsFor(burst).Category = "Music";

                    var session = new AudioBalanceSession();
                    session.Analyze(profile, cache);
                    var integrated = session.Rows.First(r => r.Clip == burst).Analysis.Lufs;

                    profile.SettingsFor(burst).Category = "SFX"; // MomentaryMax
                    session.Analyze(profile, cache);
                    var momentary = session.Rows.First(r => r.Clip == burst).Analysis.Lufs;

                    Assert.Greater(momentary - integrated, 0.5f,
                        "A cache keyed without Mode would serve the Integrated value straight back.");

                    // Both modes are now cached independently, so switching back is a hit.
                    profile.SettingsFor(burst).Category = "Music";
                    session.Analyze(profile, cache);

                    Assert.AreEqual(integrated, session.Rows.First(r => r.Clip == burst).Analysis.Lufs, 1e-3f,
                        "Switching back must hit the Integrated entry, not re-measure to a different value.");
                }
                finally
                {
                    if (System.IO.File.Exists(cachePath))
                    {
                        System.IO.File.Delete(cachePath);
                    }
                }
            }
        }

        [Test]
        public void RenamingACategory_MovesItsClipsWithIt()
        {
            // ClipSettings.Category is a name string and FindCategory falls back to
            // Categories[0] on a miss, so renaming a category without re-pointing its clips
            // silently moves the whole group to Music/Integrated -- shifting every baked gain,
            // with an undo entry that looks like a harmless rename.
            var anchor = Tone("anchor", -23.0);
            var oneShot = Tone("oneShot", -20.0);
            var profile = Profile(anchor);
            profile.SettingsFor(oneShot).Category = "SFX";

            Assert.AreEqual(MeasureMode.MomentaryMax,
                profile.FindCategory(profile.FindSettings(oneShot).Category).Mode,
                "Precondition: SFX is MomentaryMax.");

            profile.RenameCategory("SFX", "SFX (UI)");

            Assert.AreEqual("SFX (UI)", profile.FindSettings(oneShot).Category,
                "The clip must follow its category through the rename.");
            Assert.AreEqual(MeasureMode.MomentaryMax,
                profile.FindCategory(profile.FindSettings(oneShot).Category).Mode,
                "Without re-pointing, this falls through to Categories[0] (Music/Integrated).");
        }

        [Test]
        public void RenamingACategory_ToAnEmptyName_IsIgnored()
        {
            // A half-typed field must not orphan a whole category's clips.
            var anchor = Tone("anchor", -23.0);
            var oneShot = Tone("oneShot", -20.0);
            var profile = Profile(anchor);
            profile.SettingsFor(oneShot).Category = "SFX";

            profile.RenameCategory("SFX", string.Empty);

            Assert.AreEqual("SFX", profile.FindSettings(oneShot).Category);
            Assert.IsNotNull(profile.Categories.Find(c => c.Name == "SFX"));
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

        [Test]
        public void Analyze_WhenCancelledPartWayThrough_KeepsAndSolvesTheRowsAlreadyMeasured()
        {
            // Cancelling at index 0 cannot test this: Rows is empty either way, so an
            // implementation that blanks the table on cancel, or that skips Resolve and leaves
            // every Gain at default, passes. Both the XML doc and the commit message claim
            // "rows measured so far are kept and still solved" -- this is the assertion that
            // actually holds them to it.
            var anchor = Tone("anchor", -23.0);
            var profile = Profile(anchor, Tone("b", -12.0), Tone("c", -20.0));
            var session = new AudioBalanceSession();

            var completed = session.Analyze(profile, null,
                (clip, index, total) => index == 2); // let 0 and 1 through, cancel at 2

            Assert.IsFalse(completed);
            Assert.AreEqual(2, session.Rows.Count,
                "The two clips measured before the cancel must survive it.");

            foreach (var row in session.Rows)
            {
                Assert.AreEqual(ClipStatus.Ok, row.Analysis.Status);

                // Gain.Clip, NOT Gain.Status: ClipStatus.Ok is 0, so an unsolved default
                // GainResult reports Status == Ok and that assertion would be vacuous.
                // Clip is null until the solver populates it.
                Assert.IsNotNull(row.Gain.Clip,
                    $"'{row.Clip.name}' was measured but never solved -- Resolve was skipped on the cancel path.");
            }

            // The solver ran over exactly the surviving rows: anchor (-23) and b (-12) are
            // 11 dB apart, so the quieter pins at 0 and the louder sits ~11 dB down.
            Assert.AreEqual(0f, session.Rows.First(r => r.Clip.name == "anchor").Gain.FinalGainDb, 0.3f);
            Assert.AreEqual(-11f, session.Rows.First(r => r.Clip.name == "b").Gain.FinalGainDb, 0.5f);
        }

        [Test]
        public void Analyze_DoesNotEnrolTheAnchorOrAnyClipIntoTheProfile()
        {
            // Measuring is a READ. SettingsFor appends on a miss, so routing the measure path
            // through it wrote to the profile asset outside any Undo scope, from a method the
            // tests call directly. Enrolment is now the window's job, done explicitly inside
            // Undo in RunAnalysis.
            var anchor = Tone("anchor", -23.0);
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            profile.Anchor = anchor; // deliberately NOT enrolled

            var session = new AudioBalanceSession();
            session.Analyze(profile, null);

            Assert.AreEqual(ClipStatus.Ok, session.AnchorStatus,
                "The anchor is still measured, it is just not enrolled as a side effect.");
            CollectionAssert.IsEmpty(profile.Clips,
                "Analyze must not append a ClipSettings for the anchor -- that is a mutation.");
        }

        [Test]
        public void Resolve_DoesNotEnrolAnUnknownClipIntoTheProfile()
        {
            // The same defect via the other door: GainSolver takes offset/trim lookups, and
            // passing profile.OffsetDbFor/TrimDbFor routed solving through SettingsFor too.
            var anchor = Tone("anchor", -23.0);
            var profile = Profile(anchor);
            var session = new AudioBalanceSession();
            session.Analyze(profile, null);

            var before = profile.Clips.Count;
            profile.Clips.Clear(); // every row's clip is now unknown to the profile
            session.Resolve(profile);

            Assert.AreEqual(1, before, "Precondition: the anchor was enrolled by the Profile helper.");
            CollectionAssert.IsEmpty(profile.Clips, "Resolve must not re-create settings while solving.");
        }
    }
}
