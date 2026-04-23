# AI Integration Prompt — New Game Onboarding

Paste the prompt below into a new Claude Code conversation **opened inside this repo**.
Claude will read the framework docs, ask you questions one section at a time, and generate everything needed to wire up the new game.

---

## How to use

1. Open Claude Code in `hoppa-level-editor-core/`
2. Copy everything inside the code block below and send it as your first message
3. Answer Claude's questions — share GDD files, screenshots, or design docs when asked
4. Claude will produce all files and Unity assets at the end

---

## The Prompt

```
I want to integrate a new game into the Hoppa Level Editor framework in this repo.

Before asking me anything, read these files to understand the framework:
- WORKFLOW_DEVELOPER.md   (full integration steps and code patterns)
- WORKFLOW_DESIGNER.md    (what the system looks like from a designer's perspective)
- PLANNING.md             (architecture decisions and phase history)
- CURRENT_TASK.md         (what's currently in flight)
- Assets/YarnTwist/       (a complete worked example — use it as the reference implementation)

Once you've read those, interview me about the new game one section at a time.
Don't ask more than 3–4 questions per message.
If I mention a GDD, design document, screenshots, or a reference game — stop asking
and analyze them yourself. Extract what you can and only ask me about what's
genuinely unclear or missing.

Work through these sections in order:

─── 1. GAME BASICS ───────────────────────────────────────────────
- What is the game called?
- Describe the core mechanic in 1–2 sentences.
- Do you have any docs, GDD, screenshots, or reference material I should read first?

─── 2. GRID ──────────────────────────────────────────────────────
- What are the grid dimensions? (width × height — fixed or variable per level?)
- Any special orientation or coordinate conventions?

─── 3. COLOR PALETTE ─────────────────────────────────────────────
- Does the game use colors? List them (name + approximate color if known).
- Do colored items have special states? (e.g. hidden, locked, highlighted)

─── 4. CELL TYPES ────────────────────────────────────────────────
For each cell type, I need:
  • Name and stable ID (e.g. "yt.box")
  • What data it holds (color, direction, count, queue, flag...)
  • How it looks on the grid (filled rect, icon, arrow, text badge...)
  • What a designer configures in the inspector (dropdowns, toggles, sliders...)

─── 5. TOP SECTION ───────────────────────────────────────────────
- Is there a game-specific panel above the grid? (like Yarn Twist's spool columns)
- If yes: what data does it hold, what does a designer configure, how is it laid out?

─── 6. VALIDATION RULES ──────────────────────────────────────────
- What makes a level invalid? (broken connections, unbalanced counts, unreachable cells...)
- What are warnings vs hard errors?
- Are any rule parameters something a designer might tune? (e.g. balls-per-box = 9)

─── 7. WIN / LOSE ────────────────────────────────────────────────
- What is the win condition?
- What is the lose condition (if any)?
- Same for every level, or configurable per level?

─── 8. LEVEL METADATA ────────────────────────────────────────────
- What schema ID / version string should I use? (e.g. "yarn-twist.v1")
- Any extra per-level fields? (move limit, star thresholds, unlock condition...)

─────────────────────────────────────────────────────────────────

Once I've answered everything, produce — in this order:

CODE FILES
  □ All Runtime cell type classes (ICellData subclasses)
  □ All Editor cell definition classes (CellTypeDefinition subclasses)
  □ TopSectionData model class (if top section exists)
  □ TopSectionPanel subclass (if top section exists)
  □ All ValidationRuleBase subclasses with custom Inspector editors
  □ LevelAsset subclass + ScriptableObjectExporter subclass
  □ Assembly definition files (Runtime + Editor .asmdef)

DATA FILES
  □ At least 2 sample level JSON files:
      - A simple tutorial-style level (2 colors, basic cells only)
      - A complex level using every cell type and feature
  □ Both levels must be mathematically valid (pass all validation rules)

UNITY ASSETS (via script-execute MCP)
  □ Color palette .asset
  □ One .asset per cell definition
  □ One .asset per validation rule
  □ Exporter .asset
  □ GameProfile .asset (fully wired)
  □ Organized into subfolders: Palette/, CellDefs/, Rules/, Exporters/

DOCS
  □ Update CURRENT_TASK.md with the new game's status and any framework gaps found

CONVENTIONS TO FOLLOW
  • Prefix any guessed or assumed value with #TEMP
  • Cell type IDs: short dot-namespaced strings, never rename after levels are saved
  • Use [JsonProperty("camelCase")] on every serialized field in cell classes
  • Editor .asmdef: includePlatforms = ["Editor"]
  • First cell type in GameProfile must always be the Empty cell (index 0)
  • Call PushUndoSnapshot() before any TopSection mutation in OnGUI
  • Use Write/Edit tools directly for .cs and .json files — never PowerShell
```
