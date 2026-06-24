using System;
using UnityEditor;

namespace Hoppa.YAK.Editor
{
    // Resolves the OpenAI key without ever committing it: env var first
    // (works headless/CI), per-machine EditorPrefs fallback. Never on an asset.
    public static class YAKImageApiKey
    {
        public const string EnvVar = "OPENAI_API_KEY";
        private const string PrefKey = "Hoppa.YAK.OpenAIKey";

        public static string Resolve()
        {
            var env = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(env)) return env.Trim();
            var pref = EditorPrefs.GetString(PrefKey, "");
            return string.IsNullOrEmpty(pref) ? null : pref.Trim();
        }

        public static bool HasKey => !string.IsNullOrEmpty(Resolve());

        public static string Source()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVar))) return "env";
            if (!string.IsNullOrEmpty(EditorPrefs.GetString(PrefKey, ""))) return "EditorPrefs";
            return "none";
        }

        public static void SetEditorPrefKey(string key) => EditorPrefs.SetString(PrefKey, key ?? "");
        public static void ClearEditorPrefKey() => EditorPrefs.DeleteKey(PrefKey);
    }
}
