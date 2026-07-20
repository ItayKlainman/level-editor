using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Pins the accumulate-then-consume discipline behind the window's "this edit changed a
    /// MEASUREMENT input" flag.
    ///
    /// <para>
    /// <b>Scope, stated plainly:</b> these tests cover the accumulator, NOT the IMGUI frame
    /// interleaving that motivated it. The triggering scenario -- a
    /// <c>DelayedTextField</c> committing a rejected rename on focus-loss while ANOTHER control
    /// holds <c>hotControl</c>, one to two frames after a mode change opened the gesture --
    /// cannot be driven through <c>AudioBalanceWindow</c> from a test: the flag, the gesture and
    /// the profile are all private and the scenario needs real focus transfer between two live
    /// controls. Writing a harness that re-implements the branch would pass vacuously against
    /// the broken window, so it is not written. What IS enforced here is the property that makes
    /// the bug unreachable: nothing except <c>Consume</c>/<c>Reset</c> can clear the flag, so no
    /// later frame can wipe an earlier frame's pending re-analysis.
    /// </para>
    /// </summary>
    public class PendingAnalyzeTests
    {
        [Test]
        public void ALaterUneventfulEdit_CannotWipeAnEarlierPendingReanalysis()
        {
            // The exact shape of the bug: frame 1 changes a category's MeasureMode (needs a
            // re-MEASURE); frame 2 is a rejected rename, which changes no measurement input.
            // The old code assigned `false` on the rejection path, dropping the mode change --
            // and CommitCategoryEdit then fell through to Resolve, which CANNOT change a
            // measurement and would have baked the old-mode LUFS.
            var pending = new PendingAnalyze();

            pending.Observe(true);
            pending.Observe(false);

            Assert.IsTrue(pending.Consume(),
                "A rejected rename must not cancel a mode change from an earlier frame of the " +
                "same gesture -- Resolve cannot re-measure, so the gain would be silently wrong.");
        }

        [Test]
        public void Consume_ReportsThenClears()
        {
            var pending = new PendingAnalyze();
            pending.Observe(true);

            Assert.IsTrue(pending.Consume());
            Assert.IsFalse(pending.Consume(), "One commit must not re-trigger analysis forever.");
        }

        [Test]
        public void ObservingNothing_NeverAsksForAnalysis()
        {
            var pending = new PendingAnalyze();
            pending.Observe(false);

            Assert.IsFalse(pending.Consume(),
                "An offset-only edit is a Resolve, not a full re-measure.");
        }

        [Test]
        public void Reset_DropsAPendingRequest()
        {
            // Switching profiles: an edit in flight belonged to the OLD profile, so carrying its
            // pending re-analysis across would apply it to the new one.
            var pending = new PendingAnalyze();
            pending.Observe(true);

            pending.Reset();

            Assert.IsFalse(pending.Consume());
        }
    }
}
