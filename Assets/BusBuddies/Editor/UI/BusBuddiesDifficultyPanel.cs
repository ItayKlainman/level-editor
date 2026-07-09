using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Bus Buddies per-level Difficulty panel. Rendered in the right column of the
    // Level Editor (beneath Spool Analysis) via the generic ProfileRightPanel hook.
    // The six designer knobs are bound to the open level's GameData: they load on
    // Open and are written back on Apply / Auto-fill. Measured APS is shown as a
    // read-only number after an auto-fill (it does NOT gate the fill).
    //
    // GUI is not unit-tested; the state<->GameData round-trip (LoadFrom / WriteTo)
    // is (BusBuddiesDifficultyPanelTests).
    public sealed class BusBuddiesDifficultyPanel : ProfileRightPanel
    {
        private BusBuddiesDifficultySettings _settings = new BusBuddiesDifficultySettings();
        private LevelDocument       _boundDoc;
        private LevelAnalysisResult _lastAnalysis;
        private string              _status;

        public override float PreferredHeight => 250f;

        // Current knob state (exposed for tests + Apply).
        public BusBuddiesDifficultySettings Settings
        {
            get => _settings;
            set => _settings = value ?? new BusBuddiesDifficultySettings();
        }

        // Load knob state from a level's GameData (config supplies missing defaults).
        public void LoadFrom(LevelDocument doc, BusBuddiesAutofillConfig cfg)
        {
            _settings = BusBuddiesDifficultySettings.ReadFrom(doc, cfg);
            _boundDoc = doc;
        }

        // Persist knob state into a level's GameData.
        public void WriteTo(LevelDocument doc) => _settings.WriteTo(doc);

        public override void OnGUI(Rect rect, LevelEditorSession session, GameProfile profile)
        {
            var completer = profile?.LevelCompleter as BusBuddiesAutofiller;
            var cfg = completer != null ? completer.Config : null;
            var doc = session?.Document;

            // Rebind (reload knobs) whenever the open document changes.
            if (doc != null && !ReferenceEquals(doc, _boundDoc)) LoadFrom(doc, cfg);

            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f));
            GUILayout.Label("Difficulty", EditorStyles.boldLabel);

            _settings.BusesChunks      = EditorGUILayout.IntSlider("Buses Chunks", _settings.BusesChunks, 1, 5);
            _settings.DeviationPercent = EditorGUILayout.Slider("Deviation %", _settings.DeviationPercent, 0f, 1f);
            _settings.Columns          = EditorGUILayout.IntSlider("Columns", _settings.Columns, 1, 5);
            _settings.Difficulty       = EditorGUILayout.IntSlider("Difficulty", _settings.Difficulty, 1, 5);
            _settings.NoSingleBusColor = EditorGUILayout.ToggleLeft("No 1-bus color", _settings.NoSingleBusColor);
            _settings.RoundToFive      = EditorGUILayout.ToggleLeft("Round to 5", _settings.RoundToFive);

            // Live readout: avg px/bus + estimated total buses.
            int chunksBase = cfg != null ? cfg.ChunksBase : 10;
            int chunksStep = cfg != null ? cfg.ChunksStep : 5;
            int avg = BusBuddiesCapacityMath.Avg(_settings.BusesChunks, chunksBase, chunksStep);
            int totalPixels = CountColoredCells(doc);
            int estBuses = BusBuddiesCapacityMath.EstimateBusCount(totalPixels, avg);
            EditorGUILayout.LabelField($"avg {avg} px/bus · ~{estBuses} buses ({totalPixels} px)",
                EditorStyles.miniLabel);

            GUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(doc == null))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply")) OnApply(session);
                using (new EditorGUI.DisabledScope(completer == null))
                {
                    if (GUILayout.Button("Auto-fill")) OnAutofill(session, profile, completer);
                }
                GUILayout.EndHorizontal();
            }

            if (_lastAnalysis != null && _lastAnalysis.ApsEstimate > 0f)
                EditorGUILayout.LabelField(
                    $"Measured APS: {_lastAnalysis.ApsEstimate:0.0}{(_lastAnalysis.ApsCalibrated ? "" : " (uncal.)")}",
                    EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.LabelField(_status, EditorStyles.wordWrappedMiniLabel);

            GUILayout.EndArea();
        }

        private void OnApply(LevelEditorSession session)
        {
            var doc = session?.Document;
            if (doc == null) return;
            session.PushUndoSnapshot();
            WriteTo(doc);
            session.MarkDirty();
            _status = "Settings applied to level.";
        }

        private void OnAutofill(LevelEditorSession session, GameProfile profile, BusBuddiesAutofiller completer)
        {
            var doc = session?.Document;
            if (doc == null || completer == null) return;
            try
            {
                session.PushUndoSnapshot();
                WriteTo(doc); // settings transport: GameData, then autofill reads it back
                var res = completer.Complete(doc, profile, new CompletionRequest());
                _lastAnalysis = res?.Analysis;
                if (res?.TopSection != null)
                {
                    doc.TopSection = res.TopSection;
                    session.MarkDirty();
                    session.RunValidation();
                    _status = res.Succeeded ? "Auto-filled (solvable)." : "Auto-filled, best-effort: " + res.FailureReason;
                }
                else
                {
                    _status = "Auto-fill failed: " + (res?.FailureReason ?? "no top section produced");
                }
            }
            catch (System.Exception ex)
            {
                _status = "Auto-fill failed: " + ex.Message;
                Debug.LogError("BusBuddiesDifficultyPanel.OnAutofill: " + ex);
            }
        }

        private static int CountColoredCells(LevelDocument doc)
        {
            if (doc?.Grid == null) return 0;
            int n = 0;
            foreach (var cell in doc.Grid.Cells)
                if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId)) n++;
            return n;
        }
    }
}
