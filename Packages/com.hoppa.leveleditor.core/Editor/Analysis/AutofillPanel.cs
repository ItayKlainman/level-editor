using System;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Side panel for the right column of LevelEditorWindow. Renders only when
    // profile.LevelAnalyzer != null. Provides:
    //  - Conveyor capacity dropdown (24 / 30 / Custom)
    //  - Difficulty + Seed widgets (for the auto-fill button)
    //  - Analyze button (read-only — runs the analyzer on the current doc)
    //  - Auto-fill button (replaces the document's top section with a fresh fill)
    //  - Last-result block (win paths, solvable, candidates, elapsed)
    public sealed class AutofillPanel
    {
        private const float HeaderH = 22f;
        private const float RowH    = 20f;
        private const float ButtonH = 22f;
        private const float Pad     = 8f;

        private static readonly Color BlockBg     = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color SubtleText  = new Color(0.70f, 0.75f, 0.85f);
        private static readonly Color OkText      = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color ErrText     = new Color(1.00f, 0.45f, 0.40f);

        private int    _difficulty = 5;
        private int    _seed;
        private bool   _seedLocked;
        private int    _capacityChoice = 24;     // 24 / 30 / 0=custom
        private int    _customCapacity = 24;

        private LevelAnalysisResult   _lastAnalysis;
        private LevelCompletionResult _lastCompletion;
        private string                _statusMessage;

        public float PreferredHeight => 220f;

        public void OnGUI(Rect rect, LevelEditorSession session, GameProfile profile)
        {
            EditorGUI.DrawRect(rect, BlockBg);

            float y = rect.y + 4f;
            float w = rect.width - Pad * 2f;
            float x = rect.x + Pad;

            GUI.Label(new Rect(x, y, w, HeaderH), "Spool Analysis", EditorStyles.boldLabel);
            y += HeaderH;

            // Conveyor capacity dropdown
            int newChoice = EditorGUI.IntPopup(
                new Rect(x, y, w, RowH),
                "Conveyor",
                _capacityChoice,
                new[] { "24 (L1–15)", "30 (L16+)", "Custom…" },
                new[] { 24, 30, 0 });
            if (newChoice != _capacityChoice)
            {
                _capacityChoice = newChoice;
                if (newChoice != 0) _customCapacity = newChoice;
            }
            y += RowH + 2f;
            if (_capacityChoice == 0)
            {
                _customCapacity = Mathf.Max(1, EditorGUI.IntField(new Rect(x, y, w, RowH), "Custom slots", _customCapacity));
                y += RowH + 2f;
            }

            // Difficulty
            EditorGUI.LabelField(new Rect(x, y, 60f, RowH), "Difficulty", EditorStyles.miniLabel);
            _difficulty = (int)Mathf.Round(GUI.HorizontalSlider(
                new Rect(x + 60f, y + 4f, w - 60f - 26f, RowH),
                Mathf.Clamp(_difficulty, 1, 10), 1f, 10f));
            GUI.Label(new Rect(x + w - 22f, y, 22f, RowH), _difficulty.ToString(), EditorStyles.miniLabel);
            y += RowH + 2f;

            // Seed
            EditorGUI.LabelField(new Rect(x, y, 40f, RowH), "Seed", EditorStyles.miniLabel);
            float seedW = w - 40f - 28f - 28f - 4f;
            _seed = EditorGUI.IntField(new Rect(x + 40f, y, seedW, RowH), _seed);
            _seedLocked = GUI.Toggle(
                new Rect(x + 40f + seedW + 2f, y, 28f, RowH),
                _seedLocked, new GUIContent("\U0001f512", "Lock seed: subsequent auto-fills reproduce the same spool layout"),
                GUI.skin.button);
            if (GUI.Button(new Rect(x + 40f + seedW + 32f, y, 28f, RowH),
                    new GUIContent("\U0001f3b2", "Randomize seed")))
            {
                _seed = new System.Random().Next(1, int.MaxValue);
                _seedLocked = false;
            }
            y += RowH + 6f;

            // Buttons — Task 13 wires Analyze, Task 14 wires Auto-fill
            float halfW = (w - 4f) * 0.5f;
            using (new EditorGUI.DisabledGroupScope(profile?.LevelAnalyzer == null || session?.Document == null))
            {
                if (GUI.Button(new Rect(x, y, halfW, ButtonH), new GUIContent("Analyze",
                        "Run the analyzer on the current document. Does not modify the level.")))
                {
                    OnAnalyze(session, profile);
                }
            }
            using (new EditorGUI.DisabledGroupScope(profile?.LevelCompleter == null || session?.Document == null))
            {
                if (GUI.Button(new Rect(x + halfW + 4f, y, halfW, ButtonH), new GUIContent("Auto-fill",
                        "Replace the top section with a fresh spool layout targeted at the current Difficulty.")))
                {
                    OnAutofill(session, profile);
                }
            }
            y += ButtonH + 6f;

            // Status / result
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.Label(new Rect(x, y, w, RowH * 2f), _statusMessage, EditorStyles.wordWrappedMiniLabel);
                y += RowH * 2f;
            }

            if (_lastAnalysis != null)
            {
                var color = _lastAnalysis.Solvable ? OkText : ErrText;
                var savedColor = GUI.contentColor;
                GUI.contentColor = color;
                GUI.Label(new Rect(x, y, w, RowH * 3f),
                    _lastAnalysis.ToString(),
                    EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = savedColor;
            }
        }

        private void OnAnalyze(LevelEditorSession session, GameProfile profile)
        {
            _statusMessage = null;
            _lastCompletion = null;

            try
            {
                _lastAnalysis = profile.LevelAnalyzer.Analyze(session.Document, profile, new AnalysisRequest
                {
                    Mode = AnalysisMode.Count,
                    WinPathCap = 10_000,
                    TimeoutMs  = 5_000,
                    ConveyorCapacityOverride = ResolveCapacity(),
                });
            }
            catch (Exception ex)
            {
                _lastAnalysis = null;
                _statusMessage = "Analyze failed: " + ex.Message;
                Debug.LogError("AutofillPanel.OnAnalyze: " + ex);
            }
        }

        private void OnAutofill(LevelEditorSession session, GameProfile profile)
        {
            _statusMessage = null;
            try
            {
                int seed = _seedLocked && _seed != 0
                    ? _seed
                    : new System.Random().Next(1, int.MaxValue);
                if (!_seedLocked) _seed = seed;

                _lastCompletion = profile.LevelCompleter.Complete(session.Document, profile, new CompletionRequest
                {
                    Difficulty = _difficulty,
                    Seed = seed,
                    ConveyorCapacityOverride = ResolveCapacity(),
                });
                _lastAnalysis = _lastCompletion?.Analysis;

                if (_lastCompletion?.TopSection != null)
                {
                    session.PushUndoSnapshot();
                    session.Document.TopSection = _lastCompletion.TopSection;
                    session.MarkDirty();
                    session.RunValidation();

                    if (!_lastCompletion.Succeeded)
                        _statusMessage = "Couldn't hit Difficulty band — best candidate applied (Ctrl-Z to revert).";
                }
                else
                {
                    _statusMessage = "Auto-fill failed: " + (_lastCompletion?.FailureReason ?? "no top section produced");
                }
            }
            catch (Exception ex)
            {
                _statusMessage = "Auto-fill failed: " + ex.Message;
                Debug.LogError("AutofillPanel.OnAutofill: " + ex);
            }
        }

        private int ResolveCapacity() => _capacityChoice == 0 ? _customCapacity : _capacityChoice;
    }
}
