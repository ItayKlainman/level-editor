using UnityEngine;

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
    /// The naive shape -- <c>Undo.RecordObject</c> inside
    /// <c>EditorGUI.EndChangeCheck</c> -- looks correct but fires on every <c>OnGUI</c> frame
    /// in which the value differs. Dragging a <c>FloatField</c>'s label therefore produces
    /// dozens of undo entries for what the user experienced as one edit, and re-runs
    /// <see cref="GainSolver"/> over every row for each of them. This type is the state
    /// machine that fixes both: undo is recorded on the first changed frame, and the commit
    /// (SetDirty plus re-solve) is deferred to <c>MouseUp</c>/<c>DragExited</c>.
    /// </para>
    ///
    /// <para>
    /// It is a plain class rather than something wired into IMGUI precisely so it can be
    /// unit-tested without opening an <c>EditorWindow</c> -- the window keeps one instance
    /// per independently-draggable control group and feeds it
    /// <c>Event.current.type</c> plus the result of <c>EndChangeCheck</c>.
    /// </para>
    /// </summary>
    public sealed class EditGesture
    {
        private bool _pending;

        /// <summary>
        /// Advances the state machine by one <c>OnGUI</c> frame. <paramref name="changed"/> is
        /// what <c>EditorGUI.EndChangeCheck</c> returned; <paramref name="eventType"/> is
        /// <c>Event.current.type</c>.
        ///
        /// <para>
        /// A change arriving on a <c>MouseDrag</c> event opens a gesture: the value is applied
        /// by the caller immediately (so the field stays live under the cursor) but the commit
        /// waits. A change arriving on any other event -- an <c>EnumPopup</c> selection, a
        /// <c>DelayedTextField</c> committing on Enter -- is complete on arrival and needs no
        /// gesture at all.
        /// </para>
        /// </summary>
        public EditStep Advance(EventType eventType, bool changed)
        {
            if (changed)
            {
                var isFirst = !_pending;

                if (eventType == EventType.MouseDrag)
                {
                    _pending = true;
                    return isFirst ? EditStep.Record : EditStep.None;
                }

                _pending = false;
                return isFirst ? EditStep.RecordAndCommit : EditStep.Commit;
            }

            // Only close a gesture that is actually open; otherwise every stray MouseUp in
            // the window would commit a phantom edit and mark the profile dirty.
            if (_pending && (eventType == EventType.MouseUp || eventType == EventType.DragExited))
            {
                _pending = false;
                return EditStep.Commit;
            }

            return EditStep.None;
        }
    }
}
