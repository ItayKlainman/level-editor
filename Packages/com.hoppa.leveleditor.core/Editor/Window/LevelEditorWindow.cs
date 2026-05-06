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

        private readonly PalettePanel      _palette     = new PalettePanel();
        private readonly GridCanvasPanel   _canvas      = new GridCanvasPanel();
        private readonly ToolbarPanel      _toolbar     = new ToolbarPanel();
        private readonly SummaryPanel      _summary     = new SummaryPanel();
        private readonly ValidationPanel   _validation  = new ValidationPanel();
        private readonly MultiSelectPanel  _multiSelect = new MultiSelectPanel();
        private TopSectionPanel _topSection = new EmptyTopSectionPanel();

        private LevelEditorSession _session;
        [SerializeField] private GameProfile _profile;
        private bool               _inOrderMode;

        private const string LastDirPrefKey = "Hoppa.LevelEditor.LastSaveDir";

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
            _toolbar.OnNew         += HandleNew;
            _toolbar.OnOpen        += HandleOpen;
            _toolbar.OnSave        += HandleSave;
            _toolbar.OnSaveAs      += HandleSaveAs;
            _toolbar.OnExport      += HandleExport;
            _toolbar.OnUndo        += HandleUndo;
            _toolbar.OnRedo        += HandleRedo;
            _toolbar.OnTestPlay    += HandleTestPlay;
            _toolbar.OnOrderToggle += HandleOrderToggle;
        }

        private void OnDisable()
        {
            _toolbar.OnNew         -= HandleNew;
            _toolbar.OnOpen        -= HandleOpen;
            _toolbar.OnSave        -= HandleSave;
            _toolbar.OnSaveAs      -= HandleSaveAs;
            _toolbar.OnExport      -= HandleExport;
            _toolbar.OnUndo        -= HandleUndo;
            _toolbar.OnRedo        -= HandleRedo;
            _toolbar.OnTestPlay    -= HandleTestPlay;
            _toolbar.OnOrderToggle -= HandleOrderToggle;
            _session?.Dispose();
            _session    = null;
            _topSection = new EmptyTopSectionPanel();
        }

        private void OnGUI()
        {
            float w = position.width;
            float h = position.height;

            // ── Toolbar ───────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(0f, 0f, w, ToolbarH), ToolbarBg);
            EditorGUI.DrawRect(new Rect(0f, ToolbarH - 1f, w, 1f), Divider);
            _toolbar.OrderMode = _inOrderMode;
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

            // ── Left: palette ─────────────────────────────────────────
            _palette.OnGUI(new Rect(0f, bodyY, PaletteW, innerH), _session);

            // ── Centre: top section + canvas ──────────────────────────
            float centerX = PaletteW + 1f;
            float centerW = w - PaletteW - RightW - 2f;
            float topH    = _topSection.PreferredHeight;
            if (topH > 0f)
                _topSection.OnGUI(new Rect(centerX, bodyY, centerW, topH), _session);
            _canvas.OnGUI(new Rect(centerX, bodyY + topH, centerW, innerH - topH), _session);

            // ── Right: validation (50%) + summary (50%) ──────────────────
            float rightX   = w - RightW + 1f;
            float validH   = Mathf.Floor(innerH * 0.50f);
            float summaryH = innerH - validH;

            var clicked = _validation.OnGUI(new Rect(rightX, bodyY, RightW, validH), _session.LastValidation);
            if (clicked.HasValue) _session.SelectedCell = clicked;

            float summaryY = bodyY + validH;
            EditorGUI.DrawRect(new Rect(rightX, summaryY, RightW, 1f), Divider);
            var summaryRect = new Rect(rightX, summaryY + 1f, RightW, summaryH - 1f);
            if (_session.MultiSelection.Count > 0)
                _multiSelect.OnGUI(summaryRect, _session);
            else
                _summary.OnGUI(summaryRect, _session);

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

        private void DrawProfileSelector(Rect rect)
        {
            EditorGUI.DrawRect(rect, CanvasBg);
            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical(GUILayout.Width(300f));
            GUILayout.Label("Level Editor", EditorStyles.largeLabel);
            GUILayout.Space(8f);
            _profile = (GameProfile)EditorGUILayout.ObjectField(
                "Game Profile", _profile, typeof(GameProfile), false);
            GUILayout.Space(8f);
            using (new EditorGUI.DisabledGroupScope(_profile == null))
            {
                if (GUILayout.Button(new GUIContent("New Level", "Create a new empty level with this profile")))
                    HandleNew();
                GUILayout.Space(4f);
                if (GUILayout.Button(new GUIContent("Open Level…", "Open an existing level JSON file")))
                    HandleOpen();
            }
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        private void HandleNew()
        {
            if (_profile == null && !TryAutoPickProfile()) return;
            if (!ConfirmDiscard()) return;
            _session?.Dispose();
            _session    = LevelEditorSession.CreateEmpty(_profile);
            _topSection = _profile.CreateTopSection();
            AutoActivateCellType();
            _session.RunValidation();
            Repaint();
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
        private void HandleOrderToggle() { _inOrderMode = !_inOrderMode; Repaint(); }

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
                var doc  = new JsonLevelSerializer().Load(json, _profile.BuildRegistry());
                _session?.Dispose();
                _session          = new LevelEditorSession(_profile, doc);
                _session.FilePath = path;
                _topSection       = _profile.CreateTopSection();
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
                return true;
            }
            EditorUtility.DisplayDialog("No Profile Selected",
                "Assign a Game Profile in the editor first.", "OK");
            return false;
        }

        private bool ConfirmDiscard()
        {
            if (_session == null || !_session.IsDirty) return true;
            return EditorUtility.DisplayDialog("Unsaved Changes",
                "You have unsaved changes. Discard them?", "Discard", "Cancel");
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
