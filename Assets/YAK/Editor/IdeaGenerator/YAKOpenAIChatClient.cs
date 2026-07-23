using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Hoppa.YAK.Editor
{
    // Thin OpenAI Chat Completions transport for the Idea Generator. Builds the
    // request body (pure), constructs the UnityWebRequest, and reads the first
    // completion's content. Mirrors YAKOpenAIImageClient's split.
    public static class YAKOpenAIChatClient
    {
        public const string Endpoint = "https://api.openai.com/v1/chat/completions";

        public static string BuildChatRequestJson(string systemPrompt, string userPrompt, string model)
        {
            var o = new JObject
            {
                ["model"] = model,
                ["messages"] = new JArray {
                    new JObject { ["role"]="system", ["content"]=systemPrompt },
                    new JObject { ["role"]="user",   ["content"]=userPrompt },
                },
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static UnityWebRequest CreateRequest(string json, string apiKey)
        {
            var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return req;
        }

        public static bool TryReadContent(UnityWebRequest req, out string content, out string error)
        {
            content = null; error = null;
            if (req.result != UnityWebRequest.Result.Success)
            {
                var body = req.downloadHandler?.text;
                error = $"HTTP {req.responseCode}: {req.error}. {(body != null && body.Length > 300 ? body.Substring(0,300) : body)}";
                return false;
            }
            try
            {
                var o = JObject.Parse(req.downloadHandler.text);
                content = (string)o["choices"]?[0]?["message"]?["content"];
                if (string.IsNullOrEmpty(content)) { error = "Empty completion."; return false; }
                return true;
            }
            catch (System.Exception ex) { error = "Parse error: " + ex.Message; return false; }
        }
    }
}
