# YAK Idea Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an in-editor "Idea Generator" mode to the Level Editor's Generate tab that turns a subject taxonomy into deduped, conversion-friendly idea lines (LLM-backed) and appends them to `ideas.txt`.

**Architecture:** A new generic Layer-1 hook (`ProfileGeneratePanel`, mirroring `ProfileLeftPanel`) lets a profile add a second Generate-tab mode. The Idea Generator's pure logic, OpenAI text transport, Knowledge-Base data, and IMGUI panel all live in YAK (Layer-2), following YAK's existing three-way split (pure core / transport / window). The BB profile references the YAK panel via the hook. Editor-core-only; not mirrored to the BB game.

**Tech Stack:** Unity 2022.3, C#, IMGUI (EditorWindow-adjacent panels), Newtonsoft.Json, UnityWebRequest, NUnit EditMode tests. OpenAI Chat Completions API (`/v1/chat/completions`).

## Global Constraints

- Unity `2022.3`; only external dep is `com.unity.nuget.newtonsoft-json: 3.2.1`.
- Write `.cs`/`.json` with editor tools directly; never via shell heredocs.
- No emoji in IMGUI captions destined for 2022.3 (use BMP glyphs / `EditorGUIUtility.IconContent`).
- Layer-1 (`Packages/com.hoppa.leveleditor.core`, asmdef `Hoppa.LevelEditor.Core.Editor`) must stay ignorant of YAK / ideas.txt / OpenAI. All YAK code lives in `Assets/YAK` (asmdef `Hoppa.YAK.Editor`, which references the Layer-1 editor asmdef).
- Reuse `YAKImageApiKey.Resolve()` for the key — no new key plumbing.
- Idea-Quality Rules (converter failure modes): one concrete subject, clear silhouette, small palette, no text/logos/numbers, no detached particles, no two-subject/scene phrasing, lowercase, no trailing period.
- Package bump `0.12.0 → 0.13.0`; CHANGELOG entry under `## [Unreleased]`.
- Nothing writes to disk except the Export action.

---

## File Structure

**Layer-1 (`Packages/com.hoppa.leveleditor.core/Editor/`):**
- Create `Panels/ProfileGeneratePanel.cs` — abstract base for a profile-supplied Generate-tab panel.
- Modify `Infrastructure/GameProfile.cs` — add `_generatePanelScript`, `HasGeneratePanel`, `CreateGeneratePanel()`.
- Modify `Generator/GeneratorModePanel.cs` — top mode toggle (Level Generator | Idea Generator) + render the profile panel.
- Modify `Window/LevelEditorWindow.cs` — allow the Generate tab to open when a generate panel exists (even with no `LevelGenerator`); forward a repaint signal.
- Modify `package.json`, `CHANGELOG.md`.

**Layer-2 (`Assets/YAK/`):**
- Create `Data/IdeaKnowledgeBase.json` — subject taxonomy + modifiers + composition types + complexity distribution + design rules (seeded from the boss's Knowledge Base).
- Create `Editor/IdeaGenerator/IdeaKnowledgeBase.cs` — POCO model + `Parse(json)` (pure).
- Create `Editor/IdeaGenerator/IdeaGeneratorCore.cs` — pure: prompt builder, response parser, dedupe, export-text builder.
- Create `Editor/IdeaGenerator/YAKOpenAIChatClient.cs` — transport: `BuildChatRequestJson`, `CreateRequest`, `TryReadContent`.
- Create `Editor/IdeaGenerator/YAKIdeaGeneratorPanel.cs` — `ProfileGeneratePanel` subclass: UI + request pump + Export IO.
- Modify `Editor/ImageLibrary/YAKImageLibraryConfig.cs` — add `TextModel` field.
- Modify `SourceImages/prompts.txt` — add `[collectible]` block.
- Modify the BB profile asset — set `_generatePanelScript` to the YAK panel.
- Create `Tests/Editor/IdeaGeneratorTests.cs` — pure-core + transport tests.
- Modify `Tests/Editor/YakImageLibraryTests.cs` — tolerate the new `[collectible]` prompt block.

---

## Task 1: Layer-1 hook — ProfileGeneratePanel base + GameProfile wiring

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Panels/ProfileGeneratePanel.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs`

**Interfaces:**
- Produces: `abstract class ProfileGeneratePanel { Action RequestRepaint {get;set;}; string Title {get;}; virtual void OnEnterMode(); virtual void OnExitMode(); abstract void OnGUI(Rect rect, GameProfile profile); }`
- Produces on `GameProfile`: `bool HasGeneratePanel { get; }` and `ProfileGeneratePanel CreateGeneratePanel()`.

- [ ] **Step 1: Create the base class**

```csharp
using System;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // A profile-supplied panel that adds a second mode to the Generate tab
    // (alongside the built-in level generator). Plain POCO instantiated via
    // reflection, mirroring ProfileLeftPanel/ProfileRightPanel.
    public abstract class ProfileGeneratePanel
    {
        // Set by the host so async work can request a repaint. May be null.
        public Action RequestRepaint { get; set; }

        // Short label shown on the mode toggle (e.g. "Idea Generator").
        public abstract string Title { get; }

        public virtual void OnEnterMode() { }
        public virtual void OnExitMode() { }

        public abstract void OnGUI(Rect rect, GameProfile profile);
    }
}
```

- [ ] **Step 2: Add the MonoScript field to GameProfile**

In `GameProfile.cs`, next to `_leftPanelScript` (line ~46), add:

```csharp
        [SerializeField] private MonoScript _generatePanelScript;
```

- [ ] **Step 3: Add the property + factory**

Next to `CreateLeftPanel()` (lines ~152-159), add (mirrors it exactly):

```csharp
        public bool HasGeneratePanel => _generatePanelScript != null;

        public ProfileGeneratePanel CreateGeneratePanel()
        {
            if (_generatePanelScript == null) return null;
            var type = _generatePanelScript.GetClass();
            if (type == null || !typeof(ProfileGeneratePanel).IsAssignableFrom(type)) return null;
            try   { return (ProfileGeneratePanel)Activator.CreateInstance(type); }
            catch { return null; }
        }
```

- [ ] **Step 4: Compile-verify**

Run the `assets-refresh` MCP tool. Expected: clean compile, no errors in `console-get-logs`.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Panels/ProfileGeneratePanel.cs Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs
git commit -m "feat(core): ProfileGeneratePanel hook for a profile-supplied Generate-tab mode"
```

---

## Task 2: Layer-1 — Generate-tab mode toggle + open-gate

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Generator/GeneratorModePanel.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Window/LevelEditorWindow.cs`

**Interfaces:**
- Consumes: `GameProfile.HasGeneratePanel`, `GameProfile.CreateGeneratePanel()`, `GameProfile.LevelGenerator`.
- Produces: `GeneratorModePanel` renders either the level generator or the profile's `ProfileGeneratePanel` based on an internal sub-mode toggle; exposes `event Action OnRequestRepaint`.

- [ ] **Step 1: Add sub-mode state + repaint event to GeneratorModePanel**

At the top of `GeneratorModePanel` (after line ~50), add fields:

```csharp
        public event Action OnRequestRepaint;

        private enum SubMode { Level, Profile }
        private SubMode _subMode = SubMode.Level;
        private ProfileGeneratePanel _profilePanel;
        private GameProfile _profilePanelProfile;
```

- [ ] **Step 2: Cache/build the profile panel and draw the toggle in OnGUI**

Replace the `profile.LevelGenerator == null` early-return block (lines ~70-76) with logic that (a) builds the profile panel when the profile changes, (b) chooses a sensible default sub-mode, (c) draws a toggle row only when both modes are available, and (d) dispatches. Insert at the start of `OnGUI` after the `profile == null` guard:

```csharp
            if (!ReferenceEquals(_profilePanelProfile, profile))
            {
                _profilePanel?.OnExitMode();
                _profilePanel = profile.HasGeneratePanel ? profile.CreateGeneratePanel() : null;
                if (_profilePanel != null)
                {
                    _profilePanel.RequestRepaint = () => OnRequestRepaint?.Invoke();
                    _profilePanel.OnEnterMode();
                }
                _profilePanelProfile = profile;
                bool hasLevel = profile.LevelGenerator != null;
                _subMode = hasLevel ? SubMode.Level : SubMode.Profile;
            }

            bool hasLevelGen = profile.LevelGenerator != null;
            bool hasProfileGen = _profilePanel != null;

            if (!hasLevelGen && !hasProfileGen)
            {
                EditorGUI.DrawRect(rect, PreviewBg);
                GUI.Label(rect, "This Game Profile has no Level Generator assigned.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Toggle row (only when both modes exist).
            var contentRect = rect;
            if (hasLevelGen && hasProfileGen)
            {
                var toggleRect = new Rect(rect.x, rect.y, rect.width, 22f);
                EditorGUI.DrawRect(toggleRect, ParamsBg);
                float halfW = rect.width * 0.5f;
                if (GUI.Toggle(new Rect(rect.x, rect.y, halfW, 22f), _subMode == SubMode.Level,
                        "Level Generator", EditorStyles.toolbarButton)) _subMode = SubMode.Level;
                if (GUI.Toggle(new Rect(rect.x + halfW, rect.y, halfW, 22f), _subMode == SubMode.Profile,
                        _profilePanel.Title, EditorStyles.toolbarButton)) _subMode = SubMode.Profile;
                contentRect = new Rect(rect.x, rect.y + 22f, rect.width, rect.height - 22f);
            }
            else if (hasProfileGen && !hasLevelGen)
            {
                _subMode = SubMode.Profile;
            }

            if (_subMode == SubMode.Profile && _profilePanel != null)
            {
                _profilePanel.OnGUI(contentRect, profile);
                return;
            }
            // else fall through to the existing level-generator layout, using contentRect.
```

Then change the existing params/preview split (lines ~79-89) to be based on `contentRect` instead of `rect`.

- [ ] **Step 3: Dispose the profile panel on exit**

In `GeneratorModePanel.OnExitMode()` (line ~60) add `_profilePanel?.OnExitMode();`. In `DisposePreview()` leave as-is (that's for the level preview session).

- [ ] **Step 4: LevelEditorWindow — open the tab when a generate panel exists**

In `LevelEditorWindow.cs`:
- Line ~144, change the ShowGenerate wiring to also allow a profile panel:
```csharp
            _toolbar.ShowGenerate = _profile != null && (_profile.LevelGenerator != null || _profile.HasGeneratePanel);
```
- In `HandleGenerateToggle()` (line ~540), change the guard:
```csharp
            if (_profile == null || (_profile.LevelGenerator == null && !_profile.HasGeneratePanel)) return;
```
- Wire the repaint event once, where `_generator` is created/first used (near line 19 usage): after entering generator mode in `HandleGenerateToggle`, ensure the subscription exists. Add a one-time hookup in the constructor or `OnEnable`:
```csharp
            _generator.OnRequestRepaint -= Repaint;
            _generator.OnRequestRepaint += Repaint;
```

- [ ] **Step 5: Compile-verify + manual smoke**

Run `assets-refresh`; confirm clean compile. (Manual UI check happens in the verify stage.)

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Generator/GeneratorModePanel.cs Packages/com.hoppa.leveleditor.core/Editor/Window/LevelEditorWindow.cs
git commit -m "feat(core): Generate-tab mode toggle renders a profile-supplied generate panel"
```

---

## Task 3: Knowledge Base data + parser

**Files:**
- Create: `Assets/YAK/Data/IdeaKnowledgeBase.json`
- Create: `Assets/YAK/Editor/IdeaGenerator/IdeaKnowledgeBase.cs`
- Test: `Assets/YAK/Tests/Editor/IdeaGeneratorTests.cs`

**Interfaces:**
- Produces: `class IdeaKnowledgeBase { List<SubjectLibrary> Subjects; List<Modifier> Modifiers; List<string> CompositionTypes; List<ComplexityLevel> ComplexityDistribution; List<string> DesignRules; static IdeaKnowledgeBase Parse(string json); }`, `class SubjectLibrary { string Name; List<string> Entries; }`, `class Modifier { string Name; string Guidance; }`, `class ComplexityLevel { string Name; int Percent; }`.

- [ ] **Step 1: Create the KB JSON** (`Assets/YAK/Data/IdeaKnowledgeBase.json`) seeded from the boss's Knowledge Base:

```json
{
  "subjects": [
    { "name": "Animals", "entries": ["fox","wolf","bear","red panda","hedgehog","squirrel","beaver","otter","badger","lynx","deer","moose","rabbit","hare","mole","chipmunk","labrador","husky","corgi","pug","shiba inu","persian cat","maine coon","hamster","guinea pig","ferret","goldfish","budgie","cow","highland cow","pig","sheep","goat","horse","pony","donkey","chicken","rooster","duck","goose","turkey","alpaca","llama","whale","orca","dolphin","shark","hammerhead","seal","walrus","sea lion","jellyfish","octopus","squid","crab","lobster","seahorse","starfish","pufferfish","clownfish","owl","eagle","falcon","crow","raven","penguin","puffin","flamingo","peacock","toucan","parrot","kiwi","swan","hummingbird","tiger","lion","leopard","jaguar","gorilla","orangutan","chimpanzee","elephant","rhino","hippo","sloth","tapir","pangolin","camel","fennec fox","meerkat","jerboa","scorpion","horned lizard","polar bear","arctic fox","beluga","narwhal","snowy owl","crocodile","alligator","gecko","chameleon","iguana","turtle","snake","bee","butterfly","dragonfly","firefly","ladybug","beetle","mantis"] },
    { "name": "Food & Drinks", "entries": ["apple","strawberry","watermelon","lemon","avocado","mushroom","carrot","cake","cupcake","donut","cookie","ice cream","popsicle","waffle","pancake","bread","croissant","pretzel","bagel","pie","burger","pizza","fries","hot dog","taco","burrito","sushi","ramen","dumpling","onigiri","mochi","bubble tea","coffee","tea","cocoa","juice","smoothie","soda","milkshake"] },
    { "name": "Nature", "entries": ["flower","tree","leaf","acorn","pinecone","cactus","succulent","bamboo","vine","moss","coral","seashell","rock","crystal","mountain","cloud","rainbow","sun","moon","star","aurora","volcano","waterfall"] },
    { "name": "Fantasy", "entries": ["dragon","baby dragon","phoenix","griffin","unicorn","pegasus","kitsune","slime","ghost","spirit","fairy","wizard","witch","golem","mimic","gargoyle","potion","spell book","magic crystal","rune","floating island"] },
    { "name": "Vehicles", "entries": ["car","sports car","vintage car","race car","truck","monster truck","bus","school bus","fire truck","police car","ambulance","train","steam train","bullet train","tram","bike","scooter","motorcycle","boat","pirate ship","submarine","helicopter","airplane","rocket","ufo"] },
    { "name": "Sports", "entries": ["football","basketball","baseball","tennis","golf","bowling","volleyball","rugby","hockey","surfing","skateboarding","snowboarding","skiing","running","swimming","boxing","karate","judo","gymnastics","weightlifting","archery","fencing","climbing","chess","esports"] },
    { "name": "Sports Equipment", "entries": ["ball","bat","racket","helmet","glove","trophy","medal","whistle","dumbbell","yoga mat","surfboard","snowboard","skis","running shoe","gym bag","stopwatch"] },
    { "name": "Tools", "entries": ["hammer","screwdriver","wrench","pliers","saw","axe","drill","ladder","toolbox","paint brush","paint roller","tape measure","shovel","rake","watering can","wheelbarrow","chainsaw"] },
    { "name": "Technology", "entries": ["laptop","desktop","keyboard","mouse","monitor","smartphone","tablet","smartwatch","camera","drone","robot","vr headset","game controller","microphone","speaker"] },
    { "name": "Household", "entries": ["chair","sofa","bed","pillow","blanket","lamp","clock","mirror","fridge","oven","toaster","blender","pan","kettle","cup","plate","spoon","fork","vacuum","washing machine"] },
    { "name": "Professions", "entries": ["chef","doctor","nurse","firefighter","police officer","scientist","teacher","farmer","carpenter","blacksmith","pirate","knight","wizard","astronaut","musician","photographer"] },
    { "name": "Hobbies", "entries": ["painting","gardening","camping","fishing","reading","knitting","photography","pottery","cooking","gaming","puzzle solving"] },
    { "name": "Music", "entries": ["piano","guitar","violin","drums","trumpet","saxophone","flute","harp","ukulele","accordion","microphone","headphones"] },
    { "name": "Architecture", "entries": ["castle","cabin","lighthouse","windmill","barn","treehouse","temple","pagoda","bridge","igloo","greenhouse"] },
    { "name": "Seasonal & Holidays", "entries": ["christmas tree","jack-o-lantern","easter egg","valentine heart","birthday cake","carnival mask","dreidel","chinese lantern","beach ball","autumn leaf","snowman","spring blossom"] },
    { "name": "Clothing", "entries": ["hat","crown","helmet","glasses","scarf","boot","sneaker","glove","cape","backpack","umbrella","watch","bow tie"] },
    { "name": "Toys & Games", "entries": ["teddy bear","toy train","toy robot","doll","kite","yo-yo","rubik's cube","dice","chess piece","playing card","puzzle piece"] }
  ],
  "modifiers": [
    { "name": "Expression", "guidance": "a facial expression or mood; use sparingly, not on every concept" },
    { "name": "Accessory", "guidance": "one worn or held item (hat, glasses, scarf)" },
    { "name": "Theme", "guidance": "a stylistic overlay (space, pirate, rockstar, winter)" },
    { "name": "Action", "guidance": "a single self-contained pose or motion" },
    { "name": "Companion", "guidance": "one small integral secondary element; risks despeckle, use rarely" },
    { "name": "Environment Piece", "guidance": "one grounding object; keep it minimal, never a full scene" }
  ],
  "compositionTypes": ["subject only","subject with expression","subject performing an action","subject wearing or holding an accessory","subject interacting with another object","subject emerging from another object","hybrid subject","mini storytelling scene"],
  "complexityDistribution": [
    { "name": "Very Simple", "percent": 10 },
    { "name": "Simple", "percent": 25 },
    { "name": "Medium", "percent": 30 },
    { "name": "Complex", "percent": 20 },
    { "name": "Hybrid", "percent": 10 },
    { "name": "Mini Scene", "percent": 5 }
  ],
  "designRules": [
    "Do not always add a face.",
    "Do not always add emotions.",
    "Do not always add accessories.",
    "Prefer variety over consistency.",
    "Mix minimalist and rich concepts.",
    "Keep silhouettes clear.",
    "Encourage surprising niche ideas.",
    "Avoid repetitive combinations.",
    "One concrete subject only; no text, logos, or numbers.",
    "No detached particles, no two-subject or full-scene phrasing.",
    "Lowercase, no trailing period, phrased to slug cleanly."
  ]
}
```

- [ ] **Step 2: Write the failing parse test** in a new `IdeaGeneratorTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class IdeaGeneratorTests
    {
        private const string MiniJson = @"{
          ""subjects"":[{""name"":""Animals"",""entries"":[""fox"",""owl""]},{""name"":""Music"",""entries"":[""guitar""]}],
          ""modifiers"":[{""name"":""Accessory"",""guidance"":""one worn item""}],
          ""compositionTypes"":[""subject only""],
          ""complexityDistribution"":[{""name"":""Simple"",""percent"":25},{""name"":""Medium"",""percent"":30}],
          ""designRules"":[""Keep silhouettes clear.""]
        }";

        [Test]
        public void Parse_ReadsSubjectsModifiersDistributionRules()
        {
            var kb = IdeaKnowledgeBase.Parse(MiniJson);
            Assert.AreEqual(2, kb.Subjects.Count);
            Assert.AreEqual("Animals", kb.Subjects[0].Name);
            CollectionAssert.AreEqual(new[]{"fox","owl"}, kb.Subjects[0].Entries);
            Assert.AreEqual("Accessory", kb.Modifiers[0].Name);
            Assert.AreEqual(2, kb.ComplexityDistribution.Count);
            Assert.AreEqual(30, kb.ComplexityDistribution[1].Percent);
            Assert.AreEqual(1, kb.DesignRules.Count);
        }
    }
}
```

- [ ] **Step 3: Run it — expect FAIL** (`IdeaKnowledgeBase` not defined). Use the `tests-run` MCP tool filtered to class `IdeaGeneratorTests`.

- [ ] **Step 4: Implement `IdeaKnowledgeBase.cs`** (pure, Newtonsoft):

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hoppa.YAK.Editor
{
    public sealed class IdeaKnowledgeBase
    {
        public List<SubjectLibrary> Subjects = new List<SubjectLibrary>();
        public List<Modifier> Modifiers = new List<Modifier>();
        public List<string> CompositionTypes = new List<string>();
        public List<ComplexityLevel> ComplexityDistribution = new List<ComplexityLevel>();
        public List<string> DesignRules = new List<string>();

        public static IdeaKnowledgeBase Parse(string json)
            => JsonConvert.DeserializeObject<IdeaKnowledgeBase>(json) ?? new IdeaKnowledgeBase();
    }

    public sealed class SubjectLibrary { public string Name; public List<string> Entries = new List<string>(); }
    public sealed class Modifier { public string Name; public string Guidance; }
    public sealed class ComplexityLevel { public string Name; public int Percent; }
}
```

- [ ] **Step 5: Run the test — expect PASS.**

- [ ] **Step 6: Commit**

```bash
git add Assets/YAK/Data/IdeaKnowledgeBase.json Assets/YAK/Editor/IdeaGenerator/IdeaKnowledgeBase.cs Assets/YAK/Tests/Editor/IdeaGeneratorTests.cs
git commit -m "feat(yak): idea-generator knowledge base data + parser"
```

---

## Task 4: IdeaGeneratorCore — prompt builder

**Files:**
- Create: `Assets/YAK/Editor/IdeaGenerator/IdeaGeneratorCore.cs`
- Test: `Assets/YAK/Tests/Editor/IdeaGeneratorTests.cs` (append)

**Interfaces:**
- Produces: `static class IdeaGeneratorCore` with `static string BuildPrompt(IdeaKnowledgeBase kb, IReadOnlyList<string> subjects, IReadOnlyList<string> modifiers, int amount, IReadOnlyList<string> existingIdeas)`.

- [ ] **Step 1: Write the failing test** (append to `IdeaGeneratorTests`):

```csharp
        [Test]
        public void BuildPrompt_IncludesSubjectsModifiersAmountRulesAndExisting()
        {
            var kb = IdeaKnowledgeBase.Parse(MiniJson);
            var p = IdeaGeneratorCore.BuildPrompt(kb,
                new[]{"Animals","Music"}, new[]{"Accessory"}, 20,
                new[]{"a red fox"});
            StringAssert.Contains("Animals", p);
            StringAssert.Contains("Music", p);
            StringAssert.Contains("Accessory", p);
            StringAssert.Contains("20", p);
            StringAssert.Contains("Keep silhouettes clear.", p);   // a design rule
            StringAssert.Contains("a red fox", p);                  // existing-idea uniqueness context
            StringAssert.Contains("Simple", p);                     // complexity distribution
        }
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement `IdeaGeneratorCore.BuildPrompt`:**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hoppa.YAK.Editor
{
    public static class IdeaGeneratorCore
    {
        public static string BuildPrompt(IdeaKnowledgeBase kb,
            IReadOnlyList<string> subjects, IReadOnlyList<string> modifiers,
            int amount, IReadOnlyList<string> existingIdeas)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Generate exactly {amount} pixel-art collectible idea lines.");
            sb.AppendLine("Flow per idea: pick one subject from the allowed libraries, pick a composition type, pick a complexity, add ONLY necessary modifiers, ensure uniqueness.");
            sb.AppendLine();
            sb.AppendLine("ALLOWED SUBJECT LIBRARIES (with example entries):");
            foreach (var s in kb.Subjects.Where(s => subjects.Contains(s.Name)))
                sb.AppendLine($"- {s.Name}: {string.Join(", ", s.Entries.Take(40))}");
            sb.AppendLine();
            sb.AppendLine("ALLOWED MODIFIERS (optional, use only when appropriate):");
            foreach (var m in kb.Modifiers.Where(m => modifiers.Contains(m.Name)))
                sb.AppendLine($"- {m.Name}: {m.Guidance}");
            sb.AppendLine();
            sb.AppendLine("COMPOSITION TYPES: " + string.Join("; ", kb.CompositionTypes));
            sb.AppendLine("TARGET COMPLEXITY DISTRIBUTION: " +
                string.Join(", ", kb.ComplexityDistribution.Select(c => $"{c.Name} {c.Percent}%")));
            sb.AppendLine();
            sb.AppendLine("DESIGN RULES:");
            foreach (var r in kb.DesignRules) sb.AppendLine($"- {r}");
            sb.AppendLine();
            sb.AppendLine("Do NOT repeat or lightly reword any of these EXISTING ideas:");
            foreach (var e in existingIdeas) sb.AppendLine($"- {e}");
            sb.AppendLine();
            sb.AppendLine("OUTPUT FORMAT: group by subject library. For each library used, emit a line '## <Library>' then one idea per line (lowercase, no trailing period, no numbering).");
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(yak): idea-generator prompt builder"`

---

## Task 5: IdeaGeneratorCore — response parser

**Files:** Modify `IdeaGeneratorCore.cs`; append tests.

**Interfaces:**
- Produces: `static List<IdeaGroup> ParseResponse(string modelText)`; `class IdeaGroup { string Subject; List<string> Ideas; }`.

- [ ] **Step 1: Failing test:**

```csharp
        [Test]
        public void ParseResponse_GroupsBySubject_SkipsNoiseAndNumbering()
        {
            var text = "## Animals\n1. a red fox\n- an owl with big eyes\n\n## Music\na smiling guitar\nrandom preamble that isn't a header line? keep as idea only under a subject";
            var groups = IdeaGeneratorCore.ParseResponse(text);
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("Animals", groups[0].Subject);
            CollectionAssert.Contains(groups[0].Ideas, "a red fox");   // numbering "1." stripped
            CollectionAssert.Contains(groups[0].Ideas, "an owl with big eyes"); // "- " stripped
            Assert.AreEqual("Music", groups[1].Subject);
            CollectionAssert.Contains(groups[1].Ideas, "a smiling guitar");
        }

        [Test]
        public void ParseResponse_LinesBeforeAnyHeaderAreIgnored()
        {
            var groups = IdeaGeneratorCore.ParseResponse("intro line\n## Music\na guitar");
            Assert.AreEqual(1, groups.Count);
            CollectionAssert.DoesNotContain(groups[0].Ideas, "intro line");
        }
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement:**

```csharp
        public sealed class IdeaGroup { public string Subject; public List<string> Ideas = new List<string>(); }

        public static List<IdeaGroup> ParseResponse(string modelText)
        {
            var groups = new List<IdeaGroup>();
            IdeaGroup current = null;
            foreach (var rawLine in (modelText ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("##"))
                {
                    current = new IdeaGroup { Subject = line.TrimStart('#', ' ').Trim() };
                    groups.Add(current);
                    continue;
                }
                if (current == null) continue; // ignore preamble before first header
                var idea = StripLeadingMarker(line);
                if (idea.Length > 0) current.Ideas.Add(idea);
            }
            return groups;
        }

        private static string StripLeadingMarker(string s)
        {
            s = s.TrimStart();
            // strip "- ", "* ", "1. ", "12) "
            int i = 0;
            if (i < s.Length && (s[i] == '-' || s[i] == '*')) { i++; while (i < s.Length && s[i] == ' ') i++; return s.Substring(i).Trim(); }
            int d = i; while (d < s.Length && char.IsDigit(s[d])) d++;
            if (d > i && d < s.Length && (s[d] == '.' || s[d] == ')')) { d++; while (d < s.Length && s[d] == ' ') d++; return s.Substring(d).Trim(); }
            return s.Trim();
        }
```

- [ ] **Step 4: Run — expect PASS.** **Step 5: Commit** `feat(yak): idea-generator response parser`.

---

## Task 6: IdeaGeneratorCore — dedupe

**Files:** Modify `IdeaGeneratorCore.cs`; append tests.

**Interfaces:**
- Produces: `static string NormalizeIdea(string idea)`; `static List<IdeaGroup> MarkAndFilterDuplicates(List<IdeaGroup> groups, IEnumerable<string> existing, out int dupeCount)` (dupes removed from returned groups; caller may show count).

- [ ] **Step 1: Failing test:**

```csharp
        [Test]
        public void Normalize_IgnoresArticleCasePunctuation()
        {
            Assert.AreEqual(IdeaGeneratorCore.NormalizeIdea("A Red Fox."),
                            IdeaGeneratorCore.NormalizeIdea("red fox"));
        }

        [Test]
        public void MarkAndFilterDuplicates_RemovesAgainstExistingAndWithinBatch()
        {
            var groups = new List<IdeaGeneratorCore.IdeaGroup> {
                new IdeaGeneratorCore.IdeaGroup { Subject="Animals",
                    Ideas = new List<string>{ "a red fox", "an owl", "a RED fox" } }
            };
            var kept = IdeaGeneratorCore.MarkAndFilterDuplicates(groups, new[]{"the owl"}, out var dupes);
            CollectionAssert.AreEqual(new[]{"a red fox"}, kept[0].Ideas); // owl dropped (existing), 2nd fox dropped (within-batch)
            Assert.AreEqual(2, dupes);
        }
```

- [ ] **Step 2: Run — expect FAIL. Step 3: Implement:**

```csharp
        public static string NormalizeIdea(string idea)
        {
            var s = (idea ?? string.Empty).Trim().ToLowerInvariant().TrimEnd('.', '!', '?', ' ');
            foreach (var art in new[]{"a ","an ","the "})
                if (s.StartsWith(art)) { s = s.Substring(art.Length); break; }
            return s.Trim();
        }

        public static List<IdeaGroup> MarkAndFilterDuplicates(
            List<IdeaGroup> groups, IEnumerable<string> existing, out int dupeCount)
        {
            var seen = new HashSet<string>();
            foreach (var e in existing) seen.Add(NormalizeIdea(e));
            dupeCount = 0;
            foreach (var g in groups)
            {
                var kept = new List<string>();
                foreach (var idea in g.Ideas)
                {
                    var key = NormalizeIdea(idea);
                    if (key.Length == 0 || seen.Contains(key)) { dupeCount++; continue; }
                    seen.Add(key);
                    kept.Add(idea);
                }
                g.Ideas = kept;
            }
            return groups;
        }
```

- [ ] **Step 4: Run — expect PASS. Step 5: Commit** `feat(yak): idea-generator dedupe`.

---

## Task 7: IdeaGeneratorCore — export-text builder

**Files:** Modify `IdeaGeneratorCore.cs`; append tests.

**Interfaces:**
- Produces: `static string BuildAppendBlock(List<IdeaGroup> groups, int batchNumber, string styleKey = "collectible")`; `static int NextBatchNumber(string existingIdeasRaw)`.

- [ ] **Step 1: Failing test:**

```csharp
        [Test]
        public void BuildAppendBlock_EmitsStyleBatchAndPerSubjectHeaders()
        {
            var groups = new List<IdeaGeneratorCore.IdeaGroup> {
                new IdeaGeneratorCore.IdeaGroup { Subject="Animals", Ideas=new List<string>{"a red fox"} },
                new IdeaGeneratorCore.IdeaGroup { Subject="Music",   Ideas=new List<string>{"a smiling guitar"} },
            };
            var block = IdeaGeneratorCore.BuildAppendBlock(groups, 3);
            StringAssert.Contains("# @style: collectible", block);
            StringAssert.Contains("# @batch: 3", block);
            StringAssert.Contains("# Animals", block);
            StringAssert.Contains("a red fox", block);
            StringAssert.Contains("# Music", block);
            StringAssert.Contains("a smiling guitar", block);
        }

        [Test]
        public void NextBatchNumber_IsOneMoreThanMaxCollectibleBatch()
        {
            var raw = "# @style: collectible\n# @batch: 1\na cat\n# @batch: 2\na dog\n";
            Assert.AreEqual(3, IdeaGeneratorCore.NextBatchNumber(raw));
        }
```

- [ ] **Step 2: Run — expect FAIL. Step 3: Implement:**

```csharp
        public static string BuildAppendBlock(List<IdeaGroup> groups, int batchNumber, string styleKey = "collectible")
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"# @style: {styleKey}");
            sb.AppendLine($"# @batch: {batchNumber}");
            foreach (var g in groups)
            {
                if (g.Ideas.Count == 0) continue;
                sb.AppendLine($"# {g.Subject}");
                foreach (var idea in g.Ideas) sb.AppendLine(idea);
            }
            return sb.ToString();
        }

        public static int NextBatchNumber(string existingIdeasRaw)
        {
            int max = 0;
            foreach (var raw in (existingIdeasRaw ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                const string tag = "# @batch:";
                if (line.StartsWith(tag) && int.TryParse(line.Substring(tag.Length).Trim(), out var n))
                    max = System.Math.Max(max, n);
            }
            return max + 1;
        }
```

Note: `NextBatchNumber` scans all `# @batch:` tags; since batch numbers are shared across sections this yields a globally-unique batch id, which is fine.

- [ ] **Step 4: Run — expect PASS. Step 5: Commit** `feat(yak): idea-generator export-block builder`.

---

## Task 8: OpenAI chat transport

**Files:**
- Create: `Assets/YAK/Editor/IdeaGenerator/YAKOpenAIChatClient.cs`
- Modify: `Assets/YAK/Editor/ImageLibrary/YAKImageLibraryConfig.cs`
- Test: append to `IdeaGeneratorTests.cs`

**Interfaces:**
- Produces: `static class YAKOpenAIChatClient { const string Endpoint; static string BuildChatRequestJson(string systemPrompt, string userPrompt, string model); static UnityWebRequest CreateRequest(string json, string apiKey); static bool TryReadContent(UnityWebRequest req, out string content, out string error); }`
- Produces on config: `public string TextModel = "gpt-4o-mini";`

- [ ] **Step 1: Add the config field.** In `YAKImageLibraryConfig.cs` under the `[Header("OpenAI")]` group add:
```csharp
        public string TextModel = "gpt-4o-mini"; // model for the Idea Generator (chat completions)
```

- [ ] **Step 2: Failing test** (transport body only — no network, mirrors `BuildRequestJson` tests):

```csharp
        [Test]
        public void BuildChatRequestJson_HasModelAndBothMessages()
        {
            var json = YAKOpenAIChatClient.BuildChatRequestJson("SYS", "USER", "gpt-4o-mini");
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.AreEqual("gpt-4o-mini", (string)o["model"]);
            var msgs = (Newtonsoft.Json.Linq.JArray)o["messages"];
            Assert.AreEqual(2, msgs.Count);
            Assert.AreEqual("system", (string)msgs[0]["role"]);
            Assert.AreEqual("SYS", (string)msgs[0]["content"]);
            Assert.AreEqual("user", (string)msgs[1]["role"]);
            Assert.AreEqual("USER", (string)msgs[1]["content"]);
        }
```

- [ ] **Step 3: Run — expect FAIL. Step 4: Implement** (mirrors `YAKOpenAIImageClient`):

```csharp
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Hoppa.YAK.Editor
{
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
```

- [ ] **Step 5: Run — expect PASS. Step 6: Commit** `feat(yak): OpenAI chat transport for the idea generator + config TextModel`.

---

## Task 9: prompts.txt [collectible] block + shipped-file test update

**Files:**
- Modify: `Assets/YAK/SourceImages/prompts.txt`
- Modify: `Assets/YAK/Tests/Editor/YakImageLibraryTests.cs`

- [ ] **Step 1: Add the `[collectible]` block** to `prompts.txt` (place after `[funny]`, before `[default]`). Content:

```
[collectible]
A single cute pixel-art collectible of {idea}, centered, bold clean silhouette, flat solid colors, thick readable shapes, no background clutter.
```

- [ ] **Step 2: Read the shipped-file test.** Open `YakImageLibraryTests.cs` and locate `ShippedFiles_EveryBossBriefSectionBindsToARealPrompt_With20IdeasPerBatch` (and any test asserting the exact style-key set `{animals,objects,hybrids,fantasy,funny}`).

- [ ] **Step 3: Update the assertion** so a `[collectible]` prompt block is allowed to exist WITHOUT ideas bound to it yet (it is runtime-populated by the Idea Generator). Concretely: if the test iterates ideas→prompt (every idea's style resolves to a real block), a new empty prompt block does not break it. If a test asserts the prompt-block set equals exactly the 5 boss-brief keys, change it to assert the 5 boss-brief keys are a **subset** of the blocks, and that `collectible` is present. Add one positive assertion:
```csharp
            Assert.IsTrue(blocks.ContainsKey("collectible"), "collectible style block must exist for the Idea Generator");
```
(Adjust `blocks` to the actual local variable name from `ParseStyleBlocks`.)

- [ ] **Step 4: Run the full YAK EditMode suite** (`tests-run`, assembly `Hoppa.YAK.Editor.Tests`). Expected: all green, including the updated shipped-file test.

- [ ] **Step 5: Commit** `git add Assets/YAK/SourceImages/prompts.txt Assets/YAK/Tests/Editor/YakImageLibraryTests.cs && git commit -m "feat(yak): add collectible prompt block; update shipped-file test"`

---

## Task 10: YAKIdeaGeneratorPanel (UI + pump + export IO)

**Files:**
- Create: `Assets/YAK/Editor/IdeaGenerator/YAKIdeaGeneratorPanel.cs`

**Interfaces:**
- Consumes: `ProfileGeneratePanel` (base), `IdeaKnowledgeBase`, `IdeaGeneratorCore`, `YAKOpenAIChatClient`, `YAKImageApiKey`, `YAKImageLibraryConfig`.
- Produces: `sealed class YAKIdeaGeneratorPanel : ProfileGeneratePanel` (concrete; referenced by the BB profile's `_generatePanelScript`).

This task is IMGUI + IO plumbing composed from the already-tested core; verify by compile + the verify stage (no new unit test — mirrors the untested `GeneratorModePanel`/window pattern).

- [ ] **Step 1: Implement the panel.** Responsibilities:
  - `Title => "Idea Generator"`.
  - Load KB once from `Assets/YAK/Data/IdeaKnowledgeBase.json` (via `AssetDatabase.LoadAssetAtPath<TextAsset>` or `File.ReadAllText` under `Application.dataPath`), then `IdeaKnowledgeBase.Parse`.
  - State: `HashSet<string> _selectedSubjects` (default: all), `HashSet<string> _selectedModifiers` (default: none = "let the system decide"), `int _amount` (default 20; dropdown 5/10/20/50/100/200), `List<IdeaGroup> _results`, `int _dupeCount`, `string _status`, `bool _inFlight`, `UnityWebRequest _req`.
  - Layout in `OnGUI(rect, profile)`: left column = amount dropdown (`EditorGUI.Popup` over `new[]{5,10,20,50,100,200}`), **Generate** button (disabled if `_inFlight` or no subjects), status label, **Export Ideas** button (disabled unless `_results` has kept lines); right = two scroll lists of toggles (Subjects from `kb.Subjects`, Modifiers from `kb.Modifiers`); center/bottom = results grouped by subject, each idea a row with a keep toggle and a "(dupe)" tag suppressed (dupes already filtered).
  - **Generate**: read `ideas.txt` (`File.ReadAllText`), build the prompt via `IdeaGeneratorCore.BuildPrompt(kb, subjects, modifiers, amount, existingLines)`, resolve key via `YAKImageApiKey.Resolve()` (if null → status "No OpenAI API key"), build request via `YAKOpenAIChatClient.BuildChatRequestJson(system, user, config.TextModel)` + `CreateRequest`, `_req.SendWebRequest()`, set `_inFlight=true`, subscribe `EditorApplication.update += Pump`.
    - Use a short system prompt: `"You are a pixel-art collectible idea generator. Follow the user's rules exactly and output only the grouped idea lines."` The full instruction set is the user prompt from `BuildPrompt`.
  - **Pump**: when `_req.isDone`, unsubscribe, `_inFlight=false`; `TryReadContent` → on success `ParseResponse` then `MarkAndFilterDuplicates(groups, existingLines, out _dupeCount)` → `_results`; set status (`"Generated N ideas (M duplicates removed)"` or the error string). Call `RequestRepaint?.Invoke()` (and `UnityEditorInternal.InternalEditorUtility.RepaintAllViews()` as a fallback) each pump so the status refreshes.
  - **Export**: `batch = IdeaGeneratorCore.NextBatchNumber(existingRaw)`; `block = BuildAppendBlock(keptGroups, batch)`; `File.AppendAllText(ideasPath, block)`; `AssetDatabase.Refresh()`; status `"Exported K ideas to ideas.txt (batch N)"`; clear `_results`.
  - `OnExitMode()`: if `_inFlight`, dispose `_req`, unsubscribe `Pump`.
  - No emoji in captions.

- [ ] **Step 2: Compile-verify** with `assets-refresh`; clean console.

- [ ] **Step 3: Commit** `git add Assets/YAK/Editor/IdeaGenerator/YAKIdeaGeneratorPanel.cs && git commit -m "feat(yak): Idea Generator panel (UI + request pump + export)"`

---

## Task 11: Wire the BB profile + package bump

**Files:**
- Modify: the BB profile asset (`Assets/BusBuddies/**/BusBuddiesProfile.asset` — locate with `assets-find t:GameProfile` or by name).
- Modify: `Packages/com.hoppa.leveleditor.core/package.json`, `Packages/com.hoppa.leveleditor.core/CHANGELOG.md`

- [ ] **Step 1: Set `_generatePanelScript`** on the BB profile to the `YAKIdeaGeneratorPanel` MonoScript. Use the `assets-modify`/`object-modify` MCP tool (set the `_generatePanelScript` object reference to the `.cs` MonoScript for `YAKIdeaGeneratorPanel`). Verify via `assets-get-data` that the field now references the script.

- [ ] **Step 2: Bump the package version** in `package.json`:
```json
  "version": "0.13.0",
```

- [ ] **Step 3: Add the CHANGELOG entry** under `## [Unreleased]`:
```markdown
### Added
- `ProfileGeneratePanel` hook: a profile can supply a second mode for the Generate tab, selected via a top toggle (Level Generator | <panel title>). Backward-compatible; profiles without the hook are unchanged.
```

- [ ] **Step 4: Full EditMode run** (`tests-run`) — expected: prior suite + new `IdeaGeneratorTests` all green, clean compile.

- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: wire BB profile to the Idea Generator panel; bump package 0.13.0"`

---

## Task 12: End-to-end manual verification (verify stage)

Not a code task — performed by the verifier/lead. Confirms:
- [ ] With the BB profile active, the Generate tab shows the `Level Generator | Idea Generator` toggle; the level generator still works.
- [ ] Idea Generator: subjects/modifiers tables render; amount dropdown works; Generate with no key shows the "No OpenAI API key" status (or, once billing is lifted, returns grouped ideas).
- [ ] Export appends a correct `# @style: collectible` / `# @batch: N` block to `ideas.txt` and other games' Generate tabs are unaffected.

---

## Self-Review notes (author)

- **Spec coverage:** UI (Task 10) · KB data (Task 3) · engine prompt/parse/dedupe/export (Tasks 4-7) · transport (Task 8) · export binding + prompts.txt (Tasks 7, 9) · Layer-1 hook + toggle + packaging (Tasks 1, 2, 11) · error handling (Task 10 pump/status) · testing (Tasks 3-9). All spec sections mapped.
- **Known breakage handled:** the shipped-file test that hard-asserts the 5 boss-brief styles is updated in Task 9.
- **Type consistency:** `IdeaGroup`, `NormalizeIdea`, `BuildPrompt`, `ParseResponse`, `MarkAndFilterDuplicates`, `BuildAppendBlock`, `NextBatchNumber`, `BuildChatRequestJson`/`TryReadContent`, `CreateGeneratePanel`/`HasGeneratePanel`, `ProfileGeneratePanel.Title/RequestRepaint/OnGUI` — names consistent across tasks.
- **Out of scope (per spec §11):** per-concept prompts, metadata/rarity, in-UI KB editing, the paid image run, BB-game mirroring.
