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
        private static readonly Color BannerBg    = new Color(0.45f, 0.12f, 0.10f);

        private int    _difficulty = 5;
        private int    _seed;
        private bool   _seedLocked;
        private int    _capacityChoice = 24;     // 24 / 30 / 0=custom
        private int    _customCapacity = 24;

        private LevelAnalysisResult   _lastAnalysis;
        private LevelCompletionResult _lastCompletion;
        private string                _statusMessage;

        public float PreferredHeight => 300f;

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

            // Save Solution — find a winning sequence and write it to a text file.
            using (new EditorGUI.DisabledGroupScope(profile?.LevelAnalyzer == null || session?.Document == null))
            {
                if (GUI.Button(new Rect(x, y, w, ButtonH), new GUIContent("Save Solution…",
                        "Find a guaranteed winning tap sequence for the current level and save it to a .txt file.")))
                {
                    OnSaveSolution(session, profile);
                }
            }
            y += ButtonH + 6f;

            // Status / result
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.Label(new Rect(x, y, w, RowH * 2f), _statusMessage, EditorStyles.wordWrappedMiniLabel);
                y += RowH * 2f;
            }

            // Prominent unsolvable banner.
            if (_lastAnalysis != null && !_lastAnalysis.Solvable)
            {
                float bh = RowH * 2.4f;
                EditorGUI.DrawRect(new Rect(x, y, w, bh), BannerBg);
                var savedC = GUI.contentColor;
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(x + 6f, y + 3f, w - 12f, RowH), "⚠  NO SOLVABLE PATH FOUND", EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(_lastAnalysis.FailureReason))
                    GUI.Label(new Rect(x + 6f, y + RowH + 1f, w - 12f, RowH * 1.4f), _lastAnalysis.FailureReason, EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = savedC;
                y += bh + 4f;
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
                y += RowH * 3f;

                if (_lastCompletion != null)
                {
                    GUI.Label(new Rect(x, y, w, RowH),
                        $"{_lastCompletion.CandidatesTried} candidate(s) · {_lastCompletion.ElapsedMs} ms total",
                        EditorStyles.miniLabel);
                }
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
                    WinPathCap = 100_000,
                    TimeoutMs  = 5_000,
                    ConveyorCapacityOverride = ResolveCapacity(),
                    // Layer the imperfect-information win-rate onto the exact count so
                    // the result line reports a difficulty signal even when the count caps.
                    RolloutCount = 200,
                    PlayerLookahead = 4,
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

        private void OnSaveSolution(LevelEditorSession session, GameProfile profile)
        {
            _statusMessage = null;
            _lastCompletion = null;
            try
            {
                _lastAnalysis = profile.LevelAnalyzer.Analyze(session.Document, profile, new AnalysisRequest
                {
                    Mode = AnalysisMode.Solvable,
                    RecordSolution = true,
                    TimeoutMs = 10_000,
                    ConveyorCapacityOverride = ResolveCapacity(),
                });

                if (_lastAnalysis == null || !_lastAnalysis.Solvable
                    || _lastAnalysis.SolutionSteps == null || _lastAnalysis.SolutionSteps.Count == 0)
                {
                    // The red "no solvable path" banner renders automatically from _lastAnalysis.
                    _statusMessage = "No solvable path — nothing to save.";
                    return;
                }

                // Default filename matches the Summary panel: the saved level's
                // filename (doc.LevelId can be a stale template id like "level_001").
                string defaultId = !string.IsNullOrEmpty(session.FilePath)
                    ? System.IO.Path.GetFileNameWithoutExtension(session.FilePath)
                    : (string.IsNullOrEmpty(session.Document.LevelId) ? "level" : session.Document.LevelId);
                string path = EditorUtility.SaveFilePanel("Save Solution", Application.dataPath,
                    defaultId + ".solution.txt", "txt");
                if (string.IsNullOrEmpty(path)) return; // cancelled

                // Header level name comes from the file actually chosen, so it can
                // never disagree with its own filename (handles dialog renames too).
                string levelId = System.IO.Path.GetFileNameWithoutExtension(path); // e.g. "level_044.solution"
                const string solutionSuffix = ".solution";
                if (levelId.EndsWith(solutionSuffix, StringComparison.OrdinalIgnoreCase))
                    levelId = levelId.Substring(0, levelId.Length - solutionSuffix.Length);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Level {levelId} — solution ({_lastAnalysis.SolutionSteps.Count} steps)");
                sb.AppendLine();
                foreach (var step in _lastAnalysis.SolutionSteps) sb.AppendLine(step);
                System.IO.File.WriteAllText(path, sb.ToString());
                AssetDatabase.Refresh();

                _statusMessage = $"Saved {_lastAnalysis.SolutionSteps.Count}-step solution to {System.IO.Path.GetFileName(path)}.";
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex)
            {
                _statusMessage = "Save Solution failed: " + ex.Message;
                Debug.LogError("AutofillPanel.OnSaveSolution: " + ex);
            }
        }

        private int ResolveCapacity() => _capacityChoice == 0 ? _customCapacity : _capacityChoice;
    }
}
