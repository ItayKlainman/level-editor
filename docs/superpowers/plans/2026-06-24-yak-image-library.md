# YAK Image Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an offline Unity Editor tool that auto-fills the YAK generator's `SourceImageFolder` with AI-generated, grid-friendly PNGs from a plain-text list of level ideas.

**Architecture:** A pure, unit-tested decision core (`YAKImageLibraryCore`: parse ideas / slug filenames / gap-diff / build prompt) wrapped by thin I/O edges (a config ScriptableObject, an API-key resolver, an OpenAI HTTP client, and an EditorWindow that drives generation off an `EditorApplication.update` pump). The tool is a pure *producer* into the image folder; the existing generator/batch consume the PNGs unchanged.

**Tech Stack:** Unity 6 Editor (C#), UnityWebRequest, Newtonsoft.Json (already in the project), NUnit EditMode tests, OpenAI Images API.

## Global Constraints

- All new code lives under `Assets/YAK/Editor/ImageLibrary/` in the **existing** `Hoppa.YAK.Editor` namespace and `YAK.Editor` asmdef. **No new asmdef.**
- Tests live under `Assets/YAK/Tests/Editor/` in namespace `Hoppa.YAK.Editor.Tests` (existing test asmdef already references `YAK.Editor`).
- **No secrets in committed assets:** the OpenAI key is read from env var `OPENAI_API_KEY` first, then `EditorPrefs`. Never serialize it on any ScriptableObject.
- **No absolute paths in committed assets:** the output folder is stored **project-relative** (e.g. `Assets/YAK/SourceImages`).
- **Model name is configurable** (default `gpt-image-1`) — it deprecates 2026-10-23.
- Do **not** modify `YAKLevelGenerator.cs`, `YAKImageToGrid.cs`, or `YAKBatchHarness.cs`.
- Palette is read via the public `GameProfile.ColorPalette` → `ColorPaletteAsset.ColorIds` / `TryGetColor(id, out Color)` surface. Do not reach into `YAKStaticManagerColorSource` directly.
- Run EditMode tests via the Unity MCP `tests-run` tool (assembly filter `YAK.Editor.Tests`), not a CLI.

---

### Task 1: Pure core — `ParseIdeas`

**Files:**
- Create: `Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs`
- Test: `Assets/YAK/Tests/Editor/YakImageLibraryTests.cs`

**Interfaces:**
- Produces: `static List<string> YAKImageLibraryCore.ParseIdeas(string raw)` — one idea per line; trims; skips blank lines and `#`-prefixed comment lines; de-dupes case-insensitively, preserving first-seen order.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.YAK.Editor;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakImageLibraryTests
    {
        [Test]
        public void ParseIdeas_TrimsSkipsCommentsAndDedupes()
        {
            string raw = "monkey eating banana\n  dolphin  \n\n# a comment\npopsicle\nDolphin\n";
            var ideas = YAKImageLibraryCore.ParseIdeas(raw);

            Assert.AreEqual(new List<string> { "monkey eating banana", "dolphin", "popsicle" }, ideas,
                "blank lines and # comments dropped; case-insensitive de-dupe keeps first 'dolphin'");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Use the Unity MCP `tests-run` tool: EditMode, assembly `YAK.Editor.Tests`, method `ParseIdeas_TrimsSkipsCommentsAndDedupes`.
Expected: FAIL — `YAKImageLibraryCore` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hoppa.YAK.Editor
{
    // Pure, Unity-free decision core for the Image Library tool. No network, no
    // disk, no EditorPrefs — everything here is unit-tested. The window/client do I/O.
    public static class YAKImageLibraryCore
    {
        // One idea per line; trims; skips blank + '#' comment lines; de-dupes
        // case-insensitively preserving first-seen order.
        public static List<string> ParseIdeas(string raw)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                if (seen.Add(t)) result.Add(t);
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run `tests-run` for the same method. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs Assets/YAK/Tests/Editor/YakImageLibraryTests.cs
git commit -m "feat(yak): image-library core — ParseIdeas"
```

---

### Task 2: Pure core — `IdeaToFileName` (deterministic, collision-safe slug)

**Files:**
- Modify: `Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs`
- Test: `Assets/YAK/Tests/Editor/YakImageLibraryTests.cs`

**Interfaces:**
- Produces: `static string YAKImageLibraryCore.IdeaToFileName(string idea)` — returns `<slug>_<hash8>.png`. Slug = lowercased, non-alphanumerics collapsed to single `-`, trimmed, capped at 40 chars, empty→`idea`. `hash8` = 8-hex FNV-1a of the trimmed idea. Deterministic; distinct ideas never collide (hash differs).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void IdeaToFileName_IsDeterministic_SlugifiesAndAvoidsCollisions()
        {
            string a1 = YAKImageLibraryCore.IdeaToFileName("Monkey eating a Banana!");
            string a2 = YAKImageLibraryCore.IdeaToFileName("Monkey eating a Banana!");
            string b  = YAKImageLibraryCore.IdeaToFileName("monkey eating a banana");

            Assert.AreEqual(a1, a2, "same idea -> same filename");
            Assert.AreNotEqual(a1, b, "different ideas -> different filenames (hash suffix)");
            StringAssert.IsMatch("^[a-z0-9-]+_[0-9a-f]{8}\\.png$", a1);
            StringAssert.StartsWith("monkey-eating-a-banana_", a1);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run `tests-run` for `IdeaToFileName_IsDeterministic_SlugifiesAndAvoidsCollisions`.
Expected: FAIL — `IdeaToFileName` not defined.

- [ ] **Step 3: Write minimal implementation** (add to `YAKImageLibraryCore`)

```csharp
        // <slug>_<hash8>.png — deterministic and collision-free across distinct ideas.
        public static string IdeaToFileName(string idea)
        {
            string trimmed = (idea ?? string.Empty).Trim();
            var sb = new StringBuilder();
            bool prevDash = false;
            foreach (char c in trimmed.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); prevDash = false; }
                else if (!prevDash && sb.Length > 0) { sb.Append('-'); prevDash = true; }
            }
            string slug = sb.ToString().Trim('-');
            if (slug.Length == 0) slug = "idea";
            if (slug.Length > 40) slug = slug.Substring(0, 40).Trim('-');
            return slug + "_" + Fnv1aHex(trimmed) + ".png";
        }

        private static string Fnv1aHex(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in s) { h ^= c; h *= 16777619u; }
                return h.ToString("x8");
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run `tests-run`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs Assets/YAK/Tests/Editor/YakImageLibraryTests.cs
git commit -m "feat(yak): image-library core — IdeaToFileName slug"
```

---

### Task 3: Pure core — `FindMissing` (gap-diff)

**Files:**
- Modify: `Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs`
- Test: `Assets/YAK/Tests/Editor/YakImageLibraryTests.cs`

**Interfaces:**
- Consumes: `IdeaToFileName` (Task 2).
- Produces: `static List<string> YAKImageLibraryCore.FindMissing(IEnumerable<string> ideas, IEnumerable<string> existingFileNames)` — returns the ideas whose `IdeaToFileName` is absent from `existingFileNames`. `existingFileNames` may be full paths or names (compared by `Path.GetFileName`, case-insensitive).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void FindMissing_ReturnsOnlyIdeasWithoutAFile()
        {
            var ideas = new[] { "dolphin", "popsicle", "rocket" };
            string have = YAKImageLibraryCore.IdeaToFileName("popsicle");
            var existing = new[] { "C:/x/" + have, "unrelated.png" };

            var missing = YAKImageLibraryCore.FindMissing(ideas, existing);

            Assert.AreEqual(new List<string> { "dolphin", "rocket" }, missing);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run `tests-run` for `FindMissing_ReturnsOnlyIdeasWithoutAFile`.
Expected: FAIL — `FindMissing` not defined.

- [ ] **Step 3: Write minimal implementation** (add to `YAKImageLibraryCore`)

```csharp
        public static List<string> FindMissing(IEnumerable<string> ideas, IEnumerable<string> existingFileNames)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingFileNames != null)
                foreach (var f in existingFileNames)
                    if (!string.IsNullOrEmpty(f)) existing.Add(Path.GetFileName(f));

            var missing = new List<string>();
            if (ideas == null) return missing;
            foreach (var idea in ideas)
                if (!existing.Contains(IdeaToFileName(idea)))
                    missing.Add(idea);
            return missing;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run `tests-run`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs Assets/YAK/Tests/Editor/YakImageLibraryTests.cs
git commit -m "feat(yak): image-library core — FindMissing gap-diff"
```

---

### Task 4: Pure core — `BuildPrompt` (+ default style preamble)

**Files:**
- Modify: `Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs`
- Test: `Assets/YAK/Tests/Editor/YakImageLibraryTests.cs`

**Interfaces:**
- Produces:
  - `const string YAKImageLibraryCore.DefaultStylePreamble` — flat-sticker style text containing the `{idea}` placeholder.
  - `static string YAKImageLibraryCore.BuildPrompt(string idea, IReadOnlyList<string> colorDescriptors, string stylePreamble)` — substitutes `{idea}` (or appends `Subject: <idea>.` if no placeholder), then appends `Use only these flat solid colors: <joined>.` when descriptors are non-empty. Empty/null `stylePreamble` falls back to `DefaultStylePreamble`.

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void BuildPrompt_InjectsIdeaAndColorsAndConstraints()
        {
            var colors = new List<string> { "Red #ff0000", "Sky Blue #66ccff" };
            string p = YAKImageLibraryCore.BuildPrompt("a popsicle", colors, null);

            StringAssert.Contains("a popsicle", p);
            StringAssert.Contains("Red #ff0000", p);
            StringAssert.Contains("Sky Blue #66ccff", p);
            StringAssert.Contains("No gradients", p);     // from default preamble
            StringAssert.DoesNotContain("{idea}", p);     // placeholder was substituted
        }

        [Test]
        public void BuildPrompt_NoColors_OmitsColorClause()
        {
            string p = YAKImageLibraryCore.BuildPrompt("a rocket", new List<string>(), null);
            StringAssert.Contains("a rocket", p);
            StringAssert.DoesNotContain("Use only these flat solid colors", p);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run `tests-run` for both `BuildPrompt_*` methods.
Expected: FAIL — `BuildPrompt`/`DefaultStylePreamble` not defined.

- [ ] **Step 3: Write minimal implementation** (add to `YAKImageLibraryCore`)

```csharp
        public const string DefaultStylePreamble =
            "A single centered {idea}, flat vector sticker style, bold solid fill colors, " +
            "thick clean shapes, plain solid background. No gradients, no shading, no text, " +
            "no drop shadows, no photorealism.";

        public static string BuildPrompt(string idea, IReadOnlyList<string> colorDescriptors, string stylePreamble)
        {
            string preamble = string.IsNullOrEmpty(stylePreamble) ? DefaultStylePreamble : stylePreamble;
            string subject = (idea ?? string.Empty).Trim();
            string body = preamble.Contains("{idea}")
                ? preamble.Replace("{idea}", subject)
                : preamble.TrimEnd() + " Subject: " + subject + ".";
            if (colorDescriptors != null && colorDescriptors.Count > 0)
                body += " Use only these flat solid colors: " + string.Join(", ", colorDescriptors) + ".";
            return body;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run `tests-run`. Expected: PASS (both methods).

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKImageLibraryCore.cs Assets/YAK/Tests/Editor/YakImageLibraryTests.cs
git commit -m "feat(yak): image-library core — BuildPrompt + default style preamble"
```

---

### Task 5: `YAKImageLibraryConfig` ScriptableObject

**Files:**
- Create: `Assets/YAK/Editor/ImageLibrary/YAKImageLibraryConfig.cs`

**Interfaces:**
- Consumes: `YAKImageLibraryCore.DefaultStylePreamble` (Task 4).
- Produces: `YAKImageLibraryConfig` SO with public fields: `TextAsset IdeasAsset`, `GameProfile Profile`, `string OutputFolder`, `string[] ExcludedNeutralIds`, `string Model`, `string ImageSize`, `string Quality`, `string StylePreamble`, `int MaxImagesPerRun`, `float EstimatedUsdPerImage`.

This task has no unit test (it is plain serialized data). The deliverable is that it compiles and a `.asset` can be created from the menu.

- [ ] **Step 1: Write the file**

```csharp
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Tuning for the Image Library tool. Holds NO secrets (the API key is resolved
    // at call time by YAKImageApiKey). OutputFolder is project-relative.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Image Library Config")]
    public sealed class YAKImageLibraryConfig : ScriptableObject
    {
        [Header("Ideas")]
        [Tooltip("Plain-text asset, one level idea per line. '#' lines are comments.")]
        public TextAsset IdeasAsset;

        [Header("Palette source")]
        [Tooltip("Profile whose wool palette is injected into the prompt.")]
        public GameProfile Profile;
        [Tooltip("Color ids treated as background neutrals and EXCLUDED from the prompt (subject/wool colors only).")]
        public string[] ExcludedNeutralIds = { "Grey", "GreyLight", "GreyDark", "DarkGrey", "White", "Black" };

        [Header("Output")]
        [Tooltip("Project-relative folder for generated PNGs — typically the generator's SourceImageFolder.")]
        public string OutputFolder = "Assets/YAK/SourceImages";

        [Header("OpenAI")]
        [Tooltip("Image model id. gpt-image-1 deprecates 2026-10-23 — change here when migrating.")]
        public string Model = "gpt-image-1";
        public string ImageSize = "1024x1024";
        [Tooltip("Quality tier: low | medium | high.")]
        public string Quality = "medium";

        [Header("Prompt")]
        [TextArea(3, 6)]
        public string StylePreamble = YAKImageLibraryCore.DefaultStylePreamble;

        [Header("Safety")]
        [Tooltip("Hard cap on images generated in a single run.")]
        [Min(1)] public int MaxImagesPerRun = 50;
        [Tooltip("USD per image for the chosen size/quality — used only for the pre-run cost estimate dialog.")]
        public float EstimatedUsdPerImage = 0.07f;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Use the Unity MCP `assets-refresh` tool, then `console-get-logs` (filter Error). Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKImageLibraryConfig.cs
git commit -m "feat(yak): image-library config ScriptableObject"
```

---

### Task 6: `YAKImageApiKey` resolver (env → EditorPrefs)

**Files:**
- Create: `Assets/YAK/Editor/ImageLibrary/YAKImageApiKey.cs`
- Test: `Assets/YAK/Tests/Editor/YakImageLibraryTests.cs`

**Interfaces:**
- Produces:
  - `static string YAKImageApiKey.Resolve()` — returns env `OPENAI_API_KEY` if set, else the EditorPrefs value, else `null`.
  - `static bool YAKImageApiKey.HasKey { get; }`
  - `static string YAKImageApiKey.Source()` — `"env"` | `"EditorPrefs"` | `"none"` (for the UI status line).
  - `static void YAKImageApiKey.SetEditorPrefKey(string key)` / `static void ClearEditorPrefKey()`.

- [ ] **Step 1: Write the failing test** (add to `YakImageLibraryTests`)

```csharp
        [Test]
        public void ApiKey_EnvVarTakesPrecedence_ThenEditorPrefs()
        {
            string prevEnv = System.Environment.GetEnvironmentVariable(YAKImageApiKey.EnvVar);
            try
            {
                YAKImageApiKey.ClearEditorPrefKey();
                System.Environment.SetEnvironmentVariable(YAKImageApiKey.EnvVar, "env-key");
                Assert.AreEqual("env-key", YAKImageApiKey.Resolve());
                Assert.AreEqual("env", YAKImageApiKey.Source());

                System.Environment.SetEnvironmentVariable(YAKImageApiKey.EnvVar, null);
                YAKImageApiKey.SetEditorPrefKey("pref-key");
                Assert.AreEqual("pref-key", YAKImageApiKey.Resolve());
                Assert.AreEqual("EditorPrefs", YAKImageApiKey.Source());
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(YAKImageApiKey.EnvVar, prevEnv);
                YAKImageApiKey.ClearEditorPrefKey();
            }
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run `tests-run` for `ApiKey_EnvVarTakesPrecedence_ThenEditorPrefs`.
Expected: FAIL — `YAKImageApiKey` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run `tests-run`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKImageApiKey.cs Assets/YAK/Tests/Editor/YakImageLibraryTests.cs
git commit -m "feat(yak): OpenAI API key resolver (env -> EditorPrefs)"
```

---

### Task 7: `YAKOpenAIImageClient` (request JSON unit-tested; transport manual)

**Files:**
- Create: `Assets/YAK/Editor/ImageLibrary/YAKOpenAIImageClient.cs`
- Test: `Assets/YAK/Tests/Editor/YakImageLibraryTests.cs`

**Interfaces:**
- Produces:
  - `static string YAKOpenAIImageClient.BuildRequestJson(string prompt, string model, string size, string quality)` — pure; returns the OpenAI Images request body (`model`, `prompt`, `n`=1, `size`, `quality`). **Unit-tested.**
  - `static UnityWebRequest YAKOpenAIImageClient.CreateRequest(string json, string apiKey)` — POST to `https://api.openai.com/v1/images/generations`, `Authorization: Bearer`, `Content-Type: application/json`. I/O wrapper.
  - `static bool YAKOpenAIImageClient.TryReadResult(UnityWebRequest req, out byte[] png, out string url, out string error)` — on HTTP error → `error`; else parse `data[0].b64_json` → `png` (and `url=null`); else `data[0].url` → `url` (and `png=null`); else `error`.

Only `BuildRequestJson` is unit-tested. `CreateRequest`/`TryReadResult` are exercised by the manual smoke test in Task 9.

- [ ] **Step 1: Write the failing test** (add to `YakImageLibraryTests`)

```csharp
        [Test]
        public void BuildRequestJson_ContainsModelPromptSizeQualityAndN1()
        {
            string json = YAKOpenAIImageClient.BuildRequestJson("draw a cat", "gpt-image-1", "1024x1024", "medium");
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.AreEqual("gpt-image-1", (string)o["model"]);
            Assert.AreEqual("draw a cat", (string)o["prompt"]);
            Assert.AreEqual("1024x1024", (string)o["size"]);
            Assert.AreEqual("medium", (string)o["quality"]);
            Assert.AreEqual(1, (int)o["n"]);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run `tests-run` for `BuildRequestJson_ContainsModelPromptSizeQualityAndN1`.
Expected: FAIL — `YAKOpenAIImageClient` not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
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
```

- [ ] **Step 4: Run test to verify it passes**

Run `tests-run`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKOpenAIImageClient.cs Assets/YAK/Tests/Editor/YakImageLibraryTests.cs
git commit -m "feat(yak): OpenAI image client (request JSON + response parse)"
```

---

### Task 8: `YAKImageLibraryWindow` (EditorWindow, update-pump generation)

**Files:**
- Create: `Assets/YAK/Editor/ImageLibrary/YAKImageLibraryWindow.cs`

**Interfaces:**
- Consumes: `YAKImageLibraryCore` (Tasks 1–4), `YAKImageLibraryConfig` (Task 5), `YAKImageApiKey` (Task 6), `YAKOpenAIImageClient` (Task 7), and `GameProfile.ColorPalette` (`ColorIds` + `TryGetColor`).

This task is I/O/UI — no unit test. Deliverable: the window opens from the menu, scans gaps, and (with a key + ideas asset) generates missing PNGs into the output folder. Verified manually in Task 9.

- [ ] **Step 1: Write the file**

```csharp
using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
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
```

- [ ] **Step 2: Verify it compiles**

`assets-refresh` then `console-get-logs` (filter Error). Expected: no compile errors. Confirm `Window ▸ Hoppa ▸ YAK ▸ Image Library` opens.

- [ ] **Step 3: Commit**

```bash
git add Assets/YAK/Editor/ImageLibrary/YAKImageLibraryWindow.cs
git commit -m "feat(yak): Image Library editor window (offline AI image fill)"
```

---

### Task 9: Full suite + end-to-end smoke + docs

**Files:**
- Create: `Assets/YAK/SourceImages/ideas.sample.txt` (a tiny sample ideas list)
- Modify: `Packages/com.hoppa.leveleditor.core/CHANGELOG.md`

**Interfaces:** none (verification + docs).

- [ ] **Step 1: Run the full EditMode suite**

Run the Unity MCP `tests-run` tool: EditMode, assembly `YAK.Editor.Tests` (then optionally the whole suite).
Expected: all green, including the new `YakImageLibraryTests` (ParseIdeas, IdeaToFileName, FindMissing, BuildPrompt ×2, ApiKey, BuildRequestJson).

- [ ] **Step 2: Create the sample ideas list**

Write `Assets/YAK/SourceImages/ideas.sample.txt`:

```text
# YAK level ideas — one per line. '#' lines are comments.
monkey eating a banana
dolphin jumping over a wave
a popsicle
red apple
smiling sun
```

- [ ] **Step 3: Manual smoke test (requires a real key)**

This step needs the user (the lead) — it spends money and hits the network:
1. Set the key: either `OPENAI_API_KEY` env var, or the window's Set button.
2. Create a `YAKImageLibraryConfig` asset (menu `Hoppa/YAK/Image Library Config`); assign the YAK `GameProfile` and `ideas.sample.txt`; leave `OutputFolder = Assets/YAK/SourceImages`.
3. Open the window, **Scan Gaps** → expect 5 missing.
4. Lower `MaxImagesPerRun` to 2 to keep cost ~$0.14, **Generate Missing**, confirm the cost dialog.
5. Expect 2 PNGs in `Assets/YAK/SourceImages/`, statuses flip to "done", Scan Gaps now shows 2 have / 3 missing.
6. Point `YAKGeneratorConfig.SourceImageFolder` at the same folder and run a generate — confirm a real image now drives the grid.

> NOTE: this is a manual gate. If running unattended, stop here and report that Steps 1–2 are done and Step 3 awaits the lead's key + Unity.

- [ ] **Step 4: Update the changelog**

Add under the top (Unreleased / next version) section of `Packages/com.hoppa.leveleditor.core/CHANGELOG.md`:

```markdown
### Added
- YAK Image Library tool (`Window ▸ Hoppa ▸ YAK ▸ Image Library`): offline auto-fill of the
  generator's source-image folder via the OpenAI Images API. Reads a plain-text ideas list,
  builds a flat-sticker prompt injecting the game's wool palette, and gap-fills one PNG per idea.
  Pure core (parse/slug/gap-diff/prompt) is unit-tested; key resolved from env/EditorPrefs (never committed).
```

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/SourceImages/ideas.sample.txt Packages/com.hoppa.leveleditor.core/CHANGELOG.md
git commit -m "docs(yak): image-library sample ideas + changelog"
```

---

## Self-Review

**Spec coverage:**
- Auto-fill folder from ideas list → Tasks 1,3,8. ✓
- Grid-friendly palette-injected prompt (wool only) → Tasks 4,8 (`WoolColorDescriptors` excludes neutrals). ✓
- Cheap re-runs / gap-fill → Task 3 + Task 8 ScanGaps. ✓
- Safety: no freeze (update-pump, Task 8), cost cap + confirm (Task 8 StartRun), crash-safe temp→rename (Task 8 WritePng), 429 backoff (Task 8 Pump) → all ✓.
- No pipeline changes → enforced by Global Constraints; no task touches the generator. ✓
- Model configurable, key from env/EditorPrefs, project-relative folder → Tasks 5,6. ✓
- Plain-text ideas, `#` comments → Task 1. ✓
- Pure core unit-tested, I/O manual → Tasks 1–4,6,7 tested; 8 manual. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code. ✓

**Type consistency:** `YAKImageLibraryCore` method names (`ParseIdeas`, `IdeaToFileName`, `FindMissing`, `BuildPrompt`, `DefaultStylePreamble`) match across Tasks 1–4 and their consumers in Task 8. `YAKOpenAIImageClient.BuildRequestJson/CreateRequest/TryReadResult` and `YAKImageApiKey.Resolve/HasKey/Source/SetEditorPrefKey/ClearEditorPrefKey/EnvVar` match between definition (Tasks 6,7) and use (Task 8). ✓

> Known limitation (acceptable for v1): generation runs one request at a time; a large ideas list takes a while. The offline model makes this fine — re-runs resume from gaps.
