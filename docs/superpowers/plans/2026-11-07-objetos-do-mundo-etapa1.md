# Objetos do mundo — Etapa 1 (Base) — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A ferramenta "Objetos do mundo" do Mundo 3D passa a ter um rascunho em memória dos 9 pedaços em volta da câmera, seleção de qualquer objeto à vista com cartão de informações, mover/girar/apagar construções e modelos, Ver colisão e um Salvar tudo ou nada com conferências.

**Architecture:** Um rascunho (`WorldDraft`) guarda, por pedaço de 1 km, os objetos lidos dos arquivos do mapa (`.bmd` do `litmodels.pck`, itens do `.ecwld`, ornamentos do `.ecbsd`, brushes do `map.bht`). Toda mudança é um passo com desfazer no rascunho; o desenho do mundo lê do rascunho; o Salvar monta todos os arquivos em memória, confere e grava tudo ou nada. A edição de cenário de hoje (efeitos, sons, bichos) passa a usar o mesmo rascunho, para haver um só dono do `.ecwld`.

**Tech Stack:** C# / .NET 10 WinForms, OpenTK (Mundo 3D), formatos próprios em `PerfectWorld/`, testes sem tela por linha de comando (padrão `ZoneSelfTest`).

**Spec:** `docs/superpowers/specs/2026-11-07-objetos-do-mundo-design.md`

## Global Constraints
- Só a versão 1.5.5. Dados de teste: cliente `D:/Perfect World/PW_Clientes/PWI_en_v1.6.2=v1.5.5/element`, servidor `D:/Perfect World/PW_Server/pw_server_162-155/home/gamed/config`. Testes gravam só em cópias numa pasta temporária.
- Nada é gravado nem enviado antes do Salvar. Trabalho pesado em segundo plano (`WorldLoader.Work`); a tela nunca espera.
- Tratamento de erro em tudo: validar antes de agir; arquivo/rede em `try/catch`; frase simples via `Loc.T` + detalhe em `AppLog`; nada meio gravado.
- Formato só ganha edição depois de regravação **idêntica** provada (regra do projeto).
- Toda ação com Ctrl+Z/Ctrl+Y; Esc cancela o passo e depois sai da ferramenta (`EscapeStep`/`LeaveTool`).
- Todo texto novo via `Loc.T` com a chave em `lang/en.json`, `lang/ru.json`, `lang/zh-CN.json`.
- Layout: regras do Mundo 3D (painel da ferramenta no lugar do menu, seta voltar, espaço livre embaixo).
- Respeitar `docs/REGRAS-DO-JOGO.md`; objeto sólido mudado obriga a atualizar o `map.bht` do servidor no mesmo Salvar.
- Commit ao fim de cada tarefa (mensagem em português, linha `Co-Authored-By` do projeto); push ao fim; nunca release.

## Review Focus
1. Mesmo objeto mexido, desfeito e refeito várias vezes, depois salvo: o arquivo gravado tem que ser igual ao de uma mexida só (Task 2, teste `undo-redo-salva-igual`).
2. Pedaço que não abre (arquivo ruim ou faltando) não pode derrubar a ferramenta nem os outros pedaços (Task 3, teste `pedaco-ruim-so-olhar`).
3. Jogo ou outro programa com o `litmodels.pck` aberto na hora do Salvar: nada muda em disco e o rascunho continua (Task 6, teste `pck-preso-volta-tudo`).
4. Câmera anda para longe com mudança não salva num pedaço: esse pedaço não pode ser solto (Task 4, teste `pedaco-sujo-fica`).
5. Construção "gêmea" (mesmos brushes em dois `.bmd`, ex. a05) mexida: as duas andam juntas e o `map.bht` não duplica o deslocamento (Task 6, teste `gemeas-uma-vez`).

---

### Task 1: Estudo dos formatos (portão)

Responder, com prova na linha de comando, o que a etapa precisa saber. Nada de tela.

**Files:**
- Create: `PerfectWorld/WorldEdit/ObjectsSelfTest.cs` (classe `ObjectsSelfTest`, mesmo `Report` do `ZoneSelfTest`)
- Modify: `Program.cs` (registrar `--objects-test <element> <config> <saída>` ao lado de `--zone-test`)
- Modify: `docs/kb/PW-KB.md` (§S6 colisão, §S17 `.ecwld`/`.ecbsd`), `~/.claude/skills/pw-engine/SOURCE-MAP.md` §M0

**Interfaces:**
- Produces: `ObjectsSelfTest.Run(string element, string? config, string output)`; arquivo de saída termina com `falhas: N`.

- [ ] **Step 1: Escrever as checagens de estudo em `ObjectsSelfTest.Study`**
  Checagens (nome → condição), sobre o mapa `world` e as instâncias `a05` e `a26`:
  - `bmd-cabecalho-volta-igual`: para todo `.bmd` do mapa, trocar `Dir`/`Up`/`Pos`/`Scale` pelos mesmos valores e regravar dá os mesmos bytes.
  - `bmd-giro-brushes`: girar um `.bmd` 90° em torno do centro: os brushes do cliente (`BmdModel.CdBrushes`) e os do `map.bht` achados pela caixa (`BhtFile.Inside`) giram juntos e a caixa nova contém todos os vértices (`BmdModel.WorldVertices`).
  - `ecwld-modelo-campos`: posição, `Dir`, `Up` e escala do item `Kind.EcModel` ficam nos deslocamentos que `WorldScene.SceneModel` lê; regravar sem mudança dá bytes iguais.
  - `apagar-construcao`: caminho para tirar uma construção: entrada do `.bmd` no pacote, ornamento do `.ecbsd`, referência `Kind.Ornament` do `.ecwld`, brushes do `map.bht` (o `BhtFile` tem árvore espacial depois dos brushes: `TreeStart`). Provar uma forma que o cliente e o gs aceitam (remover, ou neutralizar o brush sem mexer nos índices) e regravar.
  - `limite-pedaco`: maior contagem de objetos por bloco do `.ecwld` e de `.bmd` por pedaço no cliente 1.5.5; procurar limite fixo no código do cliente (`EC_SceneBlock.cpp`) e do gs.
- [ ] **Step 2: Rodar**
  Run: `bin/Debug/net10.0-windows/MyAceEditor.exe --objects-test "<element>" "<config>" objects-study.txt` (com `dotnet build` antes)
  Expected: `falhas: 0`, ou falhas que explicam o que não deu.
- [ ] **Step 3: Registrar e decidir**
  Cada resposta vira linha `[T 07/11]` na PW-KB; onde fica no C++ vai no SOURCE-MAP §M0. **Portão:** se girar ou apagar construção não ficar provado, parar e mostrar ao usuário; essa operação sai da etapa 1 (as tarefas abaixo a tratam como "só leitura" com o motivo).
- [ ] **Step 4: Commit** `Objetos do mundo: estudo de girar e apagar construções e modelos`

### Task 2: Rascunho — tipos e passos

**Files:**
- Create: `PerfectWorld/WorldEdit/WorldDraft.cs`, `PerfectWorld/WorldEdit/DraftOps.cs`
- Modify: `PerfectWorld/WorldEdit/ObjectsSelfTest.cs` (seção `Draft`, sem arquivos)

**Interfaces:**
- Produces:
  - `enum ObjKind { Building, Model, Tree, Light, Effect, Sound, Critter }`
  - `readonly record struct ObjTransform(Vector3 Pos, Vector3 Dir, Vector3 Up, float Scale)` (`System.Numerics`)
  - `sealed class DraftObject { string Key; ObjKind Kind; int Piece; string Source; ObjTransform Original; ObjTransform Now; bool Deleted; bool Solid; (Vector3 Min, Vector3 Max) LocalBox; string? ReadOnlyWhy; bool Changed => Deleted || Now != Original; }`
  - `sealed class DraftPiece { int Id; bool Loaded; string? ReadOnlyWhy; List<DraftObject> Objects; bool Dirty => Objects.Any(o => o.Changed) || Added.Count > 0; List<DraftObject> Added; }`
  - `sealed class WorldDraft { string Map; IReadOnlyDictionary<int, DraftPiece> Pieces; DraftObject? Find(string key); bool Dirty; int UndoCount; int RedoCount; event Action? Changed; }`
  - `abstract record DraftOp(string Key)`; `record MoveOp(string Key, ObjTransform To)`; `record DeleteOp(string Key)`; `record RestoreOp(string Key)`
  - `WorldDraft.Apply(DraftOp op)` → `string?` (null = aplicado; texto = motivo da recusa); `bool Undo()`; `bool Redo()`
- [ ] **Step 1: Checagens** `draft-aplica`, `draft-desfaz`, `draft-refaz`, `draft-recusa-so-olhar` (objeto com `ReadOnlyWhy` recusa e não entra no desfazer), `draft-recusa-faixa` (posição NaN/infinita ou escala ≤ 0 recusa), `undo-redo-salva-igual` (50 movimentos aleatórios + desfazer/refazer até o fim deixa `Now` igual ao último `Apply`).
- [ ] **Step 2: Rodar e ver falhar** (tipos não existem: não compila).
- [ ] **Step 3: Implementar.** O desfazer guarda só `(Key, antes, depois)` por passo; `Apply` limpa o refazer; `Changed` dispara depois de cada mudança.
- [ ] **Step 4: Rodar** `--objects-test` → seção `Draft` toda `OK`.
- [ ] **Step 5: Commit** `Objetos do mundo: rascunho com passos e desfazer`

### Task 3: Ler os pedaços para o rascunho

**Files:**
- Create: `PerfectWorld/WorldEdit/DraftLoader.cs`
- Modify: `PerfectWorld/WorldEdit/ObjectsSelfTest.cs` (seção `Load`)

**Interfaces:**
- Consumes: `WorldDraft`, `DraftPiece`, `DraftObject` (Task 2); `PckArchive`, `BmdModel.Read`, `EcwldFile`, `WorldScene`, `BhtFile`.
- Produces: `sealed class DraftSources(string ElementDir, string Map, string? ServerConfigDir)`; `static DraftPiece DraftLoader.ReadPiece(DraftSources src, int piece)` (puro, roda em segundo plano; erro vira `ReadOnlyWhy`, nunca exceção); chave `Key`: construção = caminho do `.bmd` no pacote; item do `.ecwld` = `"ecwld:{Kind}:{id}"`.
- [ ] **Step 1: Checagens** `le-pedacos-world` (todos os pedaços do `world` abrem; contagem de construções = contagem de `.bmd` por pedaço que o `WorldBuildings.IndexPieces` acha), `le-modelos-e-cenario` (modelos, efeitos, sons e bichos do pedaço batem com `WorldScene`), `pedaco-ruim-so-olhar` (`.ecwld` truncado numa cópia: o pedaço vem com `ReadOnlyWhy` e os vizinhos abrem), `solido-pela-colisao` (construção com brushes no `map.bht` tem `Solid = true`).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar `ReadPiece`.** Pedaço = número de `WorldTerrain.PieceAt` (o mesmo do `WorldBuildings`); objetos do `.ecwld` entram pelo bloco cujo `RowCol` cobre o pedaço.
- [ ] **Step 4: Rodar** → seção `Load` toda `OK`.
- [ ] **Step 5: Commit** `Objetos do mundo: leitura dos pedaços para o rascunho`

### Task 4: Pedaços em volta da câmera, em segundo plano

**Files:**
- Create: `UI/WorldView.ObjectTool.cs` (parcial do `WorldView`; nesta tarefa só a parte de pedaços)
- Modify: `UI/WorldView.cs:599-612` (quando a ferramenta está aberta, a mesma lista 3x3 também vai para o rascunho)

**Interfaces:**
- Consumes: `DraftLoader.ReadPiece` (Task 3), `WorldLoader.Work(Func<T>, Action<T>, bool urgent)`.
- Produces: `WorldDraft? _draft` no `WorldView`; `void DraftWant(IReadOnlyCollection<int> pieces)`; status `"Carregando objetos… {n}/{total}"` (`Loc.T("obj.loading", ...)`).
- [ ] **Step 1: Checagem** em `ObjectsSelfTest` com um `WorldDraft` e um carregador síncrono: `pedaco-sujo-fica` (pedaço com mudança não sai quando a lista nova não o inclui; pedaço limpo sai).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** `WorldDraft.Retain(IReadOnlyCollection<int> wanted)` solta só os limpos; `DraftWant` pede os que faltam ao `WorldLoader` e junta na volta (na thread da tela).
- [ ] **Step 4: Rodar** `--objects-test` e `--world-shot` com a ferramenta aberta (nome `objload`): o status aparece e some.
- [ ] **Step 5: Commit** `Objetos do mundo: pedaços 3x3 em segundo plano`

### Task 5: O desenho lê do rascunho

**Files:**
- Modify: `UI/WorldBuildings.cs` (matriz por `.bmd` vinda de `Func<string, ObjTransform?>`; escondido se apagado), `UI/WorldModels.cs` (mesmo para modelos do `.ecwld`), `UI/WorldView.SceneEdit.cs` (efeitos, sons e bichos passam a ler e mudar o rascunho; `_ecw`/`_bsd` em bytes saem)
- Modify: `UI/WorldView.BuildingSave.cs` (as `Offsets` viram passos `MoveOp` no rascunho)

**Interfaces:**
- Consumes: `WorldDraft.Find`, `DraftObject.Now/Deleted`.
- Produces: `WorldBuildings.TransformOf` e `WorldModels.TransformOf` (`Func<string, ObjTransform?>`), ligados pelo `WorldView` quando a ferramenta abre.
- [ ] **Step 1: Captura de antes** `--world-shot` em `1090,240,1033,0,-20,objdraw0` sem a ferramenta.
- [ ] **Step 2: Implementar** a leitura do rascunho; mover construção existente continua igual para o usuário.
- [ ] **Step 3: Conferir** `--world-shot` com um `MoveOp` de teste aplicado (`MYACE_OBJTEST=move`) mostra a construção no lugar novo; sem ele, a imagem é igual à de antes.
- [ ] **Step 4: Commit** `Objetos do mundo: desenho das construções, modelos e cenário pelo rascunho`

### Task 6: Salvar tudo ou nada

**Files:**
- Create: `PerfectWorld/WorldEdit/DraftSaver.cs`, `PerfectWorld/WorldEdit/DraftChecks.cs`
- Modify: `PerfectWorld/PckWriter.cs` (só se o estudo da Task 1 exigir remover entrada), `PerfectWorld/WorldEdit/ObjectsSelfTest.cs` (seção `Save`)

**Interfaces:**
- Consumes: rascunho (Tasks 2-3), resultado do estudo (Task 1), `PckWriter.Write`, `AtomicFileSave.Write`, `BackupStore.Save`/`Read`, `BhtFile`.
- Produces:
  - `sealed record CheckItem(bool Blocks, string Text, string Key)`; `static List<CheckItem> DraftChecks.Run(WorldDraft d, DraftSources src, Func<float, float, float>? groundAt, IEnumerable<(Vector3 Pos, string What)> points)`
  - `sealed record SavePlan(Dictionary<string, byte[]> PckFiles, Dictionary<string, byte[]> Files, List<string> Summary)`; `static SavePlan DraftSaver.Plan(WorldDraft d, DraftSources src)` (só memória; motivo em `InvalidDataException`)
  - `static SaveResult DraftSaver.Write(SavePlan p, DraftSources src)` (reusa `SaveResult`/`SaveKind` do `ZonePair`)
- [ ] **Step 1: Checagens**
  - `plano-sem-mudanca-vazio`: rascunho sem mudança → plano sem arquivos.
  - `plano-relido-igual`: mover uma construção e um modelo numa cópia; gravar; reler com `DraftLoader` → `Now` igual ao salvo, o resto byte a byte igual ao original.
  - `gemeas-uma-vez`: mover uma construção gêmea da a05 → os brushes do `map.bht` andam uma vez só.
  - `fora-do-mapa-bloqueia`, `limite-bloqueia` (valor da Task 1), `flutuando-avisa`, `em-cima-de-npc-avisa` (com um ponto de `npcgen` de teste).
  - `pck-preso-volta-tudo`: com o `litmodels.pck` da cópia aberto sem compartilhar, `Write` devolve `SaveKind.Locked` e nenhum arquivo da cópia muda (hash antes = depois).
  - `falha-no-meio-volta-tudo`: falha simulada depois do 1º arquivo (`DraftSaver.FailAfter = 1`, só teste) → `SaveKind.RolledBack` e os arquivos já gravados voltam pelo `BackupStore`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** Ordem: plano em memória → conferência de releitura de cada arquivo montado → backup de todos → escrita de cada um → se qualquer escrita falhar, restaurar os já escritos a partir do backup e devolver `RolledBack`. O `PlanBuildings` de hoje passa a ser parte do `Plan`.
- [ ] **Step 4: Rodar** → seção `Save` toda `OK`.
- [ ] **Step 5: Commit** `Objetos do mundo: salvar tudo ou nada com conferências`

### Task 7: A tela da ferramenta

**Files:**
- Modify: `UI/WorldView.ObjectTool.cs` (painel), `UI/WorldView.Tools.cs:137-152` (os botões cinza viram as pílulas Construções | Árvores | Grama | Modelos | Luzes, apagadas com a dica `Loc.T("obj.pillSoon", "Chega na próxima etapa da ferramenta.")`), `UI/WorldView.cs` (`UndoAny`/`RedoAny`: `SceneUndo` vira `ObjectUndo`; clique e Delete com a ferramenta aberta), `lang/en.json`, `lang/ru.json`, `lang/zh-CN.json`

**Interfaces:**
- Consumes: `_draft` (Task 4), `DraftChecks`/`DraftSaver` (Task 6), `SavedDialog.Show(owner, detail, send)`, gizmo atual (`DrawGizmo`/`GizmoDrag`).
- Produces: `bool ObjectUndo(bool redo)` (mesma regra de ordem do `SceneUndo`: só desfaz se foi a última ação, pela profundidade guardada); `void SaveObjects()`.
- [ ] **Step 1: Implementar a seleção** de qualquer tipo à vista (construções pelo `WorldBuildings.Pick`, modelos, efeitos, sons, bichos; árvores e luzes aparecem no cartão só para leitura nesta etapa), com contorno no selecionado.
- [ ] **Step 2: Implementar o cartão**: tipo, nome e arquivo, X/Y/Z, giro (graus), escala (só leitura), largura × altura × profundidade, quantas vezes o modelo aparece no mapa, Colisão (só leitura), e os campos editáveis mandando `MoveOp`; "Grudar no chão" (padrão ligado) usa a altura do terreno no arrasto; Delete manda `DeleteOp`; interruptor "Ver colisão" desenha as caixas dos brushes.
- [ ] **Step 3: Implementar o Salvar**: resumo em frases → lista de conferências (bloqueio impede; aviso tem "Levar até ele") → `DraftSaver` → `SavedDialog` com envio; erro vira frase simples e o rascunho fica.
- [ ] **Step 4: Esc e sair**: passo em andamento cancela; com mudança não salva, a pergunta de abandonar (`LeaveTool`).
- [ ] **Step 5: Conferir** `--world-shot` (nomes `objpanel`, `objcard`, `objchecks`) e `--objects-test` (tudo `OK`); textos novos nos 3 arquivos de idioma.
- [ ] **Step 6: Commit** `Objetos do mundo: painel, cartão, seleção e salvar`

### Task 8: Registro

**Files:**
- Modify: `docs/kb/ENGINE-KB.md` (§E6 arquivos novos, §E7 a ferramenta), `docs/MUNDO-3D-EDITOR.md` (passo 6), `docs/AFAZERES.md` (etapa 1 feita, falta o teste no jogo)
- [ ] **Step 1: Escrever** as linhas (telegráficas, com tag `[T]` e o que não foi testado no jogo).
- [ ] **Step 2: Commit e push** `Objetos do mundo: etapa 1 registrada`
