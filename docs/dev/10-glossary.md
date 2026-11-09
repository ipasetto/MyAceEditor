# 10. Glossary

| Term | Meaning |
|---|---|
| **Angelica** | The game engine of Perfect World (A3D renderer, AFile packages, AUI interface) |
| **client / server** | `element\` on the player's PC / the Linux game server (`gs`, `gdeliveryd`, `gamedbd`…). Many data files exist on both sides and must match |
| **`gs`** | The game server process that runs the maps |
| **`elements.data`** | The database of items, monsters, NPCs, recipes, configs; layout depends on the version |
| **`.cfg` layout** | A text file in `PerfectWorld/cfg/` that describes the blocks and fields of one `elements.data` version |
| **`.pck` / `.pkx`** | Angelica package; big packages are split in a `.pck` plus a `.pkx` |
| **piece** | A 1024 × 1024 m square of a map (the world map has 8 × 11); terrain, collision and monster files are per piece |
| **area** | A 256 × 256 m square inside a piece (4 × 4 per piece); ground textures and light maps are per area |
| **block** | A square of the scene file (`.ecwld`) that lists the objects inside it |
| **`.ecwld` / `.ecbsd`** | The scene of a map: references to buildings, models, trees, effects, sounds, water, grass; and their data |
| **`.bmd`** | A building model (lit model) inside `litmodels.pck`, with its collision brushes |
| **brush (collision)** | A convex volume of the collision file (`map.bht`) the server uses for walls and floors |
| **`.rmap` / `.dhmap`** | Server files: where monsters can walk (1 bit per metre) and floor heights above the ground |
| **`.octr`** | Server file: where flying monsters can fly (compact octree of 4 m cubes) |
| **`.t2lm` / `.t2lm1`** | Ground light map, day / night (shadows are painted here, not in the texture) |
| **npcgen** | `npcgen.data`: spawn points of monsters, NPCs and resources |
| **draft** | The in-memory copy of an edit in the 3D World; nothing is written until Save |
| **save plan** | The set "file → new bytes" a save will write, built and checked before anything is written |
| **backup set** | A JSON list of every file of one save with its backup copy; restored all at once |
| **backup session** | All saves of one opening of the 3D World merged into one backup set per map |
| **round trip** | Reading and writing a file with no change; must give identical bytes before a format becomes editable |
| **pill** | A tab-like button at the top of a tool panel (Objects / Terrain in World editing) |
| **headless** | A test run that shows no questions and exits by itself |
| **KB** | Knowledge base: `docs/kb/PW-KB.md` (the game) and `docs/kb/ENGINE-KB.md` (this code) |
