namespace Hoppa.AudioBalance.Editor
{
    /// <summary>What one category edit needs the caller to do about it.</summary>
    public readonly struct CategoryEditResult
    {
        /// <summary>The rename was refused -- almost always a collision with another category.
        /// The caller should say so; silently reverting the field looks like dropped input.</summary>
        public readonly bool RenameRejected;

        /// <summary>The edit changed a MEASUREMENT input, so the commit must run
        /// <see cref="AudioBalanceSession.Analyze"/> and not
        /// <see cref="AudioBalanceSession.Resolve"/>.</summary>
        public readonly bool NeedsAnalyze;

        public CategoryEditResult(bool renameRejected, bool needsAnalyze)
        {
            RenameRejected = renameRejected;
            NeedsAnalyze = needsAnalyze;
        }
    }

    /// <summary>
    /// Applies one frame's category edit and reports what it implies.
    ///
    /// <para>
    /// This is pure sequencing, extracted out of <c>OnGUI</c> because that is the only way it
    /// can be tested. The order is load-bearing: the rename is applied <b>before</b> the
    /// re-measure decision is made, so a REJECTED rename contributes nothing to that decision
    /// rather than cancelling it. The earlier in-window version folded a plain <c>false</c> on
    /// the rejection path, wiping a mode change that arrived in the same gesture -- the commit
    /// then fell through to <c>Resolve</c>, which cannot re-measure, and baked a gain computed
    /// from the OLD mode's LUFS.
    /// </para>
    ///
    /// <para>
    /// Offset and mode are applied regardless of whether the rename succeeded: a name collision
    /// is no reason to discard the other values the user changed in the same edit.
    /// </para>
    /// </summary>
    public static class CategoryEdit
    {
        public static CategoryEditResult Apply(AudioBalanceProfile profile, AudioCategory category,
            string newName, float newOffsetDb, MeasureMode newMode)
        {
            if (profile == null || category == null)
            {
                return new CategoryEditResult(false, false);
            }

            var renameRequested = newName != category.Name;

            // Applied FIRST -- see the class doc. RenameCategory sets the name AND re-points
            // every clip referencing it; doing only the former would orphan the whole group.
            var renameApplied = renameRequested && profile.RenameCategory(category, newName);

            // A mode change changes the MEASUREMENT. An APPLIED rename re-points every clip that
            // referenced the old name, which can change their effective mode too. Evaluated
            // before category.Mode is overwritten, below. A rejected rename contributes false
            // here, which -- because the caller only ever ORs this in -- cannot cancel anything.
            var needsAnalyze = newMode != category.Mode || renameApplied;

            category.OffsetDb = newOffsetDb;
            category.Mode = newMode;

            return new CategoryEditResult(renameRequested && !renameApplied, needsAnalyze);
        }
    }
}
