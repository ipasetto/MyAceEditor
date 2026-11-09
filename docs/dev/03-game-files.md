# 3. Game files

The complete byte-level descriptions are in the knowledge base [`docs/kb/PW-KB.md`](../kb/PW-KB.md) (Portuguese,
search the index `§0`). This chapter tells you which file is read by which class, and on which side it lives.

## Client (`element\`)

| File | Class | Notes |
|---|---|---|
| `data\elements.data` | `CfgElementsReader` / `ElementsWriter` | The item/NPC/monster database. Layout comes from `PerfectWorld/cfg/*.cfg` chosen by version |
| `data\tasks.data` (+ `tasks.data1..N` on newer clients) | `TasksFile` | Quests |
| `data\gshop.data` | `GShopFile` | In-game shop (client side) |
| `data\aipolicy.data` | `AiPolicyFile` | Monster AI; the server has its own copy |
| `*.pck` / `*.pkx` | `PckArchive`, `PckWriter` | Angelica packages (`surfaces`, `interfaces`, `configs`, `models`, `litmodels`…). Split packages (`.pck` + `.pkx`) are supported |
| `maps\<map>\<map>.trn2` | `TerrainFile` | Terrain header, texture list, pieces |
| `maps\<map>\<map>_N.t2bk` | `TerrainFile`, `HeightFiles` | Terrain heights of piece N |
| `maps\<map>\<map>_N.t2mk` | `GroundMaskFile` | Ground texture masks (layers) |
| `maps\<map>\<map>_N.t2lm` / `.t2lm1` | `TerrainFile`, `LightDraft` | Ground light map, day / night: one DXT1 DDS per 256 m area |
| `maps\<map>\<map>.ecwld` | `EcwldFile`, `WorldScene` | Scene: buildings, models, trees, effects, sounds, water references, per block |
| `maps\<map>\<map>.ecbsd` + `bsdata\N.dat` | `WorldScene`, `WaterDraft`, `GrassDraft` | Scene data blocks: ornaments, critters, water areas, grass |
| `maps\<map>\precinct.clt`, `region.clt` | `PrecinctFile` | Zones: names, music, rules |
| `litmodels.pck` | `BmdModel`, `PckWriter` | Building models (`.bmd`) with their collision brushes |

## Server (`config\` of the game server)

| File | Class | Notes |
|---|---|---|
| `elements.data`, `tasks.data`, `gshopsev.data`, `aipolicy.data` | same readers as the client | Must match the client (the Verification page checks the pair) |
| `<map>\npcgen.data` | `NpcGenFile` | Spawn points of monsters, NPCs and resources |
| `<map>\map.bht` | `BhtFile` | Collision brushes of the whole map (buildings) |
| `<map>\map\N.hmap` | `HeightFiles` | Server height map of piece N (513 × 513 floats) |
| `<map>\movemap\N.rmap` / `N.dhmap` | `MoveMapFile` | Where monsters can walk (1 bit per metre) and floor heights above the ground (bridges, floors) |
| `<map>\airmap\N.octr` | `OctreeFile` | Flight space for flying monsters (compact octree) |
| `<map>\watermap\N.wmap` | `WaterDraft` | Water rectangles the server uses |
| `domain.data`, `domain.sev` | `DomainFile`, `DomainMap` | Clan war territories |

## Version support

`elements.data` is read through 170 `.cfg` layouts (v6–v623). Other formats are read by version-aware readers.
Development and testing follow **1.5.5**; see [`docs/COMPATIBILIDADE.md`](../COMPATIBILIDADE.md) for what was ever
tested on other versions.

## Adding support for a new format — the checklist

1. Write a reader in `PerfectWorld/` that turns bytes into objects and keeps unknown bytes as they are.
2. Write the writer and a command-line round-trip mode in `Program.cs` that reads and writes every file of that kind
   in a real client and reports how many came back byte-identical.
3. Only when the round trip is 100% identical, add editing in the UI.
4. Record the format in `docs/kb/PW-KB.md` (section from the `§0` index) and the new file in
   `docs/kb/ENGINE-KB.md` §E6.
