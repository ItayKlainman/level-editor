# Designer Guide — Hoppa Level Editor

Everything you need to create and edit levels. No coding required.

---

## Opening the editor

Go to `Window → Hoppa → Level Editor` in Unity.

Click **Open Profile** and select your game's profile (e.g. `YarnTwistProfile`).
The editor loads automatically — palette, grid size, and rules all come from the profile.

---

## The layout

```
┌── Toolbar ──────────────────────────────────────────────────────┐
│  New  Open  Save  Save As  |  Undo  Redo  |  ▶ Test Play        │
├─── Palette ──┬── Top Section (e.g. spool columns) ─────────────┤
│              │                                                   │
│  Cell list   │  Grid canvas — paint here                        │
│              │                                                   │
├──────────────┴──────────────────────┬───────────────────────────┤
│                                     │  Validation panel         │
│                                     │  (errors & warnings)      │
└─────────────────────────────────────┴───────────────────────────┘
│  status bar: unsaved indicator · cursor (x,y) · schema version  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Creating a level

1. Click **New** — the grid fills with empty cells.
2. Pick a cell type from the **Palette** on the left.
3. **Left-click** to place. **Right-click** to erase.
4. Click a placed cell to open its properties (color, direction, hidden, etc.).
5. Watch the **Validation** panel — fix any red errors before saving.
6. Click **Save**. Two files are created side by side:
   - `YT_001.json` — the editable source file
   - `YT_001.asset` — what the game actually loads at runtime

---

## Yarn Twist — cell types

| Cell | What it does in the game |
|---|---|
| **Empty** | Open space — yarn passes through freely |
| **Wall** | Blocked permanently — yarn can't enter |
| **Yarn Box** | Holds 9 balls of a chosen color |
| **Tunnel** | Spits out balls in a set direction from a queue of colors |
| **Arrow Box** | Like a box, but redirects yarn in one direction |

---

## Yarn Twist — setting up the top section (spool columns)

The area above the grid shows 4 spool columns. Each column holds up to 9 spools.

- Click **+** to add a spool, **−** to remove the last one.
- Pick the color from the dropdown next to each spool.
- Check **Hidden** to hide a spool from the player until it gets filled.

**The golden rule — balls must equal spool capacity, per color:**

> 1 box = 9 balls · 1 spool = 3 capacity
>
> 2 pink boxes (18 balls) → need 6 pink spools (6 × 3 = 18 capacity) ✓

The **Yarn Color Balance** rule in the Validation panel tells you exactly which colors are off.

---

## Understanding the Validation panel

| Color | Meaning | Action |
|---|---|---|
| Red / Error | Level cannot ship | Must fix |
| Yellow / Warning | Potential issue | Review — might be intentional |
| Blue / Info | Informational | No action needed |

**Common Yarn Twist errors:**

- *"Color 'pink': 18 balls vs 15 spool capacity"* — add one more pink spool
- *"Arrow box at (2,1) points outside the grid"* — move the box or change its direction
- *"Tunnel at (1,3) output is permanently blocked by a wall"* — remove the wall or change the tunnel's direction

---

## Undo / Redo

`Ctrl+Z` to undo · `Ctrl+Y` to redo. Every paint stroke, erase, and property change is tracked.

---

## Quick tips

- The **first cell in the palette** is always the eraser — pick it to clear cells.
- **Save As** duplicates the current level — great for making variants of an existing layout.
- The **status bar** shows the cursor's grid position `(x, y)` so you can count cells precisely.
- The `.json` file is human-readable — open it in any text editor to inspect or compare levels.
- The `.asset` file is what the game uses — don't move or rename it without telling a developer.

---

## What you never need to touch

- **Top Section Script** on the Game Profile — set by a developer once, leave it alone.
- The `Config/` folder assets (palette, cell defs, rules) — developer territory, just use them.
- Any `.asmdef` file — assembly configuration, not for designers.
