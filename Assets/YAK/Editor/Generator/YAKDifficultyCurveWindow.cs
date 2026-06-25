using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.YAK.Editor
{
    public sealed class YAKDifficultyCurveWindow : EditorWindow
    {
        private const string ProfilePath = "Assets/YAK/Data/Config/YAKProfile.asset";
        private YAKDifficultyCurveConfig _config;
        private Vector2 _scroll;
        private int _attemptsPerLevel = 30;

        [MenuItem("Window/Hoppa/YAK/Difficulty Curve")]
        public static void Open() => GetWindow<YAKDifficultyCurveWindow>("Difficulty Curve");

        private void OnGUI()
        {
            _config = (YAKDifficultyCurveConfig)EditorGUILayout.ObjectField(
                "Curve Config", _config, typeof(YAKDifficultyCurveConfig), false);
            if (_config == null)
            {
                EditorGUILayout.HelpBox("Assign or create a YAK Difficulty Curve config " +
                    "(Create ▸ Hoppa ▸ YAK ▸ Generator ▸ YAK Difficulty Curve).", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawPresets();
            EditorGUILayout.Space();
            DrawCurve();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_config.Summary(), EditorStyles.boldLabel);

            var errors = _config.Validate();
            using (new EditorGUI.DisabledScope(errors.Count > 0))
            {
                _attemptsPerLevel = EditorGUILayout.IntSlider("Attempts / level", _attemptsPerLevel, 1, 200);
                if (GUILayout.Button("Generate Curve", GUILayout.Height(30)))
                    Generate();
            }
            foreach (var e in errors) EditorGUILayout.HelpBox(e, MessageType.Error);
        }

        private void DrawPresets()
        {
            EditorGUILayout.LabelField("Tier Presets", EditorStyles.boldLabel);
            for (int i = 0; i < _config.Presets.Count; i++)
            {
                var p = _config.Presets[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                p.Name = EditorGUILayout.TextField(p.Name);
                if (GUILayout.Button("Duplicate", GUILayout.Width(80))) { _config.Duplicate(i); Dirty(); break; }
                if (GUILayout.Button("Delete", GUILayout.Width(60)))   { _config.DeletePreset(i); Dirty(); break; }
                EditorGUILayout.EndHorizontal();
                p.GridWidth     = EditorGUILayout.IntField("Grid Width", p.GridWidth);
                p.GridHeight    = EditorGUILayout.IntField("Grid Height", p.GridHeight);
                p.MaxColors     = EditorGUILayout.IntField("Max Colors", p.MaxColors);
                p.AvgCapacity   = EditorGUILayout.IntField("Avg Spool Capacity", p.AvgCapacity);
                p.CapacitySlack = EditorGUILayout.Slider("Capacity Slack", p.CapacitySlack, 0f, 1f);
                p.ConveyorSlots = EditorGUILayout.IntField("Conveyor Slots", p.ConveyorSlots);
                p.ColumnRange   = EditorGUILayout.Vector2IntField("Spool Columns [min,max]", p.ColumnRange);
                p.HiddenRatio   = EditorGUILayout.Slider(
                    new GUIContent("Hidden Spool %", "APS does not yet model hidden spools — manual difficulty until analyzer support lands."),
                    p.HiddenRatio, 0f, 1f);
                p.TargetAps     = EditorGUILayout.FloatField("Target APS", p.TargetAps);
                p.ApsTolerance  = EditorGUILayout.FloatField("APS Tolerance", p.ApsTolerance);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("New Tier")) { _config.Presets.Add(new TierPreset()); Dirty(); }
        }

        private void DrawCurve()
        {
            EditorGUILayout.LabelField("Curve (ordered)", EditorStyles.boldLabel);
            var names = _config.Presets.ConvertAll(p => p.Name).ToArray();
            for (int i = 0; i < _config.Curve.Count; i++)
            {
                var seg = _config.Curve[i];
                EditorGUILayout.BeginHorizontal();
                int idx = Mathf.Max(0, System.Array.IndexOf(names, seg.TierName));
                idx = EditorGUILayout.Popup(idx, names);
                if (names.Length > 0) seg.TierName = names[Mathf.Clamp(idx, 0, names.Length - 1)];
                seg.LevelCount = EditorGUILayout.IntField(seg.LevelCount, GUILayout.Width(60));
                if (GUILayout.Button("↑", GUILayout.Width(24)) && i > 0) { (_config.Curve[i-1], _config.Curve[i]) = (_config.Curve[i], _config.Curve[i-1]); Dirty(); }
                if (GUILayout.Button("↓", GUILayout.Width(24)) && i < _config.Curve.Count-1) { (_config.Curve[i+1], _config.Curve[i]) = (_config.Curve[i], _config.Curve[i+1]); Dirty(); }
                if (GUILayout.Button("✕", GUILayout.Width(24))) { _config.Curve.RemoveAt(i); Dirty(); break; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add Segment")) { _config.Curve.Add(new CurveSegment()); Dirty(); }
        }

        private void Dirty() { EditorUtility.SetDirty(_config); }

        private void Generate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            if (profile == null) { EditorUtility.DisplayDialog("YAK Curve", "Profile not found at " + ProfilePath, "OK"); return; }
            AssetDatabase.SaveAssets();
            string dir = YAKCurveBatchHarness.RunCurve(_config, profile, _attemptsPerLevel, null);
            if (dir != null)
            {
                var win = GetWindow<BatchReviewWindow>();   // open review on the staging dir
                win.Show();
                EditorUtility.RevealInFinder(dir);
            }
        }
    }
}
