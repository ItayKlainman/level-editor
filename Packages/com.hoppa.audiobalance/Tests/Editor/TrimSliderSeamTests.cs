using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// The trim slider is the first control in this window that genuinely STREAMS, so this is
    /// where the undo-batching machine gets its first real exercise.
    ///
    /// <para>
    /// <see cref="EditGestureSeamTests"/> drives a labelled <c>FloatField</c>, whose label is a
    /// number-drag hot zone. Task 11's own category offset field is the label-less overload with
    /// an EMPTY drag zone, so it never streamed and the per-frame-undo defect stayed latent
    /// there. A slider does stream -- this harness asserts that as a PRECONDITION
    /// (<c>ChangeFrames &gt; 1</c>) rather than assuming it, because a harness that produces one
    /// change frame would report 1 record / 1 commit no matter how broken the batching was.
    /// </para>
    ///
    /// <para>
    /// The wiring mirrors <c>AudioBalanceWindow.DrawClipRow</c> exactly, including the
    /// second, unconditional <c>Advance(false, ...)</c> poll after the row loop that closes a
    /// gesture whose terminating frame carried no new value. If that wiring changes in the
    /// window, change it here too -- a unit test over <see cref="EditGesture"/> alone cannot
    /// prove the window feeds it the right signal, which is exactly how the event-type version
    /// passed its tests while being unreachable from production.
    /// </para>
    /// </summary>
    public class TrimSliderSeamTests
    {
        private class TrimHarnessWindow : EditorWindow
        {
            public readonly EditGesture Gesture = new EditGesture();
            public int Records;
            public int Commits;
            public int ChangeFrames;
            public int Frames;
            public bool SawUsedEventType;
            public float Trim;

            private void OnGUI()
            {
                var type = Event.current.type;
                if (type == EventType.Layout || type == EventType.Repaint)
                {
                    return;
                }

                Frames++;

                // --- exactly what DrawClipRow does ---
                EditorGUI.BeginChangeCheck();
                var trim = EditorGUI.Slider(new Rect(10f, 10f, 200f, 18f), Trim, -12f, 12f);

                if (EditorGUI.EndChangeCheck())
                {
                    ChangeFrames++;

                    var step = Gesture.Advance(true, GUIUtility.hotControl != 0);
                    if (step == EditStep.Record || step == EditStep.RecordAndCommit)
                    {
                        Records++;
                    }

                    // Applied every frame so the slider stays live under the cursor; only the
                    // commit is deferred.
                    Trim = trim;

                    if (step == EditStep.Commit || step == EditStep.RecordAndCommit)
                    {
                        Commits++;
                    }
                }

                // --- and the once-per-frame poll DrawClips runs after the row loop ---
                if (Gesture.Advance(false, GUIUtility.hotControl != 0) == EditStep.Commit)
                {
                    Commits++;
                }

                if (Event.current.type == EventType.Used)
                {
                    SawUsedEventType = true;
                }
            }
        }

        [Test]
        public void ARealTrimSliderDrag_ProducesExactlyOneUndoEntryAndOneResolve()
        {
            var window = ScriptableObject.CreateInstance<TrimHarnessWindow>();

            try
            {
                window.position = new Rect(0f, 0f, 400f, 200f);
                window.ShowUtility();

                var start = new Vector2(30f, 19f); // on the slider track, left of centre

                window.SendEvent(new Event
                {
                    type = EventType.MouseDown, mousePosition = start, button = 0, clickCount = 1
                });

                for (var i = 1; i <= 4; i++)
                {
                    window.SendEvent(new Event
                    {
                        type = EventType.MouseDrag,
                        mousePosition = start + new Vector2(i * 20f, 0f),
                        delta = new Vector2(20f, 0f),
                        button = 0
                    });
                }

                window.SendEvent(new Event
                {
                    type = EventType.MouseUp, mousePosition = start + new Vector2(80f, 0f), button = 0
                });

                Assert.Greater(window.Frames, 0, "Precondition: the harness actually ran OnGUI.");
                Assert.IsTrue(window.SawUsedEventType,
                    "Precondition: the control consumed the event, so the poll site sees " +
                    "EventType.Used -- the condition that makes an event-type gesture machine " +
                    "dead code.");
                Assert.Greater(window.ChangeFrames, 1,
                    "Precondition: the slider must STREAM -- more than one frame reporting a " +
                    "change. With a single change frame this test would pass against completely " +
                    "unbatched code, which is how the same defect stayed latent behind Task 11's " +
                    "label-less FloatField.");

                Assert.AreEqual(1, window.Records,
                    "One drag = one undo entry. More means the gesture machine is not seeing the " +
                    "drag and is treating every frame as a complete discrete edit.");
                Assert.AreEqual(1, window.Commits,
                    "One drag = one Resolve. Each commit re-solves EVERY row, so a per-frame " +
                    "commit is the expensive half of the defect.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IdleFrames_NeverCommitAPhantomTrimEdit()
        {
            var window = ScriptableObject.CreateInstance<TrimHarnessWindow>();

            try
            {
                window.position = new Rect(0f, 0f, 400f, 200f);
                window.ShowUtility();

                // Clicks well away from the slider: OnGUI runs, nothing is edited, nothing ever
                // takes hotControl. (MouseMove is NOT usable here -- SendEvent does not deliver
                // it to OnGUI at all, so the harness never ran and the test was green for the
                // wrong reason. The Frames precondition caught exactly that.)
                var empty = new Vector2(300f, 150f);

                for (var i = 0; i < 3; i++)
                {
                    window.SendEvent(new Event
                    {
                        type = EventType.MouseDown, mousePosition = empty, button = 0, clickCount = 1
                    });
                    window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = empty, button = 0 });
                }

                Assert.Greater(window.Frames, 0, "Precondition: the harness actually ran OnGUI.");
                Assert.AreEqual(0, window.Records, "Nothing was edited, so nothing may be recorded.");
                Assert.AreEqual(0, window.Commits,
                    "The idle poll must not dirty the profile or re-solve on frames with no edit.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }
    }
}
