# Session Notes — Studio Level Editor

> Read this first when resuming work. Companion to `PLANNING.md` (the
> authoritative spec) and `CLAUDE.md` (the project tagline).

---

## Current status (as of 2026-04-21)

- **Project**: `hoppa-level-editor-core` (this repo) — the standalone
  Unity project that will host the UPM package
  `com.hoppa.leveleditor.core`.
- **State**: greenfield Unity 2022.3 LTS template. No package source
  written yet. No game-specific code in this repo (Yarn Sort lives in a
  separate game project).
- **What exists in repo:**
  - `CLAUDE.md` — project tagline (one-paragraph summary)
  - `PLANNING.md` — full finalized plan (sections 1–6, decisions, phases, verification)
  - `SESSION_NOTES.md` — this file
  - Default Unity 2022 template assets (`Assets/`, `Packages/manifest.json`
    with URP + Input System, etc.)
- **What does NOT exist yet:**
  - The package folder `Packages/com.hoppa.leveleditor.core/` (Phase 0)
  - Any `.cs` source files
  - The `com.unity.nuget.newtonsoft-json` dependency in `Packages/manifest.json`

---

## Phase status

| Phase | Status | Description |
|-------|--------|-------------|
| **Planning** | Complete | All architectural and product decisions resolved. See PLANNING.md §6. |
| **Phase 0 — Package skeleton** | Not started — awaiting user go-ahead | Create the UPM package folder structure, package.json, asmdefs, README, CHANGELOG stub, Newtonsoft dep. |
| Phase 1 — Core data + serialization | Pending | Will not start until Phase 0 is confirmed by user. |
| Phase 2 — Validation engine | Pending | |
| Phase 3 — EditorWindow + grid canvas | Pending | |
| Phase 4 — Top-section + export | Pending | |
| Phase 5 — Yarn Sort Layer 2 | Pending (in separate game repo) | |
| Phase 6 — Future: Second-game onboarding | Deferred (post-Yarn-Sort) | Driver game TBD based on next project. Revisit framework for generalization using a real driver, not a hypothetical. |

**Phase 0 has an explicit exit protocol** (PLANNING.md §5 → Phase 0):
the agent must STOP and wait for user confirmation before starting Phase 1.
Do not auto-proceed. Same gate applies between every subsequent phase
unless the user says otherwise.

---

## Resolved decisions (full list)

### Naming & namespaces
- Package name: `com.hoppa.leveleditor.core`
- Display name (tentative): "Hoppa Level Editor Core"
- C# root namespaces:
  - Runtime: `Hoppa.LevelEditor.Core`
  - Editor: `Hoppa.LevelEditor.Core.Editor`
- Asmdef names match: `Hoppa.LevelEditor.Core.Runtime.asmdef`,
  `Hoppa.LevelEditor.Core.Editor.asmdef`
- Editor asmdef has `"includePlatforms": ["Editor"]`.

### ID prefix convention
- Generic framework rules and cell types: `core.*`
  (e.g. `core.palette-colors-exist`, `core.empty`, `core.wall`).
- Game-specific rules and cell types: studio's existing short per-game
  prefix used for class/file names. Examples:
  - SnakesUp → `snu.*`
  - CoffeeGO → `cfg.*`
- **Yarn Sort prefix is unconfirmed.** Plan uses `ys.*` as a placeholder.
  **Action item before Phase 5:** confirm `YRN` vs `YS` with the team.
- Applies to `IValidationRule.Id`, `ICellTypeDefinition.TypeId`, and
  optionally `schemaVersion` (currently planned as `yarn-sort.v1` for
  human readability — re-decide at Phase 5 kickoff).

### Package hosting
- **Development**: local file path
  `"file:../hoppa-level-editor-core/Packages/com.hoppa.leveleditor.core"`
  in the consumer game project's `Packages/manifest.json`.
- **Production**: private GitHub repo (Git URL ref); no npm registry,
  no tarballs.
- CI on consumer projects will need a PAT or deploy key.

### Unity version
- Floor: 2022.3 LTS (`"unity": "2022.3"` in `package.json`).
- Forward-compat with 2023 / 6000 LTS is nice-to-have, not a gate.

### UI
- IMGUI throughout — confirmed, no UI Toolkit.
- `IEditorPanel.OnGUI(Rect rect, LevelEditorSession session)` is the
  panel contract.

### Serialization
- JSON is source of truth; ScriptableObjects are derived artifacts
  regenerated on save by `ScriptableObjectExporter`.
- Newtonsoft.Json via `com.unity.nuget.newtonsoft-json` is a hard
  dependency (declared in package.json).
- Cell polymorphism via `JsonConverter` driven by `ICellTypeRegistry`.
- Exporter updates existing `.asset` files in place to preserve GUIDs
  for `AssetReference` stability.

### Color palette
- Per-project `ColorPaletteAsset` (one asset per game).
- Levels reference colors by string `colorId`, never by index.

### Testing
- **Required**: Runtime unit tests — serialization round-trip,
  validation rules, schema migration.
- **Required**: Editor smoke tests via `[UnityTest]` — window opens
  from empty state, save/load round-trip, panels populate.
- **Out of scope**: full UI interaction tests (click flows, drag-paint
  sequences). Internal-tool maintenance cost too high.

### Yarn Sort GDD (Layer 2 specifics — NOT framework constraints)
- **Tunnel queue**: plain colored boxes only *for Yarn Sort* (`hidden`
  flag allowed on queued boxes). The generic framework does not restrict
  tunnel queue contents.
- **Hidden + Arrow Box**: valid combo. Hidden applies to color only;
  direction is always visible.
- **Spool column colors**: mixed colors allowed top-to-bottom in a
  column.
- **Row ordering**: `"rowOrder": "bottomUp"` (JSON cells array index 0
  is the bottom row, y=0).
- **Arrow Box target**: single adjacent cell in the arrow's direction
  (not a ray).
- **Level ID format**: `level_XXX` zero-padded human-readable string.

### Things deferred (not blocking Phase 1)
- Addressables integration for level assets
- Test Play harness wiring (stub for now)
- Editor theming/icons (placeholders early)
- Localized display names for cell types

---

## Exact next step (for the next session)

> **Wait for the user's explicit "start Phase 0" before doing anything.**

Once the user gives the go-ahead, execute Phase 0 as specified in
PLANNING.md §5 → "Phase 0 — Package skeleton". Concretely:

1. Create the directory tree under
   `E:\Projects\Hoppa\hoppa-level-editor-core\Packages\com.hoppa.leveleditor.core\`
   matching PLANNING.md §1 → "Package folder structure" exactly. Do
   not create `.cs` files yet — only directories and the metadata files
   below.
2. Create the following files (no game logic, just scaffolding):
   - `package.json` with:
     - `"name": "com.hoppa.leveleditor.core"`
     - `"version": "0.1.0"`
     - `"unity": "2022.3"`
     - `"displayName": "Hoppa Level Editor Core"`
     - `"description"` — one-line summary (pull from CLAUDE.md/PLANNING.md context)
     - `"dependencies": { "com.unity.nuget.newtonsoft-json": "<resolve current stable>" }`
   - `README.md` — short intro, link to `../../PLANNING.md`
   - `CHANGELOG.md` — Keep-a-Changelog format with
     `## [0.1.0] - <today's ISO date>` and one entry: "Initial package skeleton."
   - `LICENSE.md` — placeholder (ask the user which license)
   - `Runtime/Hoppa.LevelEditor.Core.Runtime.asmdef`
   - `Editor/Hoppa.LevelEditor.Core.Editor.asmdef` (with
     `"includePlatforms": ["Editor"]`, references the Runtime asmdef)
   - `Tests/Runtime/Hoppa.LevelEditor.Core.Tests.asmdef` (references
     Runtime + Unity test framework)
   - `Tests/Editor/Hoppa.LevelEditor.Core.Editor.Tests.asmdef`
     (references Runtime + Editor + test framework, editor-only)
3. Add `"com.unity.nuget.newtonsoft-json": "<resolved version>"` to
   the project's `Packages/manifest.json` so the editor can compile
   against it.
4. **Phase 0 exit protocol** (PLANNING.md §5):
   a. Verify on-disk folder structure matches PLANNING.md §1 exactly.
   b. Confirm `CHANGELOG.md` stub exists.
   c. Stop. Report what was created and wait for explicit user
      confirmation before starting Phase 1.

---

## Open action items

- [ ] **Confirm Yarn Sort prefix** with the team (`YRN` vs `YS`) — needed
  before Phase 5, but easy to substitute later via find-and-replace in the
  game project.
- [ ] **License decision** for the package (MIT, proprietary internal,
  Apache-2.0, etc.) — ask user at Phase 0 kickoff.
- [ ] **Tentative `displayName` and `description`** in `package.json` —
  ask user at Phase 0 kickoff if they want anything specific.
- [ ] **Newtonsoft.Json version** — pick the latest stable
  `com.unity.nuget.newtonsoft-json` available for Unity 2022.3 (likely
  `3.2.x`). Verify before Phase 0.

---

## Pointers

- Authoritative spec: `PLANNING.md`
- Project tagline: `CLAUDE.md`
- Unity manifest (consumer project): `Packages/manifest.json`
- Where the package will live: `Packages/com.hoppa.leveleditor.core/`
- Yarn Sort game project: separate repo (path TBD; will reference this
  package via `file:` URL during dev)
