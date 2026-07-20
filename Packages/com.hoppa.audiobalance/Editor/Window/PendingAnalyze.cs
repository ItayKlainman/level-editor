namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Tracks whether the category edit currently in flight changed a MEASUREMENT input -- a
    /// <see cref="MeasureMode"/> or a category name -- rather than only an offset.
    ///
    /// <para>
    /// It exists because the decision is made on the frame the value changes but acted on when
    /// the gesture ENDS, which can be several frames later (see <see cref="EditGesture"/>). The
    /// distinction is load-bearing: <see cref="AudioBalanceSession.Resolve"/> cannot change a
    /// measurement, so committing a mode change through it silently keeps the old-mode LUFS and
    /// bakes a wrong gain.
    /// </para>
    ///
    /// <para>
    /// <b>The invariant is that the flag only ever accumulates.</b> It was a plain bool, and the
    /// rejected-rename path assigned <c>false</c> to it -- clearing the WHOLE flag rather than
    /// its own contribution. A rename rejected while a gesture opened by a mode change was still
    /// open would therefore drop the mode change, and the commit would fall through to
    /// <c>Resolve</c>. Narrow (it needs a text field committing on focus-loss while another
    /// control holds <c>hotControl</c>) but real, and exactly the failure the commit path's own
    /// doc warns about. Making the type unable to express "clear one contribution" removes the
    /// shape of the bug rather than the instance of it.
    /// </para>
    /// </summary>
    public sealed class PendingAnalyze
    {
        private bool _needed;

        public bool Needed => _needed;

        /// <summary>
        /// Folds one frame's observation in. Note the <c>|=</c>: there is deliberately no way to
        /// retract an observation, so no later frame can cancel an earlier one.
        /// </summary>
        public void Observe(bool measurementChanged)
        {
            _needed |= measurementChanged;
        }

        /// <summary>Reports whether a re-measure is owed and clears the request.</summary>
        public bool Consume()
        {
            var needed = _needed;
            _needed = false;
            return needed;
        }

        /// <summary>
        /// Drops a pending request outright. For profile switches only: an edit in flight
        /// belonged to the OLD profile, so carrying it across would apply it to the new one.
        ///
        /// <para>
        /// Named for its one legitimate caller rather than <c>Reset()</c>, because this method
        /// is the single exception to the accumulate-only invariant above -- and a bare
        /// <c>Reset()</c> reads as harmless at a call site that has no business clearing a
        /// request it does not own. That is exactly the bug this type was introduced to remove.
        /// </para>
        /// </summary>
        public void ResetForProfileSwitch()
        {
            _needed = false;
        }
    }
}
