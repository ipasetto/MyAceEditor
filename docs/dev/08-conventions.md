# 8. Conventions and rules

These rules come from real problems (crashed servers, lost work, confused users). A change that breaks one of them is
not finished.

## Engineering rules

1. **Round trip before editing** — a format is editable only after read + write gives identical bytes.
2. **Never destroy user data** — backup + atomic write for every file (chapter 5).
3. **Client and server together** — edit the pair, never one side.
4. **Formats don't know the UI; the UI doesn't know bytes.**
5. **Native and light** — no web views, no heavy dependencies; icons and texts come from the game itself.
6. **Error handling everywhere** — validate before acting; every file, network, server and process call in
   `try/catch`; a short plain message for the user (`Loc.T`) plus the technical detail in the log; never a half-written
   file or a half-sent operation; nothing fails silently.
7. **Game rules are enforced on save** — `PerfectWorld/ItemRules.cs` and `QuestRules.cs` implement
   [`docs/REGRAS-DO-JOGO.md`](../REGRAS-DO-JOGO.md). Never switch them off. A new rule goes into the document and the code.
8. **Zero build warnings.**

## The tool package

Every tool (3D World tools, editor windows) has all of these:

1. A question before unsaved work is lost — on Esc, the back arrow, the close button and closing the window.
2. Ctrl+C / Ctrl+V of what it edits, with a preview following the mouse and **one object per click** (after the click
   the new object is selected and insertion stops).
3. A close button you can click.
4. **Esc** cancels the current step first, then leaves the tool.
5. **Ctrl+Z / Ctrl+Y** on every action.
6. All-or-nothing save with a **visible confirmation** (`SavedDialog`), and *Send to server now* when needed.
7. One backup set per save; reload after a backup is restored (`BackupSets.Restored`).
8. Command-line tests never show a question and end the process.
9. With a tool open, the 3D World right-click menu does not open; the mouse wheel never changes typed fields.
10. Layout checked on a picture: buttons never glued together, no cut text in any language, buttons of a group the
    same size, the key shown under the button name, the panel reaching the bottom of the window.

## Universal shortcuts

The same job has the same key everywhere:

| Key | Job |
|---|---|
| **B** | brush |
| **E** | eraser |
| **I** | eyedropper |
| **Esc** | step back, then leave |
| **Ctrl+Z / Ctrl+Y** | undo / redo |
| **Ctrl+C / Ctrl+V** | copy / paste |
| **Ctrl + wheel** | brush size |

When one panel has several sections with these jobs (ground texture, shadow and light, grass), B/E/I act on the
section in use. Extra modes of a section use numbers (height 1–4, water 5–6, shadow/light 7–8). A key only turns a
tool on; Esc or right-click turn it off.

## UI kit and layout

Shared pieces are in `UI/UiKit.cs`: the command ribbon (`Ribbon`, `Cmd`), navigation bar (`NavBar`), status card,
segmented pills, section lists, icons drawn from the Segoe Fluent/MDL2 font (`UiKit.Icon`, no image files), the dark
toolbar renderer of the 3D World and `SavedDialog`. Reuse them; check a new glyph by drawing candidates before choosing.

Design for the least technical user: talk about what the player sees, not about files; one concept per step; nothing is
saved before the Save button, which is always visible and shows a summary in sentences.

## Knowledge bases

Every new fact about the game (a format, a field, a rule) goes into `docs/kb/PW-KB.md` in the same change, with a
confidence tag (`[C]` read in the game source, `[T]` tested in our code…). Every new file of ours goes into
`docs/kb/ENGINE-KB.md` §E6 and every new feature into §E7.
