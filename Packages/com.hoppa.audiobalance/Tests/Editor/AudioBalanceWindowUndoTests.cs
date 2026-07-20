using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// The window's <c>undoRedoPerformed</c> handler.
    ///
    /// <para>
    /// Four of the undo entries this window pushes restore a MEASUREMENT input, not just a
    /// target: <c>"Edit Audio Category"</c> (restores a category's <see cref="MeasureMode"/>),
    /// <c>"Change Audio Category"</c> and <c>"Assign Audio Category"</c> (restore
    /// <c>ClipSettings.Category</c>, which resolves to a different mode), and
    /// <c>"Scan Audio Folders"</c> (un-enrolment falls back to <see cref="MeasureMode.Integrated"/>).
    /// <see cref="AudioBalanceSession.Resolve"/> rebuilds its analyses from <c>row.Analysis</c>
    /// verbatim and only re-reads offset and trim, so it cannot correct any of them.
    /// </para>
    ///
    /// <para>
    /// This is not a cosmetic staleness. <c>GainSolver</c>'s headroom pass subtracts
    /// <c>max(raw)</c> across the whole set, so a handful of rows left on the wrong measurement
    /// moves the 0 dB ceiling for <b>every clip in the library</b> -- with no marker, no warning,
    /// and <c>Write Table</c> still enabled.
    /// </para>
    /// </summary>
    public class AudioBalanceWindowUndoTests
    {
        private static readonly BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static object Field(AudioBalanceWindow window, string name)
        {
            return typeof(AudioBalanceWindow).GetField(name, Instance).GetValue(window);
        }

        private static void SetField(AudioBalanceWindow window, string name, object value)
        {
            typeof(AudioBalanceWindow).GetField(name, Instance).SetValue(window, value);
        }

        /// <summary>
        /// A short one-shot: 0.5 s of tone then 3.5 s of silence. Integrated gating discards the
        /// tail, so it reads roughly 1.5 dB quieter than MomentaryMax's loudest 400 ms window --
        /// which is exactly the divergence a category change between the two default modes
        /// produces, and what makes "did it actually re-measure?" observable at all.
        /// </summary>
        private static float[] OneShot()
        {
            return SignalFactory.Concat(
                SignalFactory.Sine(-18.0, 0.5, 2, 48000),
                SignalFactory.Silence(3.5, 2, 48000));
        }

        [Test]
        public void OnUndoRedo_WhenTheUndoRestoredAMeasurementInput_ReMeasuresRatherThanOnlyReSolving()
        {
            using (var fixture = new AudioAssetFixture())
            {
                var clip = fixture.CreateClip("oneshot", OneShot(), 2, 48000);

                var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
                profile.ResetToDefaultCategories();
                profile.SettingsFor(clip).Category = "Music"; // Integrated

                var window = ScriptableObject.CreateInstance<AudioBalanceWindow>();

                try
                {
                    SetField(window, "_profile", profile);

                    // Null cache: forces a real measurement on both passes, and keeps the test
                    // from touching the shared Library/ cache file.
                    SetField(window, "_cache", null);

                    var session = (AudioBalanceSession)Field(window, "_session");
                    session.Analyze(profile, null);

                    Assert.AreEqual(1, session.Rows.Count, "Fixture precondition: one row.");
                    Assert.AreEqual(ClipStatus.Ok, session.Rows[0].Analysis.Status,
                        "Fixture precondition: the clip must be measurable.");

                    var integrated = session.Rows[0].Analysis.Lufs;

                    // Stand-in for the state an undo restores: the profile now says this clip
                    // belongs to a category with a DIFFERENT MeasureMode than the one its
                    // current measurement was taken under.
                    profile.FindSettings(clip).Category = "SFX"; // MomentaryMax

                    typeof(AudioBalanceWindow)
                        .GetMethod("OnUndoRedo", Instance)
                        .Invoke(window, null);

                    var after = session.Rows[0].Analysis.Lufs;

                    Assert.Greater(after - integrated, 0.5f,
                        "The handler must re-measure under the restored category's mode. Resolve " +
                        "rebuilds analyses from row.Analysis verbatim, so it leaves the old-mode " +
                        "LUFS in place and bakes a gain from it.");
                }
                finally
                {
                    Object.DestroyImmediate(window);
                    Object.DestroyImmediate(profile);
                }
            }
        }

        [Test]
        public void OnUndoRedo_WithNoRowsYet_DoesNotKickOffAnalysis()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();

            var window = ScriptableObject.CreateInstance<AudioBalanceWindow>();

            try
            {
                SetField(window, "_profile", profile);
                SetField(window, "_cache", null);

                Assert.DoesNotThrow(() => typeof(AudioBalanceWindow)
                    .GetMethod("OnUndoRedo", Instance)
                    .Invoke(window, null));

                var session = (AudioBalanceSession)Field(window, "_session");
                Assert.AreEqual(0, session.Rows.Count,
                    "An undo on a never-analyzed profile must not trigger a full-library decode.");
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
