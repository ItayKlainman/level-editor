using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Audio Balance panel. Laid out with absolute GUI.* rects rather than GUILayout: a
    /// GUILayout island whose buttons open modal dialogs corrupts the layout stack when the
    /// modal throws ExitGUIException, which is exactly the crash class LevelEditorWindow hit.
    ///
    /// <para>
    /// That immunity is what makes it safe to call EditorUtility.OpenFolderPanel and
    /// DisplayDialog from inside OnGUI here (see AddFolder) -- with no layout stack to
    /// corrupt, an ExitGUIException unwinding through this OnGUI has nothing to leave
    /// inconsistent. The convention still holds for anything added later: after a modal
    /// that mutates state the GUI is about to keep reading, call GUIUtility.ExitGUI() to
    /// abandon the rest of the frame rather than drawing against half-changed state. Any
    /// future GUILayout block in this window would forfeit the immunity entirely.
    /// </para>
    ///
    /// <para>
    /// Deliberately thin: everything worth testing lives in <see cref="AudioBalanceSession"/>
    /// (measure/solve orchestration) and <see cref="EditGesture"/> (undo/commit batching), so
    /// OnGUI is layout and dispatch only.
    /// </para>
    /// </summary>
    public sealed class AudioBalanceWindow : EditorWindow
    {
        private const float ToolbarHeight = 24f;
        private const float RowHeight = 20f;
        private const float Pad = 6f;

        /// <summary>
        /// Height of the categories box, computed rather than fixed. The block is a header
        /// row, one row per category, and the Add Category button; at RowHeight = 20 a fixed
        /// 130f overflowed at just FIVE categories (4 + 20 + 20n + 18 needs 142f at n = 5),
        /// spilling over the clip table with overlapping click rects. The window ships an
        /// "Add Category" button and defaults to three, so that is two clicks away.
        /// </summary>
        private float CategoryBlockHeight =>
            RowHeight * ((_profile?.Categories?.Count ?? 0) + 2) + 10f;

        // [SerializeField] is required for these to survive a script recompile: EditorWindow
        // serializes private fields only when they are marked. Without it the designer's
        // profile selection and scroll position are silently dropped on every recompile.
        // _session and _cache deliberately are NOT serialized -- neither is serializable, and
        // both are cheap to rebuild in OnEnable.
        [SerializeField] private AudioBalanceProfile _profile;
        [SerializeField] private Vector2 _clipScroll;

        private readonly AudioBalanceSession _session = new AudioBalanceSession();
        private LoudnessCache _cache;

        /// <summary>
        /// Built lazily and reused. Constructing a GUIStyle inside OnGUI allocates on every
        /// repaint; EditorStyles is also null during some early domain-reload frames, so it
        /// cannot simply be a field initialiser.
        /// </summary>
        private static GUIStyle _hintStyle;

        private static GUIStyle HintStyle
        {
            get
            {
                if (_hintStyle == null && EditorStyles.miniLabel != null)
                {
                    _hintStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false };
                    _hintStyle.normal.textColor = Color.gray;
                }

                return _hintStyle ?? GUIStyle.none;
            }
        }

        /// <summary>
        /// One gesture for the whole category block: only one control in a window can be
        /// dragged at a time, so a single instance is sufficient and keeps the commit in one
        /// place.
        /// </summary>
        private readonly EditGesture _categoryGesture = new EditGesture();

        /// <summary>
        /// Whether the edit currently being committed changed a measurement input (a mode or
        /// a name) rather than only an offset. Sticky across the frames of one drag, because
        /// the decision is made when the value changes but acted on when the gesture ends.
        /// </summary>
        private bool _categoryEditNeedsAnalyze;

        [MenuItem("Window/Hoppa/Audio Balance")]
        public static void Open()
        {
            var window = GetWindow<AudioBalanceWindow>(false, "Audio Balance", true);
            window.minSize = new Vector2(720f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            _cache = LoudnessCache.Load();
        }

        private void OnDisable()
        {
            _cache?.Save();
        }

        private void OnGUI()
        {
            var y = Pad;

            DrawToolbar(new Rect(Pad, y, position.width - Pad * 2f, ToolbarHeight));
            y += ToolbarHeight + Pad;

            if (_profile == null)
            {
                // ASCII only in IMGUI captions: default-font glyph coverage is not
                // guaranteed, and a blank caption is indistinguishable from a broken one.
                GUI.Label(new Rect(Pad, y, position.width - Pad * 2f, 40f),
                    "Assign an Audio Balance Profile to begin.\n" +
                    "Create one via Assets > Create > Hoppa > Audio > Audio Balance Profile.");
                return;
            }

            DrawAnchor(new Rect(Pad, y, position.width - Pad * 2f, RowHeight));
            y += RowHeight + Pad;

            DrawCategories(new Rect(Pad, y, position.width - Pad * 2f, CategoryBlockHeight));
            y += CategoryBlockHeight + Pad;

            DrawClips(new Rect(Pad, y, position.width - Pad * 2f, position.height - y - Pad));
        }

        private void DrawToolbar(Rect rect)
        {
            var x = rect.x;

            GUI.Label(new Rect(x, rect.y, 50f, rect.height), "Profile");
            x += 54f;

            var picked = (AudioBalanceProfile)EditorGUI.ObjectField(
                new Rect(x, rect.y, 220f, rect.height - 4f), _profile,
                typeof(AudioBalanceProfile), false);

            if (picked != _profile)
            {
                _profile = picked;
                _session.Clear();

                // Any edit gesture in flight belonged to the OLD profile; carrying its pending
                // re-analysis flag across would apply it to the new one.
                _categoryEditNeedsAnalyze = false;

                // Task 12 adds a _selected set here too -- see ClearSelection() in that task.
                // Anything holding onto clips from the old profile MUST be dropped here.
            }

            x += 226f;

            if (GUI.Button(new Rect(x, rect.y, 110f, rect.height - 4f), "Add Folder..."))
            {
                AddFolder();
            }

            x += 116f;

            GUI.enabled = _profile != null;

            if (GUI.Button(new Rect(x, rect.y, 90f, rect.height - 4f), "Analyze"))
            {
                GUI.enabled = true;
                RunAnalysis();

                // The analysis replaced every row this frame's remaining draw calls are about
                // to read, and ran a modal progress bar on top. Abandon the frame rather than
                // rendering against half-changed state.
                GUIUtility.ExitGUI();
            }

            GUI.enabled = true;
        }

        private void DrawAnchor(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y, 50f, rect.height), "Anchor");

            var picked = (AudioClip)EditorGUI.ObjectField(
                new Rect(rect.x + 54f, rect.y, 220f, rect.height - 2f),
                _profile.Anchor, typeof(AudioClip), false);

            if (picked != _profile.Anchor)
            {
                Undo.RecordObject(_profile, "Set Audio Balance Anchor");
                _profile.Anchor = picked;
                EditorUtility.SetDirty(_profile);

                // Re-run only if an analysis has already happened. The anchor is the outlier
                // REFERENCE, so a stale AnchorLufs/AnchorStatus is wrong in both directions:
                // the outlier column stays computed against the old anchor, and the
                // suppression gate (AnchorStatus == Ok) can be stuck on or off. Leaving the
                // LUFS readout stale next to a freshly-picked clip is the "reads as a broken
                // binding" failure the audit called out. The Gain column will visibly NOT
                // move, which is correct and documented: the anchor cancels out of
                // FinalGainDb.
                //
                // Cost: this is a full RunAnalysis -- an AssetDatabase.FindAssets sweep,
                // enrolment over every discovered clip, and a cache write. Clip measurement
                // itself is a cache hit, but the pass is not free. Guarded on Rows.Count so
                // picking an anchor on a never-analyzed profile does not silently kick off a
                // full-library decode the user did not ask for.
                if (_session.Rows.Count > 0)
                {
                    RunAnalysis();
                    GUIUtility.ExitGUI();
                }
            }

            var summary = _session.AnchorStatus == ClipStatus.Ok
                ? $"{_session.AnchorLufs:0.0} LUFS"
                : "not analyzed";

            GUI.Label(new Rect(rect.x + 282f, rect.y, 120f, rect.height), summary);

            // Designers WILL swap the anchor and expect the Gain column to move. It cannot:
            // the anchor term cancels in the headroom subtraction. Saying so at the point of
            // use is the difference between a documented design and a bug report. The README
            // and guide say it too, but nobody reads those while clicking this field.
            GUI.Label(new Rect(rect.x + 406f, rect.y, rect.width - 412f, rect.height),
                "reference for outliers + readout only; category offsets set relative placement",
                HintStyle);
        }

        private void DrawCategories(Rect rect)
        {
            GUI.Box(rect, GUIContent.none);

            var y = rect.y + 4f;
            GUI.Label(new Rect(rect.x + 6f, y, 240f, RowHeight), "Categories (offset dB)");
            y += RowHeight;

            for (var i = 0; i < _profile.Categories.Count; i++)
            {
                var category = _profile.Categories[i];
                if (category == null)
                {
                    y += RowHeight;
                    continue;
                }

                var x = rect.x + 6f;

                // BeginChangeCheck rather than comparing values every frame: a raw
                // comparison fires Undo.RecordObject on EVERY OnGUI pass where the value
                // differs, so one drag of a field produces dozens of undo entries and
                // re-runs the solver for each. EditGesture then collapses the drag itself
                // into a single undo entry and a single commit.
                EditorGUI.BeginChangeCheck();

                var name = EditorGUI.DelayedTextField(
                    new Rect(x, y, 120f, RowHeight - 2f), category.Name);
                x += 126f;

                var offset = EditorGUI.FloatField(new Rect(x, y, 60f, RowHeight - 2f), category.OffsetDb);
                x += 66f;

                var mode = (MeasureMode)EditorGUI.EnumPopup(
                    new Rect(x, y, 130f, RowHeight - 2f), category.Mode);

                if (EditorGUI.EndChangeCheck())
                {
                    // A mode change changes the MEASUREMENT, which Resolve cannot do. A rename
                    // re-points every clip that referenced the old name, which can change their
                    // effective mode too. Both must re-analyze; the cache makes it near-free
                    // for any clip whose mode did not actually move.
                    var renamedFrom = name != category.Name ? category.Name : null;
                    _categoryEditNeedsAnalyze |= mode != category.Mode || renamedFrom != null;

                    // hotControl, not the event type -- by this point the controls above have
                    // consumed the event and Use() has rewritten BOTH type and rawType to
                    // Used. See the EditGesture class doc.
                    var step = _categoryGesture.Advance(true, GUIUtility.hotControl != 0);

                    if (step == EditStep.Record || step == EditStep.RecordAndCommit)
                    {
                        Undo.RecordObject(_profile, "Edit Audio Category");
                    }

                    // Apply immediately regardless of step so the field stays live under a
                    // drag; only the commit is deferred. RenameCategory sets the name AND
                    // re-points every clip referencing it -- doing only the former would
                    // silently reassign the whole group to Categories[0]. It takes the
                    // category BY REFERENCE: resolving by name renamed the wrong row whenever
                    // two categories shared a name, which "Add Category" makes easy.
                    if (renamedFrom != null && !_profile.RenameCategory(category, name))
                    {
                        // Rejected -- almost always a collision with an existing category.
                        // Say so: silently reverting the field looks like dropped input.
                        _categoryEditNeedsAnalyze = false;
                        ShowNotification(new GUIContent($"'{name}' is already a category"));
                    }

                    category.OffsetDb = offset;
                    category.Mode = mode;

                    if (step == EditStep.Commit || step == EditStep.RecordAndCommit)
                    {
                        CommitCategoryEdit();
                    }
                }

                y += RowHeight;
            }

            // One poll per frame closes a gesture whose terminating frame carried no new value
            // (a mouse release does not change the number, it just ends the drag).
            //
            // Two known, accepted consequences -- documented so Task 12 does not rediscover
            // them as bugs:
            //
            // (1) DEFERRED COMMIT UNDER A BUTTON. On the MouseUp frame of a click on Analyze or
            //     Add Category, that button still holds hotControl when this poll runs, so a
            //     pending gesture commits one frame LATER. Output is correct either way -- the
            //     values were already applied per frame -- but the ordering means clicking
            //     Analyze mid-gesture runs the pass against the applied value and the deferred
            //     CommitCategoryEdit can then fire a SECOND RunAnalysis if
            //     _categoryEditNeedsAnalyze is set. Wasted work on a large library, not wrong
            //     numbers.
            //
            // (2) MID-GESTURE EDITS ARE NOT SetDirty-ED. Values apply on each frame but
            //     SetDirty only lands at commit, and _categoryGesture is deliberately not
            //     serialized, so a domain reload while the mouse is still down (a script
            //     compile finishing mid-drag) loses that edit. Rare and low-stakes; the
            //     alternative is the per-frame SetDirty this class exists to avoid.
            if (_categoryGesture.Advance(false, GUIUtility.hotControl != 0) == EditStep.Commit)
            {
                CommitCategoryEdit();
            }

            if (GUI.Button(new Rect(rect.x + 6f, y, 110f, RowHeight - 2f), "Add Category"))
            {
                Undo.RecordObject(_profile, "Add Audio Category");

                // Unique by construction. Every new row used to be called "New", so two clicks
                // produced two identically-named categories -- which made renaming ambiguous
                // and, before RenameCategory took the target by reference, actively wrong.
                // RenameCategory now also refuses to create a duplicate, so this keeps the
                // Add path consistent with that rule rather than immediately violating it.
                _profile.Categories.Add(new AudioCategory(
                    UniqueCategoryName("New"), 0f, MeasureMode.MomentaryMax));

                EditorUtility.SetDirty(_profile);

                // The block just grew by one row, so every rect computed for this frame below
                // it is stale. Redraw rather than paint the clip table at the wrong offset.
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// <paramref name="baseName"/>, or "<paramref name="baseName"/> N" for the lowest N
        /// that no existing category is using.
        /// </summary>
        private string UniqueCategoryName(string baseName)
        {
            if (_profile.Categories.Find(c => c != null && c.Name == baseName) == null)
            {
                return baseName;
            }

            for (var i = 2; ; i++)
            {
                var candidate = baseName + " " + i;
                if (_profile.Categories.Find(c => c != null && c.Name == candidate) == null)
                {
                    return candidate;
                }
            }
        }

        /// <summary>
        /// Ends a category edit: persist it, then bring the session back in step. Re-analysis
        /// is the correct response to a mode or name change -- <see cref="AudioBalanceSession.Resolve"/>
        /// cannot change a measurement, and would silently keep the old-mode LUFS.
        /// </summary>
        private void CommitCategoryEdit()
        {
            EditorUtility.SetDirty(_profile);

            if (_categoryEditNeedsAnalyze)
            {
                _categoryEditNeedsAnalyze = false;

                // Only meaningful once rows exist; otherwise there is nothing to re-measure
                // and a bare category rename would trigger a full-library decode.
                if (_session.Rows.Count > 0)
                {
                    RunAnalysis();
                    GUIUtility.ExitGUI();
                }

                return;
            }

            _session.Resolve(_profile);
        }

        /// <summary>Replaced with the full sortable table in Task 12.</summary>
        private void DrawClips(Rect rect)
        {
            GUI.Box(rect, GUIContent.none);

            var content = new Rect(0f, 0f, rect.width - 20f, _session.Rows.Count * RowHeight + 4f);
            _clipScroll = GUI.BeginScrollView(rect, _clipScroll, content);

            var y = 2f;
            foreach (var row in _session.Rows)
            {
                var label = row.Analysis.Status == ClipStatus.Ok
                    ? $"{row.Clip.name}    {row.Analysis.Lufs:0.0} LUFS    {row.Gain.FinalGainDb:0.0} dB"
                    : $"{row.Clip.name}    {row.Analysis.Reason}";

                GUI.Label(new Rect(4f, y, content.width - 8f, RowHeight), label);
                y += RowHeight;
            }

            GUI.EndScrollView();
        }

        private void AddFolder()
        {
            var absolute = EditorUtility.OpenFolderPanel("Add Audio Folder", "Assets", string.Empty);
            if (string.IsNullOrEmpty(absolute))
            {
                return;
            }

            var relative = ToProjectRelative(absolute);
            if (relative == null)
            {
                EditorUtility.DisplayDialog("Audio Balance",
                    "Pick a folder inside this project's Assets directory.", "OK");
                return;
            }

            if (!_profile.Folders.Contains(relative))
            {
                Undo.RecordObject(_profile, "Add Audio Folder");
                _profile.Folders.Add(relative);
                EditorUtility.SetDirty(_profile);
            }
        }

        /// <summary>
        /// Absolute paths in a committed asset break every other checkout, so folders are
        /// always stored relative to the project root.
        /// </summary>
        private static string ToProjectRelative(string absolutePath)
        {
            var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return null;
            }

            var normalizedRoot = projectRoot.Replace('\\', '/') + "/";
            var normalizedPath = absolutePath.Replace('\\', '/');

            return normalizedPath.StartsWith(normalizedRoot)
                ? normalizedPath.Substring(normalizedRoot.Length)
                : null;
        }

        private void RunAnalysis()
        {
            var discovered = LoudnessAnalyzer.FindClips(_profile.Folders);

            // Enrolment -- creating a ClipSettings for a clip the profile has not seen -- is a
            // mutation, so it happens HERE, once, inside an Undo scope and before SetDirty.
            // AudioBalanceSession never enrols; it reads through FindSettings only. That split
            // is deliberate: SettingsFor appends on a miss, so leaving it in the measure path
            // meant the profile asset was written from a method the tests call directly, with
            // no Undo entry and no defined ordering against SetDirty.
            Undo.RecordObject(_profile, "Scan Audio Folders");

            foreach (var clip in discovered)
            {
                _profile.SettingsFor(clip);
            }

            // The anchor gets a baked gain entry like any other clip (lead's call), so it must
            // be enrolled explicitly rather than as a side effect of being measured.
            if (_profile.Anchor != null)
            {
                _profile.SettingsFor(_profile.Anchor);
            }

            EditorUtility.SetDirty(_profile);

            try
            {
                // The progress bar is driven BY the analysis, not alongside it. An earlier
                // revision ran a display-only loop and then called Analyze once, so the bar
                // swept to 100% instantly, the editor froze with no feedback, and Cancel
                // exited only the display loop while the real work ran to completion.
                // Throttled: a fully-cached clip measures in microseconds, but each
                // DisplayCancelableProgressBar call is a modal repaint costing far more than
                // the cache hit it reports. At 500 mostly-cached clips an unthrottled bar is
                // the dominant cost of the whole pass. Always draw the first clip so the bar
                // appears immediately, then roughly every 50 ms.
                var lastTick = 0d;
                var cancelled = false;

                var completed = _session.Analyze(_profile, _cache, (clip, index, total) =>
                {
                    var now = EditorApplication.timeSinceStartup;
                    if (index != 0 && now - lastTick < 0.05d && !cancelled)
                    {
                        return false;
                    }

                    lastTick = now;
                    cancelled = EditorUtility.DisplayCancelableProgressBar(
                        "Audio Balance",
                        $"Analyzing {clip.name}  ({index + 1}/{total})",
                        total == 0 ? 0f : index / (float)total);

                    return cancelled;
                });

                if (!completed)
                {
                    ShowNotification(new GUIContent("Analysis cancelled - showing partial results"));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // Only rewrite the Library/ JSON when a measurement was actually stored. Every
            // category name/mode commit and every anchor pick routes through here, and a
            // fully-cached pass stores nothing -- writing the file each time would be a disk
            // write per category keystroke.
            if (_cache != null && _cache.IsDirty)
            {
                _cache.Save();
            }

            Repaint();
        }
    }
}
