namespace Hoppa.AudioBalance.Editor
{
    /// <summary>What the caller should do with an edit this frame.</summary>
    public enum EditStep
    {
        /// <summary>Nothing to do. Either no change, or a mid-drag frame already covered by
        /// the undo entry recorded when the gesture started.</summary>
        None = 0,

        /// <summary>Record an undo entry. The gesture is still in progress, so do not commit
        /// yet -- committing per frame is the expensive half of the problem.</summary>
        Record = 1,

        /// <summary>Commit: mark the asset dirty and re-solve. The undo entry already exists
        /// from the frame the gesture started.</summary>
        Commit = 2,

        /// <summary>A complete edit in a single frame: record undo and commit together.</summary>
        RecordAndCommit = 3
    }

    /// <summary>
    /// Collapses one continuous edit gesture into exactly one undo entry and exactly one
    /// commit.
    ///
    /// <para>
    /// The naive shape -- <c>Undo.RecordObject</c> inside <c>EditorGUI.EndChangeCheck</c> --
    /// looks correct but fires on every <c>OnGUI</c> frame in which the value differs.
    /// Dragging a slider or a number field's label therefore produces dozens of undo entries
    /// for what the user experienced as one edit, and re-runs <see cref="GainSolver"/> over
    /// every row for each of them.
    /// </para>
    ///
    /// <para>
    /// <b>The gesture signal is <c>GUIUtility.hotControl != 0</c>, not the event type.</b> This
    /// is load-bearing and was measured, not assumed. By the time a container has drawn its
    /// controls, the control that owns the drag has already called <c>Event.Use()</c>, and
    /// <c>Use()</c> rewrites <b>both</b> <c>Event.current.type</c> AND
    /// <c>Event.current.rawType</c> to <c>EventType.Used</c> -- verified by driving real
    /// <c>MouseDown</c>/<c>MouseDrag</c>/<c>MouseUp</c> events through a live IMGUI window and
    /// logging both properties after the control drew. <c>rawType</c> is commonly believed to
    /// survive consumption; in this position it does not, so an event-type-based gesture
    /// machine is dead code in production and silently degenerates to record-and-commit on
    /// every frame. <c>hotControl</c> is set for the whole gesture and cleared on mouse-up, and
    /// consumption does not touch it.
    /// </para>
    ///
    /// <para>
    /// It is a plain class rather than something wired into IMGUI precisely so it can be
    /// unit-tested without opening an <c>EditorWindow</c> -- the window feeds it the result of
    /// <c>EndChangeCheck</c> plus <c>GUIUtility.hotControl != 0</c>, both read immediately
    /// after the controls draw. <c>EditGestureSeamTests</c> additionally drives the real IMGUI
    /// seam end to end, because a unit test over this class alone cannot prove the window hands
    /// it the right signal -- which is exactly how the event-type version passed its tests
    /// while being unreachable from the window.
    /// </para>
    /// </summary>
    public sealed class EditGesture
    {
        private bool _pending;

        /// <summary>
        /// Advances the state machine by one <c>OnGUI</c> frame.
        ///
        /// <para>
        /// <paramref name="changed"/> is what <c>EditorGUI.EndChangeCheck</c> returned.
        /// <paramref name="dragActive"/> is <c>GUIUtility.hotControl != 0</c>, read after the
        /// controls have drawn -- see the class doc for why the event type cannot be used here.
        /// </para>
        ///
        /// <para>
        /// A change arriving while a control holds <c>hotControl</c> opens a gesture: the value
        /// is applied by the caller immediately (so the field stays live under the cursor) but
        /// the commit waits for the release. A change arriving with no hot control -- an
        /// <c>EnumPopup</c> selection, a <c>DelayedTextField</c> committing on Enter -- is
        /// complete on arrival and needs no gesture at all.
        /// </para>
        /// </summary>
        public EditStep Advance(bool changed, bool dragActive)
        {
            if (changed)
            {
                var isFirst = !_pending;

                if (dragActive)
                {
                    _pending = true;
                    return isFirst ? EditStep.Record : EditStep.None;
                }

                _pending = false;
                return isFirst ? EditStep.RecordAndCommit : EditStep.Commit;
            }

            // Only close a gesture that is actually open, and only once the control has
            // released hotControl; otherwise an idle frame would commit a phantom edit and
            // mark the profile dirty.
            if (_pending && !dragActive)
            {
                _pending = false;
                return EditStep.Commit;
            }

            return EditStep.None;
        }

        /// <summary>
        /// Abandons an open gesture without committing it. For profile switches only: an edit
        /// in flight belonged to the OLD profile, and leaving the gesture armed meant the next
        /// idle poll returned <see cref="EditStep.Commit"/> and marked the NEWLY selected
        /// profile dirty -- an asset the user never touched.
        /// </summary>
        public void Reset()
        {
            _pending = false;
        }
    }
}
