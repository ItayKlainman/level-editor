# Web Companion for the Level Editor — Plan

> Status: draft for team review.
> Companion to `PLANNING.md` (the canonical spec for the Unity package)
> and `SESSION_NOTES.md` (longer-lived project context).

---

## Context

The studio's level-editor framework (`hoppa-level-editor-core`, currently
shipping at `v0.5.16`) is a Unity Editor tool. Designers who don't run
Unity can't author levels today, which makes the editor a bottleneck for
the broader team.

The goal of this plan is to extend the editor to **non-Unity teammates**
without disrupting the Unity-using workflow that already works.

### Decisions confirmed before drafting

- **Audience**: non-Unity designers authoring *full* levels — paint grid,
  configure top section, validate, export — same fidelity as the Unity tool.
- **Migration mode**: **Companion**. Unity remains canonical for the
  Unity-using team; the new external tool is added alongside, sharing JSON
  as the source of truth.
- **Extensibility**: non-Unity users only edit *existing* games. Adding
  new games or new cell types stays a Unity-team responsibility; the Unity
  team publishes a small config artifact (a "manifest") that the external
  tool consumes.

---

## Approach in one paragraph

Build a **browser-based level editor** as a sibling app to the Unity tool.
JSON `LevelDocument` files remain the shared source of truth — both
editors read and write the same files. A new per-game **manifest** (a
single JSON file generated from each `GameProfile` in Unity) carries the
palette, cell-type definitions, validation rules, and export descriptors
the web app needs to know about each game. The web app stays game-agnostic:
to onboard a future game, the Unity team publishes a new manifest and the
web tool picks it up — no web code changes.

---

## Portability assessment

Read of the existing codebase showed:

**Already portable** (pure .NET + Newtonsoft, no Unity dependency):
- `LevelDocument`, `LevelMetadata`, `GridData<T>`, `CellRef`, `ColorId`
- `JsonLevelSerializer`, `CellDataConverter`, `SchemaRegistry`,
  `ISchemaMigration`
- All `ICellData` cell-type interfaces and the registry contracts
- Validation contracts and the data-rule implementations
- Per-game cell *data* classes (`YarnBoxCell`, `YAKWoolCell`, etc.)

**Lightly Unity-tainted** (six Runtime files using `UnityEngine` for
incidentals like `Color` and `Debug.Log`):
- `Runtime/Validation/IColorPalette.cs`, `ValidationRuleBase.cs`,
  `ValidationReport.cs`, plus the three rule implementations under
  `Runtime/Validation/Rules/`
- Removable with a mechanical cleanup: swap `UnityEngine.Color` for a
  small `RgbaColor` struct, swap `Debug.Log` for an `ILogger`, flip
  `noEngineReferences: true` on the Runtime asmdef as a hygiene gate.

**Unity-locked** (would be a full rewrite if moved — and that's why we're
*not* moving them; we're building a separate web UI instead):
- The seven IMGUI panels and `LevelEditorWindow`
- `GameProfile`, `CellTypeDefinition`, `ColorPaletteAsset`,
  `LevelExporterAsset`, `EditorPanelAsset`, `StringIntMapping` —
  ScriptableObject configuration storage
- Undo system (Unity's native `Undo` API)
- Per-game cell renderers and inspectors

---

## Architecture

```
                   ┌─────────────────────────────┐
   Unity Editor    │  com.hoppa.leveleditor.core │   Web editor (browser)
   (canonical)     │  Runtime (pure .NET)        │   (companion)
                   └──────────────┬──────────────┘
                                  │
                  ┌───────────────┴───────────────┐
                  │  Shared JSON contracts:       │
                  │   • LevelDocument (per level) │
                  │   • GameManifest (per game)   │
                  │   • Validation rules (data)   │
                  └───────────────────────────────┘
```

### Two JSON schemas (both owned by Layer 1)

1. **`LevelDocument`** — already exists, unchanged.
2. **`GameManifest`** — new. One per game (`yarn-twist.manifest.json`,
   `yak.manifest.json`). Extracted from each `GameProfile` SO. Carries:
   - schema id + version
   - palette: `[{ id, displayName, rgba }, ...]`
   - cell types: id, display name, icon, fields, palette group, simple
     render hint
   - top-section descriptor (kind + per-field schema)
   - validation rules: data-rule list + a list of code-rule ids the web
     tool should treat as "Unity-only, validate in Unity"
   - export descriptor: declarative mapping where possible (color → int
     via `StringIntMapping`, output shape)

### Manifest publishing flow (Unity side)

- New Layer 1 exporter: `GameManifestExporter`. Reads a `GameProfile`
  plus its referenced assets, emits `*.manifest.json` to a configurable
  output directory. Triggered manually from `Export ▸` and runnable from
  a `[MenuItem]` for batch re-publish.
- The manifest is committed to the same repo (or a published artifact)
  the web tool reads from.

### Validation sharing

- **Data rules** (declaratively configured — `ColorBalanceRule`,
  `PaletteColorsExistRule`, `GridNonEmptyRule`): reused directly from
  the Runtime asmdef by the Blazor app (see stack choice below). Zero
  drift.
- **Code rules** (`YarnArrowBoxTargetRule`, `YarnTunnelOutputRule`, etc.)
  fall into two buckets:
  1. **Lift to data rules** where possible. Example: "this cell type
     needs a non-Wall neighbor in the direction stored in field X"
     becomes a generic `NeighborReachableRule`. Worth doing for the
     obvious ones — every future game then benefits.
  2. **Stay code-only** for the rest. Web tool shows them as "needs
     Unity to verify" with a re-validate hint. Acceptable; can be
     revisited per game.

### Export sharing

- **YAK** is largely data-shaped (color → int via mapping, pixel array
  flatten, JSON shape). Encode in the manifest's `export` block and run
  a generic exporter shared between Unity and web.
- **YarnTwist** has more bespoke logic (existing-entry update by
  `levelId`, filename-derived key fallback). Two paths:
  1. Express it in the manifest descriptor → generic exporter handles
     it. Preferred.
  2. Keep export Unity-side for Yarn initially; web tool produces editor
     JSON only and a designer flips to Unity for final export. Acceptable
     interim.

---

## Stack: Blazor WebAssembly

Chosen because the user is C#-fluent and has not built a web app before.

| Aspect | TypeScript + React | **Blazor WebAssembly (chosen)** |
|---|---|---|
| Reuse of Runtime asmdef | Reimplement validation in TS (~150 LOC) | **Direct C# reuse — zero drift** |
| Language overlap with Unity work | None (TS/JS) | **Same C# as Unity code** |
| Learning surface for a web newcomer | Large (npm, JS event loop, React state, TS types) | **Small (hosting + a few browser APIs)** |
| UI ecosystem | Best-in-class | Thinner — acceptable for an internal tool |
| Initial bundle size | ~few hundred KB | Several MB (.NET runtime, one-time) |
| Static hosting | Yes | Yes |
| Pattern transfer from Unity IMGUI panels | Low | **High — `GridCanvasPanel` translates almost directly** |

Practical consequence: phases C–F lean on C# patterns you already write
daily. The assistant guides the browser-specific bits (File System
Access API, HTML5 Canvas, Blazor component lifecycle).

---

## Hosting: Cloudflare Pages

| Item | Choice | Cost |
|---|---|---|
| App hosting | **Cloudflare Pages** (static site, fully client-side) | **$0** — free tier covers internal-team-scale traffic |
| URL | Default `hoppa-studio.pages.dev` | $0 |
| Custom domain (optional) | e.g. `editor.hoppa-play.com` | ~$10–15/year |
| Backend / database | None | — |

Why Cloudflare Pages over Vercel / Netlify / GitHub Pages:
- First-party MCP (see next section) — the assistant can deploy and tail
  logs directly.
- Fast global CDN, generous free tier.
- Clean Blazor WASM static deploys with no JS-tooling assumptions.

GitHub Pages remains a viable fallback if Cloudflare ever doesn't fit —
no code changes needed to switch hosts.

---

## MCP-augmented workflow

These tools meaningfully change how much the assistant can do directly
versus dictating steps. Some are already enabled in the user's setup;
two are worth adding for this project.

**Already on:**

| MCP | What the assistant uses it for |
|---|---|
| GitHub | Create the web-editor repo, push code, manage CI workflow files |
| Playwright | Drive a real browser to test the running Blazor app — navigate, click, screenshot, verify rendering |
| Context7 | Pull current Blazor / Razor / browser-API docs (no stale patterns) |
| Exa | Web search for niche issues |

**Add as Phase 0 setup:**

| MCP | What the assistant uses it for |
|---|---|
| Cloudflare (official) | Deploy to Cloudflare Pages, tail build/runtime logs, manage DNS if a custom domain is bought |

Net effect on the time estimate: the Playwright MCP is the biggest
unlock — it removes most of the "assistant can't see the browser"
round-trip overhead in phases C and D.

---

## Files to be created / modified

### Layer 1 — `Packages/com.hoppa.leveleditor.core/`

**New:**
- `Runtime/Manifest/GameManifest.cs` — POCO mirroring the JSON schema.
- `Runtime/Manifest/GameManifestSerializer.cs` — Newtonsoft round-trip.
- `Editor/Exporters/GameManifestExporter.cs` — reads a `GameProfile` +
  referenced assets, emits `*.manifest.json`. Hooked into `Export ▸`.
- `Runtime/Validation/Rules/NeighborReachableRule.cs` (and similar
  generic rules) — data-driven equivalents to lift current code rules
  into.

**Cleanup pass (mechanical, non-behavioural):**
- Replace `UnityEngine.Color` in Runtime with `RgbaColor` struct.
- Replace `UnityEngine.Debug.Log` with `Hoppa.LevelEditor.Core.ILogger`
  (Unity adapter lives in the Editor asm).
- Flip `noEngineReferences: true` on the Runtime asmdef.
- Affected files: the six Runtime files listed in the portability
  section above.

### Per-game Layer 2 (Yarn + YAK)

- Profiles get an `OutputDir` field for the manifest exporter.
- Optional: lift specific code rules into data rules where feasible.

### New repo — `hoppa-level-editor-web/`

Blazor WebAssembly project. Reads `GameManifest` + `LevelDocument` JSON,
renders the editor UI in the browser. Deployed to Cloudflare Pages via
GitHub Actions.

---

## Phasing and time estimate

Solo developer, learning web for the first time, with assistant guidance
plus the MCP toolchain above. Each phase ends in something demoable.
Phase gating per the project's working protocol: pause for explicit
approval between phases.

| Phase | Deliverable | Estimate |
|---|---|---|
| **0. MCP setup** | Cloudflare MCP installed and authenticated. Empty `hoppa-level-editor-web` repo created and wired to Cloudflare Pages (deploys "hello world" on push). | 0.5 day |
| **A. Runtime portability cleanup** | Runtime asmdef compiles with `noEngineReferences: true`. No behavioural change in Unity. Tests still pass. | 0.5–1 day |
| **B. `GameManifest` schema + Unity exporter** | `Export ▸ Game Manifest` writes `yak.manifest.json` and `yarn-twist.manifest.json`. Hand-validated against the existing GameProfile contents. | 2–3 days |
| **C. Web editor MVP (read-only viewer)** | Blazor app reads a `GameManifest` + `LevelDocument`, renders grid canvas, palette panel, top section, validation panel. No editing yet. Most of the web learning curve lives here. | **2–3 weeks** |
| **D. Web editor — full authoring** | Paint, drag, multi-select, undo, top-section editing, palette picking. Save back to `LevelDocument` JSON via File System Access API. Round-trip identical bytes to a Unity-saved file. | 2–3 weeks |
| **E. Web editor — validation parity** | Data rules running in the browser (direct C# reuse). Per-game code rules either lifted to data rules or marked "Unity-only" in the manifest. | 3–5 days |
| **F. Web editor — export parity** | Web tool produces game-format JSON (e.g. YAK's `LevelConfig` with int colors + pixel array) via the manifest's `export` descriptor. Hand-diffed against Unity export output. | 3–5 days |

**Totals**

| Scenario | Calendar time |
|---|---|
| Optimistic (Blazor clicks fast, no unknowns bite) | ~4 weeks |
| **Realistic with MCP toolchain** | **~5–8 weeks** |
| Pessimistic (multiple unknowns bite) | ~12 weeks |

Reassess after Phase C — that's when there's real signal on pace.

Phase A is the only intrusive change to existing Unity-side code (and
it's mechanical). Everything else is additive — the existing Unity
editor keeps working unchanged throughout.

---

## Division of labour (assistant vs. user)

| Phase | Assistant writes | Calendar time saved |
|---|---|---|
| 0. MCP setup | ~70% | ~50% |
| A. Runtime cleanup | ~95% | ~70% |
| B. Manifest + exporter | ~85% | ~50% |
| C. Web MVP | ~88% (with Playwright) | ~55% |
| D. Full authoring | ~88% | ~55% |
| E. Validation parity | ~93% | ~75% |
| F. Export parity | ~92% | ~75% |

**What the assistant cannot do, even with MCPs:**
- Taste / design judgment ("the spacing feels cramped" — needs your eye).
- Multi-factor auth dances when setting up accounts the first time.
- Decisions that affect the team.
- Replace the learning needed for you to maintain the app long-term.

**Two knobs that change the mix:**
1. **Pacing for learning vs. speed.** Faster if the assistant writes
   more and you review; slower-but-deeper if you write more with the
   assistant explaining. Adjustable mid-project.
2. **Test discipline.** Tests-alongside means fewer "it broke" round-trips.

---

## Verification

Per phase:
- **A**: existing test suite (`Tests/Runtime`, `Tests/Editor`) passes;
  `noEngineReferences` flag flips to `true` without errors.
- **B**: open `LevelEditorWindow` with `YAKProfile`, click `Export ▸
  Game Manifest`, diff the output against a hand-written expected
  manifest. Repeat for `YarnTwistProfile`.
- **C–F (web side)**: "golden level" round-trip test —
  1. Open a real `level_001.json` (YAK) in the web tool, view it,
     screenshot a known fixture (assistant can do this via Playwright).
  2. Make a known edit (e.g. paint cell (2,3) red), save.
  3. Open the saved file in Unity, confirm the edit landed in the right
     place, no other bytes changed.
  4. Run validation in both Unity and web, assert reports match
     entry-for-entry on a curated set of pass/fail fixtures.
- **Manifest fidelity**: programmatically reconstruct a synthetic
  `GameProfile`-equivalent in-memory from the manifest and assert it
  matches the live `GameProfile` SO for cell type ids, palette entries,
  validation rule ids.

---

## Things deliberately deferred

- Server-side execution of code-only validation rules (would need a
  headless `dotnet` job). Only revisit if data-rule lifting can't cover
  enough current code rules.
- Real-time collaboration (multiple designers in the same level). Not
  in scope; existing tool is single-author, web tool keeps that.
- Authentication. The MVP is unauthenticated since all state lives in
  user-side JSON files on disk. If we ever need access control, add a
  thin login layer.
- Adding new games / new cell types from the web tool. Explicitly out
  of scope per the audience decision.

---

## Risks (worth naming up front)

| Risk | Likelihood | Mitigation |
|---|---|---|
| Code rules don't lift cleanly to data rules → more per-game work than hoped | Medium | Phase E starts with one game (YAK) and measures actual lift effort before committing to YarnTwist. Worst case: keep code rules Unity-only as a documented constraint. |
| File System Access API has browser-support gaps | Low–Medium | Fallback is classic upload/download. Modern Chrome / Edge are fine; Firefox/Safari users get the fallback path. |
| Blazor WASM startup time annoys designers | Low | First load is ~5MB then aggressively cached. For an internal tool opened once per session, this is acceptable. If it becomes a complaint, swap to Blazor United / server interactive mode. |
| Manifest drifts from `GameProfile` source | Medium | Manifest regenerated on demand from the canonical SO; CI check that `Export ▸ Game Manifest` produces the same file as the one in the repo. |
| Custom domain / DNS confusion | Low | Skip it for v1 — `hoppa-studio.pages.dev` works fine. Add the domain once the tool is in real use. |

---

## Files in flight summary

```
Packages/com.hoppa.leveleditor.core/
  Runtime/
    Manifest/GameManifest.cs                        ← new
    Manifest/GameManifestSerializer.cs              ← new
    Validation/Rules/NeighborReachableRule.cs       ← new (and similar)
    (six existing files)                            ← cleanup pass
  Editor/
    Exporters/GameManifestExporter.cs               ← new
    (existing files)                                ← untouched

Assets/YarnTwist/                                    ← Layer 2: tiny additive change (OutputDir field)
Assets/YAK/                                          ← Layer 2: tiny additive change (OutputDir field)

hoppa-level-editor-web/                              ← new repo
  src/                                               ← Blazor WASM
  .github/workflows/deploy.yml                       ← deploys to Cloudflare Pages
```

---

# Quick summary for the team

> **One-paragraph version** for sharing with non-technical teammates.

We're adding a **browser-based level editor** that lives alongside the
existing Unity tool. Designers who don't run Unity can open it in a
browser, edit a level, save, and ship it without anyone needing Unity
installed. The Unity tool stays the canonical version and nobody's
existing workflow changes.

### How it works (plain version)

- **JSON is the shared language.** Both editors read and write the same
  `.json` files, so a level edited in the browser is the same level the
  Unity team sees and vice versa.
- **The Unity team publishes a small "manifest" per game** (one file)
  that tells the web editor what cell types exist, what colors are in
  the palette, and what validation rules to enforce. That's the only
  bridge the web tool needs — no shared codebase to maintain in two
  places for game-specific stuff.
- **Onboarding a new game** = the Unity team builds the Layer 2 exactly
  as they do today, clicks `Export ▸ Game Manifest`, and the web tool
  picks it up automatically. No web code changes per new game.

### Stack and cost

- Built in **Blazor WebAssembly** (C#, the same language we use for
  Unity). Picked because the developer is fluent in C# but new to web —
  this keeps the learning surface small.
- Hosted on **Cloudflare Pages** (static site, fully client-side).
- **$0/month** to run. Optional custom domain is ~$10–15/year.

### Time

- **Realistic: 5–8 weeks** of solo full-time work, with AI assistance.
- Optimistic 4 weeks; pessimistic 12 weeks if multiple unknowns bite.
- Phase-gated — we reassess after the first demoable milestone (the
  read-only viewer, ~3 weeks in).

### Risk

The Unity editor keeps working untouched. Worst case if the web tool
under-delivers, we still have what we have today. No bridges burned.

### What designers will be able to do (v1)

- Open a level file from disk.
- Paint cells, configure the top section, multi-select, undo, see
  validation errors live.
- Save the file back to disk.
- Export the game-format JSON the game expects.
- Switch between games (YAK, YarnTwist) by picking a different manifest.

### What's explicitly out of scope (v1)

- Real-time multi-designer collaboration.
- Adding new games or new cell types from the web (still a Unity-team
  job — same as today).
- Authentication / login. The tool runs locally in the browser; access
  control isn't needed for v1.

### Open question for the team

The trickiest piece is per-game **code-based validation rules** (the
ones written in C# that do neighbor lookups, like the YarnTwist arrow-
box-target check). The web tool can't run those out of the box. Two
options per rule:
1. Rewrite as a data rule (one-time effort, every future game benefits).
2. Mark it "Unity-only" — web tool shows a "verify in Unity" hint.

We'll measure how many fall into each bucket while doing YAK's
validation parity, then decide on a per-rule basis.
