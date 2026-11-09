# MyAceEditor — developer guide

This guide is for a programmer who wants to read, change or extend MyAceEditor on their own, without an AI
assistant. It explains how the program is organised, how data flows from the game files to the screen and back, how
saving stays safe, how the 3D World editor works inside, and the rules every change has to follow.

MyAceEditor is a native Windows desktop app (C#, .NET 10, WinForms, OpenGL through OpenTK). It reads and writes the
data files of Perfect World — client and server — without external tools.

> **Game version.** Everything is built and tested on **Perfect World 1.5.5** (client PWI 1.6.2 = v1.5.5 and the
> 1.5.5 server). Many readers handle other versions, but only 1.5.5 is checked on every change.

## Reading order

| # | Chapter | You will learn |
|---|---|---|
| 1 | [Getting started](01-getting-started.md) | Build, run, the folder layout, where settings and logs go |
| 2 | [Architecture](02-architecture.md) | Layers, the open → edit → check → save flow, the main classes |
| 3 | [Game files](03-game-files.md) | Which game file each class reads and writes, and where the formats are documented |
| 4 | [The 3D World inside](04-3d-world.md) | `WorldView` and its tools, drafts, saving, undo, the monster path files |
| 5 | [Saving and backups](05-saving-and-backups.md) | Atomic writes, the backup store, backup sets and sessions, restore |
| 6 | [Localization](06-localization.md) | `Loc.T`, the `lang/*.json` catalogues, the layout check |
| 7 | [Testing](07-testing.md) | Command-line test modes, screenshot modes, headless rules |
| 8 | [Conventions and rules](08-conventions.md) | UI kit, shortcuts, error handling, the "tool package", game rules |
| 9 | [Adding a tool](09-adding-a-tool.md) | A step-by-step recipe for a new 3D World tool |
| 10 | [Glossary](10-glossary.md) | Perfect World and project terms |

## Other documents in the repository

| Document | Language | What for |
|---|---|---|
| [`docs/kb/PW-KB.md`](../kb/PW-KB.md) | Portuguese | Game knowledge base: every format, field, protocol and rule found so far, with confidence tags |
| [`docs/kb/ENGINE-KB.md`](../kb/ENGINE-KB.md) | Portuguese | What is implemented and in which file (§E6 files, §E7 features) |
| [`docs/REGRAS-DO-JOGO.md`](../REGRAS-DO-JOGO.md) | Portuguese | Rules that crash the server or break items/quests, enforced on save |
| [`docs/ARQUITETURA.md`](../ARQUITETURA.md) | Portuguese | Architecture with diagrams (older, shorter version of chapter 2) |
| [`docs/COMPATIBILIDADE.md`](../COMPATIBILIDADE.md) | Portuguese | What was tested on which game version |
| [`CONTRIBUTING.md`](../../CONTRIBUTING.md) | Portuguese | How to contribute, commits, legal notes |

The knowledge bases are large: do not read them from top to bottom. Each starts with an index (`§0`); search for the
section you need.
