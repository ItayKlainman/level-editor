using System.IO;
using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class LevelEditorWindow : EditorWindow
    {
        [MenuItem("Window/Level Editor")]
        public static void Open() => GetWindow<LevelEditorWindow>("Level Editor");

        private readonly PalettePanel        _palette     = new PalettePanel();
        private readonly GridCanvasPanel     _canvas      = new GridCanvasPanel();
        private readonly ToolbarPanel        _toolbar     = new ToolbarPanel();
        private readonly SummaryPanel        _summary     = new SummaryPanel();
        private readonly ValidationPanel     _validation  = new ValidationPanel();
        private readonly MultiSelectPanel    _multiSelect = new MultiSelectPanel();
        private readonly GeneratorModePanel  _generator   = new GeneratorModePanel();
        private readonly ImageToGridModePanel _imagePanel = new ImageToGridModePanel();
        private readonly AutofillPanel       _autofill    = new AutofillPanel();
        private TopSectionPanel _topSection    = new EmptyTopSectionPanel();
        private TopSectionPanel _bottomSection = new EmptyTopSectionPanel();

        // Optional game-specific right-column section, provided by the profile.
        // Cached and rebuilt when the active profile changes.
        private ProfileRightPanel _rightPanel;
        private GameProfile       _rightPanelProfile;

        // Optional game-specific left-column section (between palette and TOOLS),
        // provided by the profile. Cached and rebuilt when the active profile changes.
        private ProfileLeftPanel  _leftPanel;
        private GameProfile       _leftPanelProfile;

        private LevelEditorSession _session;
        [SerializeField] private GameProfile _profile;
        private bool               _inOrderMode;
        private bool               _inGeneratorMode;
        private bool               _inImageMode;

        // True when the current level was opened via a profile importer (foreign format,
        // e.g. the game's own schema). Save then round-trips through the exporters instead
        // of writing the editor's internal LevelDocument over the source file.
        private bool               _openedForeignFormat;

        // Exposed so Layer 2 importers can resolve cell types via the active registry.
        public GameProfile Profile => _profile;

        // Public entry point for Layer 2 importers: load a LevelDocument JSON file
        // from disk into this window. Requires a profile to be selected.
        public void OpenLevelFile(string path)
        {
            if (_profile == null && !TryAutoPickProfile()) return;
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(LastDirPrefKey, Path.GetDirectoryName(path));
            LoadFromPath(path);
            Focus();
        }

        private const string LastDirPrefKey    = "Hoppa.LevelEditor.LastSaveDir";
        private const string ProfileGuidPrefKey = "Hoppa.LevelEditor.ProfileGuid";
        private const string BottomHPrefKey    = "Hoppa.LevelEditor.BottomSectionH";

        // Optional user override for the bottom-section height (drag-splitter).
        // -1 = use the panel's PreferredHeight. Persisted via EditorPrefs.
        private float _bottomSectionOverrideH = -1f;
        private bool  _isDraggingBotSplitter;
        private const float SplitterHitH = 6f;
        private const float SplitterVisH = 2f;
        private static readonly Color SplitterIdle  = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color SplitterHover = new Color(0.35f, 0.68f, 1.00f, 0.65f);

        private const float ToolbarH   = 28f;
        private const float PaletteW   = 195f;
        private const float RightW     = 260f;
        private const float StatusBarH = 20f;

        // ── Cohesive dark blue-grey palette ───────────────────────────
        private static readonly Color ToolbarBg   = new Color(0.18f, 0.20f, 0.26f);
        private static readonly Color PaletteBg   = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color CanvasBg    = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color RightBg     = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color StatusBg    = new Color(0.13f, 0.14f, 0.17f);
        private static readonly Color Divider     = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color DirtyAmber  = new Color(1.00f, 0.65f, 0.10f);

        private void OnEnable()
        {
            if (_profile == null)
            {
                var guid = EditorPrefs.GetString(ProfileGuidPrefKey, string.Empty);
                if (!string.IsNullOrEmpty(guid))
                    _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(AssetDatabase.GUIDToAssetPath(guid));
            }

            _bottomSectionOverrideH = EditorPrefs.GetFloat(BottomHPrefKey, -1f);

            _toolbar.OnNew            += HandleNew;
            _toolbar.OnOpen           += HandleOpen;
            _toolbar.OnSave           += HandleSave;
            _toolbar.OnSaveAs         += HandleSaveAs;
            _toolbar.OnExport         += HandleExport;
            _toolbar.OnUndo           += HandleUndo;
            _toolbar.OnRedo           += HandleRedo;
            _toolbar.OnTestPlay       += HandleTestPlay;
            _toolbar.OnOrderToggle    += HandleOrderToggle;
            _toolbar.OnGenerateToggle += HandleGenerateToggle;
            _toolbar.OnImageToggle    += HandleImageToggle;
            _generator.OnUseLevel     += HandleGeneratorUseLevel;
            _imagePanel.OnUseLevel    += HandleGeneratorUseLevel;
        }

        private void OnDisable()
        {
            _toolbar.OnNew            -= HandleNew;
            _toolbar.OnOpen           -= HandleOpen;
            _toolbar.OnSave           -= HandleSave;
            _toolbar.OnSaveAs         -= HandleSaveAs;
            _toolbar.OnExport         -= HandleExport;
            _toolbar.OnUndo           -= HandleUndo;
            _toolbar.OnRedo           -= HandleRedo;
            _toolbar.OnTestPlay       -= HandleTestPlay;
            _toolbar.OnOrderToggle    -= HandleOrderToggle;
            _toolbar.OnGenerateToggle -= HandleGenerateToggle;
            _toolbar.OnImageToggle    -= HandleImageToggle;
            _generator.OnUseLevel     -= HandleGeneratorUseLevel;
            _imagePanel.OnUseLevel    -= HandleGeneratorUseLevel;
            _session?.Dispose();
            _session       = null;
            _topSection    = new EmptyTopSectionPanel();
            _bottomSection = new EmptyTopSectionPanel();
        }

        private void OnGUI()
        {
            float w = position.width;
            float h = position.height;

            // ── Toolbar ───────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(0f, 0f, w, ToolbarH), ToolbarBg);
            EditorGUI.DrawRect(new Rect(0f, ToolbarH - 1f, w, 1f), Divider);
            _toolbar.OrderMode    = _inOrderMode;
            _toolbar.GenerateMode = _inGeneratorMode;
            _toolbar.ShowGenerate = _profile?.LevelGenerator != null;
            _toolbar.ImageMode    = _inImageMode;
            _toolbar.ShowImage    = _profile?.ImageToGrid != null;
            _toolbar.OnGUI(new Rect(0f, 0f, w, ToolbarH), _session);

            // ── Status bar ────────────────────────────────────────────
            float statusY = h - StatusBarH;
            EditorGUI.DrawRect(new Rect(0f, statusY, w, StatusBarH), StatusBg);
            EditorGUI.DrawRect(new Rect(0f, statusY, w, 1f), Divider);
            DrawStatusBar(new Rect(0f, statusY, w, StatusBarH));

            float bodyY  = ToolbarH;
            float innerH = h - ToolbarH - StatusBarH;

            if (_inOrderMode)
            {
                EditorGUI.DrawRect(new Rect(0f, bodyY, w, innerH), CanvasBg);
                if (_profile?.OrderPanel != null)
                    _profile.OrderPanel.OnGUI(new Rect(0f, bodyY, w, innerH), _session);
                else
                    DrawCenteredMessage(new Rect(0f, bodyY, w, innerH),
                        _profile == null
                            ? "Select a Game Profile first."
                            : "No Order Panel configured in this Game Profile.\n\nAssign an EditorPanelAsset to the profile's Order Panel field.");
                return;
            }

            if (_inGeneratorMode)
            {
                EditorGUI.DrawRect(new Rect(0f, bodyY, w, innerH), CanvasBg);
                _generator.OnGUI(new Rect(0f, bodyY, w, innerH), _profile);
                return;
            }

            if (_inImageMode)
            {
                EditorGUI.DrawRect(new Rect(0f, bodyY, w, innerH), CanvasBg);
                _imagePanel.OnGUI(new Rect(0f, bodyY, w, innerH), _profile);
                return;
            }

            if (_session == null)
            {
                DrawProfileSelector(new Rect(0f, bodyY, w, innerH));
                return;
            }

            // ── Column backgrounds ────────────────────────────────────
            EditorGUI.DrawRect(new Rect(0f,          bodyY, PaletteW, innerH), PaletteBg);
            EditorGUI.DrawRect(new Rect(PaletteW,    bodyY, w - PaletteW - RightW, innerH), CanvasBg);
            EditorGUI.DrawRect(new Rect(w - RightW,  bodyY, RightW, innerH), RightBg);

            // Column dividers
            EditorGUI.DrawRect(new Rect(PaletteW,   bodyY, 1f, innerH), Divider);
            EditorGUI.DrawRect(new Rect(w - RightW, bodyY, 1f, innerH), Divider);

            // ── Left: palette (+ optional profile left panel) ─────────
            // Lazily instantiate and cache the profile's left-column panel.
            if (!ReferenceEquals(_leftPanelProfile, _profile))
            {
                _leftPanel = _profile != null ? _profile.CreateLeftPanel() : null;
                _leftPanelProfile = _profile;
            }
            _palette.OnGUI(new Rect(0f, bodyY, PaletteW, innerH), _session, _leftPanel, _profile);

            // ── Centre: top section + canvas + bottom section ────────
            float centerX = PaletteW + 1f;
            float centerW = w - PaletteW - RightW - 2f;
            float canvasH = _canvas.RequiredHeight(centerW, _session);

            // Per-profile layout flip: when SpoolsBelowGrid is set, the top-section
            // panel (its draw rect only — it stays _topSection) is relocated BELOW
            // the grid and the grid anchors to the TOP. Default: classic layout.
            bool spoolsBelow = _profile != null && _profile.SpoolsBelowGrid;
            _topSection.ReverseRowOrder = spoolsBelow;

            if (spoolsBelow)
            {
                // Grid on top (top-anchored), spool (top-section) panel below it.
                // The spool panel reserves at least its PreferredHeight; the user
                // can drag the splitter to give it more room (persisted). The grid
                // takes the leftover top region and scrolls internally if needed.
                float minPanelH = _topSection.PreferredHeight;
                float maxPanelH = Mathf.Max(minPanelH, innerH - 100f);
                float panelH    = 0f;
                if (minPanelH > 0f)
                {
                    panelH = (_bottomSectionOverrideH > 0f)
                        ? Mathf.Clamp(_bottomSectionOverrideH, minPanelH, maxPanelH)
                        : minPanelH;
                }

                float gridH = Mathf.Max(0f, innerH - panelH);
                _canvas.OnGUI(new Rect(centerX, bodyY, centerW, gridH), _session);
                if (panelH > 0f)
                    _topSection.OnGUI(new Rect(centerX, bodyY + innerH - panelH, centerW, panelH), _session);

                // Splitter between grid and the relocated spool panel.
                if (panelH > 0f)
                    HandleBottomSplitter(centerX, bodyY + innerH - panelH, centerW, minPanelH, maxPanelH, bodyY, innerH);

                // _bottomSection stays empty in this layout — nothing to draw.
            }
            else
            {
                // Classic layout: top → grid → bottom. The bottom section reserves
                // at least its PreferredHeight; the user can drag the splitter to
                // give it more room (persisted via EditorPrefs). The canvas takes
                // whatever's left between the two sections and scrolls internally
                // if its content exceeds that space. The top section soaks up any
                // leftover height (preserves Yarn's bottom-anchored grid).
                float minTopH = _topSection.PreferredHeight;
                float minBotH = _bottomSection.PreferredHeight;

                // Bottom: PreferredHeight as floor; user can drag larger (capped so
                // the grid keeps at least ~100px and 1 row of cells).
                float maxBotH = Mathf.Max(minBotH, innerH - 100f);
                float botH    = 0f;
                if (minBotH > 0f)
                {
                    botH = (_bottomSectionOverrideH > 0f)
                        ? Mathf.Clamp(_bottomSectionOverrideH, minBotH, maxBotH)
                        : minBotH;
                }

                float remaining = innerH - botH;
                float topH      = (canvasH + minTopH <= remaining)
                    ? Mathf.Max(0f, remaining - canvasH)
                    : minTopH;
                topH = Mathf.Clamp(topH, 0f, remaining);

                if (topH > 0f)
                    _topSection.OnGUI(new Rect(centerX, bodyY, centerW, topH), _session);
                _canvas.OnGUI(new Rect(centerX, bodyY + topH, centerW, innerH - topH - botH), _session);
                if (botH > 0f)
                    _bottomSection.OnGUI(new Rect(centerX, bodyY + innerH - botH, centerW, botH), _session);

                // Splitter between canvas and bottom section (only when both exist).
                if (botH > 0f)
                    HandleBottomSplitter(centerX, bodyY + innerH - botH, centerW, minBotH, maxBotH, bodyY, innerH);
            }

            // ── Right column: validation + summary [+ autofill] [+ profile panel] ──
            float rightX = w - RightW + 1f;
            bool  showAutofill = _profile?.LevelAnalyzer != null;

            // Optional game-specific right-panel section (Layer 2 provides it via
            // GameProfile). Lazily instantiated and cached per profile.
            if (!ReferenceEquals(_rightPanelProfile, _profile))
            {
                _rightPanel = _profile != null ? _profile.CreateRightPanel() : null;
                _rightPanelProfile = _profile;
            }
            bool  showRightPanel = _rightPanel != null;
            float rightPanelH = showRightPanel ? Mathf.Min(_rightPanel.PreferredHeight, innerH * 0.45f) : 0f;
            float upperH = innerH - rightPanelH; // region shared by validation/summary/autofill

            float validRatio   = showAutofill ? 0.40f : 0.50f;
            float summaryRatio = showAutofill ? 0.30f : 0.50f;

            float validH   = Mathf.Floor(upperH * validRatio);
            float summaryH = Mathf.Floor(upperH * summaryRatio);
            float autoH    = showAutofill ? upperH - validH - summaryH : 0f;

            var clicked = _validation.OnGUI(new Rect(rightX, bodyY, RightW, validH), _session.LastValidation);
            if (clicked.HasValue) _session.SelectedCell = clicked;

            float summaryY = bodyY + validH;
            EditorGUI.DrawRect(new Rect(rightX, summaryY, RightW, 1f), Divider);
            var summaryRect = new Rect(rightX, summaryY + 1f, RightW, summaryH - 1f);
            if (_session.MultiSelection.Count > 0)
                _multiSelect.OnGUI(summaryRect, _session);
            else
                _summary.OnGUI(summaryRect, _session);

            if (showAutofill)
            {
                float autoY = bodyY + validH + summaryH;
                EditorGUI.DrawRect(new Rect(rightX, autoY, RightW, 1f), Divider);
                _autofill.OnGUI(new Rect(rightX, autoY + 1f, RightW, autoH - 1f), _session, _profile);
            }

            if (showRightPanel)
            {
                float rpY = bodyY + upperH;
                EditorGUI.DrawRect(new Rect(rightX, rpY, RightW, 1f), Divider);
                _rightPanel.OnGUI(new Rect(rightX, rpY + 1f, RightW, rightPanelH - 1f), _session, _profile);
            }

            if (GUI.changed) Repaint();
        }

        private void DrawStatusBar(Rect rect)
        {
            float x  = rect.x + 8f;
            float y  = rect.y + (rect.height - EditorGUIUtility.singleLineHeight) * 0.5f;
            float lh = EditorGUIUtility.singleLineHeight;

            if (_session == null)
            {
                GUI.Label(new Rect(x, y, rect.width - 16f, lh),
                    "Open or create a level to begin.", EditorStyles.miniLabel);
                return;
            }

            if (_session.IsDirty)
            {
                var old = GUI.contentColor;
                GUI.contentColor = DirtyAmber;
                GUI.Label(new Rect(x, y, 62f, lh),
                    new GUIContent("● Unsaved", "Level has unsaved changes"),
                    EditorStyles.miniLabel);
                GUI.contentColor = old;
                x += 68f;
            }

            var hover = _canvas.HoverCell;
            if (hover.HasValue)
            {
                GUI.Label(new Rect(x, y, 72f, lh),
                    new GUIContent($"({hover.Value.X}, {hover.Value.Y})", "Cursor cell"),
                    EditorStyles.miniLabel);
                x += 78f;
            }

            GUI.Label(new Rect(x, y, 240f, lh),
                new GUIContent($"schema: {_session.Document.SchemaVersion}", "Schema version"),
                EditorStyles.miniLabel);
        }

        // Drawn with absolute rects (no GUILayout) on purpose: the New/Open buttons
        // invoke modal dialogs (DisplayDialog / OpenFilePanel), which raise an
        // ExitGUIException mid-OnGUI. If this screen used GUILayout BeginArea/Begin*
        // groups, that exception would unwind past their End* calls and corrupt the
        // layout-group stack — surfacing next event as "EndLayoutGroup: BeginLayoutGroup
        // must be called first" / "Stack empty" at EndArea. Absolute rects have no such
        // stack, so the dialogs are safe.
        private void DrawProfileSelector(Rect rect)
        {
            EditorGUI.DrawRect(rect, CanvasBg);

            const float colW   = 300f;
            const float titleH = 24f;
            const float gap    = 8f;
            const float btnH   = 24f;
            const float btnGap = 4f;
            float       fieldH = EditorGUIUtility.singleLineHeight;

            float totalH = titleH + gap + fieldH + gap + btnH + btnGap + btnH;
            float x = rect.x + (rect.width  - colW)   * 0.5f;
            float y = rect.y + (rect.height - totalH) * 0.5f;

            GUI.Label(new Rect(x, y, colW, titleH), "Level Editor", EditorStyles.largeLabel);
            y += titleH + gap;

            EditorGUI.BeginChangeCheck();
            _profile = (GameProfile)EditorGUI.ObjectField(
                new Rect(x, y, colW, fieldH), "Game Profile", _profile, typeof(GameProfile), false);
            if (EditorGUI.EndChangeCheck()) SaveProfilePref();
            y += fieldH + gap;

            using (new EditorGUI.DisabledGroupScope(_profile == null))
            {
                if (GUI.Button(new Rect(x, y, colW, btnH),
                    new GUIContent("New Level", "Create a new empty level with this profile")))
                    HandleNew();
                y += btnH + btnGap;
                if (GUI.Button(new Rect(x, y, colW, btnH),
                    new GUIContent("Open Level…", "Open an existing level JSON file")))
                    HandleOpen();
            }
        }

        private void HandleNew()
        {
            if (_profile == null && !TryAutoPickProfile()) return;
            if (!ConfirmDiscard()) return;

            NewLevelDialog.Show(_profile.GridWidth, _profile.GridHeight, (w, h) =>
            {
                _session?.Dispose();
                _session       = LevelEditorSession.CreateEmpty(_profile, w, h);
                _openedForeignFormat = false; // native editor level
                _topSection    = _profile.CreateTopSection();
                _bottomSection = _profile.CreateBottomSection();
                AutoActivateCellType();
                _session.RunValidation();
                Repaint();
            });
        }

        private void HandleOpen()
        {
            if (_profile == null && !TryAutoPickProfile()) return;
            if (!ConfirmDiscard()) return;
            string startDir = EditorPrefs.GetString(LastDirPrefKey, Application.dataPath);
            string path = EditorUtility.OpenFilePanel("Open Level", startDir, "json");
            if (!string.IsNullOrEmpty(path))
            {
                EditorPrefs.SetString(LastDirPrefKey, Path.GetDirectoryName(path));
                LoadFromPath(path);
            }
        }

        private void HandleSave()
        {
            if (_session == null) return;
            if (string.IsNullOrEmpty(_session.FilePath)) { HandleSaveAs(); return; }
            SaveToPath(_session.FilePath);
        }

        private void HandleSaveAs()
        {
            if (_session == null) return;
            string defaultName = string.IsNullOrEmpty(_session.FilePath)
                ? _session.Document.LevelId + ".json"
                : Path.GetFileName(_session.FilePath);
            string startDir = string.IsNullOrEmpty(_session.FilePath)
                ? EditorPrefs.GetString(LastDirPrefKey, Application.dataPath)
                : Path.GetDirectoryName(_session.FilePath);
            string path = EditorUtility.SaveFilePanel("Save Level As", startDir, defaultName, "json");
            if (!string.IsNullOrEmpty(path))
            {
                EditorPrefs.SetString(LastDirPrefKey, Path.GetDirectoryName(path));
                SaveToPath(path);
            }
        }

        private void HandleExport()
        {
            if (_session == null) return;

            var exporters = _profile.Exporters;
            if (exporters.Count == 0)
            {
                EditorUtility.DisplayDialog("No Exporters",
                    "No exporters are configured in this Game Profile.\n\nAdd a LevelExporterAsset to the profile's Exporters list.", "OK");
                return;
            }

            if (_session.IsDirty)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Unsaved Changes",
                    "The level has unsaved changes. Save before exporting?",
                    "Save & Export", "Export Anyway", "Cancel");
                if (choice == 2) return;
                if (choice == 0) HandleSave();
                if (_session == null) return;
            }

            if (string.IsNullOrEmpty(_session.FilePath))
            {
                EditorUtility.DisplayDialog("Save Required",
                    "Save the level to a file before exporting.\n\n" +
                    "Use Save As and give it a numbered filename (e.g. level_005.json). " +
                    "The number is used to determine the level's slot in the output.", "OK");
                return;
            }

            int successCount = 0;
            var errors = new System.Text.StringBuilder();
            foreach (var exporter in exporters)
            {
                if (exporter == null) continue;
                try
                {
                    bool ok = exporter.Export(_session.Document, _session.CellTypes, _session.FilePath ?? string.Empty);
                    if (ok) successCount++;
                    else    errors.AppendLine($"• {exporter.Name}: returned false");
                }
                catch (System.Exception ex)
                {
                    errors.AppendLine($"• {exporter.Name}: {ex.Message}");
                }
            }

            if (errors.Length > 0)
                EditorUtility.DisplayDialog("Export Failed", errors.ToString().TrimEnd(), "OK");
            else
                EditorUtility.DisplayDialog("Export Complete",
                    $"{successCount} exporter(s) completed successfully.", "OK");

            AssetDatabase.Refresh();
        }

        private void HandleUndo()        { if (_session?.Undo() == true) Repaint(); }
        private void HandleRedo()        { if (_session?.Redo() == true) Repaint(); }
        private void HandleOrderToggle()
        {
            _inOrderMode = !_inOrderMode;
            if (_inOrderMode && _inGeneratorMode) { _inGeneratorMode = false; _generator.OnExitMode(); }
            if (_inOrderMode && _inImageMode)     { _inImageMode = false;     _imagePanel.OnExitMode(); }
            Repaint();
        }

        private void HandleGenerateToggle()
        {
            if (_profile?.LevelGenerator == null) return;
            _inGeneratorMode = !_inGeneratorMode;
            if (_inGeneratorMode)
            {
                if (_inOrderMode) _inOrderMode = false;
                if (_inImageMode) { _inImageMode = false; _imagePanel.OnExitMode(); }
                _generator.OnEnterMode();
            }
            else
            {
                _generator.OnExitMode();
            }
            Repaint();
        }

        private void HandleImageToggle()
        {
            if (_profile?.ImageToGrid == null) return;
            _inImageMode = !_inImageMode;
            if (_inImageMode)
            {
                if (_inOrderMode) _inOrderMode = false;
                if (_inGeneratorMode) { _inGeneratorMode = false; _generator.OnExitMode(); }
                _imagePanel.OnEnterMode();
            }
            else
            {
                _imagePanel.OnExitMode();
            }
            Repaint();
        }

        private void HandleGeneratorUseLevel(LevelDocument doc)
        {
            if (doc == null || _profile == null) return;
            if (!ConfirmDiscard()) return;

            _session?.Dispose();
            _session          = new LevelEditorSession(_profile, doc);
            _session.FilePath = null;
            _openedForeignFormat = false; // freshly generated native level
            _session.MarkDirty();
            _topSection       = _profile.CreateTopSection();
            _bottomSection    = _profile.CreateBottomSection();
            AutoActivateCellType();
            _session.RunValidation();

            _inGeneratorMode = false;
            _generator.OnExitMode();
            _inImageMode = false;
            _imagePanel.OnExitMode();
            Repaint();
        }

        private void HandleTestPlay()
        {
            HandleSave();
            if (_session != null && !EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
        }

        private void LoadFromPath(string path)
        {
            try
            {
                var json = File.ReadAllText(path);

                // Auto-detect a foreign game format: if a profile importer recognizes the
                // file, load through it (and remember so Save round-trips via the exporter).
                LevelDocument doc = null;
                _openedForeignFormat = false;
                foreach (var importer in _profile.Importers)
                {
                    if (importer == null || !importer.CanImport(json)) continue;
                    doc = importer.Import(json, _profile.BuildRegistry());
                    _openedForeignFormat = true;
                    break;
                }
                if (doc == null)
                    doc = new JsonLevelSerializer().Load(json, _profile.BuildRegistry());

                _session?.Dispose();
                _session          = new LevelEditorSession(_profile, doc);
                _session.FilePath = path;
                _topSection       = _profile.CreateTopSection();
                _bottomSection    = _profile.CreateBottomSection();
                AutoActivateCellType();
                _session.RunValidation();
                Repaint();
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Open Failed", ex.Message, "OK");
            }
        }

        private void SaveToPath(string path)
        {
            try
            {
                _session.RunValidation();
                if (_session.LastValidation?.HasErrors == true)
                {
                    bool proceed = EditorUtility.DisplayDialog(
                        "Validation Errors",
                        "The level has validation errors. Save anyway?",
                        "Save Anyway", "Cancel");
                    if (!proceed) return;
                }

                // Foreign-format levels (opened via an importer) must NOT be overwritten
                // with the editor's internal LevelDocument JSON — that would corrupt the
                // game file. Round-trip through the exporters only.
                if (!_openedForeignFormat)
                    new JsonExporter().Export(_session.Document, _session.CellTypes, path);
                foreach (var exporter in _profile.Exporters)
                    exporter?.Export(_session.Document, _session.CellTypes, path);

                _session.FilePath = path;
                _session.MarkClean();
                AssetDatabase.Refresh();
                Repaint();
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Save Failed", ex.Message, "OK");
            }
        }

        private void AutoActivateCellType()
        {
            if (_session == null || _profile.CellTypes.Count == 0) return;
            int idx = _profile.CellTypes.Count > 1 ? 1 : 0;
            var def = _profile.CellTypes[idx];
            _session.ActiveCellType = def;
            _session.BrushTemplate  = def.CreateDefault();
        }

        private bool TryAutoPickProfile()
        {
            var guids = AssetDatabase.FindAssets("t:GameProfile");
            if (guids.Length == 1)
            {
                _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
                SaveProfilePref();
                return true;
            }
            EditorUtility.DisplayDialog("No Profile Selected",
                "Assign a Game Profile in the editor first.", "OK");
            return false;
        }

        private void SaveProfilePref()
        {
            if (_profile == null) { EditorPrefs.DeleteKey(ProfileGuidPrefKey); return; }
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_profile));
            EditorPrefs.SetString(ProfileGuidPrefKey, guid);
        }

        private bool ConfirmDiscard()
        {
            if (_session == null || !_session.IsDirty) return true;
            return EditorUtility.DisplayDialog("Unsaved Changes",
                "You have unsaved changes. Discard them?", "Discard", "Cancel");
        }

        // Draws a thin draggable splitter on the boundary between the canvas
        // and the bottom-section panel. Drag to resize; persists in EditorPrefs.
        // Hit area is wider than the visible line so it's easy to grab.
        private void HandleBottomSplitter(float centerX, float boundaryY, float centerW,
            float minBotH, float maxBotH, float bodyY, float innerH)
        {
            var hitRect = new Rect(centerX, boundaryY - SplitterHitH * 0.5f,
                centerW, SplitterHitH);
            var visRect = new Rect(centerX, boundaryY - SplitterVisH * 0.5f,
                centerW, SplitterVisH);

            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeVertical);

            bool isHover = hitRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(visRect, (isHover || _isDraggingBotSplitter) ? SplitterHover : SplitterIdle);

            var e = Event.current;
            if (!_isDraggingBotSplitter
                && e.type == EventType.MouseDown && e.button == 0 && isHover)
            {
                _isDraggingBotSplitter = true;
                e.Use();
            }
            else if (_isDraggingBotSplitter)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float newBotH = bodyY + innerH - e.mousePosition.y;
                    _bottomSectionOverrideH = Mathf.Clamp(newBotH, minBotH, maxBotH);
                    EditorPrefs.SetFloat(BottomHPrefKey, _bottomSectionOverrideH);
                    Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDraggingBotSplitter = false;
                    e.Use();
                }
            }
        }

        private static void DrawCenteredMessage(Rect rect, string message)
        {
            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(message, EditorStyles.wordWrappedLabel, GUILayout.MaxWidth(400f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }
    }
}
