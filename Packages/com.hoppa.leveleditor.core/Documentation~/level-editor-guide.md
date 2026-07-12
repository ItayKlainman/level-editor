# Making Levels — Beginner's Guide

A plain-English guide for anyone new to the Hoppa Level Editor. No coding needed.

---

## What is this thing?

It's a **tool that lives inside Unity** (the game engine) for building puzzle levels
for our sorting games — like **Yarn Kingdom** and **Bus Buddies**.

You draw or generate a level, the tool **checks that a player can actually beat it**,
and then it **sends the level to the game**. That's the whole job.

---

## The big picture

Every level goes through the same journey. Keep this picture in your head:

```
   A picture or idea
          |
          v
   [ Grid ]        <- the colored board the player sees
          |
          v
   [ Buses / Spools ]   <- the containers the player fills
          |
          v
   [ Check ]       <- a robot plays it: can it be won? how hard?
          |
          v
   [ Export ]      <- the level is saved into the game
          |
          v
   Playable in the game
```

> **Note on words:** In **Yarn Kingdom** the containers are called **spools**.
> In **Bus Buddies** they're called **buses**. Same idea, different theme.
> This guide says "spools/buses" to mean both.

---

## The pieces and what each one does

Think of these as helpers. You don't need all of them every time.

| Piece | Its job | When you use it |
|---|---|---|
| **Level Editor window** | Your main workspace — where you see the board and buttons | Always |
| **🖼 Image** | Turns a **picture** (an apple, a ladybug…) into a colored board automatically | When you want the board to look like something |
| **✨ Generate** | Builds a **whole level by itself** from a picture + a few settings | When you want a fast, complete level |
| **Autofill** | Looks at your board and **fills in the buses/spools for you** so the level is winnable | Almost every level |
| **Analyzer** ("the robot") | **Plays the level** to answer: *Can it be won?* and *How hard is it?* | Every level, before you export |
| **Export ▸** | **Sends the finished level to the game** (writes the game's level file) | When the level is ready |
| **Batch** | Makes **many levels at once** | When you need lots of levels |
| **Difficulty Curve** | Makes a **full set of levels that get harder** (easy → hard) | When building a whole campaign |

---

## Step by step: make ONE level

### 1. Open the editor
In Unity's top menu: **Window ▸ Level Editor**.

### 2. Pick your game
The editor works on one game at a time (a **Game Profile** — e.g. Yarn Kingdom or
Bus Buddies). Make sure the right profile is selected so you get the right colors and rules.

### 3. Get a board — pick ONE of three ways
- **Paint it by hand** — click cells and drop colors, like a paint program.
- **From a picture** — click **🖼 Image**, choose a picture, and it becomes a colored board.
- **Let it build one** — click **✨ Generate** and it makes a complete board for you.

### 4. Fill the buses/spools
Open the **Autofill** panel and run it. The tool works out which colors and how many
containers you need so the level can actually be finished. You can tweak the result by hand.

### 5. Check it (the robot)
Run the **Analyzer**. It plays the level and tells you:
- **Solvable?** — Yes means a player *can* win it. (If it says No, change the board or the buses/spools and try again.)
- **Difficulty** — shown as a number called **APS**. Higher = harder.

### 6. Save
Click **Save** (or Ctrl+S). Give it a clear name/number.

### 7. Export to the game
Click **Export ▸**. This writes the level into the game's level file. Done.

### 8. See it in the game
Open the actual game project, load the level, and play it.

That's a full level, start to finish.

---

## Fixing colors when a picture converts wrong

Sometimes a picture converts to the wrong colors (a green frog coming out yellow). Two knobs
in the **🖼 Image** panel help:

- **Sampling = Dominant** (on by default) — each square takes its main color instead of a
  blurry average, so squares come out truer.
- **Color Remap** — add a rule "*this* color → *that* palette color." Click the swatch, use
  the **eyedropper** to grab the color off the source image, choose the target color, and
  raise **Reach** until it catches.

---

## Setting the difficulty (Bus Buddies)

Bus Buddies levels have a **Difficulty panel** on the right side of the editor. Instead of
guessing a difficulty number, you set a few simple sliders and the tool builds the buses to
match — and it **always keeps the level winnable**.

Here's every knob and what it does:

| Knob | What it does | Turn it up to… |
|---|---|---|
| **Buses Chunks (1–5)** | How many pixels ride in each bus. Higher = **bigger, fewer buses**. | make fewer, fuller buses |
| **Deviation %** | How much bus sizes vary. 0% = every bus the same size; 50% = sizes swing well above and below the average. | get a mix of big and small buses |
| **Number of Columns (1–5)** | How many bus queues the level uses. | spread buses across more lanes |
| **Difficulty (1–5)** | How **buried** the main colors are. Low = the colors you need are easy to reach. High = the main colors sit deep in the queues, so the player must **park buses in the active row to dig down to them**. | make the player plan/work more |
| **☐ No 1-bus color** | Stops any color from being a single bus — splits it into at least two. | cleaner, more interesting spreads |
| **☐ Round to 5** | Tries to make bus sizes tidy multiples of 5 (10, 15, 20…). Not always possible, but it tries. | neater numbers |

**How the "Difficulty" dig works (the important one):**
The tool looks at your picture and finds the **main colors** — the ones with the most pixels
(say, the purple that makes up most of a grape). As you raise Difficulty, it pushes those
main-color buses toward the **back** of the queues so they're tapped last. But it always leaves
**a few of each main color near the front** so the level is never stuck from the start — and it
**runs the robot to confirm the level is still winnable**, easing off if it went too far. So higher
Difficulty = more digging, never an impossible level.

**How to use it:** open a level, set the six knobs, and click **Apply / Auto-fill** — the buses
rebuild to match. Your settings are **saved with the level**, so re-opening it shows the same
values, ready to tweak.

**About APS:** for Bus Buddies, APS is now just a **number the robot reports** (an estimate of how
hard it turned out) — it no longer drives anything. Your six knobs are the control; APS is the
readout. *(Yarn Kingdom still uses APS as its target.)*

**Batches:** the same six settings live on each **tier** in the Difficulty Curve, so a whole
campaign can ramp these knobs from easy to hard.

---

## Designer workflow (Bus Buddies): touch up a real game level

This is the everyday job: take a level that's already in the game, tweak it, and save it
straight back. You never deal with any "editor format" — the game file goes in and the game
file comes out.

**Where the game's levels live:** `Assets/_BUB/Resources/Configs/Levels/level_<N>.json`
(inside the Bus Buddies project). These are the real levels the game loads.

**1. Open the level.** In the Level Editor, click **Open** and pick a
`level_<N>.json` from that folder. The editor recognizes the game format automatically and
loads the picture **and** its buses, ready to edit. *(If you ever saw an "Open Failed" error
here before — that's fixed; game levels open directly now.)*

**2. Touch it up.** Repaint pixels with the color palette, and edit the buses in the bottom
panel (color, capacity, hidden, add/remove/reorder, move between columns). The **Validation**
panel on the right flags problems (e.g. a color whose pixels don't add up to its buses).

**3. Save it back.** Click **Save**. Because you opened a game level, Save writes the **game
format right back to the same `level_<N>.json`** — no extra files, nothing else to do. The
game will load your changes next time it reads that level.

> Tip: work on a copy first if you want to keep the original — duplicate the
> `level_<N>.json` in the folder before opening it.

**Picking which levels to touch up:** the team generates big batches of candidate levels
(see below). Review them in **Window ▸ Hoppa ▸ Batch Review**, keep the nice ones, then use
this open → tweak → save flow on each.

---

## Making MANY levels at once

- **Batch** — *Window ▸ Hoppa ▸ [Game] ▸ Run Batch*. Generates a pile of levels, then you
  review them in **Window ▸ Hoppa ▸ Batch Review** (thumbnails + info) and keep the good ones.
- **Difficulty Curve** — *Window ▸ Hoppa ▸ [Game] ▸ Difficulty Curve*. Define "tiers"
  (easy, medium, hard…) and how many of each, and it builds the whole ordered set.

---

## Words you'll keep seeing

- **Grid / cell** — the board and its individual squares.
- **Spool / bus** — the container the player fills with matching colors.
- **Palette** — the set of colors this game allows.
- **Solvable** — a real player can win the level. The robot checks this.
- **APS** — the difficulty score the robot measures. Higher = harder.
- **Profile (Game Profile)** — the setting that tells the editor *which game* you're building for.
- **Export** — saving the level into the game's own level file.

---

## Golden rules

1. **Always run the Analyzer before exporting.** Never ship a level the robot couldn't win.
2. **Save often** (Ctrl+S).
3. **Pick the right game profile first** — wrong profile means wrong colors and rules.
4. If something looks wrong in-game, come back here, fix it, re-check, and export again.
