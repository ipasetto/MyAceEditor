---
name: pw-engine
description: Arquitetura do Perfect World (client+server, protocolo, formatos) e da engine/editor MyAceEditor. Use SEMPRE que o trabalho envolver Perfect World, elements.data/tasks.data/gshop/aipolicy/npcgen, .pck/.pkx, daemons/servidor PW, o projeto MyAceEditor, parser .cfg, itens/skills/NPCs, ou icones/UI do jogo.
---

# PW Engine — como se orientar

Duas bases de conhecimento (KBs), mesmo formato (telegrafico, secoes §, tags de confianca).
Caminhos relativos a raiz do repositorio MyAceEditor:

| KB | caminho | conteudo |
|---|---|---|
| **PW-KB** (jogo) | `docs/kb/PW-KB.md` | esqueleto client+server, daemons/portas, protocolo/login, formatos (.pck, elements, tasks, gshop, aipolicy, npcgen, mapas), storage, versoes |
| **ENGINE-KB** (engine) | `docs/kb/ENGINE-KB.md` | o que ja esta implementado, mapa capacidade -> arquivos -> §PW-KB, licoes, roadmap, abertos |
| Arquitetura (humanos) | `docs/ARQUITETURA.md` | visao geral com diagramas; atualizar quando mudar camada/fluxo |

## Como ler (economia de token — PW-KB tem ~35k tokens)
1. Leia SO o indice (`## §0`) da KB, depois Grep a secao: `## §S17`, `## §E3` etc.
2. Nunca leia a PW-KB inteira sem necessidade. ENGINE-KB e pequena, pode ler inteira.
3. Comece por `ENGINE-KB §E2` (diz qual secao da PW-KB cobre cada capacidade).

## Regras do projeto
- Recurso/token do usuario e limitado: nada pesado (RE profunda, fetch grande, loops longos) sem autorizacao explicita.
- Nativo C#/WinForms (sem web). Icones e textos sempre do proprio jogo.
- Editar dado do jogo = editar o PAR client+server (PW-KB §S7).
- Formato so ganha edicao depois de round-trip byte-identico; backup + escrita atomica sempre.
- Repositorio publico: nunca gravar senhas, chaves de rede (isec/osec), dados pessoais ou bytes de patch nas KBs.

## Protocolo de atualizacao (OBRIGATORIO ao descobrir algo)
- Fato do JOGO -> PW-KB (secao certa). Fato da ENGINE/codigo -> ENGINE-KB.
- Toda linha com tag conf ([C][A][B][F][M][W][I][?], e [T]=testado no nosso codigo).
- Contradicao resolvida -> §Z e corrige a linha original. Duvida nova -> §Q.
- Feature pronta -> ENGINE-KB §E8 -> §E7; arquivo novo -> §E6; mudou camada/fluxo -> docs/ARQUITETURA.md.
- Telegrafico, sem dumps; preferir referencia cruzada (PW-KB§Sx).
- Ao fim de uma tarefa que gerou conhecimento novo, atualizar a KB na mesma sessao e avisar o usuario em 1 linha.
