# studio-level-editor-core
Standalone Unity project for a generic level editor framework.
Not a game — will be exported as a UPM package.

## Working protocol (read every session)

1. **Authoritative spec:** `PLANNING.md` (sections 1–6, decisions, phases, verification).
2. **Resume context:** `SESSION_NOTES.md` (longer-lived state) and `CURRENT_TASK.md` (what's actively in flight).
3. **Phase gating:** the project is broken into phases (PLANNING.md §5). After completing a phase, **stop and wait for explicit user approval** before starting the next one. Do not auto-proceed.
4. **Progress tracking — `CURRENT_TASK.md`:**
   - Maintain a `CURRENT_TASK.md` at the repo root that captures the active phase, what's done, what's in progress, what's blocked, and any open questions.
   - Update it after every meaningful chunk of work (new files created, decisions made, blockers hit) so a fresh session can resume cold without losing context.
   - When a phase ends and the user approves moving on, archive the closed phase's notes into a brief "Completed phases" section and reset the active section for the next phase.
5. **Memory:** check `C:\Users\itay0\.claude\projects\E--Projects-Hoppa-hoppa-level-editor-core\memory\MEMORY.md` for prior framing decisions before making architectural choices.
