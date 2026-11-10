# 7. Testing

MyAceEditor is tested with **command-line modes** of the same `MyAceEditor.exe`. They run without showing questions,
write a report and exit. Run the modes that cover what you changed and paste the result in your pull request.

All paths below are examples; use your own client (`...\element`) and server (`...\gamed\config`) folders.
Test modes that save write only into temporary copies, never into your game folder.

## Format round trips

| Command | Proves |
|---|---|
| `--roundtrip <elements.data>` | Reading and writing `elements.data` gives the same bytes |
| `--tasks <tasks.data>` | Same for quests |
| `--gshop`, `--aipolicy`, `--npcgen` (+ file) | Same for the shop, AI policies, spawn points |
| `--pck <file.pck>` | Package reading; `--pck-split-test` for split packages |
| `--ecwld-check`, `--bht-check`, `--bmd-check` | Scene, collision and building model files of the client/server |
| `--check <element> <server home>` | Client × server pair (versions and data) |
| `--client-check <element>` | Instance names, zone music, sound files of a client |

## The 3D World

**`--objects-test <element> <config> <out>`** — the big self-test of the editable world
(`PerfectWorld/WorldEdit/ObjectsSelfTest.cs`): formats round trips (`.ecwld`, `.bmd`, `map.bht`, `.rmap`, `.dhmap`,
`.octr`, `.wmap`, `.t2mk`, `.t2bk`…), drafts, the save plans of buildings, trees, grass, terrain and water, monster
paths and flight, failure handling (a failed save puts everything back), backups and backup sessions. It ends with
`falhas: 0` when everything passes.

**`--world-shot <map folder> <out> <camera>...`** — opens the 3D World with no questions, places the camera and saves a
picture per camera. A camera is `x,y,z,yaw,pitch[,name]` (game coordinates; `y` can be `g` for "ground + 2 m").
The name runs a scenario before the picture:

| Name starts with | Scenario |
|---|---|
| `dftworld` | World editing: stroke on Terrain, switch to Objects, move a building, undo order, abandon |
| `dftlight` | Shadow and light brush: darker day and night, undo/redo, eraser, keys, only touched blocks in the save plan, eyedropper picks a shadow, painting kept after a day/night switch |
| `dftdrop` | Eyedroppers of grass and texture |
| `dftundosave` | Ctrl+Z after a Save brings back the files written by that Save, Ctrl+Y writes them again, "No" changes nothing (temporary file only) |
| `dftinfo` | Info card appears with a selection and disappears without; Show collision of the selected building |
| `dftlayout` | Cut-text check of every control (run with `MYACE_LANG`) |
| `dfttex`, `dftgrass`, `dftcard`, `dftpaste`, `dftmove`, `dftcat`, `dfttree` | Terrain, grass, object card, paste, move, catalogue, trees |

It also saves `window-*.png` pictures of each tool panel and runs the Esc checks of every tool (`OK esc-…` lines).

## Screenshots

| Command | Picture |
|---|---|
| `--snapshot <out.png> [tab] [block id]` | The main window, off screen, with the last folder |
| `--copy-block-test <element> <config> <out file>` | Not a picture: copies a building to open ground in the world and reports how many metres under it the save plan closes in the `.rmap` |
| `--click-test <out folder>` | Not a picture: opens the 3D World from the editor (with creatures) and clicks a creature with no tool, in Paths and in NPCs and monsters; `click-test.txt` says what got selected |
| `--readme-shots <out folder>` | All README pictures: every tab and every 3D World tool with something selected (the 3D window is on screen while it runs) |
| `--gm-shot`, `--prefs-shot`, `--wq-shot` | GM panel, preferences, quest panel |

## Headless rules (for anyone writing a new test mode)

- Set `WorldView.Headless = true` (and `SavedDialog.Quiet = true`) before creating windows: message boxes then answer
  themselves with the answer that writes nothing, and log the question.
- Never open a real dialog that waits for the user (for example a picker window) — the test would hang on the user's
  screen. Prepare the state instead.
- Write into a temporary folder; delete it at the end.
- End the process with `Environment.Exit` when the mode is done.
