# 4. The 3D World inside

The 3D World is one window, `UI/WorldView`, split into partial files by feature.

## Files

| File | What it holds |
|---|---|
| `WorldView.cs` | Window, camera (W A S D + mouse), render loop (`Step` on a 10 ms timer, `Render`), picking, markers, keys, Esc, undo dispatch |
| `WorldView.Tools.cs` | The floating tool menu, opening/closing tools (`OpenTool`, `CloseTool`, `LeaveTool`), shared buttons |
| `WorldView.WorldEdit.cs` | **World editing**: the Objects / Terrain pills, one Save (`SaveWorld`), one abandon question, undo order across pills |
| `WorldView.ObjectTool.cs`, `SceneEdit.cs`, `BuildingSave.cs` | Objects pill: selection, gizmo, card, copy/paste, insert buildings/trees/models, the objects save |
| `WorldView.TerrainTool.cs` | Terrain pill: ground texture brush, height brushes, terrain save |
| `WorldView.WaterTool.cs` | Water section of the Terrain pill |
| `WorldView.GrassTool.cs` | Grass and flowers section of the Terrain pill |
| `WorldView.LightBrush.cs` | Shadow and light section of the Terrain pill |
| `WorldView.Eyedropper.cs` | The eyedropper mode shared by texture, grass and light |
| `WorldView.InfoCard.cs` | The floating card of the selected thing; remembered brush values (`BrushMemory`) |
| `WorldView.PathEdit.cs`, `AirPath.cs` | Paths and areas tool |
| `WorldView.QuestTool.cs` | Quests tool |
| `WorldView.CreateTool.cs` | NPCs and monsters tool |
| `WorldView.TeleportTool.cs` | Teleports tool |
| `WorldView.ZoneTool.cs` | Zones tool |
| `WorldView.Headless.cs` | Message boxes that answer themselves in test runs; the layout check |
| `WorldView.Showcase.cs` | Scenes for the README pictures (`--readme-shots`) |

Renderers live in their own classes: `WorldTerrain` (terrain meshes, ground textures, light maps), `WorldBuildings`,
`WorldTrees`, `WorldGrass`, `WorldWater`, the sky and models. They load in the background through `WorldLoader`.

## Tools

`enum Tool { None, Scene, Paths, Quests, Create, Teleports, Zones, Domains, Terrain }`. The user sees **World editing**
as one tool; inside it is still `Tool.Scene` (Objects pill) and `Tool.Terrain` (Terrain pill). `SwitchWorldPill`
moves between them without asking anything; `WorldEditOpen` is true for both.

A tool's panel replaces the tool menu in the floating panel on the right. Every tool follows the same package
(chapter 8): Esc steps back then leaves, unsaved work asks before it is lost, Ctrl+Z/Ctrl+Y, Ctrl+C/Ctrl+V where it
makes sense, one Save with a confirmation window and a backup.

## Drafts — nothing is written until Save

Every editable thing has a **draft** in `PerfectWorld/WorldEdit/` that holds the changes in memory, with its own undo:

| Draft | Edits |
|---|---|
| `WorldDraft` (+ `DraftLoader`) | Buildings, models, trees: original and current transform, deleted, added copies; pieces of 1 km load around the camera |
| `TerrainDraft` | Ground texture masks and heights; owns `WaterDraft` (water) and `LightDraft` (shadow and light) |
| `GrassDraft` | Grass and flower areas |
| `LightDraft` | Light map pixels of the touched 256 m areas, day and night |

The renderers draw the draft live (for example `WorldTerrain.SetLight` uploads the painted light map), so the user sees
the result before saving.

## Saving World editing

`SaveWorld` saves in this order:

1. **Objects and grass** — `DraftSaver.Plan` builds a `SavePlan` (file → new bytes) on a background task; then
   `DraftChecks` (game rules), the confirmation with a summary, and `DraftSaver.Write` (with a progress bar).
2. **Terrain and water** — `TerrainDraft.Files` builds the files, `TerrainSaver.Write` writes them. Water rereads the
   `.ecwld` / `.ecbsd` from disk, which already contain step 1, and the objects scene in memory is refreshed after.

One summary window is shown at the end. Each saver writes all its files or none.

### What the objects save does to the server

When buildings move, appear or disappear, `DraftChecks.BuildingEffects` rebuilds, around them:

- `map.bht` — the collision brushes of the building (taken from the `.bmd`, moved/rotated);
- `movemap\N.rmap` + `N.dhmap` — where monsters walk, with floors on bridges (`MoveMapFile.UpdateWithFloors`:
  flood fill from walkable neighbours, 1 m step, a vertical ray finds the floor, a body of 0.45 m checks walls).
  Besides the brushes, the **mesh** of the edited buildings counts (`MeshObstacles`): a triangle in the body band
  (floor + 0.5 to + 1.8 m) is a wall, a lying triangle is a floor — so a bridge without brushes is still walked on and
  its rails still block;
- `airmap\N.octr` — flight space (`OctreeFile.Refresh`: a 4 m cube touching a brush becomes a border cube).

Trees have no collision brushes in 1.5.5, but the original `.rmap` has a closed circle around each trunk; copied or
moved trees get the same circle (`TreePassage`, radius learned per tree type from the original trees) plus the
footprint of the trunk and its roots from the ground to half the tree height, grown by 0.5 m (`MeshFootprint.Tree`);
branches do not block. Bamboo-like types (radius 0 in the original map) stay passable.

## Undo across pills

Each draft has its own undo stack. `WorldDid(kind)` records the order of actions (`T` terrain/water, `G` grass,
`O` objects, `S` scene effects, `L` shadow/light); `WorldUndo` undoes the last one whatever pill is open. A recorded
step whose draft was saved or discarded fails and is skipped.

## Input

The mouse position is read once per frame (`HoverUpdate` from `Step`): the ray to the ground, gizmo highlight, brush
circles and the status bar. Brush strokes are filled between frames so a fast mouse leaves no gaps.

## Coordinates

Game coordinates: X east, Y up, Z north. OpenGL here uses −Z for north, so `Gl(x, y, z)` mirrors Z. The world map is
8 × 11 pieces of 1024 m; each piece has 4 × 4 areas of 256 m (texture masks and light maps are per area).
