# Objetos do mundo — Etapa 2a (copiar e colar construções e modelos, escala) — plano

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (escolhido: nativo). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Ctrl+C/Ctrl+V cria construções e modelos novos a partir dos que já existem (cópia onde está o mouse, grudada no chão, já escolhida com as setas), o cartão muda o tamanho das construções, e o Salvar grava tudo ou nada com a colisão do servidor.

**Architecture:** Objeto novo entra no rascunho como `DraftObject` em `DraftPiece.Added`, com `CopyOf` = chave da origem; desfazer/refazer usa o mesmo passo (apagado ↔ não apagado). O desenho e o clique usam a malha da origem com a matriz nova. O `DraftSaver` monta: `.bmd` novo (nome livre no pedaço) com cabeçalho e brushes transformados, ornamento novo no FIM do `.ecbsd` (o cliente lê por offset: `CECScene::LoadOrnament` faz `Seek(dwOffset)`), referência nova (ID novo) nos blocos do `.ecwld` que a caixa cobre, brushes acrescentados ao `map.bht` (grade fixa). Modelo novo = item clonado no `.ecwld` com ID novo.

**Spec:** `docs/superpowers/specs/2026-11-07-objetos-do-mundo-design.md` (etapa 2; catálogo fica para a 2b, decisão do usuário 07/11).

## Global Constraints
- Só 1.5.5; testes só em cópias/memória; tratamento de erro em tudo; textos via `Loc.T` em en/ru/zh-CN; Ctrl+Z em tudo; nada grava antes do Salvar.
- Cópia só dentro do pedaço de 1 km da origem (mesma regra de mover).
- ID de exportação novo = maior ID do `.ecwld` + 1 (único: o cliente guarda ornamentos pelo ID).
- Escala de modelo não existe no 1.5.5 (só construção).

## Review Focus
1. Colar, desfazer, refazer várias vezes e salvar: só uma construção nova no arquivo (teste `colar-desfaz-refaz-uma`).
2. Colar e apagar a cópia antes de salvar: nada novo no arquivo (teste `colar-e-apagar-nada`).
3. Duas cópias da mesma origem: nomes `.bmd` e IDs diferentes (teste `duas-copias-nomes-diferentes`).
4. Origem sem colisão no servidor: cópia sem brushes no `map.bht` (teste `copia-sem-colisao`).
5. Escala 2× numa construção: brushes do `.bmd` e do `map.bht` com a mesma chave depois (teste `escala-chave`).

### Task 1: Estudo (portão)
**Files:** `PerfectWorld/WorldEdit/ObjectsSelfTest.cs` (seção `Study2`), `PerfectWorld/WorldEdit/ObjectFormats.cs`, `PerfectWorld/BhtFile.cs`, PW-KB.
**Produces:** `ObjectFormats.BlockAreas(EcwldFile, byte[] head)` → área (x0,z0,x1,z1) por bloco; `ObjectFormats.ScaleBrush(byte[] d, int at, float s, Vector3 c)`; `BhtFile.With(IEnumerable<byte[]> brushes)` → byte[]; `ObjectFormats.NewOrnament(byte[] ecbsd, int srcOff, string newName, ObjTransform t)` → (byte[] ecbsd, int off).
- [ ] Checagens: `bloco-area` (ornamentos de cada bloco do world/a05 caem na área calculada do bloco); `bht-acrescenta` (With + releitura: contagem +n, árvore igual); `ecbsd-acrescenta` (ornamento novo no fim relido pelo offset com o nome novo); `escala-brush` (2× em volta do centro: caixa dobra, planos passam pelos cantos novos); `nome-ornamento` (extensão usada pelos vizinhos: .bmd ou .lmd).
- [ ] Rodar `--objects-test` → `falhas: 0`; registrar na PW-KB; portão: o que não provar fica fora da 2a.
- [ ] Commit `Objetos do mundo: estudo de construção nova e escala`

### Task 2: Rascunho — colar e escala
**Files:** `PerfectWorld/WorldEdit/WorldDraft.cs`, `ObjectsSelfTest.cs` (seção `Draft2`).
**Produces:** `DraftObject.CopyOf` (string?); `sealed record AddOp(string Key, DraftObject Obj) : DraftOp(Key)` (entra em `Added`; desfazer = apagado); `Find` acha os novos; escala livre em construção (modelo recusa escala ≠ 1).
- [ ] Checagens `draft-cola`, `colar-desfaz-refaz-uma`, `draft-escala-modelo-recusa`.
- [ ] Commit `Objetos do mundo: colar e escala no rascunho`

### Task 3: Salvar os novos
**Files:** `PerfectWorld/WorldEdit/DraftSaver.cs` (+ DraftChecks), `ObjectsSelfTest.cs` (seção `Save2`).
**Produces:** `Plan` grava novos e escala; resumo "N construções novas".
- [ ] Checagens: `plano-novo-relido` (o `.ecwld` novo tem a referência, o `.ecbsd` novo tem o nome, o `.bmd` novo está em PckFiles com o cabeçalho certo, os brushes estão no `map.bht`), `colar-e-apagar-nada`, `duas-copias-nomes-diferentes`, `copia-sem-colisao`, `escala-chave`, `plano-modelo-novo`, `copia-fora-do-pedaco-bloqueia`.
- [ ] Commit `Objetos do mundo: salvar construções e modelos novos`

### Task 4: Tela
**Files:** `UI/WorldBuildings.cs` (extras: cópias desenhadas e clicáveis com a malha da origem), `UI/WorldView.ObjectTool.cs` (Ctrl+V cria, escala no cartão, modelos novos desenhados), `lang/*.json`.
- [ ] Captura `dftpaste`: copia a construção mais perto, cola 10 m ao lado, confere que o clique na cópia escolhe a cópia, Ctrl+Z tira, Ctrl+Y volta, escala 1,5× muda o tamanho do cartão; plano mostra "1 construção nova".
- [ ] Commit `Objetos do mundo: copiar e colar construções e modelos na tela`

### Task 5: Registro
- [ ] ENGINE-KB §E6/§E7, AFAZERES, roteiro do Mundo 3D; push.
