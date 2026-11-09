# 1. Getting started

## Requirements

- Windows 10 or 11, 64-bit.
- The **.NET 10 SDK**.
- A Perfect World client to test with (the folder that contains `element\`). Game files are **not** in the
  repository; use your own, and work on a copy.
- Optional: a 1.5.5 server folder (for the server side of the data, the 3D World monster files, the local test server
  in WSL).

## Build and run

```bash
dotnet build            # compiles into bin/Debug/net10.0-windows/
dotnet run              # builds and opens the editor
```

The first time, use **File → Open Perfect World folder…** and pick the folder that contains `element`. The last folder
is remembered and opened automatically next time.

Distribution build — one compressed `MyAceEditor.exe` with .NET inside, written to `dist\`:

```bash
dotnet publish -c Release -p:PublishProfile=Release-win-x64
```

The profile is `Properties/PublishProfiles/Release-win-x64.pubxml` (self-contained, single file, compressed, no
ReadyToRun; trimming must stay off because WinForms does not support it). Ship the whole `dist\` folder: the program
needs `lang\`, `PerfectWorld\cfg\`, `Server\` and `glfw3.dll` next to it.

## Folder layout

| Folder / file | Contents |
|---|---|
| `Program.cs` | Entry point. With no arguments it opens the editor; with `--something` it runs a command-line mode (tests, reports, screenshots — see chapter 7) |
| `PerfectWorld/` | Everything that knows bytes: readers and writers of every game format, game rules, backups, logging |
| `PerfectWorld/WorldEdit/` | The editable side of the 3D World: drafts, savers, monster path and flight files, self-tests |
| `PerfectWorld/cfg/` | Layout files (`.cfg`) that describe `elements.data` for each version (170 layouts, v6–v623) |
| `UI/` | Everything that is shown: the main window (`EditorForm`), one view per tab, the 3D World (`WorldView*`), shared UI pieces (`UiKit`) |
| `Model/`, `Store/`, `Changes/` | The neutral data model (module → list → item → field), the loaded module state, change sets and undo history |
| `Tools/` | The same read/write operations exposed to an AI agent over MCP (`--mcp` mode) |
| `Server/` | Shell scripts the app copies into the local Linux (WSL) test server |
| `Plugins/` | A separate Linux program for server event plugins (its own project, `Plugins/MyAcePlugins.csproj`) |
| `lang/` | Interface text catalogues (`en.json`, `ru.json`, `zh-CN.json`) and the texture name glossary |
| `Tests/` | Regression data and scripts for the `.cfg` layouts and real files |
| `docs/` | Documentation; `docs/kb/` holds the knowledge bases, `docs/img/` the README pictures |

## Where the app keeps its things

| What | Where |
|---|---|
| Settings (last folder, language, favourites, brush sizes…) | `%AppData%\MyAceEditor\` |
| Log (one file per day) | `%AppData%\MyAceEditor\logs\myace-YYYYMMDD.log` |
| Backups of every file the app changed, and backup sets | `%LocalAppData%\MyAceEditor\backups\` (sets in `backups\conjuntos\`) |

When something goes wrong, the user sees a short message and the technical detail goes to the log (`AppLog.Error`).
Read the log first when you debug.

## Code style in one paragraph

C# with nullable reference types on, file-scoped namespaces, `partial` classes to split big windows by feature
(`WorldView.TerrainTool.cs`, `WorldView.WaterTool.cs`…). Comments and identifiers are mostly in Portuguese (the
project's working language); user-facing text is always passed through `Loc.T(key, portugueseText)` and translated
in `lang/*.json`. The build must stay with **zero warnings**.
