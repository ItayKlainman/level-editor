using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// The undo/commit state machine the window drives from OnGUI, tested here as the pure
    /// logic it is. This exists because the naive shape -- Undo.RecordObject inside
    /// EndChangeCheck -- fires once per OnGUI frame during a drag, producing dozens of undo
    /// entries for one gesture and re-running GainSolver over every row for each of them.
    ///
    /// <para>
    /// The inputs are (changed, dragActive), where dragActive is GUIUtility.hotControl != 0.
    /// An earlier revision keyed off the event type instead; see <c>EditGestureSeamTests</c>
    /// for why that was unreachable from the window, and why a unit test over this class alone
    /// cannot catch it.
    /// </para>
    /// </summary>
    public class EditGestureTests
    {
        [Test]
        public void ADiscreteEdit_RecordsUndoAndCommitsInOneStep()
        {
            // An EnumPopup or DelayedTextField change arrives with no control held hot: there
            // is no gesture to wait out, so it must record and commit on the same frame.
            var gesture = new EditGesture();

            Assert.AreEqual(EditStep.RecordAndCommit, gesture.Advance(true, false));
        }

        [Test]
        public void ADrag_RecordsUndoExactlyOnceAndCommitsWhenTheControlGoesCold()
        {
            var gesture = new EditGesture();

            Assert.AreEqual(EditStep.Record, gesture.Advance(true, true),
                "The first changed frame of a drag records the undo entry.");
            Assert.AreEqual(EditStep.None, gesture.Advance(true, true),
                "Subsequent drag frames must not record another undo entry.");
            Assert.AreEqual(EditStep.None, gesture.Advance(true, true));
            Assert.AreEqual(EditStep.Commit, gesture.Advance(false, false),
                "Releasing the control (hotControl back to 0) commits once.");
        }

        [Test]
        public void ADragFrameThatReportsNoChange_DoesNotOpenAGesture()
        {
            // Pressing the mouse down on a control makes it hot before it changes anything;
            // that must not count as an edit.
            var gesture = new EditGesture();

            Assert.AreEqual(EditStep.None, gesture.Advance(false, true));
            Assert.AreEqual(EditStep.None, gesture.Advance(false, false),
                "A press and release with no value change must commit nothing.");
        }

        [Test]
        public void AnIdleFrameWithNoPendingGesture_DoesNothing()
        {
            var gesture = new EditGesture();

            Assert.AreEqual(EditStep.None, gesture.Advance(false, false));
            Assert.AreEqual(EditStep.None, gesture.Advance(false, false),
                "An idle frame must not commit a phantom edit.");
        }

        [Test]
        public void AfterCommitting_TheNextDragStartsAFreshUndoEntry()
        {
            // Guards against the gesture latching open: a second drag must be separately
            // undoable, not folded into the first one's entry.
            var gesture = new EditGesture();
            gesture.Advance(true, true);
            gesture.Advance(false, false);

            Assert.AreEqual(EditStep.Record, gesture.Advance(true, true));
        }

        [Test]
        public void ADrag_IsUnaffectedByTheEventTypeBeingRewrittenToUsed()
        {
            // The regression this class was rebuilt around. The previous version took an
            // EventType and branched on MouseDrag. In production every frame arrives as
            // EventType.Used, because the control consumed the event before the window polled
            // -- so the MouseDrag branch was dead and every frame fell through to
            // RecordAndCommit. The machine no longer consults the event type AT ALL, which is
            // what makes it immune; this test pins that a real drag's frame sequence is
            // decided purely by hotControl.
            var gesture = new EditGesture();
            var records = 0;
            var commits = 0;

            // Exactly the frame sequence a real drag produces -- measured, not assumed; see
            // EditGestureSeamTests for the live-IMGUI version of the same sequence.
            var frames = new[]
            {
                (changed: false, hot: true),  // MouseDown: control goes hot, value unchanged
                (changed: true, hot: true),   // drag
                (changed: true, hot: true),   // drag
                (changed: false, hot: false)  // MouseUp: control goes cold, value unchanged
            };

            foreach (var frame in frames)
            {
                var step = gesture.Advance(frame.changed, frame.hot);
                if (step == EditStep.Record || step == EditStep.RecordAndCommit)
                {
                    records++;
                }

                if (step == EditStep.Commit || step == EditStep.RecordAndCommit)
                {
                    commits++;
                }
            }

            Assert.AreEqual(1, records, "One drag must produce exactly one undo entry.");
            Assert.AreEqual(1, commits, "One drag must produce exactly one commit.");
        }
    }
}
