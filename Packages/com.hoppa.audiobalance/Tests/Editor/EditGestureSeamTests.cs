using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Drives <see cref="EditGesture"/> through a REAL IMGUI window with REAL mouse events,
    /// because the unit tests beside it cannot prove the window feeds it the right signal.
    ///
    /// <para>
    /// That gap was not hypothetical. The first version of this machine took an
    /// <c>EventType</c> and branched on <c>MouseDrag</c>; its unit tests fed it
    /// <c>MouseDrag</c>/<c>MouseUp</c> and passed. In production those values never occur at
    /// the poll site: the control that owns the drag calls <c>Event.Use()</c> while drawing,
    /// and <c>Use()</c> rewrites BOTH <c>Event.current.type</c> and <c>Event.current.rawType</c>
    /// to <c>Used</c>. Every frame therefore hit the discrete branch, and one drag produced one
    /// undo entry and one full re-solve PER FRAME -- the exact defect the machine existed to
    /// prevent. The logic was right, the wiring was wrong, and no test spanned the seam.
    /// </para>
    ///
    /// <para>
    /// <b>Note on the control used.</b> This harness drives a <i>labelled</i>
    /// <c>EditorGUI.FloatField</c>, whose label is a number-drag hot zone. Task 11's own
    /// category offset field is the label-less overload, which has an EMPTY drag hot zone and
    /// so never streams values at all -- measured. The draggable control this protects arrives
    /// with Task 12's trim slider, which behaves identically to the field used here. The
    /// harness mirrors the production wiring (read <c>GUIUtility.hotControl</c> immediately
    /// after the controls draw); if that wiring changes in the window, change it here too.
    /// </para>
    /// </summary>
    public class EditGestureSeamTests
    {
        private class GestureHarnessWindow : EditorWindow
        {
            public readonly EditGesture Gesture = new EditGesture();
            public int Records;
            public int Commits;
            public int Frames;
            public bool SawUsedEventType;
            public float Value = 5f;

            private void OnGUI()
            {
                var type = Event.current.type;
                if (type == EventType.Layout || type == EventType.Repaint)
                {
                    return;
                }

                EditorGUI.BeginChangeCheck();
                Value = EditorGUI.FloatField(new Rect(10f, 10f, 200f, 18f), "Offset", Value);
                var changed = EditorGUI.EndChangeCheck();

                // Exactly what AudioBalanceWindow does: hotControl read AFTER the controls
                // draw, event type never consulted.
                var step = Gesture.Advance(changed, GUIUtility.hotControl != 0);

                if (Event.current.type == EventType.Used)
                {
                    SawUsedEventType = true;
                }

                Frames++;

                if (step == EditStep.Record || step == EditStep.RecordAndCommit)
                {
                    Records++;
                }

                if (step == EditStep.Commit || step == EditStep.RecordAndCommit)
                {
                    Commits++;
                }
            }
        }

        [Test]
        public void ARealNumberDrag_ProducesExactlyOneUndoEntryAndOneCommit()
        {
            var window = ScriptableObject.CreateInstance<GestureHarnessWindow>();

            try
            {
                window.position = new Rect(0f, 0f, 400f, 200f);
                window.ShowUtility();

                var start = new Vector2(30f, 19f); // over the label = the number-drag hot zone

                window.SendEvent(new Event
                {
                    type = EventType.MouseDown, mousePosition = start, button = 0, clickCount = 1
                });

                for (var i = 1; i <= 4; i++)
                {
                    window.SendEvent(new Event
                    {
                        type = EventType.MouseDrag,
                        mousePosition = start + new Vector2(i * 10f, 0f),
                        delta = new Vector2(10f, 0f),
                        button = 0
                    });
                }

                window.SendEvent(new Event
                {
                    type = EventType.MouseUp, mousePosition = start + new Vector2(40f, 0f), button = 0
                });

                Assert.Greater(window.Frames, 0, "Precondition: the harness actually ran OnGUI.");
                Assert.IsTrue(window.SawUsedEventType,
                    "Precondition: the control consumed the event, so the poll site sees EventType.Used. " +
                    "If this fails the harness is no longer reproducing the condition it exists to pin.");
                Assert.AreNotEqual(5f, window.Value,
                    "Precondition: the drag actually moved the value, so there was an edit to batch.");

                Assert.AreEqual(1, window.Records,
                    "A single drag must record exactly one undo entry. More than one means the " +
                    "gesture machine is not seeing the drag and is treating every frame as a " +
                    "complete discrete edit.");
                Assert.AreEqual(1, window.Commits,
                    "A single drag must commit exactly once -- each commit re-solves every row.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AClickWithNoDrag_CommitsNothing()
        {
            var window = ScriptableObject.CreateInstance<GestureHarnessWindow>();

            try
            {
                window.position = new Rect(0f, 0f, 400f, 200f);
                window.ShowUtility();

                var start = new Vector2(30f, 19f);
                window.SendEvent(new Event
                {
                    type = EventType.MouseDown, mousePosition = start, button = 0, clickCount = 1
                });
                window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = start, button = 0 });

                // Without this the test is green when SendEvent no-ops (headless runner, window
                // never shown) -- 0 records because nothing ran, not because nothing committed.
                Assert.Greater(window.Frames, 0, "Precondition: the harness actually ran OnGUI.");

                Assert.AreEqual(0, window.Records, "Pressing and releasing without moving is not an edit.");
                Assert.AreEqual(0, window.Commits, "...and must not dirty the asset or re-solve.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }
    }
}
