# GitHub page, developer docs and smaller build — design (approved 2026-11-09)

## Goal
Show what MyAceEditor can do (above all the 3D World editor) on the GitHub page, give a programmer without an AI
assistant a way to understand and work on the code, and make the distributed `.exe` smaller without breaking anything.
The release itself is NOT part of this work: the user will publish v1.0 (not a test build) after finishing the in-game
tests.

## Decisions (user, 2026-11-09)
- Page and developer docs in **English**; developer docs live in `docs/dev/` (Markdown, versioned with the code).
- Screenshots taken by us, automatically, with the app in English, on the PWI 1.5.5 `world` map.
  **No personal data**: no login, account or character names, no folder path with the user's name.
- Build: self-contained, single file, **compressed, no ReadyToRun** (measured: `.exe` 161 MB → 54 MB; the 20
  3D World checks and the layout check pass the same; start-up +0.13 s). Framework-dependent and trimming rejected
  (needs a runtime install that may refuse Windows 7; trimming is unsupported for WinForms).

## Parts
1. **Publish profile** `Properties/PublishProfiles/Release-win-x64.pubxml` with the settings above, so the daily build
   folder and the tests do not change. README and `docs/ARQUITETURA.md` use
   `dotnet publish -c Release -p:PublishProfile=Release-win-x64`.
2. **README rewritten** (not patched):
   - hero: name, one-line pitch, badges, download link, wide 3D World image;
   - colour map of the areas (shields badges + coloured markers, since GitHub Markdown has no coloured text):
     3D World (green), Game data (blue), Interface (purple), Translation (orange), Server & GM (red), Safety (grey);
   - **3D World first and largest**: one subsection per tool with an image, what it does and its keys —
     World editing (Objects pill / Terrain pill: texture, height, water, grass & flowers, shadow & light), Paths & areas,
     Quests, NPCs & monsters, Territories, Zones, Teleports, Save & send to server; plus "what happens by itself"
     (monster paths and flight rebuilt, undo everywhere, one backup per session, checks before saving);
   - the other areas, each with an image; tested versions, safety, install, build, docs, contributing, licence, credits.
3. **Screenshots** in `docs/img/en/` from the headless capture modes (`--snapshot`, `--world-shot`), cropped where a
   panel is the subject; small capture hooks added in code where a tool needs something selected for the picture.
4. **Developer docs** `docs/dev/`: index, getting started (build, run, CLI tests), architecture (layers, open → edit →
   check → save), game file formats (pointers to the knowledge base), 3D World internals (tools, drafts, save, undo),
   safe saving (atomic write, backup store, backup sets and sessions), localization (`Loc.T`, `lang/*.json`, layout
   check), testing (`--objects-test`, `--world-shot`, headless rules), UI conventions and project rules (UiKit,
   universal shortcuts, error handling, the tool package), how to add a tool, game rules and glossary.

## Done when
- `dotnet publish -c Release -p:PublishProfile=Release-win-x64` gives the compressed single `.exe`;
- README renders on GitHub with every image present and no broken links; no personal data in any image;
- `docs/dev/` covers the list above and every link resolves; everything committed and pushed; no release created.
