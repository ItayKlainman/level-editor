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
        private readonly Dictionary<string, string> _status = new Dictionary<string, string>(); // idea -> status text
        private string _message;

        // run state (driven by Pump)
        private bool _running;
        private Queue<string> _queue;
        private int _done, _total, _failed;
        private string _current;
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
                if (GUILayout.Button("Scan Gaps")) ScanGaps();
                using (new EditorGUI.DisabledScope(_missing.Count == 0 || !YAKImageApiKey.HasKey))
                    if (GUILayout.Button($"Generate Missing ({_missing.Count})")) StartRun();
            }
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

        private void DrawStatusList()
        {
            if (_ideas.Count == 0) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var idea in _ideas)
            {
                _status.TryGetValue(idea, out var st);
                EditorGUILayout.LabelField(idea, st ?? "");
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
            _message = $"{_ideas.Count} ideas, {_missing.Count} missing.";
        }

        private void StartRun()
        {
            int n = Mathf.Min(_missing.Count, Mathf.Max(1, _config.MaxImagesPerRun));
            float est = n * _config.EstimatedUsdPerImage;
            if (!EditorUtility.DisplayDialog("Generate images",
                    $"Generate {n} image(s) with model '{_config.Model}'.\nEstimated cost ≈ ${est:0.00}.\n\nProceed?",
                    "Generate", "Cancel"))
                return;

            if (!Directory.Exists(_config.OutputFolder)) Directory.CreateDirectory(_config.OutputFolder);
            _queue = new Queue<string>();
            for (int i = 0; i < n; i++) _queue.Enqueue(_missing[i]);
            _total = n; _done = 0; _failed = 0; _current = null; _req = null; _retries = 0; _nextAttemptAt = 0; _fetchingUrl = false;
            _running = true;
            EditorApplication.update += Pump;
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
            Repaint();
        }

        private void Pump()
        {
            // Awaiting backoff window?
            if (_req == null && _current != null && EditorApplication.timeSinceStartup < _nextAttemptAt) return;

            // Start the next idea (or retry the current one).
            if (_req == null)
            {
                if (_current == null)
                {
                    if (_queue.Count == 0) { StopRun("done"); return; }
                    _current = _queue.Dequeue();
                    _retries = 0;
                    _fetchingUrl = false;
                    _status[_current] = "generating…";
                }
                string key = YAKImageApiKey.Resolve();
                string prompt = YAKImageLibraryCore.BuildPrompt(_current, WoolColorDescriptors(), _config.StylePreamble);
                string json = YAKOpenAIImageClient.BuildRequestJson(prompt, _config.Model, _config.ImageSize, _config.Quality);
                _req = YAKOpenAIImageClient.CreateRequest(json, key);
                _req.SendWebRequest();
                EditorUtility.DisplayProgressBar("Image Library", $"{_current} ({_done}/{_total})", _total == 0 ? 0 : (float)_done / _total);
                return;
            }

            if (!_req.isDone) return;

            // Second-stage url GET completed → bytes are the PNG.
            if (_fetchingUrl)
            {
                if (_req.result == UnityWebRequest.Result.Success) WritePng(_current, _req.downloadHandler.data);
                else FailCurrent($"url fetch HTTP {_req.responseCode}: {_req.error}");
                ClearReqAndAdvanceIfWritten();
                return;
            }

            if (YAKOpenAIImageClient.TryReadResult(_req, out var png, out var url, out var error))
            {
                if (png != null) { WritePng(_current, png); ClearReqAndAdvanceIfWritten(); return; }
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
                _status[_current] = $"retry {_retries} ({_req.responseCode})…";
                _req.Dispose(); _req = null;
                return;
            }
            FailCurrent(error);
            _req.Dispose(); _req = null;
            AdvanceCurrent();
        }

        private void WritePng(string idea, byte[] png)
        {
            string name = YAKImageLibraryCore.IdeaToFileName(idea);
            string finalPath = Path.Combine(_config.OutputFolder, name);
            string tmpPath = finalPath + ".tmp";
            File.WriteAllBytes(tmpPath, png);                 // temp then atomic rename
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);
            _status[idea] = "done";
            _done++;
        }

        private void FailCurrent(string error)
        {
            _status[_current] = "FAILED: " + error;
            _failed++;
        }

        private void ClearReqAndAdvanceIfWritten()
        {
            if (_req != null) { _req.Dispose(); _req = null; }
            AdvanceCurrent();
        }

        private void AdvanceCurrent()
        {
            _current = null; _fetchingUrl = false; _retries = 0;
            Repaint();
        }
    }
}
