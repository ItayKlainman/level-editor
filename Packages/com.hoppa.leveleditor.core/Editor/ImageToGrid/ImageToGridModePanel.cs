using System;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // IMGUI panel for 🖼 Image mode (analogous to GeneratorModePanel). Left column:
    // a source-texture field, the converter asset's own inspector (Advanced), a
    // Convert button, and Use This Level. The remainder previews the converted
    // document with the profile's section panels + grid canvas.
    public sealed class ImageToGridModePanel
    {
        public event Action<LevelDocument> OnUseLevel;

        private const float ParamsW = 280f;
        private const float HeaderH = 22f;
        private const float RowH    = 20f;
        private const float Pad     = 8f;
        private const float ButtonH = 24f;

        private static readonly Color ParamsBg   = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color PreviewBg  = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color Divider    = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color DiagColor  = new Color(0.70f, 0.75f, 0.85f);
        private static readonly Color ErrorColor = new Color(1.00f, 0.45f, 0.40f);

        private Texture2D _source;
        private bool      _showAdvanced = true;
        private Vector2   _paramScroll;
        private string    _diag;
        private bool      _lastOk;

        private LevelEditorSession _previewSession;
        private TopSectionPanel    _previewTopSection;
        private TopSectionPanel    _previewBottomSection;
        private readonly GridCanvasPanel _previewCanvas = new GridCanvasPanel();

        private UnityEditor.Editor _configEditor;
        private ScriptableObject   _configEditorTarget;

        public void OnEnterMode() { DisposePreview(); _diag = null; }
        public void OnExitMode()  { DisposePreview(); }

        public void OnGUI(Rect rect, GameProfile profile)
        {
            if (profile == null)
            {
                EditorGUI.DrawRect(rect, PreviewBg);
                GUI.Label(rect, "Select a Game Profile first.", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (profile.ImageToGrid == null)
            {
                EditorGUI.DrawRect(rect, PreviewBg);
                GUI.Label(rect, "This Game Profile has no Image→Grid converter assigned.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var paramsRect  = new Rect(rect.x, rect.y, ParamsW, rect.height);
            var dividerRect = new Rect(rect.x + ParamsW, rect.y, 1f, rect.height);
            var previewRect = new Rect(rect.x + ParamsW + 1f, rect.y, rect.width - ParamsW - 1f, rect.height);

            EditorGUI.DrawRect(paramsRect,  ParamsBg);
            EditorGUI.DrawRect(dividerRect, Divider);
            EditorGUI.DrawRect(previewRect, PreviewBg);

            DrawParams(paramsRect, profile);
            DrawPreview(previewRect, profile);
        }

        private void DrawParams(Rect rect, GameProfile profile)
        {
            float scrollH     = Mathf.Max(900f, ImageToGridAssetEditor.LastContentHeight + 320f);
            var scrollContent = new Rect(0f, 0f, rect.width - 16f, scrollH);
            _paramScroll = GUI.BeginScrollView(rect, _paramScroll, scrollContent);

            float y = Pad;
            float w = scrollContent.width - Pad * 2f;

            GUI.Label(new Rect(Pad, y, w, HeaderH),
                new GUIContent("Image → Grid", "Turn a source picture into a level grid."),
                EditorStyles.boldLabel);
            y += HeaderH + 4f;

            GUI.Label(new Rect(Pad, y, w, RowH),
                new GUIContent("Source image", "The picture to convert — drag one in or click to pick."),
                EditorStyles.miniLabel);
            y += RowH;
            _source = (Texture2D)EditorGUI.ObjectField(
                new Rect(Pad, y, w, 64f), _source, typeof(Texture2D), false);
            y += 64f + 6f;

            // Advanced — the converter asset's own inspector (color cap, neutrals, …).
            _showAdvanced = EditorGUI.Foldout(new Rect(Pad, y, w, RowH), _showAdvanced,
                new GUIContent("Advanced", "Conversion settings: colors, background, outline, segmentation, sampling, and remaps."), true);
            y += RowH + 2f;
            if (_showAdvanced)
            {
                EnsureConfigEditor(profile.ImageToGrid);
                ImageToGridAssetEditor.ActivePalette = profile.ColorPalette; // publish for the remap dropdown
                // Size the embedded inspector to the measured content so the remap
                // list + buttons aren't clipped. Floor handles the first frame
                // (LastContentHeight == 0 until the inspector has drawn once).
                float advH  = Mathf.Max(200f, ImageToGridAssetEditor.LastContentHeight + 8f);
                var advRect = new Rect(Pad, y, w, advH);
                GUILayout.BeginArea(advRect);
                if (_configEditor != null) _configEditor.OnInspectorGUI();
                GUILayout.EndArea();
                y += advRect.height + 4f;
            }

            using (new EditorGUI.DisabledGroupScope(_source == null))
            {
                if (GUI.Button(new Rect(Pad, y, w, ButtonH),
                        new GUIContent("Convert", "Convert the source image into a level grid")))
                    Convert(profile);
            }
            y += ButtonH + 4f;

            using (new EditorGUI.DisabledGroupScope(_previewSession == null))
            {
                if (GUI.Button(new Rect(Pad, y, w, ButtonH),
                        new GUIContent("Use This Level", "Open this converted grid in the normal editor as an unsaved document")))
                    OnUseLevel?.Invoke(_previewSession.Document);
            }
            y += ButtonH + 8f;

            if (!string.IsNullOrEmpty(_diag))
            {
                var prev = GUI.color;
                GUI.color = _lastOk ? DiagColor : ErrorColor;
                GUI.Label(new Rect(Pad, y, w, RowH * 3f), _diag, EditorStyles.wordWrappedMiniLabel);
                GUI.color = prev;
            }

            GUI.EndScrollView();
        }

        private void DrawPreview(Rect rect, GameProfile profile)
        {
            if (_previewSession?.Document == null)
            {
                GUI.Label(rect, "Pick a source image and press Convert.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            float canvasH = _previewCanvas.RequiredHeight(rect.width, _previewSession);
            float minTopH = _previewTopSection?.PreferredHeight ?? 0f;
            float minBotH = _previewBottomSection?.PreferredHeight ?? 0f;

            float botH      = minBotH;
            float remaining = rect.height - botH;
            float topH      = (canvasH + minTopH <= remaining) ? Mathf.Max(0f, remaining - canvasH) : minTopH;
            topH = Mathf.Clamp(topH, 0f, remaining);

            if (topH > 0f && _previewTopSection != null)
                _previewTopSection.OnGUI(new Rect(rect.x, rect.y, rect.width, topH), _previewSession);
            _previewCanvas.OnGUI(new Rect(rect.x, rect.y + topH, rect.width, rect.height - topH - botH), _previewSession);
            if (botH > 0f && _previewBottomSection != null)
                _previewBottomSection.OnGUI(new Rect(rect.x, rect.y + rect.height - botH, rect.width, botH), _previewSession);
        }

        private void Convert(GameProfile profile)
        {
            if (profile?.ImageToGrid == null || _source == null) return;
            DisposePreview();
            try
            {
                var doc = profile.ImageToGrid.Convert(_source, profile);
                if (doc?.Grid != null)
                {
                    _previewSession       = new LevelEditorSession(profile, doc);
                    _previewTopSection    = profile.CreateTopSection();
                    _previewBottomSection = profile.CreateBottomSection();
                    _previewSession.RunValidation();
                    _lastOk = true;
                    _diag   = $"Converted: {doc.Grid.Width}×{doc.Grid.Height} · {DistinctColors(doc)} colors";
                }
                else
                {
                    _lastOk = false;
                    _diag   = "Conversion produced no grid.";
                }
            }
            catch (Exception ex)
            {
                _lastOk = false;
                _diag   = "Convert failed: " + ex.Message;
                Debug.LogError("ImageToGridModePanel.Convert: " + ex);
            }
        }

        private static int DistinctColors(LevelDocument doc)
        {
            var set = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if (doc.Grid?.Cells != null)
                foreach (var cell in doc.Grid.Cells)
                    if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId)) set.Add(c.ColorId);
            return set.Count;
        }

        private void EnsureConfigEditor(ScriptableObject config)
        {
            if (config == _configEditorTarget && _configEditor != null) return;
            if (_configEditor != null) UnityEngine.Object.DestroyImmediate(_configEditor);
            _configEditor       = UnityEditor.Editor.CreateEditor(config);
            _configEditorTarget = config;
        }

        private void DisposePreview()
        {
            _previewSession?.Dispose();
            _previewSession       = null;
            _previewTopSection    = null;
            _previewBottomSection = null;
        }
    }
}
