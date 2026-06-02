# Connected Spools — Mechanic 2 (YarnTwist) — Design

## Context

Second of the week's three new YarnTwist mechanics. **Connected Spools**: two spools in
*adjacent* columns are linked; each stays **locked** (can't receive yarn balls) until **both**
reach their column's bottom active row, at which point the chain breaks and both activate.

Per the project's source-of-truth rule, the YarnTwist **game** schema is authoritative
(`E:/Projects/Hoppa/YarnTwist`, `Assets/_YAT/Scripts/Gamelogic/Managers/YATLevelManager.cs`).
There is **no connection-id object** in the game (mirroring Connected Boxes). A connected spool is
a `WinderConfig` with:

- `WinderType = ConnectedWinders` (enum `YATWinderType { None, ConnectedWinders }`)
- `ConnectedColumnIndex` — partner's column index
- `ConnectedWinderIndex` — partner's index within that column's `WinderConfigs` array

The GDD's "unique connection ID + Spool A/B" is the idealized description; the **game code wins**.
The editor's "connection number" is an authoring affordance only, translated at export into the
partner's `(column, index)` pointer on **both** spools reciprocally.

### Decisions (with the user)

1. **Lock-in timing:** commit-on-first-click. The first `Add Connect` immediately assigns and
   persists a connection number; an unfinished pair is a real *incomplete* pair flagged by validation.
2. **Visual:** draw an actual chain line connecting the two linked spools across columns (plus a
   shared number badge).
3. **Difficulty (analyzer):** model the "locked until partner ready" effect.
4. **Auto-fill:** analyzer-accurate only — auto-fill still rebuilds/wipes spools (connect *after*
   auto-filling); the win-path/win-rate numbers from **Analyze** on a hand-connected level are accurate.
5. **Soft-lock prevention (follow-up):** crossing pairs cause a structural deadlock; block creating
   them in the connect UI and flag any that slip through (reorder/move/hand-edit) in validation.

### Scope / layer

Layer-2 only (YarnTwist). No `Packages/` (Layer-1) change → **no UPM tag bump**; manifest stays
pinned `#v0.5.20`. Spools live in `YarnTopSectionPanel`, which owns its own right-click — they are
**not** grid cells, so the Connected-Boxes `ICellContextActions` system is not involved.

## Data model

`Assets/YarnTwist/Runtime/YarnTopSectionData.cs` — `YarnSpoolData.ConnectionId`
(`int?`, `[JsonProperty("connId", NullValueHandling = Ignore)]`). Two spools sharing an id form a
pair; `null` = unconnected. The id is a *stable* authoring handle (survives reorder / add / delete /
column-move). Allocation = `max(existing ids) + 1`. **Display number** = ordinal rank of the id among
the distinct ids present (always contiguous `1..N`); the stored id stays stable.

## Authoring UX — `YarnTopSectionPanel` + `YarnSpoolConnection`

Right-click a spool row → `GenericMenu`: **Change Color…** (moved off the swatch into the menu),
**Add Connect** / **Disable Connect**. Pending pair is derived each frame from data (the id with one
member). Flow:

- No pending: Add Connect on an unconnected spool → allocate + **persist** the id (commit-on-first-click).
- Pending exists: Add Connect on another unconnected spool is enabled only if it is in a different,
  adjacent column **and** completing it would not create a soft-lock; otherwise the item is disabled
  with the reason.
- On a connected spool: Disable Connect clears the id on **both** members in one undo step.

Connection ops live in `Assets/YarnTwist/Editor/TopSection/YarnSpoolConnection.cs` (UI-agnostic,
unit-tested): `BuildConnInfo`, `DisplayNumber`, `AllocId`, `CanComplete`, `Connect`, `DisconnectGroup`,
`ConnectionsDeadlock`, `CompletingDeadlocks`. All mutations: `PushUndoSnapshot()` → mutate →
`Document.TopSection = JObject.FromObject(topData)` → `MarkDirty()`.

**Visuals:** a number badge per connected row (amber while pending) + a chain line drawn across
columns after the scroll views close (clamped/skipped when an endpoint scrolls out of view).

## Soft-lock prevention

Crossing pairs between a column-pair (e.g. pair A links the middle spools, pair B links one column's
top to the other's bottom) form a mutual deadlock — neither column can advance. `ConnectionsDeadlock`
is a **color-blind head-advance simulation**: assuming infinite yarn, can every column's head climb
from bottom to top while honouring locks (a connected spool clears only when its partner is
simultaneously at its head)? If any head can never reach the top, the connections soft-lock. The
simulation terminates in O(total spools) because heads are monotonic.

Enforced two ways: (a) the connect menu disables a completion that `CompletingDeadlocks`; (b)
`YarnConnectedSpoolRule` emits a soft-lock Error (safety net for reorder/move/hand-edit; blocks export).

## Validation — `YarnConnectedSpoolRule`

`ValidationRuleBase`, `Scope = Level`. Groups spools by id:

- size `1` → **Error**: *"Connected Spool Pair N is incomplete. Please select a second valid spool."*
- size `> 2` → Error.
- members not in different/adjacent columns → Error.
- `ConnectionsDeadlock(top)` → **Error**: *"Connected spools form a soft-lock… links cross."*

`.cs` guid `c2a3b4d5e6f70819203a4b5c6d7e8f91`, `.asset` (`_id: yt.connected_spool`) guid
`d3b4c5e6f7081920304a5b6c7d8e9f02`; wired into `YarnTwistProfile.asset` `_rules`.

## Export — `YarnMasterLevelExporter.BuildTopConfigs`

A pre-pass maps each id to its member `(column, dataIndex)` positions. For each spool in a **complete**
pair, its winder JObject gets `WinderType:"ConnectedWinders"` (string, matching the `Direction`
precedent) + reciprocal `ConnectedColumnIndex` / `ConnectedWinderIndex` (partner's data index within
its column's `WinderConfigs`). Unconnected/incomplete spools keep the minimal `{ColorType, Hidden}`.

## Difficulty — `YarnTwistLevelAnalyzer`

`Column` gains `PartnerCol`/`PartnerPos`; `ParseTopSection` links complete pairs (size≠2 → independent,
mirroring the box analyzer). `Model.ColPartnerCol/Pos` + `Intern` + `ComputeStructHash`. **Key:** the
shared `ResolveMatches` gates a connected head spool with the pure latching check
`spoolHead[partnerCol] < partnerPos` — `spoolHead` is monotonic, so once the partner reaches its
position the chain stays broken. This keeps the lock a pure function of `spoolHead` (already in the memo
hash), so memoisation stays correct and the hot path stays allocation-free; it covers both the DFS and
the Monte-Carlo rollouts. Caveat: belt capacity is checked at node entry (post-resolve), so a one-way
lock only reduces win paths when balls stay stuck across a node; mutual locks deadlock → unsolvable.

## Auto-fill — `YarnTwistSpoolAutofiller`

No behaviour change; an inventory comment notes it rebuilds spools and therefore clears connections.

## Tests (EditMode)

- Exporter (+3): reciprocal winder pointers; no `WinderType` when unconnected; incomplete exports as
  unconnected.
- Analyzer (+2): connected lock removes orderings vs an unconnected baseline (relational, cap 12);
  mutual lock → unsolvable.
- `YarnConnectedSpoolTests` (new): validation (complete / incomplete / same-column / non-adjacent),
  authoring (two-step connect, adjacency gate, disable clears both, connect→undo round-trip, pending
  detection), and soft-lock (crossing detection, non-crossing pass, rule error, completion block).

## Rollout

Layer-2 only → **no UPM tag bump**. After tests pass: commit + push editor-core; sync the touched files
to the YarnTwist game repo (`itay-main`) as a separate step — `YarnTopSectionData.cs`,
`TopSection/YarnTopSectionPanel.cs` + new `TopSection/YarnSpoolConnection.cs`,
`YarnMasterLevelExporter.cs`, `Analysis/YarnTwistLevelAnalyzer.cs`, `Analysis/YarnTwistSpoolAutofiller.cs`,
new `Validation/YarnConnectedSpoolRule.cs` (+`.asset`). The game `WinderConfig` already has the fields →
it consumes the export immediately; wiring the rule into the game `GameProfile` is optional.
