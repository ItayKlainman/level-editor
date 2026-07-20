using System.Collections.Generic;
using System.Linq;
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
        [SerializeField] private string _filter = string.Empty;
        [SerializeField] private ClipSortMode _sort = ClipSortMode.Name;
        [SerializeField] private bool _ascending = true;

        /// <summary>
        /// Checked rows, for bulk assign. Not serialized -- a HashSet is not a serializable
        /// field type, so marking it would be a lie. A recompile therefore clears the selection,
        /// which is the safe direction: the alternative (stale clips from another profile) is
        /// the bug <see cref="PruneSelection"/> exists to prevent.
        /// </summary>
        private readonly HashSet<AudioClip> _selected = new HashSet<AudioClip>();

        /// <summary>
        /// Clip settings resolved ONCE per row-set change, never during rendering.
        /// <c>AudioBalanceProfile.SettingsFor</c> appends on a miss, so resolving per row per
        /// OnGUI event both mutated the asset mid-render and made drawing O(n^2). Built via
        /// <see cref="ClipListView.BuildSettingsLookup"/>, which cannot write to the profile at
        /// all.
        /// </summary>
        private Dictionary<AudioClip, ClipSettings> _settings =
            new Dictionary<AudioClip, ClipSettings>();

        private int _settingsStamp = -1;

        /// <summary>
        /// Widest a clip row actually draws, with room for the preview buttons Task 13 appends.
        /// The scroll view's content rect must be at least this wide or the horizontal scrollbar
        /// never appears and the right-hand controls are silently unreachable at the window's
        /// minimum width (720).
        /// </summary>
        private const float MinRowWidth = 800f;

        /// <summary>
        /// One gesture for the whole clip table: only one control in a window can be dragged at
        /// a time, so a single instance covers every row's trim slider.
        ///
        /// <para>
        /// The trim slider is the first control in this window that genuinely STREAMS -- the
        /// category block's label-less FloatField has an empty drag hot zone and never did --
        /// so this is where the batching actually earns its keep. <c>TrimSliderSeamTests</c>
        /// drives it through real IMGUI events and asserts the streaming as a precondition.
        /// </para>
        /// </summary>
        private readonly EditGesture _trimGesture = new EditGesture();

        /// <summary>
        /// Set when a control inside the clip table needs a full re-measure. It cannot run
        /// inline: the table draws inside an open <c>GUI.BeginScrollView</c>, and RunAnalysis
        /// puts up a modal progress bar and REPLACES every row the rest of the frame is about to
        /// read. Deferred to the end of OnGUI, outside the scroll view, instead.
        /// </summary>
        private bool _analyzeRequested;

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
        /// See <see cref="PendingAnalyze"/> for why it is a type and not a bool.
        /// </summary>
        private readonly PendingAnalyze _categoryAnalyze = new PendingAnalyze();

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

            // Deferred to here on purpose -- see _analyzeRequested. At the end of OnGUI the
            // scroll view is closed and nothing further reads the rows this replaces, so the
            // modal progress bar has nothing to unwind through and no ExitGUI is needed.
            if (_analyzeRequested)
            {
                _analyzeRequested = false;

                if (_profile != null)
                {
                    RunAnalysis();
                }
            }
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
                _categoryAnalyze.Reset();
                _analyzeRequested = false;

                // Everything holding clips from the old profile is dropped here. Leaving
                // _selected populated made the bulk button read "Set Category (12)" while the
                // resolved target set was empty -- clicking looked like it worked and silently
                // did nothing.
                _selected.Clear();
                _settings.Clear();
                _settingsStamp = -1;
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
                    var renamedFrom = name != category.Name ? category.Name : null;

                    // hotControl, not the event type -- by this point the controls above have
                    // consumed the event and Use() has rewritten BOTH type and rawType to
                    // Used. See the EditGesture class doc.
                    var step = _categoryGesture.Advance(true, GUIUtility.hotControl != 0);

                    // Before any mutation, or the entry captures the post-edit state.
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
                    //
                    // Applied BEFORE the analyze flag is folded, so a rejection contributes
                    // nothing instead of clearing the flag. The old code assigned false here,
                    // which wiped a mode change from an EARLIER frame of the same gesture --
                    // the commit then fell through to Resolve, which cannot re-measure, and
                    // baked a gain from the old-mode LUFS.
                    var renameApplied = renamedFrom != null && _profile.RenameCategory(category, name);

                    if (renamedFrom != null && !renameApplied)
                    {
                        // Rejected -- almost always a collision with an existing category.
                        // Say so: silently reverting the field looks like dropped input.
                        ShowNotification(new GUIContent($"'{name}' is already a category"));
                    }

                    // A mode change changes the MEASUREMENT, which Resolve cannot do. An
                    // APPLIED rename re-points every clip that referenced the old name, which
                    // can change their effective mode too. Both must re-analyze; the cache
                    // makes it near-free for any clip whose mode did not actually move.
                    // Evaluated before category.Mode is overwritten, below.
                    _categoryAnalyze.Observe(mode != category.Mode || renameApplied);

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
            //     CommitCategoryEdit can then fire a SECOND RunAnalysis if _categoryAnalyze is
            //     set. Wasted work on a large library, not wrong numbers.
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

            if (_categoryAnalyze.Consume())
            {
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

        /// <summary>
        /// Rebuilds the clip-settings map when the row set changes, so the render path never
        /// resolves settings per row per event -- <c>SettingsFor</c> appends on a miss and
        /// <c>FindSettings</c> is a linear scan, so doing it inline was both an asset write
        /// during a repaint and O(n^2) drawing.
        ///
        /// <para>
        /// The stamp is deliberately coarse. It only has to notice that the ROW SET changed;
        /// edits that mutate a ClipSettings in place (a bulk assign, a trim) are invisible to it
        /// and need to be -- the map holds live references, so those edits are already visible
        /// through it. Profile switches reset the stamp explicitly rather than relying on the
        /// counts to differ.
        /// </para>
        /// </summary>
        private void SyncSettings()
        {
            var stamp = _profile == null
                ? -1
                : _session.Rows.Count * 397 ^ _profile.Clips.Count;

            if (stamp == _settingsStamp)
            {
                return;
            }

            _settingsStamp = stamp;
            _settings = ClipListView.BuildSettingsLookup(_profile, _session.Rows);
        }

        private string CategoryOf(AudioBalanceRow row)
        {
            return row?.Clip != null && _settings.TryGetValue(row.Clip, out var settings) && settings != null
                ? settings.Category
                : string.Empty;
        }

        /// <summary>
        /// Drops selection entries that no longer resolve. Without it the bulk button reads
        /// "Set Category (12)" while the resolved target list is empty -- clicking appears to
        /// work and silently does nothing.
        /// </summary>
        private void PruneSelection()
        {
            if (_selected.Count == 0)
            {
                return;
            }

            _selected.RemoveWhere(clip => clip == null || !_settings.ContainsKey(clip));
        }

        private void DrawClips(Rect rect)
        {
            SyncSettings();
            PruneSelection();

            DrawClipHeader(new Rect(rect.x, rect.y, rect.width, RowHeight));

            var body = new Rect(rect.x, rect.y + RowHeight + 2f, rect.width,
                Mathf.Max(0f, rect.height - RowHeight - 2f));

            GUI.Box(body, GUIContent.none);

            var visible = ClipListView.BuildVisible(_session.Rows, _filter, _sort, _ascending, CategoryOf);

            // Content must be as wide as the widest row actually IS, not as wide as the
            // viewport. Hard-coding it to the viewport means the horizontal scrollbar never
            // appears and anything past the right edge is silently unreachable at the window's
            // minimum width.
            var content = new Rect(0f, 0f,
                Mathf.Max(body.width - 20f, MinRowWidth), visible.Count * RowHeight + 4f);

            // Allocated once per frame rather than once per ROW: Select().ToArray() inside the
            // row loop is two allocations per row per event, which at a few hundred clips is
            // the dominant garbage in the window.
            var categoryNames = _profile.Categories
                .Where(c => c != null)
                .Select(c => c.Name)
                .ToArray();

            _clipScroll = GUI.BeginScrollView(body, _clipScroll, content);

            var y = 2f;
            foreach (var row in visible)
            {
                DrawClipRow(new Rect(4f, y, content.width - 8f, RowHeight), row, categoryNames);
                y += RowHeight;
            }

            GUI.EndScrollView();

            // One poll per frame closes a trim gesture whose terminating frame carried no new
            // value -- a mouse release does not change the number, it just ends the drag. Same
            // shape as the category block's poll, and subject to the same two documented
            // consequences (deferred commit under a button; mid-gesture edits not SetDirty-ed).
            if (_trimGesture.Advance(false, GUIUtility.hotControl != 0) == EditStep.Commit)
            {
                CommitTrimEdit();
            }
        }

        private void DrawClipHeader(Rect rect)
        {
            var x = rect.x;

            GUI.Label(new Rect(x, rect.y, 40f, rect.height), "Filter");
            x += 44f;

            _filter = EditorGUI.TextField(new Rect(x, rect.y, 140f, rect.height - 2f), _filter);
            x += 146f;

            GUI.Label(new Rect(x, rect.y, 32f, rect.height), "Sort");
            x += 36f;

            _sort = (ClipSortMode)EditorGUI.EnumPopup(
                new Rect(x, rect.y, 100f, rect.height - 2f), _sort);
            x += 106f;

            // ASCII captions: default IMGUI font glyph coverage is not guaranteed, and a blank
            // button is indistinguishable from a broken one.
            if (GUI.Button(new Rect(x, rect.y, 44f, rect.height - 2f), _ascending ? "Asc" : "Desc"))
            {
                _ascending = !_ascending;
            }

            x += 50f;

            GUI.enabled = _selected.Count > 0;

            if (GUI.Button(new Rect(x, rect.y, 130f, rect.height - 2f),
                    $"Set Category ({_selected.Count})"))
            {
                ShowBulkCategoryMenu();
            }

            GUI.enabled = true;
        }

        private void DrawClipRow(Rect rect, AudioBalanceRow row, string[] categoryNames)
        {
            var x = rect.x;

            var wasSelected = _selected.Contains(row.Clip);
            var isSelected = EditorGUI.Toggle(new Rect(x, rect.y, 18f, rect.height), wasSelected);

            if (isSelected != wasSelected)
            {
                if (isSelected)
                {
                    _selected.Add(row.Clip);
                }
                else
                {
                    _selected.Remove(row.Clip);
                }
            }

            x += 22f;

            GUI.Label(new Rect(x, rect.y, 170f, rect.height), row.Clip.name);
            x += 174f;

            // Resolved in SyncSettings, never here -- see that method. A clip with no settings
            // is not enrolled, so it has nothing to edit; it still shows its measurement.
            _settings.TryGetValue(row.Clip, out var settings);

            if (settings != null && categoryNames.Length > 0)
            {
                var current = Mathf.Max(0, System.Array.IndexOf(categoryNames, settings.Category));
                var picked = EditorGUI.Popup(
                    new Rect(x, rect.y, 90f, rect.height - 2f), current, categoryNames);

                if (picked != current && picked >= 0 && picked < categoryNames.Length)
                {
                    Undo.RecordObject(_profile, "Change Audio Category");
                    settings.Category = categoryNames[picked];
                    EditorUtility.SetDirty(_profile);

                    // Categories carry their own MeasureMode, so this changes HOW the clip must
                    // be measured -- Resolve cannot do that and would keep the old-mode number.
                    // Re-measure; the cache makes it a hit for every clip whose mode is
                    // unchanged. Deferred to the end of OnGUI because we are inside an open
                    // scroll view -- see _analyzeRequested.
                    _analyzeRequested = true;
                }
            }

            x += 96f;

            GUI.Label(new Rect(x, rect.y, 90f, rect.height),
                row.Analysis.Status == ClipStatus.Ok ? $"{row.Analysis.Lufs:0.0} LUFS" : "-");
            x += 94f;

            GUI.Label(new Rect(x, rect.y, 70f, rect.height),
                row.Analysis.Status == ClipStatus.Ok ? $"{row.Gain.FinalGainDb:0.0} dB" : "-");
            x += 74f;

            if (settings != null)
            {
                // A slider STREAMS: it fires a change on every OnGUI frame the value differs.
                // Comparing values directly, or recording undo inside EndChangeCheck, would push
                // one undo entry per frame -- dozens for a single drag -- and re-run GainSolver
                // over every row each time. EditGesture collapses the drag into one entry and
                // one Resolve; TrimSliderSeamTests pins that through real IMGUI events.
                EditorGUI.BeginChangeCheck();

                var trim = EditorGUI.Slider(
                    new Rect(x, rect.y, 150f, rect.height - 2f), settings.TrimDb, -12f, 12f);

                if (EditorGUI.EndChangeCheck())
                {
                    var step = _trimGesture.Advance(true, GUIUtility.hotControl != 0);

                    if (step == EditStep.Record || step == EditStep.RecordAndCommit)
                    {
                        Undo.RecordObject(_profile, "Change Audio Trim");
                    }

                    // Applied every frame so the slider stays live under the cursor; only the
                    // commit is deferred, so the Gain column catches up on release.
                    settings.TrimDb = trim;

                    if (step == EditStep.Commit || step == EditStep.RecordAndCommit)
                    {
                        CommitTrimEdit();
                    }
                }
            }

            x += 156f;

            var icon = ClipListView.StatusIcon(row);
            if (!string.IsNullOrEmpty(icon))
            {
                GUI.Label(new Rect(x, rect.y, 90f, rect.height),
                    new GUIContent(icon, row.Analysis.Reason ?? "gain is far from the category target"));
            }
        }

        /// <summary>
        /// Ends a trim edit. Resolve -- NOT Analyze -- is correct here and only here: a trim
        /// moves the target, it cannot change how the clip is measured.
        /// </summary>
        private void CommitTrimEdit()
        {
            if (_profile == null)
            {
                return;
            }

            EditorUtility.SetDirty(_profile);
            _session.Resolve(_profile);
        }

        private void ShowBulkCategoryMenu()
        {
            var menu = new GenericMenu();
            var targets = _session.Rows.Where(r => r?.Clip != null && _selected.Contains(r.Clip)).ToArray();

            if (targets.Length == 0)
            {
                // Defensive: PruneSelection should already have made this unreachable. Being
                // told nothing is selected beats a menu that silently does nothing.
                menu.AddDisabledItem(new GUIContent("Nothing selected"));
                menu.ShowAsContext();
                return;
            }

            foreach (var category in _profile.Categories)
            {
                if (category == null)
                {
                    continue;
                }

                var name = category.Name;
                menu.AddItem(new GUIContent(name), false, () =>
                {
                    ClipListView.BulkAssignCategory(targets, _profile, name);

                    // Bulk assign moves clips between categories, and categories carry their
                    // own MeasureMode -- so this needs a re-measure, not just a re-solve. The
                    // menu callback runs outside OnGUI, so it can call RunAnalysis directly.
                    RunAnalysis();
                });
            }

            menu.ShowAsContext();
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
