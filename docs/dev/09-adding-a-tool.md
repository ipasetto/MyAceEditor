# 9. Adding a tool to the 3D World

A recipe, following how the existing tools are built. Read chapters 4, 5 and 8 first.

## 1. Decide where it lives

- A new way to edit the **map itself** (something drawn on the ground or placed in the scene) usually belongs inside
  **World editing**, as a section of the Objects or Terrain pill, so it shares the one Save and the undo order.
- A separate job (like Zones or Teleports) gets its own entry in `enum Tool` and its own tile in the menu.

## 2. The draft (`PerfectWorld/WorldEdit/`)

Create a class that holds the edit in memory and knows nothing about the UI:

```csharp
public sealed class MyDraft(TerrainFile t)
{
    public bool Dirty => _undo.Count > 0;
    public void BeginStroke() { ... }
    public List<...> Paint(...) { ... }            // returns what changed, for the renderer
    public bool EndStroke() { ... }                // pushes one undo step if something changed
    public List<...>? Undo(bool redo) { ... }
    public Dictionary<string, byte[]> Files(List<string>? summary = null) { ... }   // the bytes to write
}
```

Rules: the original bytes stay untouched until `Files`; `Files` rereads/validates what it builds and changes only what
was edited (a file with no change must come out identical); throw `InvalidDataException` with a plain message if the
file on disk is not what you expect.

If it is part of the terrain, hang it on `TerrainDraft` (like `Water` and `Light`) and add it to `Dirty` and `Files`, so
`SaveWorld` and the backup session pick it up with no extra work.

## 3. The UI partial (`UI/WorldView.MyTool.cs`)

- Build the section with `SectionLabel`, `BigButton` (icon + name + key) and `SmallButton`; add it to the page in the
  pill's `Init…Page`.
- Keys: reuse **B / E / I** for brush / eraser / eyedropper; use a free number for extra modes. Route them in the pill's
  key handler, and keep "one brush at a time": turning yours on turns the others off, and theirs turn yours off.
- Mouse: start in the pill's `MouseDown` handler, continue in `TexDab`-style code called from `HoverUpdate` (once per
  frame; fill the path between frames), finish in `MouseUp` → `EndStroke` → `WorldDid('X')`.
- Draw the live result: update the renderer (as `WorldTerrain.SetLight` does) instead of rewriting files.
- Undo: add a case to `WorldUndo` for your letter and a `...UndoCore(bool redo)` that refreshes the screen.
- Esc: turn your mode off in the pill's escape handler before the tool leaves.
- Abandon: when the draft is discarded, put the screen back from disk.
- Show your state in the info card (`InfoNow`) if the user picks something.

## 4. Texts

Every string through `Loc.T("area.key", "Texto em português")`; add the keys to `lang/en.json`, `ru.json`,
`zh-CN.json`. Run the layout check (chapter 6).

## 5. Error handling

Wrap file access in `try/catch (IOException or InvalidDataException or UnauthorizedAccessException)`, log with
`AppLog.Error`, and tell the user what to do in one short sentence.

## 6. Tests

- Add a check to `ObjectsSelfTest` for the format work (round trip; the save plan changes only what it should).
- Add a `--world-shot` scenario (`dft…`) for the UI: turn the mode on, make a stroke, undo, redo, abandon; no real
  dialogs.
- Run `--objects-test`, your scenario, `dftworld` and `dftlayout` in four languages.

## 7. Document

`docs/kb/ENGINE-KB.md` §E6 (new files) and §E7 (the feature); anything new about the game in `docs/kb/PW-KB.md`;
the README tool list if users will see it.
