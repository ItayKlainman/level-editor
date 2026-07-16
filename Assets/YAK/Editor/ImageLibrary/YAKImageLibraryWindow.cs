using System;
using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Hoppa.YAK.Editor
{
    // Window ▸ Hoppa ▸ YAK ▸ Image Library. Offline tool: reads the ideas asset,
    // builds palette-injected prompts, and fills the config's OutputFolder with one
    // PNG per missing idea via OpenAI. Generation runs off EditorApplication.update
    // (one request in flight) so the editor stays responsive and cancelable.
    public sealed class YAKImageLibraryWindow : EditorWindow
    {
        [MenuItem("Window/Hoppa/YAK/Image Library")]
        public static void Open() => GetWindow<YAKImageLibraryWindow>("Image Library");

        private const string ConfigGuidPref = "Hoppa.YAK.ImageLibrary.ConfigGuid";
        private const int MaxRetriesPerIdea = 4;

        private YAKImageLibraryConfig _config;
        private string _keyEntry = "";
        private Vector2 _scroll;
        private readonly List<string> _ideas = new List<string>();
        private readonly List<YAKImageLibraryCore.IdeaEntry> _entries = new List<YAKImageLibraryCore.IdeaEntry>(); // idea -> its prompt section
        private readonly List<string> _missing = new List<string>();
        private readonly HashSet<string> _selected = new HashSet<string>();                      // ideas chosen to generate
        private readonly Dictionary<string, string> _status = new Dictionary<string, string>(); // statusKey -> status text
        private string _message;

        // One unit of generation work — a fully-built prompt, its output filename, and
        // the key its status is displayed under. Keeps prompt-building out of the pump.
        private struct GenJob { public string Prompt; public string FileName; public string StatusKey; }

        // run state (driven by Pump)
        private bool _running;
        private Queue<GenJob> _queue;
        private int _done, _total, _failed;
        private GenJob _current;
        private bool _hasCurrent;
        private int _retries;
        private double _nextAttemptAt;
        private UnityWebRequest _req;
        private bool _fetchingUrl; // second-stage GET for a url response

        private void OnEnable()
        {
            var guid = EditorPrefs.GetString(ConfigGuidPref, "");
            if (!string.IsNullOrEmpty(guid))
                _config = AssetDatabase.LoadAssetAtPath<YAKImageLibraryConfig>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private void OnDisable() => StopRun("cancelled (window closed)");

        private void OnGUI()
        {
            using (var c = new EditorGUI.ChangeCheckScope())
            {
                _config = (YAKImageLibraryConfig)EditorGUILayout.ObjectField("Config", _config, typeof(YAKImageLibraryConfig), false);
                if (c.changed && _config != null &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_config, out var g, out long _))
                    EditorPrefs.SetString(ConfigGuidPref, g);
            }
            if (_config == null) { EditorGUILayout.HelpBox("Assign a YAKImageLibraryConfig asset.", MessageType.Info); return; }

            DrawKeyRow();

            using (new EditorGUI.DisabledScope(_running))
            {
                if (GUILayout.Button("Scan Ideas")) ScanGaps();
                if (_ideas.Count > 0)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Select Missing ({_missing.Count})")) SelectOnly(_missing);
                        if (GUILayout.Button($"Select All ({_ideas.Count})"))        SelectOnly(_ideas);
                        if (GUILayout.Button("Clear"))                               _selected.Clear();
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(new GUIContent("Sample 2 per prompt",
                                "Tick the first 2 ideas of every prompt section — a cheap 10-image check that exercises all five prompts")))
                            SelectSamplePerPrompt(2);
                        if (GUILayout.Button(new GUIContent("Select all tagged",
                                "Tick every idea that belongs to a prompt section (the full boss-brief batch)")))
                            SelectSamplePerPrompt(int.MaxValue);
                    }
                }
                using (new EditorGUI.DisabledScope(_selected.Count == 0 || !YAKImageApiKey.HasKey))
                    if (GUILayout.Button($"Generate Selected ({_selected.Count})")) StartRun();
            }

            DrawStyleRow();

            if (_running && GUILayout.Button("Cancel")) StopRun("cancelled");

            if (_total > 0)
                EditorGUILayout.LabelField($"Progress: {_done}/{_total} done, {_failed} failed");
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.None);

            DrawStatusList();
        }

        private void DrawKeyRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"API key: {YAKImageApiKey.Source()}", GUILayout.Width(160));
                _keyEntry = EditorGUILayout.PasswordField(_keyEntry);
                if (GUILayout.Button("Set", GUILayout.Width(50))) { YAKImageApiKey.SetEditorPrefKey(_keyEntry); _keyEntry = ""; }
                if (GUILayout.Button("Clear", GUILayout.Width(50))) YAKImageApiKey.ClearEditorPrefKey();
            }
        }

        // Replace the current selection with exactly the given ideas.
        private void SelectOnly(IEnumerable<string> ideas)
        {
            _selected.Clear();
            foreach (var idea in ideas) _selected.Add(idea);
        }

        // Each row shows the prompt that generates it, and a header opens every new
        // section — without it there is no way to tell which prompt owns an idea, and
        // picking a spread across prompts becomes guesswork.
        private void DrawStatusList()
        {
            if (_entries.Count == 0) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(_running))
            {
                string section = null;
                string batch = null;
                foreach (var e in _entries)
                {
                    string key = string.IsNullOrEmpty(e.StyleKey) ? "default" : e.StyleKey;
                    if (key != section)
                    {
                        section = key;
                        batch = null;   // batch grouping is scoped within a section
                        GUILayout.Space(4f);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"[{key}]", EditorStyles.boldLabel, GUILayout.Width(120));
                            if (GUILayout.Button("select section", EditorStyles.miniButton, GUILayout.Width(100)))
                                SelectSection(key);
                        }
                    }

                    // Batch divider: opens each "# @batch: <name>" group with a label + a
                    // one-click select (selects that batch across ALL sections, e.g. all 100 of batch 2).
                    string b = e.Batch ?? "";
                    if (!string.Equals(b, batch ?? ""))
                    {
                        batch = b;
                        if (!string.IsNullOrEmpty(b))
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Space(16f);
                                GUILayout.Label($"── batch {b} ──", EditorStyles.miniBoldLabel, GUILayout.Width(104));
                                if (GUILayout.Button($"select batch {b}", EditorStyles.miniButton, GUILayout.Width(110)))
                                    SelectBatch(b);
                            }
                    }

                    _status.TryGetValue(e.Idea, out var st);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool sel = _selected.Contains(e.Idea);
                        bool now = EditorGUILayout.ToggleLeft(e.Idea, sel, GUILayout.Width(320));
                        if (now != sel) { if (now) _selected.Add(e.Idea); else _selected.Remove(e.Idea); }
                        GUILayout.Label(string.IsNullOrEmpty(e.Batch) ? key : $"{key}·b{e.Batch}",
                            EditorStyles.miniLabel, GUILayout.Width(80));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(st ?? "", GUILayout.Width(90));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void SelectSection(string key)
        {
            _selected.Clear();
            foreach (var e in _entries)
                if (string.Equals(string.IsNullOrEmpty(e.StyleKey) ? "default" : e.StyleKey, key,
                        StringComparison.OrdinalIgnoreCase))
                    _selected.Add(e.Idea);
        }

        // Tick every idea tagged with the given "# @batch: <name>", across all sections
        // (e.g. all 100 of batch 2). The way to (re)generate just one batch.
        private void SelectBatch(string batch)
        {
            _selected.Clear();
            foreach (var e in _entries)
                if (!string.IsNullOrEmpty(e.Batch) &&
                    string.Equals(e.Batch, batch, StringComparison.OrdinalIgnoreCase))
                    _selected.Add(e.Idea);
        }

        // Ticks N ideas from every tagged prompt section — the way to sanity-check all
        // five prompts on a small paid run instead of hammering whichever section
        // happens to sit at the end of the file.
        private void SelectSamplePerPrompt(int perPrompt)
        {
            _selected.Clear();
            var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _entries)
            {
                if (string.IsNullOrEmpty(e.StyleKey)) continue;   // untagged/legacy ideas aren't part of the brief
                taken.TryGetValue(e.StyleKey, out int n);
                if (n >= perPrompt) continue;
                taken[e.StyleKey] = n + 1;
                _selected.Add(e.Idea);
            }
        }

        private List<string> WoolColorDescriptors()
        {
            var list = new List<string>();
            var pal = _config.Profile != null ? _config.Profile.ColorPalette : null;
            if (pal == null) return list;
            var excluded = new HashSet<string>(_config.ExcludedNeutralIds ?? new string[0]);
            foreach (var id in pal.ColorIds)
            {
                if (excluded.Contains(id)) continue;
                if (pal.TryGetColor(id, out var col))
                    list.Add($"{id} #{ColorUtility.ToHtmlStringRGB(col)}");
                else
                    list.Add(id);
            }
            return list;
        }

        private void ScanGaps()
        {
            _message = null;
            _status.Clear(); _ideas.Clear(); _entries.Clear(); _missing.Clear();
            if (_config.IdeasAsset == null) { _message = "Assign an Ideas text asset."; return; }

            _entries.AddRange(YAKImageLibraryCore.ParseIdeaEntries(_config.IdeasAsset.text));
            foreach (var e in _entries) _ideas.Add(e.Idea);
            var existing = Directory.Exists(_config.OutputFolder)
                ? Directory.GetFiles(_config.OutputFolder, "*.png")
                : new string[0];
            _missing.AddRange(YAKImageLibraryCore.FindMissing(_ideas, existing));
            var missingSet = new HashSet<string>(_missing);
            foreach (var idea in _ideas) _status[idea] = missingSet.Contains(idea) ? "missing" : "have";
            SelectOnly(_missing); // sensible default; user can adjust the checkboxes
            _message = $"{_ideas.Count} ideas, {_missing.Count} missing. Tick the ideas to (re)generate.";
        }

        // One job per ticked idea, in idea-file order (deterministic), regardless of tick
        // order. Each idea is built with ITS SECTION's prompt — the five prompts split
        // the idea set — plus the shared [rules] the converter depends on.
        private void StartRun()
        {
            var jobs = new List<GenJob>();
            var colors = WoolColorDescriptors();
            var blocks = StyleBlocks();
            foreach (var e in _entries)
            {
                if (!_selected.Contains(e.Idea)) continue;
                string style = YAKImageLibraryCore.ResolveStyle(blocks, e.StyleKey, _config.StylePreamble);
                jobs.Add(new GenJob
                {
                    Prompt    = YAKImageLibraryCore.BuildPrompt(e.Idea, colors, style, _config.PixelGridSize),
                    FileName  = YAKImageLibraryCore.IdeaToFileName(e.Idea),
                    StatusKey = e.Idea,
                });
            }
            if (jobs.Count > 0) BeginRun(jobs);
        }

        // Shared run kickoff: cost-confirm dialog, cap, queue, start the pump.
        private void BeginRun(List<GenJob> jobs)
        {
            int cap = Mathf.Max(1, _config.MaxImagesPerRun);
            int n = Mathf.Min(jobs.Count, cap);
            float est = n * _config.EstimatedUsdPerImage;
            string capNote = jobs.Count > n
                ? $"\n\nNote: {jobs.Count} requested but capped to {n} (MaxImagesPerRun)."
                : "";
            if (!EditorUtility.DisplayDialog("Generate images",
                    $"Generate {n} image(s) with model '{_config.Model}'.\nEstimated cost ≈ ${est:0.00}.{capNote}\n\nProceed?",
                    "Generate", "Cancel"))
                return;

            if (!Directory.Exists(_config.OutputFolder)) Directory.CreateDirectory(_config.OutputFolder);
            _queue = new Queue<GenJob>();
            for (int i = 0; i < n; i++) _queue.Enqueue(jobs[i]);
            _total = n; _done = 0; _failed = 0; _hasCurrent = false; _req = null; _retries = 0; _nextAttemptAt = 0; _fetchingUrl = false;
            _running = true;
            EditorApplication.update += Pump;
        }

        // The five prompts, by name, from the style-prompt asset. Empty when no asset is
        // assigned — every idea then falls back to the config's Style Preamble.
        private Dictionary<string, string> StyleBlocks()
        {
            return _config.StylePromptAsset != null
                ? YAKImageLibraryCore.ParseStyleBlocks(_config.StylePromptAsset.text)
                : new Dictionary<string, string>();
        }

        // Shows which prompt each idea section is bound to, and flags any section whose
        // tag names a block that doesn't exist — otherwise a typo'd tag would silently
        // generate 20 images with the wrong prompt.
        private void DrawStyleRow()
        {
            if (_ideas.Count == 0) return;
            var blocks = StyleBlocks();
            if (blocks.Count == 0)
            {
                EditorGUILayout.LabelField("Prompts", "none — using the config Style Preamble for every idea",
                    EditorStyles.miniLabel);
                return;
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _entries)
            {
                string key = string.IsNullOrEmpty(e.StyleKey) ? "default" : e.StyleKey;
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }

            foreach (var kv in counts)
            {
                bool known = blocks.ContainsKey(kv.Key) ||
                             (kv.Key == "default" && blocks.ContainsKey("default"));
                string label = known
                    ? $"[{kv.Key}] → {kv.Value} idea(s)"
                    : $"[{kv.Key}] → {kv.Value} idea(s)  ⚠ no such prompt — falls back to [default]";
                EditorGUILayout.LabelField(known ? "Prompt" : "Prompt ⚠", label, EditorStyles.miniLabel);
            }
        }

        private void StopRun(string why)
        {
            if (!_running && _req == null) return;
            _running = false;
            EditorApplication.update -= Pump;
            if (_req != null) { _req.Dispose(); _req = null; }
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrEmpty(why)) _message = $"{why}. {_done}/{_total} done, {_failed} failed.";
            AssetDatabase.Refresh();

            // Completion popup (only on a natural finish — not cancel / window-close).
            // Safe here: StopRun("done") is invoked from the EditorApplication.update
            // pump, never from inside OnGUI, so DisplayDialog can't unbalance IMGUI.
            if (why == "done")
            {
                string full = Path.GetFullPath(_config.OutputFolder);
                string failNote = _failed > 0 ? $" ({_failed} failed)" : "";
                EditorUtility.DisplayDialog(
                    "Image generation complete",
                    $"{_done} of {_total} image(s) generated{failNote}.\n\n" +
                    $"Saved to:\n{_config.OutputFolder}\n\n{full}",
                    "OK");
            }
            Repaint();
        }

        private void Pump()
        {
            // Awaiting backoff window?
            if (_req == null && _hasCurrent && EditorApplication.timeSinceStartup < _nextAttemptAt) return;

            // Start the next job (or retry the current one).
            if (_req == null)
            {
                if (!_hasCurrent)
                {
                    if (_queue.Count == 0) { StopRun("done"); return; }
                    _current = _queue.Dequeue();
                    _hasCurrent = true;
                    _retries = 0;
                    _fetchingUrl = false;
                    _status[_current.StatusKey] = "generating…";
                }
                string key = YAKImageApiKey.Resolve();
                string json = YAKOpenAIImageClient.BuildRequestJson(_current.Prompt, _config.Model, _config.ImageSize, _config.Quality, _config.Background);
                _req = YAKOpenAIImageClient.CreateRequest(json, key);
                _req.SendWebRequest();
                EditorUtility.DisplayProgressBar("Image Library", $"{_current.StatusKey} ({_done}/{_total})", _total == 0 ? 0 : (float)_done / _total);
                return;
            }

            if (!_req.isDone) return;

            // Second-stage url GET completed → bytes are the PNG.
            if (_fetchingUrl)
            {
                if (_req.result == UnityWebRequest.Result.Success) WritePng(_req.downloadHandler.data);
                else FailCurrent($"url fetch HTTP {_req.responseCode}: {_req.error}");
                ClearReqAndAdvanceIfWritten();
                return;
            }

            if (YAKOpenAIImageClient.TryReadResult(_req, out var png, out var url, out var error))
            {
                if (png != null) { WritePng(png); ClearReqAndAdvanceIfWritten(); return; }
                // url response → issue a follow-up GET on the next Pump tick
                _req.Dispose(); _req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET) { downloadHandler = new DownloadHandlerBuffer() };
                _fetchingUrl = true;
                _req.SendWebRequest();
                return;
            }

            // Retryable (429/5xx) → backoff; otherwise fail.
            bool retryable = _req.responseCode == 429 || (_req.responseCode >= 500 && _req.responseCode < 600);
            if (retryable && _retries < MaxRetriesPerIdea)
            {
                _retries++;
                _nextAttemptAt = EditorApplication.timeSinceStartup + Mathf.Pow(2, _retries) + UnityEngine.Random.value;
                _status[_current.StatusKey] = $"retry {_retries} ({_req.responseCode})…";
                _req.Dispose(); _req = null;
                return;
            }
            FailCurrent(error);
            _req.Dispose(); _req = null;
            AdvanceCurrent();
        }

        // Writes the PNG via temp-file → atomic rename. A disk/permission/move
        // failure must NOT propagate out of Pump: that would leave _current set
        // (with _req already disposed) and silently re-issue the PAID request on
        // the next tick. Instead we fail the job cleanly so the pump advances —
        // no IO retry loop, a bad write fails the job once and moves on.
        private void WritePng(byte[] png)
        {
            string finalPath = Path.Combine(_config.OutputFolder, _current.FileName);
            string tmpPath = finalPath + ".tmp";
            try
            {
                File.WriteAllBytes(tmpPath, png);                 // temp then atomic rename
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(tmpPath, finalPath);
                _status[_current.StatusKey] = "done";
                _done++;
            }
            catch (System.Exception e)
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort temp cleanup */ }
                FailCurrent("write error: " + e.Message);        // record FAILED; caller still advances
            }
        }

        private void FailCurrent(string error)
        {
            _status[_current.StatusKey] = "FAILED: " + error;
            _failed++;
        }

        private void ClearReqAndAdvanceIfWritten()
        {
            if (_req != null) { _req.Dispose(); _req = null; }
            AdvanceCurrent();
        }

        private void AdvanceCurrent()
        {
            _hasCurrent = false; _fetchingUrl = false; _retries = 0;
            Repaint();
        }
    }
}
