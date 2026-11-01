---
name: pw-engine
description: Arquitetura do Perfect World (client+server, protocolo, formatos), mapa das fontes C++ do jogo (servidor 1.5.3, cliente 1.5.3, engine Angelica) e da engine/editor MyAceEditor. Use SEMPRE (regra do usuario) antes de qualquer trabalho que envolva Perfect World, elements.data/tasks.data/gshop/aipolicy/npcgen, .pck/.pkx, servidor/daemons/gs, o projeto MyAceEditor, parser .cfg, itens/skills/NPCs/quests, GM, ou icones/UI do jogo.
---

# PW Engine — mapa para pular direto a informacao

**Regra do usuario: consultar esta skill antes de mexer em qualquer coisa de PW.** Ela nao e para ler inteira: e um
indice. Cada camada abaixo diz onde dar o Grep. Custo alvo: 1-3 Greps pequenos por pergunta.

## 1. Por onde comecar (ordem barata)
| Pergunta | Onde | Como |
|---|---|---|
| O que esta feito / falta? | `C:/Godot/MyAceEditor/docs/AFAZERES.md` | ler o topo |
| Regra que derruba o gs / item que nao funciona | `C:/Godot/MyAceEditor/docs/REGRAS-DO-JOGO.md` | ler a tabela da secao |
| Fato do JOGO (formato, campo, daemon, protocolo, GM, mapa) | `docs/kb/PW-KB.md` | Grep `## §0` (indice) -> Grep `## §S<n>` |
| Onde no NOSSO codigo / o que ja existe | `docs/kb/ENGINE-KB.md` | `§E2` capacidade->arquivo->§PW-KB, `§E6` arquivos, `§E7` features |
| Onde no codigo C++ do JOGO (gs, daemons, cliente, engine) | `SOURCE-MAP.md` (nesta pasta) | Grep `§M0` (assunto -> arquivo:simbolo) ou o nome do arquivo; depois Grep o simbolo no arquivo |
| Arquitetura para humanos | `docs/ARQUITETURA.md` | so ao mudar camada/fluxo |

Caminhos `docs/...` sao relativos a `C:/Godot/MyAceEditor`. Copia versionada desta skill: `C:/Godot/MyAceEditor/.claude/skills/pw-engine/`
(a do usuario em `~/.claude/skills/pw-engine/` e copia dela; ao mudar uma, copiar para a outra).

## 2. Fontes do jogo (D:/Perfect World/source) — resumo; detalhe no SOURCE-MAP.md
- `source_server_153/cgame/gs` = gs (mapa, itens, addons, IA, GM, instancias, quests). `gs/template` = elementdataman/itemdataman.
- `source_server_153/cnet` = daemons (gdeliveryd, gamedbd, glinkd, gauthd, gfaction, uniquenamed, logservice, gacd) + `rpcalls.xml`.
- `source_client_153/CElementClient` = cliente (EC_* jogo, Dlg* janelas), `CElementData` = templates do elements.
- `Angelica2.2_src/Angelica2.2` = engine (A3D, AFile .pck, AUI).
- Pastas com data (cgame_2014..., cnet_2015..., release_...) = mesmas fontes em outras datas: usar as sem data.
- O gs do servidor local (`/home/gamed/gs` no WSL) vem COM simbolos: `objdump -t -C /home/gamed/gs | grep <Nome>` acha a funcao no binario.
- Textos do cliente em GBK (cp936). Regerar o mapa: `python -I .claude/skills/pw-engine/mkmap.py` (quando entrarem fontes novas).

## 3. Como ler sem gastar
1. Nunca ler PW-KB (~40k tokens) nem SOURCE-MAP (~35k tokens) inteiros: Grep o cabecalho `## §...` ou a palavra.
2. ENGINE-KB e pequena: pode ler inteira quando for mexer no codigo.
3. Codigo C++: Grep do simbolo com `-n` e ler so ~40 linhas em volta.

## 4. Regras do projeto
- OBRIGATORIO: respeitar docs/REGRAS-DO-JOGO.md (ID unico, loja <= 65534 no 1.5.x, subtipos, configs de ID fixo, referencias das quests, trava de velocidade...). Checagens em PerfectWorld/ItemRules.cs e QuestRules.cs rodam ao salvar; nunca desligar. Regra nova vai no doc e no codigo.
- Recurso/token do usuario e limitado: nada pesado (RE profunda, fetch grande, loops longos) sem autorizacao explicita.
- Nativo C#/WinForms (sem web). Icones e textos sempre do proprio jogo. Telas no padrao UiKit (barra lateral, barra de comandos, cartao de estado, pilulas, secoes). Toda acao com Ctrl+Z.
- Editar dado do jogo = editar o PAR client+server (PW-KB §S7).
- Formato so ganha edicao depois de round-trip byte-identico; backup (fora do cliente, BackupStore) + escrita atomica sempre.
- Repositorio publico: nunca gravar senhas, chaves de rede (isec/osec), dados pessoais ou bytes de patch nas KBs.

## 4b. Layout das telas (REGRA DO USUARIO: toda tela nova ou mexida segue isto; pecas em UI/UiKit.cs)
- Pensado no usuario, o mais intuitivo possivel; textos simples, sempre via Loc.T (pt-BR/en/ru/zh-CN).
- Areas de uma janela: barra lateral `NavBar` (icone + nome, dica diz para que serve), nunca abas em cima.
- Comandos: barra estilo Word `UiKit.Ribbon/Cmd/Menu` (icone em cima, texto embaixo, dica; acao principal em azul).
  Botoes soltos: `UiKit.Decorate` (plano, icone pela funcao; cuidado: "mundo" contem "undo").
- Estado que liga/desliga (servidor, jogo): `StatusCard` fixo no topo (bolinha + titulo + detalhe), atualizado sozinho.
- Sub-paginas: pilulas `SegmentBar`. Muitos campos: agrupados por funcao, `SectionList` vertical (icone + contagem) +
  titulos de secao na grade (`SectionTag`/`PaintSection`), colunas proporcionais nome | valor | significado.
- Preferencias: tela `PreferencesWindow` (menu Preferencias); 1 linha por opcao = icone, nome, explicacao, estado REAL
  lido ao abrir e interruptor `ToggleSwitch`; opcao nova = mais um `Pref` em `EditorForm.OpenPreferences`
  (secao, icone, nome, explicacao, ler, aplicar, pergunta antes se derruba algo).
- Telas vazias dizem o que fazer; listas com nome em destaque + 2a linha cinza; telas escuras (3D) com `DarkBarRenderer`.
- Icones = glifos Segoe Fluent/MDL2 desenhados (`UiKit.Icon`), sem arquivos de imagem; conferir o glifo antes (desenhar
  candidatos num PNG).
- Toda acao tem Ctrl+Z/Ctrl+Y (regra do usuario). Sair com mudanca nao salva: Salvar / Descartar / Cancelar.
- Conferir por captura sem abrir na tela do usuario: --snapshot, --gm-shot, --adm-shot, --wq-shot, --prefs-shot.

## 5. Protocolo de atualizacao (OBRIGATORIO ao descobrir algo)
- REGRA DO USUARIO: todo CODIGO novo e toda INFORMACAO nova sao registrados na mesma sessao, no lugar certo deste mapa,
  seguindo a mesma estrutura (secao §, linha telegrafica, tag de confianca), para que a proxima consulta seja um pulo e
  nao uma busca:
  | O que surgiu | Onde registrar |
  |---|---|
  | arquivo novo no MyAceEditor | ENGINE-KB §E6 (1 linha: arquivo -> o que faz) |
  | feature nova/mudada no MyAceEditor | ENGINE-KB §E7 (+ §E2 se for capacidade nova) |
  | fato do jogo (formato, campo, regra, daemon, mapa) | PW-KB, secao certa (§0 diz qual) |
  | fato de GM | PW-KB §S31 |
  | onde algo fica no C++ do jogo (arquivo:funcao) | SOURCE-MAP.md §M0 (1 linha: assunto -> arquivo:simbolo) |
  | regra que derruba o gs / item que nao funciona | docs/REGRAS-DO-JOGO.md + checagem em ItemRules/QuestRules |
  | fonte nova em D:/Perfect World/source | rodar mkmap.py (e acrescentar a area em AREAS se for pasta nova) |
  | etapa iniciada/concluida | docs/AFAZERES.md |
  Depois de mudar esta skill ou o SOURCE-MAP, copiar para `~/.claude/skills/pw-engine/` (as duas copias iguais).
- REGRA DO USUARIO: TODA informacao nova sobre QUALQUER estrutura do PW (formato, campo, struct, protocolo, comando, regra do gs/cliente, config, arquivo) entra na KB na mesma sessao, mesmo pequena. Na duvida: Grep na KB; se nao achar, grave.
- Fato do JOGO -> PW-KB (secao certa). Fato da ENGINE/codigo -> ENGINE-KB. Achou onde algo fica no codigo C++ do jogo -> acrescentar 1 linha no `§M0` do SOURCE-MAP.md (assunto -> arquivo:simbolo).
- Fato de GM -> SEMPRE em PW-KB §S31. Antes de mexer no painel do GM, leia §S31 + docs/GM-PAINEL.md.
- Toda linha com tag conf ([C][A][B][F][M][W][I][?], e [T]=testado no nosso codigo).
- Contradicao resolvida -> §Z e corrige a linha original. Duvida nova -> §Q.
- Feature pronta -> ENGINE-KB §E8 -> §E7; arquivo novo -> §E6; mudou camada/fluxo -> docs/ARQUITETURA.md.
- Telegrafico, sem dumps; preferir referencia cruzada (PW-KB§Sx, SOURCE-MAP§Mx).
- Ao fim de uma tarefa que gerou conhecimento novo, atualizar a KB na mesma sessao e avisar o usuario em 1 linha.
