# 2. Architecture

## Layers

```mermaid
flowchart TB
    UI["UI/  (WinForms + OpenGL)<br/>EditorForm, tab views, WorldView"]
    MCP["Tools/  (MCP agent mode)"]
    STORE["Store/ + Changes/<br/>loaded module, change sets, undo"]
    MODEL["Model/<br/>Module → List → Item → Field"]
    PW["PerfectWorld/<br/>readers, writers, rules, backups"]
    WE["PerfectWorld/WorldEdit/<br/>3D World drafts and savers"]
    FILES[("Game files<br/>client element/ + server config/")]
    UI --> STORE --> MODEL
    MCP --> STORE
    UI --> PW
    UI --> WE --> PW
    STORE --> PW
    PW <--> FILES
```

Two rules hold everywhere:

1. **Formats do not know the UI, and the UI does not know bytes.** A game format is a class in `PerfectWorld/` that
   reads bytes into objects and writes them back. A view in `UI/` shows and edits those objects.
2. **Client and server go together.** Many files exist on both sides (`elements.data`, `tasks.data`, `gshop` /
   `gshopsev`, `aipolicy`, the map files). Editing only one side breaks login or the game, so savers write the pair.

## The main window

`UI/EditorForm.cs` is the main window: a command ribbon on top, a navigation bar on the left and one page per area
(Elements, Quests, GShop, AI Policy, NPCGen, Interface, Server, PCK extractor, Plugins). Each page is its own control
(`NpcGenView`, `GShopView`, `AiPolicyView`, `InterfaceView`, `ServerView`, `PluginsView`…). The **3D World** is a
separate window, `UI/WorldView.cs`, opened from the toolbar; it receives the spawn points, NPC data and quest host
from `EditorForm` through delegates, so it does not depend on the main window's internals.

## The data flow: open → edit → check → save

```mermaid
sequenceDiagram
    participant U as User
    participant V as View (UI)
    participant R as Reader (PerfectWorld)
    participant C as Rules (ItemRules / QuestRules / DraftChecks)
    participant S as Saver + AtomicFileSave
    U->>V: open the game folder
    V->>R: read file (bytes → objects)
    R-->>V: objects (nothing on disk changes)
    U->>V: edit (every action goes on an undo stack)
    U->>V: Save
    V->>C: check the game rules
    C-->>V: blocking problems / warnings
    V->>S: write all files of this save (all or nothing)
    S->>S: back up each file, write beside it, verify, swap in
    S-->>V: result → saved window with a summary
```

- **Reading never changes a file.**
- **Writing only happens when the user clicks Save**, after the rules pass and the user confirms.
- **A format becomes editable only after a round trip is byte-identical**: read and write with no change must give the
  same bytes. Every reader has a command-line mode that proves it (chapter 7).

## Main classes by responsibility

| Responsibility | Classes |
|---|---|
| `elements.data` | `ElementsCfg`, `CfgElementsReader`, `ElementsReader`, `ElementsWriter`, `ElementsSchema`, `ElementsConverter` |
| Quests | `TasksFile`, `QuestBuilder`, `QuestRules` |
| Shop | `GShopFile` |
| AI | `AiPolicyFile`, `AiMeaning`, `AiTranslate` |
| Spawns | `NpcGenFile`, `NpcBuilder`, `PathFile` |
| Packages | `PckArchive` (read), `PckWriter` / `PckPatch` (write) |
| Images and icons | `DdsDecoder`, `IconAtlas`, `IconLoader` |
| Map scene | `WorldScene`, `EcwldFile`, `TerrainFile`, `BhtFile`, `PrecinctFile`, `DomainFile` |
| Game rules | `ItemRules`, `QuestRules`, `PluginRules`, `DraftChecks` (3D World) |
| Safety | `AtomicFileSave`, `BackupStore`, `BackupSets`, `AppLog` |
| Interface text | `UI/Loc.cs`, `AppLanguage`, `lang/*.json`, `TextureNameGlossary` |
| Server | `LocalServer`, `ServerAdmin`, `GameConsole`, `GmCommands`, `ServerNotice` |

`ServerNotice.Notify(file)` lights the *Save and send to server* button when a saved file still has to reach the
server.

## Threads

The UI thread owns every control. Heavy work (reading big files, building a save plan, writing a split `.pck`, loading
3D pieces) runs on a background task and comes back with `BeginInvoke`. The 3D World has its own loader
(`WorldLoader`) that hands results to the render loop a few milliseconds per frame, so the camera never waits for disk.
