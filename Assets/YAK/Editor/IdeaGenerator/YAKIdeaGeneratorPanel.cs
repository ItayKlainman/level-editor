using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Hoppa.YAK.Editor
{
    // Second mode of the Level Editor's Generate tab (BB profile). Turns a
    // subject taxonomy (IdeaKnowledgeBase) into deduped, conversion-friendly idea
    // lines via the OpenAI chat API, then appends a "# @style: collectible" batch
    // block to ideas.txt on Export. All logic (prompt/parse/dedupe/export-block)
    // lives in the tested IdeaGeneratorCore — this file is IMGUI + request pump +
    // file IO only, mirroring the untested GeneratorModePanel/window pattern.
    public sealed class YAKIdeaGeneratorPanel : ProfileGeneratePanel
    {
        public override string Title => "Idea Generator";

        private const string KbProjectPath = "Assets/YAK/Data/IdeaKnowledgeBase.json";
        private const string DefaultIdeasPath = "Assets/YAK/SourceImages/ideas.txt";
        private static readonly int[] Amounts = { 5, 10, 20, 50, 100, 200 };
        private const string SystemPrompt =
            "You are a pixel-art collectible idea generator. Follow the user's rules exactly and output only the grouped idea lines.";

        private static readonly Color ParamsBg  = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color PreviewBg = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color Divider   = new Color(0.08f, 0.09f, 0.11f);

        private IdeaKnowledgeBase _kb;
        private bool   _kbLoaded;
        private string _kbError;
        private bool   _defaultsInit;

        private readonly HashSet<string> _selectedSubjects  = new HashSet<string>();
        private readonly HashSet<string> _selectedModifiers = new HashSet<string>();
        private readonly HashSet<string> _dropped           = new HashSet<string>(); // results the user unticked
        private int _amount = 20;

        private List<IdeaGeneratorCore.IdeaGroup> _results;
        private int    _dupeCount;
        private string _status = string.Empty;
        private bool   _inFlight;
        private UnityWebRequest _req;

        private List<string> _existingLines = new List<string>();
        private string _ideasPath;

        private Vector2 _subjScroll, _modScroll, _resultsScroll;

        public override void OnExitMode()
        {
            if (_req != null)
            {
                EditorApplication.update -= Pump;
                _req.Dispose();
                _req = null;
            }
            _inFlight = false;
        }

        // ── KB load ───────────────────────────────────────────────────────
        private void EnsureKb()
        {
            if (_kbLoaded) return;
            _kbLoaded = true;
            try
            {
                var json = File.ReadAllText(KbProjectPath);
                _kb = IdeaKnowledgeBase.Parse(json);
            }
            catch (Exception ex)
            {
                _kbError = ex.Message;
                _kb = new IdeaKnowledgeBase();
            }

            if (!_defaultsInit)
            {
                _defaultsInit = true;
                foreach (var s in _kb.Subjects) _selectedSubjects.Add(s.Name); // default: all subjects
                // modifiers default to none = "let the system decide"
            }
        }

        // ── Layout ─────────────────────────────────────────────────────────
        public override void OnGUI(Rect rect, GameProfile profile)
        {
            EnsureKb();

            const float leftW  = 210f;
            const float rightW = 240f;
            var leftRect   = new Rect(rect.x, rect.y, leftW, rect.height);
            var centerRect = new Rect(rect.x + leftW + 1f, rect.y, rect.width - leftW - rightW - 2f, rect.height);
            var rightRect  = new Rect(rect.xMax - rightW, rect.y, rightW, rect.height);

            EditorGUI.DrawRect(leftRect, ParamsBg);
            EditorGUI.DrawRect(new Rect(rect.x + leftW, rect.y, 1f, rect.height), Divider);
            EditorGUI.DrawRect(centerRect, PreviewBg);
            EditorGUI.DrawRect(new Rect(rect.xMax - rightW - 1f, rect.y, 1f, rect.height), Divider);
            EditorGUI.DrawRect(rightRect, ParamsBg);

            DrawLeft(leftRect, profile);
            DrawResults(centerRect);
            DrawSelectors(rightRect);
        }

        private void DrawLeft(Rect rect, GameProfile profile)
        {
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f));
            GUILayout.Label("Idea Generator", EditorStyles.boldLabel);
            GUILayout.Space(4f);

            int idx = Array.IndexOf(Amounts, _amount);
            if (idx < 0) idx = 2;
            idx = EditorGUILayout.Popup("Amount", idx, Amounts.Select(a => a.ToString()).ToArray());
            _amount = Amounts[idx];
            GUILayout.Space(6f);

            using (new EditorGUI.DisabledScope(_inFlight || _selectedSubjects.Count == 0))
                if (GUILayout.Button(_inFlight ? "Generating..." : "Generate", GUILayout.Height(24f)))
                    StartGenerate();

            GUILayout.Space(4f);
            int keptCount = KeptCount();
            using (new EditorGUI.DisabledScope(keptCount == 0))
                if (GUILayout.Button($"Export Ideas ({keptCount})", GUILayout.Height(24f)))
                    ExportIdeas();

            GUILayout.Space(8f);
            if (!string.IsNullOrEmpty(_kbError))
                EditorGUILayout.HelpBox("Knowledge Base load error: " + _kbError, MessageType.Warning);
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);

            GUILayout.FlexibleSpace();
            GUILayout.Label($"API key: {YAKImageApiKey.Source()}", EditorStyles.miniLabel);
            GUILayout.EndArea();
        }

        private void DrawSelectors(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 8f, rect.width - 12f, rect.height - 16f));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Subjects", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(36f)))
                {
                    _selectedSubjects.Clear();
                    foreach (var s in _kb.Subjects) _selectedSubjects.Add(s.Name);
                }
                if (GUILayout.Button("None", EditorStyles.miniButton, GUILayout.Width(44f)))
                    _selectedSubjects.Clear();
            }

            _subjScroll = GUILayout.BeginScrollView(_subjScroll, GUILayout.Height((rect.height - 80f) * 0.62f));
            foreach (var s in _kb.Subjects)
            {
                bool on  = _selectedSubjects.Contains(s.Name);
                bool now = EditorGUILayout.ToggleLeft(s.Name, on);
                if (now != on) { if (now) _selectedSubjects.Add(s.Name); else _selectedSubjects.Remove(s.Name); }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            GUILayout.Label("Modifiers (optional)", EditorStyles.boldLabel);
            _modScroll = GUILayout.BeginScrollView(_modScroll);
            foreach (var m in _kb.Modifiers)
            {
                bool on  = _selectedModifiers.Contains(m.Name);
                bool now = EditorGUILayout.ToggleLeft(new GUIContent(m.Name, m.Guidance), on);
                if (now != on) { if (now) _selectedModifiers.Add(m.Name); else _selectedModifiers.Remove(m.Name); }
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawResults(Rect rect)
        {
            if (_results == null || _results.Count == 0)
            {
                GUI.Label(rect, "Select subjects, then press Generate.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f));
            GUILayout.Label($"Results — {KeptCount()} kept ({_dupeCount} duplicates removed)", EditorStyles.boldLabel);
            _resultsScroll = GUILayout.BeginScrollView(_resultsScroll);
            foreach (var g in _results)
            {
                if (g.Ideas.Count == 0) continue;
                GUILayout.Space(4f);
                GUILayout.Label(g.Subject, EditorStyles.miniBoldLabel);
                foreach (var idea in g.Ideas)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(12f);
                        bool keep = !_dropped.Contains(idea);
                        bool now  = EditorGUILayout.ToggleLeft(idea, keep);
                        if (now != keep) { if (now) _dropped.Remove(idea); else _dropped.Add(idea); }
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── Generate + pump ─────────────────────────────────────────────────
        private void StartGenerate()
        {
            _status  = string.Empty;
            var cfg  = FindConfig();
            string model = cfg != null && !string.IsNullOrEmpty(cfg.TextModel) ? cfg.TextModel : "gpt-4o-mini";
            LoadExisting(cfg);

            string key = YAKImageApiKey.Resolve();
            if (string.IsNullOrEmpty(key)) { _status = "No OpenAI API key."; return; }

            var subjects  = _kb.Subjects.Select(s => s.Name).Where(_selectedSubjects.Contains).ToList();
            var modifiers = _kb.Modifiers.Select(m => m.Name).Where(_selectedModifiers.Contains).ToList();

            string user = IdeaGeneratorCore.BuildPrompt(_kb, subjects, modifiers, _amount, _existingLines);
            string json = YAKOpenAIChatClient.BuildChatRequestJson(SystemPrompt, user, model);

            _req = YAKOpenAIChatClient.CreateRequest(json, key);
            _req.SendWebRequest();
            _inFlight = true;
            _status   = "Generating...";
            _dropped.Clear();
            EditorApplication.update += Pump;
        }

        private void Pump()
        {
            if (_req == null) { EditorApplication.update -= Pump; return; }
            if (!_req.isDone) { RequestRepaint?.Invoke(); return; }

            EditorApplication.update -= Pump;
            _inFlight = false;

            if (YAKOpenAIChatClient.TryReadContent(_req, out var content, out var error))
            {
                var groups = IdeaGeneratorCore.ParseResponse(content);
                _results   = IdeaGeneratorCore.MarkAndFilterDuplicates(groups, _existingLines, out _dupeCount);
                int n = _results.Sum(g => g.Ideas.Count);
                _status = $"Generated {n} ideas ({_dupeCount} duplicates removed).";
            }
            else
            {
                _status = error;
            }

            _req.Dispose();
            _req = null;
            _dropped.Clear();
            RequestRepaint?.Invoke();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // ── Export ──────────────────────────────────────────────────────────
        private void ExportIdeas()
        {
            if (_results == null) return;
            if (string.IsNullOrEmpty(_ideasPath)) _ideasPath = ResolveIdeasPath(FindConfig());

            var kept = _results
                .Select(g => new IdeaGeneratorCore.IdeaGroup
                {
                    Subject = g.Subject,
                    Ideas   = g.Ideas.Where(i => !_dropped.Contains(i)).ToList(),
                })
                .Where(g => g.Ideas.Count > 0)
                .ToList();

            int k = kept.Sum(g => g.Ideas.Count);
            if (k == 0) { _status = "Nothing to export (all ideas unticked)."; return; }

            string raw;
            try { raw = File.Exists(_ideasPath) ? File.ReadAllText(_ideasPath) : string.Empty; }
            catch { raw = string.Empty; }

            int batch = IdeaGeneratorCore.NextBatchNumber(raw);
            string block = IdeaGeneratorCore.BuildAppendBlock(kept, batch);
            try
            {
                File.AppendAllText(_ideasPath, block);
                AssetDatabase.Refresh();
                _status  = $"Exported {k} ideas to ideas.txt (batch {batch}).";
                _results = null;
                _dropped.Clear();
            }
            catch (Exception ex) { _status = "Export failed: " + ex.Message; }
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private int KeptCount()
        {
            if (_results == null) return 0;
            return _results.Sum(g => g.Ideas.Count(i => !_dropped.Contains(i)));
        }

        private void LoadExisting(YAKImageLibraryConfig cfg)
        {
            _ideasPath = ResolveIdeasPath(cfg);
            string raw;
            try { raw = File.Exists(_ideasPath) ? File.ReadAllText(_ideasPath) : string.Empty; }
            catch { raw = string.Empty; }
            _existingLines = YAKImageLibraryCore.ParseIdeas(raw);
        }

        private static string ResolveIdeasPath(YAKImageLibraryConfig cfg)
        {
            if (cfg != null && cfg.IdeasAsset != null)
            {
                var p = AssetDatabase.GetAssetPath(cfg.IdeasAsset);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            return DefaultIdeasPath;
        }

        private static YAKImageLibraryConfig FindConfig()
        {
            var guids = AssetDatabase.FindAssets("t:YAKImageLibraryConfig");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<YAKImageLibraryConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
