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
        private readonly List<string> _missing = new List<string>();
        private readonly HashSet<string> _selected = new HashSet<string>();                      // ideas chosen to generate
        private readonly Dictionary<string, string> _status = new Dictionary<string, string>(); // statusKey -> status text
        private string _message;

        // Theme-batch state: parsed prompts, which are ticked, images-per-prompt, and
        // the status keys of the current/last batch (for the per-image progress list).
        private readonly List<string> _prompts = new List<string>();
        private readonly HashSet<int> _promptSelected = new HashSet<int>();
        private int _imagesPerPrompt = -1;   // -1 = init from config
        private bool _showPrompts = true;
        private readonly List<string> _promptRunKeys = new List<string>();

        // One unit of generation work — a fully-built prompt, its output filename, and
        // the key its status is displayed under. Both ideas- and prompts-mode enqueue
        // these, so the Pump/OpenAI/retry/save path is shared.
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
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Select Missing ({_missing.Count})")) SelectOnly(_missing);
                        if (GUILayout.Button($"Select All ({_ideas.Count})"))        SelectOnly(_ideas);
                        if (GUILayout.Button("Clear"))                               _selected.Clear();
                    }
                using (new EditorGUI.DisabledScope(_selected.Count == 0 || !YAKImageApiKey.HasKey))
                    if (GUILayout.Button($"Generate Selected ({_selected.Count})")) StartRun();
            }

            DrawPromptsSection();

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

        private void DrawStatusList()
        {
            if (_ideas.Count == 0) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(_running))
            {
                foreach (var idea in _ideas)
                {
                    _status.TryGetValue(idea, out var st);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool sel = _selected.Contains(idea);
                        bool now = EditorGUILayout.ToggleLeft(idea, sel, GUILayout.Width(320));
                        if (now != sel) { if (now) _selected.Add(idea); else _selected.Remove(idea); }
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(st ?? "", GUILayout.Width(90));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
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
            _status.Clear(); _ideas.Clear(); _missing.Clear();
            if (_config.IdeasAsset == null) { _message = "Assign an Ideas text asset."; return; }

            _ideas.AddRange(YAKImageLibraryCore.ParseIdeas(_config.IdeasAsset.text));
            var existing = Directory.Exists(_config.OutputFolder)
                ? Directory.GetFiles(_config.OutputFolder, "*.png")
                : new string[0];
            _missing.AddRange(YAKImageLibraryCore.FindMissing(_ideas, existing));
            var missingSet = new HashSet<string>(_missing);
            foreach (var idea in _ideas) _status[idea] = missingSet.Contains(idea) ? "missing" : "have";
            SelectOnly(_missing); // sensible default; user can adjust the checkboxes
            _message = $"{_ideas.Count} ideas, {_missing.Count} missing. Tick the ideas to (re)generate.";
        }

        // Ideas mode: one job per ticked idea, in idea-file order (deterministic),
        // regardless of tick order.
        private void StartRun()
        {
            var jobs = new List<GenJob>();
            var colors = WoolColorDescriptors();
            foreach (var idea in _ideas)
                if (_selected.Contains(idea))
                    jobs.Add(new GenJob
                    {
                        Prompt   = YAKImageLibraryCore.BuildPrompt(idea, colors, _config.StylePreamble),
                        FileName = YAKImageLibraryCore.IdeaToFileName(idea),
                        StatusKey = idea,
                    });
            if (jobs.Count > 0) BeginRun(jobs);
        }

        // Prompts mode: for each ticked theme, K jobs whose subject the model invents.
        // Each gets a unique filename token so re-runs accumulate new levels.
        private void StartPromptRun()
        {
            var colors = WoolColorDescriptors();
            int k = Mathf.Max(1, _imagesPerPrompt);
            var jobs = new List<GenJob>();
            _promptRunKeys.Clear();
            for (int pi = 0; pi < _prompts.Count; pi++)
            {
                if (!_promptSelected.Contains(pi)) continue;
                string theme = _prompts[pi];
                for (int j = 0; j < k; j++)
                {
                    string statusKey = $"P{pi + 1} · {ShortTheme(theme)} #{j + 1}";
                    _status[statusKey] = "queued";
                    _promptRunKeys.Add(statusKey);
                    jobs.Add(new GenJob
                    {
                        Prompt    = YAKImageLibraryCore.BuildThemePrompt(theme, colors, _config.ThemeStylePreamble),
                        FileName  = YAKImageLibraryCore.ThemeToFileName(theme, System.Guid.NewGuid().ToString("N")),
                        StatusKey = statusKey,
                    });
                }
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

        // First ~40 chars of a theme, for compact status-row labels.
        private static string ShortTheme(string theme)
        {
            string t = (theme ?? string.Empty).Trim();
            return t.Length <= 40 ? t : t.Substring(0, 40).TrimEnd() + "…";
        }

        private void DrawPromptsSection()
        {
            _showPrompts = EditorGUILayout.Foldout(_showPrompts, "Prompts (theme batches)", true);
            if (!_showPrompts) return;

            using (new EditorGUI.DisabledScope(_running))
            {
                if (_imagesPerPrompt < 0) _imagesPerPrompt = Mathf.Max(1, _config.ImagesPerPrompt);

                if (GUILayout.Button("Scan Prompts")) ScanPrompts();

                if (_prompts.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        _config.PromptsAsset == null
                            ? "Assign a Prompts text asset on the config to generate theme batches."
                            : "Press Scan Prompts to load the themes.",
                        MessageType.None);
                }
                else
                {
                    _imagesPerPrompt = Mathf.Max(1, EditorGUILayout.IntField("Images per prompt", _imagesPerPrompt));

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Select All ({_prompts.Count})"))
                            { _promptSelected.Clear(); for (int i = 0; i < _prompts.Count; i++) _promptSelected.Add(i); }
                        if (GUILayout.Button("Clear")) _promptSelected.Clear();
                    }

                    for (int i = 0; i < _prompts.Count; i++)
                    {
                        bool sel = _promptSelected.Contains(i);
                        bool now = EditorGUILayout.ToggleLeft(
                            new GUIContent($"P{i + 1} · {ShortTheme(_prompts[i])}", _prompts[i]), sel);
                        if (now != sel) { if (now) _promptSelected.Add(i); else _promptSelected.Remove(i); }
                    }

                    int n = _promptSelected.Count * Mathf.Max(1, _imagesPerPrompt);
                    using (new EditorGUI.DisabledScope(_promptSelected.Count == 0 || !YAKImageApiKey.HasKey))
                        if (GUILayout.Button($"Generate Batch ({_promptSelected.Count} × {Mathf.Max(1, _imagesPerPrompt)} = {n})"))
                            StartPromptRun();
                }

                // Per-image status for the current/last batch.
                foreach (var key in _promptRunKeys)
                {
                    _status.TryGetValue(key, out var st);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(key, GUILayout.Width(320));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(st ?? "", GUILayout.Width(90));
                    }
                }
            }
        }

        private void ScanPrompts()
        {
            _prompts.Clear(); _promptSelected.Clear(); _promptRunKeys.Clear();
            if (_config.PromptsAsset == null) { _message = "Assign a Prompts text asset on the config."; return; }
            _prompts.AddRange(YAKImageLibraryCore.ParsePrompts(_config.PromptsAsset.text));
            for (int i = 0; i < _prompts.Count; i++) _promptSelected.Add(i); // default: all ticked
            _message = $"{_prompts.Count} theme prompt(s). Set images-per-prompt and Generate Batch.";
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
                string json = YAKOpenAIImageClient.BuildRequestJson(_current.Prompt, _config.Model, _config.ImageSize, _config.Quality);
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
                _nextAttemptAt = EditorApplication.timeSinceStartup + Mathf.Pow(2, _retries) + Random.value;
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
