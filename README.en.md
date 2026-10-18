# MyAceEditor

<p align="center">
  <a href="README.md"><img src="https://img.shields.io/badge/Portugu%C3%AAs-ler-555?style=for-the-badge" alt="Português"></a>
  <a href="README.en.md"><img src="https://img.shields.io/badge/English-selected-2ea44f?style=for-the-badge" alt="English"></a>
</p>

[![License: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)
[![Contributions welcome](https://img.shields.io/badge/contributions-welcome-brightgreen.svg)](CONTRIBUTING.md)

**Perfect World editing engine (Angelica engine)**: a native C#/WinForms desktop app (.NET 10) to read
and edit the game data, client and server, in a single window.
An **open, community project**: the goal is to make Perfect World 100% editable with a free tool.

![Elements editor](docs/img/editor.png)

## Features

- Opens the Perfect World folder and **reloads it automatically** next time.
- Reads and **writes** `elements.data` (about 40k items in 95 blocks), with byte-identical save verification and automatic backup.
- **Real game icons**, read straight from `surfaces.pck`, with nothing to extract.
- Layout in the style of the classic editors (Rody's Angelica Editor): blocks grouped by category, type
  filter, search, sub-tabs per field group, names in the official game color, drop tooltips.
- **Double-click** opens dedicated editors: classes (checkboxes), addons with their description,
  item/monster/recipe picker with icons.
- **Links**: what each item uses and who uses it, "dropped by" with chance, all clickable.
- Multi-select and bulk actions (clone, delete, change a field on many items).
- **Quests** (`tasks.data` v93): fields, texts, dialogs and subquests.
- **Client × server check**: points out what makes login fail with "wrong version".
- **MCP** mode: the same operations available to AI agents.
- Interface in Portuguese, English and Simplified Chinese (`lang/*.json`).

## Run

Requires Windows and the .NET 10 SDK.

```bash
dotnet run
```

Then **File → Open Perfect World folder…** and pick the folder that contains `element\`.

## Build the executable

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true -o dist
```

Produces `dist\MyAceEditor.exe`, a single file that runs without installing anything. Also copy the `lang\` folder next to the `.exe` (it holds the interface languages; see [CONTRIBUTING.md](CONTRIBUTING.md) to translate).

## Status

| Area | Status |
|---|---|
| `elements.data`: reading | ✅ ~98.6% (Territory War tail missing) |
| `elements.data`: writing | ✅ byte-identical, with automatic backup |
| Native `.pck`/`.pkx` + icons | ✅ reading (writing 🔜) |
| Field editors, links, "dropped by", multi-select | ✅ |
| Quests (`tasks.data` v93) | ✅ (reward editing 🔜) |
| Official item description and color (`configs.pck`) | ✅ reading |
| Client × server check | ✅ |
| GShop, AI Policy, NPCGen | 🔜 formats already documented |

Reference version tested: Perfect World 1.4.4 client/server (`elements.data` v60, `tasks.data` v93).
What's coming next is in the **[roadmap](docs/ROADMAP.md)**.

## Contributing

Contributions are very welcome! Read the **[contribution guide](CONTRIBUTING.md)** and pick a task
from the **[roadmap](docs/ROADMAP.md)** — the ones marked 🟢 are good first tasks. Docs are mostly in
Portuguese, but issues and pull requests in English are welcome.

## Documentation

| Document | What for |
|---|---|
| [docs/ARQUITETURA.md](docs/ARQUITETURA.md) | How the engine and Perfect World work: diagrams, layers, flow, formats |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Future tasks organized in milestones |
| [docs/PARIDADE-RODY.md](docs/PARIDADE-RODY.md) | Feature-by-feature checklist against the classic editors |
| [docs/kb/PW-KB.md](docs/kb/PW-KB.md) | **Game** knowledge base: client, server, protocol, binary formats, versions |
| [docs/kb/ENGINE-KB.md](docs/kb/ENGINE-KB.md) | **Engine** knowledge base: what is implemented and how |
| [.claude/skills/pw-engine](.claude/skills/pw-engine/SKILL.md) | Claude Code skill that teaches the agent to use and maintain the KBs |

## License

[GNU General Public License v3.0](LICENSE). You may use, study, modify and distribute MyAceEditor;
modified versions that are distributed must also be open source under the GPL-3.0.

## Credits

Format knowledge comes from studying community tools and documentation
(sELedit, sTASKedit, sGSHOPedit, AIPolicyEditor, sNPCedit, sMAPtool, uPCK, UIEdit, Rody's Angelica Editor,
the mmorpg-devs.ru, RaGEZONE and PWDevGroup forums), listed in [PW-KB §R](docs/kb/PW-KB.md).
The code in this repository is original.

**Contributors:** LAOCHEN (RaGEZONE) — multi-version elements.data reader (170 .cfg, v6–v623),
MPPC decoder, progress/cancel window, regression tests and Simplified Chinese translation
(`lang/zh-CN.json`, docs in [docs/zh-CN](docs/zh-CN)). The full Chinese version is in the
`contrib/laochen-zh-CN` branch.

Perfect World is a trademark of Perfect World Co., Ltd. This project is not affiliated with it and does
not distribute game files.
