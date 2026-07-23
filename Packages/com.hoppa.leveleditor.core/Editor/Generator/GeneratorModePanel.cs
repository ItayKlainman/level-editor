using System;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // IMGUI panel that replaces the center/right area when the operator enters
    // generator mode (analogous to OrderPanel for ⇅ Order mode).
    //
    // Layout: ParamsW left column with Difficulty / Target APS / Seed / Advanced
    // foldout / Regenerate / Use This Level / diagnostics. The remainder is
    // preview — the active GameProfile's top-section panel above a GridCanvas
    // showing the latest candidate document.
    public sealed class GeneratorModePanel
    {
        public event Action<LevelDocument> OnUseLevel;

        // Raised when the profile-supplied generate panel wants the host window
        // to repaint (e.g. an async request finished). LevelEditorWindow wires
        // this to EditorWindow.Repaint.
        public event Action OnRequestRepaint;

        private enum SubMode { Level, Profile }
        private SubMode _subMode = SubMode.Level;
        private ProfileGeneratePanel _profilePanel;
        private GameProfile _profilePanelProfile;

        private const float ParamsW   = 280f;
        private const float HeaderH   = 22f;
        private const float RowH      = 20f;
        private const float Pad       = 8f;
        private const float ButtonH   = 24f;

        private static readonly Color ParamsBg   = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color PreviewBg  = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color Divider    = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color DiagColor  = new Color(0.70f, 0.75f, 0.85f);
        private static readonly Color ErrorColor = new Color(1.00f, 0.45f, 0.40f);

        private readonly LevelGeneratorRequest _request = new LevelGeneratorRequest
        {
            Difficulty = 5,
            TargetAPS  = null,
            Seed       = 0,
        };

        private bool   _seedLocked;
        private bool   _showAdvanced;
        private string _aps = string.Empty;
        private Vector2 _paramScroll;

        private LevelGeneratorResult _lastResult;
        private LevelEditorSession   _previewSession;
        private TopSectionPanel      _previewTopSection;
        private TopSectionPanel      _previewBottomSection;
        private readonly GridCanvasPanel _previewCanvas = new GridCanvasPanel();

        private UnityEditor.Editor _configEditor;
        private ScriptableObject   _configEditorTarget;

        // Called by LevelEditorWindow when entering generator mode. Lets us
        // dispose any stale preview session from a previous activation.
        public void OnEnterMode()
        {
            DisposePreview();
            _lastResult = null;
        }

        public void OnExitMode()
        {
            DisposePreview();
            _profilePanel?.OnExitMode();
        }

        public void OnGUI(Rect rect, GameProfile profile)
        {
            if (profile == null)
            {
                EditorGUI.DrawRect(rect, PreviewBg);
                GUI.Label(rect, "Select a Game Profile first.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // (Re)build the profile-supplied generate panel when the active
            // profile changes, and choose a sensible default sub-mode.
            if (!ReferenceEquals(_profilePanelProfile, profile))
            {
                _profilePanel?.OnExitMode();
                _profilePanel = profile.HasGeneratePanel ? profile.CreateGeneratePanel() : null;
                if (_profilePanel != null)
                {
                    _profilePanel.RequestRepaint = () => OnRequestRepaint?.Invoke();
                    _profilePanel.OnEnterMode();
                }
                _profilePanelProfile = profile;
                bool hasLevel = profile.LevelGenerator != null;
                _subMode = hasLevel ? SubMode.Level : SubMode.Profile;
            }

            bool hasLevelGen   = profile.LevelGenerator != null;
            bool hasProfileGen = _profilePanel != null;

            if (!hasLevelGen && !hasProfileGen)
            {
                EditorGUI.DrawRect(rect, PreviewBg);
                GUI.Label(rect, "This Game Profile has no Level Generator assigned.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Toggle row (only when both modes exist).
            var contentRect = rect;
            if (hasLevelGen && hasProfileGen)
            {
                var toggleRect = new Rect(rect.x, rect.y, rect.width, 22f);
                EditorGUI.DrawRect(toggleRect, ParamsBg);
                float halfW = rect.width * 0.5f;
                if (GUI.Toggle(new Rect(rect.x, rect.y, halfW, 22f), _subMode == SubMode.Level,
                        "Level Generator", EditorStyles.toolbarButton)) _subMode = SubMode.Level;
                if (GUI.Toggle(new Rect(rect.x + halfW, rect.y, halfW, 22f), _subMode == SubMode.Profile,
                        _profilePanel.Title, EditorStyles.toolbarButton)) _subMode = SubMode.Profile;
                contentRect = new Rect(rect.x, rect.y + 22f, rect.width, rect.height - 22f);
            }
            else if (hasProfileGen && !hasLevelGen)
            {
                _subMode = SubMode.Profile;
            }

            if (_subMode == SubMode.Profile && _profilePanel != null)
            {
                _profilePanel.OnGUI(contentRect, profile);
                return;
            }

            // ── Level generator: Left params + preview split ──────────────
            var paramsRect  = new Rect(contentRect.x, contentRect.y, ParamsW, contentRect.height);
            var dividerRect = new Rect(contentRect.x + ParamsW, contentRect.y, 1f, contentRect.height);
            var previewRect = new Rect(contentRect.x + ParamsW + 1f, contentRect.y,
                contentRect.width - ParamsW - 1f, contentRect.height);

            EditorGUI.DrawRect(paramsRect,  ParamsBg);
            EditorGUI.DrawRect(dividerRect, Divider);
            EditorGUI.DrawRect(previewRect, PreviewBg);

            DrawParams(paramsRect, profile);
            DrawPreview(previewRect, profile);
        }

        // ── Params column ─────────────────────────────────────────────────

        private void DrawParams(Rect rect, GameProfile profile)
        {
            // Scrollable inner area so the Advanced foldout's inspector doesn't
            // clip when it's tall.
            var innerH = ComputeInnerHeight(profile);
            var scrollContent = new Rect(0f, 0f, rect.width - 16f, innerH);
            _paramScroll = GUI.BeginScrollView(rect, _paramScroll, scrollContent);

            float y = Pad;
            float w = scrollContent.width - Pad * 2f;

            GUI.Label(new Rect(Pad, y, w, HeaderH), "Generator", EditorStyles.boldLabel);
            y += HeaderH + 4f;

            // Difficulty slider — 1..10.
            GUI.Label(new Rect(Pad, y, 70f, RowH), "Difficulty", EditorStyles.miniLabel);
            _request.Difficulty = (int)Mathf.Round(GUI.HorizontalSlider(
                new Rect(Pad + 70f, y + 4f, w - 70f - 30f, RowH),
                Mathf.Clamp(_request.Difficulty, 1f, 10f), 1f, 10f));
            GUI.Label(new Rect(Pad + w - 28f, y, 28f, RowH),
                _request.Difficulty.ToString(), EditorStyles.miniLabel);
            y += RowH + 4f;

            // Target APS — optional float; empty = null.
            GUI.Label(new Rect(Pad, y, 70f, RowH), "Target APS", EditorStyles.miniLabel);
            _aps = GUI.TextField(new Rect(Pad + 70f, y, w - 70f, RowH), _aps ?? string.Empty);
            if (string.IsNullOrWhiteSpace(_aps))
                _request.TargetAPS = null;
            else if (float.TryParse(_aps, out var parsed))
                _request.TargetAPS = parsed;
            y += RowH + 4f;

            // Seed: int field + lock toggle + 🎲 button.
            GUI.Label(new Rect(Pad, y, 70f, RowH), "Seed", EditorStyles.miniLabel);
            float seedW = w - 70f - 28f - 28f - 4f;
            _request.Seed = EditorGUI.IntField(
                new Rect(Pad + 70f, y, seedW, RowH), _request.Seed);
            _seedLocked = GUI.Toggle(
                new Rect(Pad + 70f + seedW + 2f, y, 28f, RowH),
                _seedLocked, new GUIContent("🔒", "Lock seed: subsequent regenerations reproduce the same level"),
                GUI.skin.button);
            if (GUI.Button(new Rect(Pad + 70f + seedW + 32f, y, 28f, RowH),
                    new GUIContent("🎲", "Randomize seed")))
            {
                _request.Seed = new System.Random().Next(1, int.MaxValue);
                _seedLocked   = false;
            }
            y += RowH + 8f;

            // Advanced foldout — Unity's default inspector for the game's config SO.
            if (profile.GeneratorConfig != null)
            {
                _showAdvanced = EditorGUI.Foldout(
                    new Rect(Pad, y, w, RowH), _showAdvanced, "Advanced", true);
                y += RowH + 2f;
                if (_showAdvanced)
                {
                    EnsureConfigEditor(profile.GeneratorConfig);
                    var advRect = new Rect(Pad, y, w, EstimateConfigEditorHeight());
                    GUILayout.BeginArea(advRect);
                    if (_configEditor != null)
                        _configEditor.OnInspectorGUI();
                    GUILayout.EndArea();
                    y += advRect.height + 4f;
                }
            }
            else
            {
                GUI.Label(new Rect(Pad, y, w, RowH),
                    "(no GeneratorConfig assigned)", EditorStyles.miniLabel);
                y += RowH + 4f;
            }

            y += 4f;

            // Regenerate.
            if (GUI.Button(new Rect(Pad, y, w, ButtonH), new GUIContent("Regenerate",
                    "Run the generator with the current parameters")))
            {
                Regenerate(profile);
            }
            y += ButtonH + 4f;

            // Use This Level (disabled until we have a candidate).
            using (new EditorGUI.DisabledGroupScope(_previewSession == null))
            {
                if (GUI.Button(new Rect(Pad, y, w, ButtonH), new GUIContent("Use This Level",
                        "Open this candidate in the normal editor as an unsaved document")))
                {
                    OnUseLevel?.Invoke(_previewSession.Document);
                }
            }
            y += ButtonH + 8f;

            // Diagnostics.
            if (_lastResult != null)
            {
                var prevColor = GUI.color;
                GUI.color = _lastResult.Succeeded ? DiagColor : ErrorColor;
                GUI.Label(new Rect(Pad, y, w, RowH * 3f),
                    _lastResult.ToString(),
                    EditorStyles.wordWrappedMiniLabel);
                GUI.color = prevColor;
            }

            GUI.EndScrollView();
        }

        private float ComputeInnerHeight(GameProfile profile)
        {
            // Conservative estimate; scroll covers the rest.
            float h = Pad + HeaderH + 4f
                    + (RowH + 4f) * 3   // difficulty, aps, seed
                    + 8f
                    + RowH + 2f         // advanced foldout label
                    + (_showAdvanced ? EstimateConfigEditorHeight() + 4f : 0f)
                    + 4f
                    + ButtonH + 4f      // regenerate
                    + ButtonH + 8f      // use this
                    + RowH * 3f;        // diagnostics
            return Mathf.Max(h, 200f);
        }

        private float EstimateConfigEditorHeight()
        {
            // We can't introspect the inspector's exact height without
            // measuring — use a generous fallback. The scroll view absorbs
            // the slack.
            return 360f;
        }

        private void EnsureConfigEditor(ScriptableObject config)
        {
            if (config == _configEditorTarget && _configEditor != null) return;
            if (_configEditor != null) UnityEngine.Object.DestroyImmediate(_configEditor);
            _configEditor       = UnityEditor.Editor.CreateEditor(config);
            _configEditorTarget = config;
        }

        // ── Preview ───────────────────────────────────────────────────────

        private void DrawPreview(Rect rect, GameProfile profile)
        {
            if (_previewSession?.Document == null)
            {
                GUI.Label(rect, "Press Regenerate to produce a candidate.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Mirror LevelEditorWindow's layout: canvas takes only the height
            // its grid requires, bottom section takes PreferredHeight, top
            // section absorbs whatever's left so spool columns expand to fill
            // the same vertical real estate as in normal edit mode.
            float canvasH = _previewCanvas.RequiredHeight(rect.width, _previewSession);
            float minTopH = _previewTopSection?.PreferredHeight ?? 0f;
            float minBotH = _previewBottomSection?.PreferredHeight ?? 0f;

            float botH      = minBotH;
            float remaining = rect.height - botH;
            float topH      = (canvasH + minTopH <= remaining)
                ? Mathf.Max(0f, remaining - canvasH)
                : minTopH;
            topH = Mathf.Clamp(topH, 0f, remaining);

            if (topH > 0f && _previewTopSection != null)
                _previewTopSection.OnGUI(
                    new Rect(rect.x, rect.y, rect.width, topH), _previewSession);
            _previewCanvas.OnGUI(
                new Rect(rect.x, rect.y + topH, rect.width, rect.height - topH - botH),
                _previewSession);
            if (botH > 0f && _previewBottomSection != null)
                _previewBottomSection.OnGUI(
                    new Rect(rect.x, rect.y + rect.height - botH, rect.width, botH),
                    _previewSession);
        }

        // ── Regenerate ────────────────────────────────────────────────────

        private void Regenerate(GameProfile profile)
        {
            if (profile?.LevelGenerator == null) return;

            // Wire AdvancedConfig from the profile so Layer 2 can read tuning.
            _request.AdvancedConfig = profile.GeneratorConfig;

            // Randomize seed if not locked.
            if (!_seedLocked)
                _request.Seed = new System.Random().Next(1, int.MaxValue);

            try
            {
                _lastResult = profile.LevelGenerator.Generate(_request, profile);
            }
            catch (Exception ex)
            {
                _lastResult = new LevelGeneratorResult
                {
                    Document  = null,
                    Succeeded = false,
                    SeedUsed  = _request.Seed,
                    ElapsedMs = 0,
                };
                Debug.LogError($"Level generator threw: {ex}");
            }

            DisposePreview();
            if (_lastResult?.Document != null)
            {
                _previewSession       = new LevelEditorSession(profile, _lastResult.Document);
                _previewTopSection    = profile.CreateTopSection();
                _previewBottomSection = profile.CreateBottomSection();
                _previewSession.RunValidation();
            }
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
