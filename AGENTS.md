# MyAceEditor — instruções para IA

Antes de mudar qualquer coisa que crie, clone, copie, exclua ou mude o ID de dados do jogo (elements.data,
tasks.data, gshop, configs.pck, npcgen, aipolicy), leia e respeite **[docs/REGRAS-DO-JOGO.md](docs/REGRAS-DO-JOGO.md)**.
Essas regras vêm do código do servidor e de testes no jogo; quebrá-las derruba o servidor ou faz itens e quests
pararem de funcionar. As checagens estão em `PerfectWorld/ItemRules.cs` e `PerfectWorld/QuestRules.cs` e rodam ao
salvar: nunca as desligue nem contorne. Regra nova descoberta entra no documento e no código.

Também: arquitetura em [docs/ARQUITETURA.md](docs/ARQUITETURA.md), bases de conhecimento em `docs/kb/`
(PW-KB = jogo, ENGINE-KB = este código) e como contribuir em [CONTRIBUTING.md](CONTRIBUTING.md).
