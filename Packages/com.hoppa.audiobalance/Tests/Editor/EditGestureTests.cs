using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// The undo/commit state machine the window drives from OnGUI, tested here as the pure
    /// logic it is. This exists because the naive shape -- Undo.RecordObject inside
    /// EndChangeCheck -- fires once per OnGUI frame during a FloatField label-drag, producing
    /// dozens of undo entries for one gesture and re-running GainSolver over every row for
    /// each of them.
    /// </summary>
    public class EditGestureTests
    {
        [Test]
        public void ADiscreteEdit_RecordsUndoAndCommitsInOneStep()
        {
            // An EnumPopup or DelayedTextField change arrives complete: there is no gesture
            // to wait out, so it must record and commit on the same frame.
            var gesture = new EditGesture();

            Assert.AreEqual(EditStep.RecordAndCommit, gesture.Advance(EventType.Used, true));
        }

        [Test]
        public void ADrag_RecordsUndoExactlyOnceAndCommitsOnMouseUp()
        {
            var gesture = new EditGesture();

            Assert.AreEqual(EditStep.Record, gesture.Advance(EventType.MouseDrag, true),
                "The first changed frame of a drag records the undo entry.");
            Assert.AreEqual(EditStep.None, gesture.Advance(EventType.MouseDrag, true),
                "Subsequent drag frames must not record another undo entry.");
            Assert.AreEqual(EditStep.None, gesture.Advance(EventType.MouseDrag, true));
            Assert.AreEqual(EditStep.Commit, gesture.Advance(EventType.MouseUp, false),
                "The gesture commits once, when the mouse is released.");
        }

        [Test]
        public void ADrag_AlsoCommitsOnDragExited()
        {
            // DragExited is what arrives when the drag leaves the window; without it the
            // pending gesture would never commit and the edit would never be persisted.
            var gesture = new EditGesture();
            gesture.Advance(EventType.MouseDrag, true);

            Assert.AreEqual(EditStep.Commit, gesture.Advance(EventType.DragExited, false));
        }

        [Test]
        public void AnIdleFrameWithNoPendingGesture_DoesNothing()
        {
            var gesture = new EditGesture();

            Assert.AreEqual(EditStep.None, gesture.Advance(EventType.Repaint, false));
            Assert.AreEqual(EditStep.None, gesture.Advance(EventType.MouseUp, false),
                "A MouseUp with no gesture open must not commit a phantom edit.");
        }

        [Test]
        public void AfterCommitting_TheNextDragStartsAFreshUndoEntry()
        {
            // Guards against the gesture latching open: a second drag must be separately
            // undoable, not folded into the first one's entry.
            var gesture = new EditGesture();
            gesture.Advance(EventType.MouseDrag, true);
            gesture.Advance(EventType.MouseUp, false);

            Assert.AreEqual(EditStep.Record, gesture.Advance(EventType.MouseDrag, true));
        }
    }
}
