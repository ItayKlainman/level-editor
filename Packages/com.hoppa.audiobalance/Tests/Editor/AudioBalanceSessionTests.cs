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
