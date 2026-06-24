using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Hoppa.YAK.Editor
{
    // Thin OpenAI Images transport. Builds the request body (pure), constructs the
    // UnityWebRequest, and parses the response into PNG bytes or a follow-up url.
    // Makes no decisions about WHETHER to call or WHAT to write — the window does that.
    public static class YAKOpenAIImageClient
    {
        public const string Endpoint = "https://api.openai.com/v1/images/generations";

        public static string BuildRequestJson(string prompt, string model, string size, string quality)
        {
            var o = new JObject
            {
                ["model"]   = model,
                ["prompt"]  = prompt,
                ["n"]       = 1,
                ["size"]    = string.IsNullOrEmpty(size) ? "1024x1024" : size,
            };
            if (!string.IsNullOrEmpty(quality)) o["quality"] = quality;
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static UnityWebRequest CreateRequest(string json, string apiKey)
        {
            var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return req;
        }

        // Returns true if a usable result was extracted (png OR url). On failure,
        // returns false and sets `error`.
        public static bool TryReadResult(UnityWebRequest req, out byte[] png, out string url, out string error)
        {
            png = null; url = null; error = null;
            if (req.result != UnityWebRequest.Result.Success)
            {
                error = $"HTTP {req.responseCode}: {req.error}. {Truncate(req.downloadHandler?.text, 300)}";
                return false;
            }
            try
            {
                var root = JObject.Parse(req.downloadHandler.text);
                var first = root["data"]?[0];
                if (first == null) { error = "Response had no data[0]."; return false; }

                string b64 = (string)first["b64_json"];
                if (!string.IsNullOrEmpty(b64)) { png = Convert.FromBase64String(b64); return true; }

                string u = (string)first["url"];
                if (!string.IsNullOrEmpty(u)) { url = u; return true; }

                error = "Response had neither b64_json nor url.";
                return false;
            }
            catch (Exception e) { error = "Parse error: " + e.Message; return false; }
        }

        private static string Truncate(string s, int n)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
    }
}
