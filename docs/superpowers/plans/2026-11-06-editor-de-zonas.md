# Editor de zonas: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Editor completo das zonas do mapa (`precinct.clt` e `precinct.sev`) dentro do Mundo 3D: criar, editar, apagar e salvar, com zoom no mapa, também nos Territórios.

**Architecture:** Três camadas sem tela (`PrecinctFile` e `PrecinctServer` leem e escrevem, `ZoneRules` confere, `ZonePair` abre, compara e salva o par). Uma peça de mapa com zoom (`MapCanvas`) serve às duas ferramentas. O painel `WorldView.ZoneTool` só chama essas camadas.

**Tech Stack:** C# / .NET 10, WinForms, OpenTK (cena 3D). Testes por linha de comando, como o resto do projeto.

**Spec:** [docs/superpowers/specs/2026-11-06-editor-de-zonas-design.md](../specs/2026-11-06-editor-de-zonas-design.md). Tela: [zonas-layout.svg](../specs/2026-11-06-zonas-layout.svg). Regras para leigos: [teleportes-leigos](../specs/2026-11-06-teleportes-leigos-design.md).

## Global Constraints

- Foco 1.5.5. `.clt` sempre versão 7; `.sev` é gravado na versão 7.
- Texto da tela sempre por `Loc.T`, com a chave em `lang/en.json`, `ru.json` e `zh-CN.json`. A palavra "prioridade" não aparece na tela.
- Painel no padrão do Mundo 3D: toma o lugar do menu, seta de voltar, espaço livre abaixo do último controle.
- Toda ação tem Ctrl+Z e Ctrl+Y, antes e depois de salvar.
- Nada grava antes de **Salvar zona**. `BackupStore.Save` antes de gravar, `AtomicFileSave.Write` para gravar.
- Mudar só nome, música ou marcas não toca no `.sev` nem no carimbo.
- Zona que a pessoa não mexeu nunca barra o salvar.
- Testes não gravam em pasta real: só em pasta temporária.
- Compilar com `dotnet build -c Debug -v q -p:OutDir=<pasta temporária>\` porque o usuário costuma estar com o `bin\Debug` aberto.
- Commit e push ao fim de cada tarefa, sem a palavra Release. Mensagem termina com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Registrar na mesma tarefa: arquivo novo em ENGINE-KB §E6, fato do jogo em PW-KB §S36, etapa em `docs/AFAZERES.md`.

## Review Focus

1. **Nome com caractere que o texto do `.clt` não aceita** (aspas, quebra de linha, colado de outro programa): o campo recusa, e o arquivo nunca sai quebrado. Teste na Tarefa 1.
2. **Dois editores abertos ou arquivo trocado durante a edição:** o salvar percebe e oferece Recarregar ou Gravar por cima. Teste na Tarefa 4.
3. **Ctrl+Z depois de salvar quando o servidor foi desligado ou a pasta sumiu:** não deixa o par pela metade. Teste na Tarefa 4.
4. **Mapa com várias origens (a72, b01):** apagar ou criar numa instância não muda as áreas da outra. Teste na Tarefa 4.
5. **Zoom no limite e janela muito pequena:** o mapa não some, não inverte e o clique continua caindo no canto certo. Teste na Tarefa 5.

---

### Task 1: `PrecinctFile` completo, escrita e `At` certo

**Files:**
- Modify: `PerfectWorld/PrecinctFile.cs`
- Modify: `UI/WorldMusic.cs:97`, `UI/WorldView.TeleportTool.cs:308`
- Create: `PerfectWorld/ZoneSelfTest.cs`
- Modify: `Program.cs` (argumento `--zone-test`)

**Interfaces:**
- Produces:
  - `PrecinctFile.Area` ganha, por `init`: `int Id`, `int DstInstance`, `int SrcInstance`, `bool PkProtected`, `(float X,float Y,float Z) City`, `List<float> PointY`, `List<Mark> Marks`. `record Mark(string Name, float X, float Y, float Z)`. Os usos atuais de `Area` continuam compilando.
  - `PrecinctFile`: `int Version`, `uint Stamp`, `static PrecinctFile Parse(byte[] data)` (lança `InvalidDataException`), `byte[] Render()`, `Area? At(float x, float z, int instance)`.
  - `ZoneSelfTest.Run(string maps, string? config, string output)`: uma linha `OK nome` ou `FALHA nome: motivo` por checagem, e no fim `falhas: N`.
- Formato: o cabeçalho e as linhas estão em PW-KB §S36 e no script `prec_rt.py` do estudo. Ponto: `%f  %f  %f`. Cidade: `x, y, z`. A linha de números da área vai numa só linha, porque o cliente lê assim.

- [ ] **Step 1: Escrever os testes em `ZoneSelfTest`**
  - `clt-identico`: para cada `maps/*/precinct.clt`, `Parse(bytes).Render()` é igual a `bytes`. Meta: 71 de 71 no 155 BR.
  - `at-regra`: com `random(seed 7)`, 40.000 pontos no mundo; `At` é igual a um laço escrito no teste: passa por todas as áreas da instância em ordem e troca quando `Contains` e `Priority <= atual`.
  - `nome-proibido`: `Render` com nome contendo `"` ou `\n` lança `ArgumentException`.
- [ ] **Step 2: Rodar e ver falhar**

Run: `MyAceEditor.exe --zone-test "D:\Perfect World\PW_Clientes\PW 155 BR\element\maps" "D:\Perfect World\PW_Server\pw_server_162-155\home\gamed\config" zona.txt`
Expected: não compila ou `FALHA clt-identico`.
- [ ] **Step 3: Implementar `Parse`, `Render`, `At`**

`Read` passa a chamar `Parse`, mantendo o retorno null para arquivo que não existe. `At` percorre em ordem, pula `SrcInstance != instance`, troca com `<=`. `WorldMusic` e `DomainAt` passam a instância do mapa aberto, e a sobrecarga sem instância sai.
- [ ] **Step 4: Rodar**

Expected: `falhas: 0`, `clt-identico 71/71`.
- [ ] **Step 5: Commit** `Zonas: precinct.clt com todos os campos, regravação idêntica e zona certa no ponto`

### Task 2: `PrecinctServer`

**Files:**
- Create: `PerfectWorld/PrecinctServer.cs`
- Modify: `PerfectWorld/ZoneSelfTest.cs`

**Interfaces:**
- Produces: `sealed class PrecinctServer`, com `uint Version`, `uint Stamp`, `List<ServerArea> Areas`; `record ServerArea(int Priority, int Dst, int Src, int Domain, bool Pk, (float X,float Y,float Z) City, List<(float X,float Y,float Z)> Points)`; `static PrecinctServer Parse(byte[] d)`, `byte[] Render()`, `static PrecinctServer From(PrecinctFile clt, uint stamp)` (versão 7, mesma ordem, inclui ID 0).
- Formato: u32 versão, i32 n, [v≥5] u32 carimbo; por área: nPontos, prioridade, destino, [v4] origem, [v6] domínio, [v7] bool pk, cidade, pontos.

- [ ] **Step 1: Testes**
  - `sev-identico`: `Parse(b).Render()` igual a `b` nos arquivos de `config/*/precinct.sev`, mantendo a versão lida.
  - `sev-do-clt`: em mapa cujo par combina, `From(clt, stamp)` tem as mesmas áreas que o `.sev` real, com pontos iguais.
- [ ] **Step 2: Rodar e ver falhar**
- [ ] **Step 3: Implementar**
- [ ] **Step 4: Rodar.** Expected: `sev-identico` 71/71, `falhas: 0`.
- [ ] **Step 5: Commit** `Zonas: leitura e escrita do precinct.sev`

### Task 3: `ZoneRules`

**Files:**
- Create: `PerfectWorld/ZoneRules.cs`
- Modify: `PerfectWorld/ZoneSelfTest.cs`, `docs/REGRAS-DO-JOGO.md`

**Interfaces:**
- Produces: `enum ZoneProblemKind { Block, Ask }`; `record ZoneProblem(ZoneProblemKind Kind, string Key, string Text, int Corner = -1)`; `static List<ZoneProblem> ZoneRules.Check(PrecinctFile file, PrecinctFile.Area area, ZoneContext ctx)`; `record ZoneContext(bool IsNew, bool ShapeTouched, Func<string,bool> MusicExists, Func<int,bool> DomainExists, Func<float,float,bool> HasGround)`; `static bool Crosses(IReadOnlyList<(float X,float Z)> pts, out int corner)`; `static int FreeId(PrecinctFile file)`; `static float Holes(PrecinctFile before, PrecinctFile after, int instance)` (fração de amostras que tinham zona e ficaram sem).

Textos e tipos: tabela **Ao editar** da spec, copiados de lá.

- [ ] **Step 1: Testes**: `regra-nome-vazio`, `regra-3-cantos`, `regra-cruzada-so-mexida` (zona cruzada com `ShapeTouched=false` passa; com `true` barra e aponta o canto), `regra-antigas` (as 94 zonas originais que se cruzam não barram sem mexer), `regra-cidade-nova` (nova com cidade 0,0,0 barra; antiga passa), `regra-territorio`, `regra-musica-pergunta` (tipo `Ask`), `free-id` (não repete no mapa), `buraco` (apagar zona única cria fração > 0).
- [ ] **Step 2: Rodar e ver falhar**
- [ ] **Step 3: Implementar**
- [ ] **Step 4: Rodar.** Expected: `falhas: 0`.
- [ ] **Step 5: Commit** `Zonas: regras da zona, com seção nova em REGRAS-DO-JOGO`

### Task 4: `ZonePair`

**Files:**
- Create: `PerfectWorld/ZonePair.cs`
- Modify: `PerfectWorld/ZoneSelfTest.cs`

**Interfaces:**
- Consumes: Tarefas 1 a 3, `BackupStore.Save(string)`, `AtomicFileSave.Write(string, byte[])`.
- Produces:
  - `enum PairState { Ok, ClientOnly, NoServerFile, ServerBroken, StampDiffers, ListsDiffer, ReadOnly }`.
  - `sealed class ZonePair`: `static ZonePair Open(string mapDir, string? serverMapDir)`; `PrecinctFile Client`; `PrecinctServer? Server`; `PairState State`; `string StateText`; `IReadOnlyList<int> Instances`; `bool CanEditServerFields`.
  - `SaveResult Save(PrecinctFile changed, bool overwriteExternal = false)`; `record SaveResult(SaveKind Kind, string Message, string? BackupPath)`; `enum SaveKind { ClientOnly, Both, ChangedOnDisk, Locked, Failed, RolledBack }`.
  - `byte[] Snapshot()` e `SaveResult Restore(byte[] snapshot)`, que regrava o par com carimbo novo.
  - `SaveResult UseClientAsTruth()` para o caso a40.
  - `static bool ServerChanged(PrecinctFile a, PrecinctFile b)`: forma, prioridade, destino, origem, território, PK, cidade ou ordem.
- Sequência: os 8 passos da seção **Salvar** da spec, nessa ordem. Lista **Ao abrir** da spec define `State`.

- [ ] **Step 1: Testes em pasta temporária** (copiar `a01` e `a40`)
  - `carimbo-so-com-servidor`: renomear mantém `.sev` e carimbo; mover canto troca o carimbo nos dois, e o novo difere do antigo.
  - `outras-zonas-iguais`: criar, renomear, mover e apagar não muda as outras áreas.
  - `origens-separadas` (Review Focus 4): em cópia de `a72`, apagar uma zona da instância aberta mantém todas as de outra origem, byte a byte no texto de cada área.
  - `falha-segundo-arquivo`: `.sev` aberto com `FileShare.None` faz o `.clt` voltar, com `Kind == Locked` ou `RolledBack`.
  - `mudou-no-disco` (Review Focus 2): trocar o `.clt` depois de `Open` dá `ChangedOnDisk`, sem gravar; com `overwriteExternal` grava.
  - `restore-sem-servidor` (Review Focus 3): apagar a pasta do `.sev` depois de salvar faz `Restore` devolver `Failed` sem tocar no `.clt`.
  - `estado-a23` e `estado-a40`: `StampDiffers` e `ListsDiffer`.
  - `v6-sobe`: `.sev` v6 sai v7.
- [ ] **Step 2: Rodar e ver falhar**
- [ ] **Step 3: Implementar**
- [ ] **Step 4: Rodar.** Expected: `falhas: 0`; conferir com `git status` que nenhum arquivo real mudou.
- [ ] **Step 5: Commit** `Zonas: par cliente e servidor com salvar seguro, volta e retrato`

### Task 5: `MapCanvas` e zoom nos Territórios

**Files:**
- Create: `UI/MapCanvas.cs`
- Modify: `UI/DomainEditorWindow.cs:190-198` e usos de `View`, `ToScreen`, `ToMap`
- Modify: `Program.cs` (`--domain-test` ganha captura com zoom)

**Interfaces:**
- Produces: `sealed class MapCanvas : Control`, com `Image? Map`, `SizeF WorldSize`, `float MetersPerPixel`; `PointF ToScreen(PointF map)`, `PointF ToMap(Point screen)`; `void FitAll()`, `void Frame(RectangleF map)`; `event Action<Graphics>? PaintOver`; `event Action<MouseEventArgs, PointF>? MapDown, MapMove, MapUp, MapDoubleClick`; `Func<Point,bool>? HitSomething`, para saber se arrastar move o mapa; `const float MaxZoom = 16`.
- Gestos: seção **Zoom** da spec. Botões **+**, **−**, **Tudo** e escala em metros são desenhados pela própria peça.

- [ ] **Step 1: Teste `canvas-ida-e-volta`** em `ZoneSelfTest`: `ToMap(ToScreen(p))` volta com erro < 0,5 px em zoom 1, 4 e 16; roda do mouse mantém o ponto sob o cursor; zoom para em 16 e em encaixar; tamanho 1×1 (Review Focus 5) não gera infinito nem NaN.
- [ ] **Step 2: Rodar e ver falhar**
- [ ] **Step 3: Implementar e trocar o `_canvas` da `DomainEditorWindow`**
- [ ] **Step 4: Rodar `--zone-test` e `--domain-test`.** Expected: `falhas: 0` e `--domain-test` como antes. Ver a captura com zoom.
- [ ] **Step 5: Commit** `Mapa com zoom em peça própria; Territórios passam a usar`

### Task 6: Ladrilho Zonas, lista e mapa

**Files:**
- Create: `UI/WorldView.ZoneTool.cs`
- Modify: `UI/WorldView.Tools.cs:12` (`Tool.Zones`, ladrilho, `OpenTool`, `ShotTool`), `Program.cs` (`--zone-shot`), `lang/*.json`

**Interfaces:**
- Consumes: `ZonePair.Open`, `MapCanvas`, `WorldView.WorldMap()`, `StatusCard`.
- Produces: `void OpenZones()`, `void UpdateZonePanel()`, `void OpenZone(PrecinctFile.Area)`; campos `_zones` (`ZonePair`), `_zoneWork` (`PrecinctFile`, cópia em edição), `_zoneCanvas` (`MapCanvas`).
- Pasta do servidor: `Path.Combine(ServerConfigDir, mapa)`.

- [ ] **Step 1:** implementar a lista: cartão do par com o texto de `ZonePair.StateText`, **Nova zona**, busca, lista de nome + segunda linha, caixa **Instância** quando há mais de uma.
- [ ] **Step 2:** desenhar as zonas no `PaintOver`, com as cores da spec, nome só quando cabe, e barra **Aqui o jogador vê** usando `At`.
- [ ] **Step 3:** clique escolhe a zona de `At`; clicar de novo no mesmo ponto passa para a de baixo.
- [ ] **Step 4:** rodar `--zone-shot`. Expected: captura da lista igual ao `zonas-layout.svg` no mapa do mundo, e a23 com cartão amarelo.
- [ ] **Step 5: Commit** `Mundo 3D: ladrilho Zonas com lista e mapa`

### Task 7: Passos 1 e 2 (nome, área, partes)

**Files:**
- Modify: `UI/WorldView.ZoneTool.cs`, `lang/*.json`
- Create: `PerfectWorld/ZoneEdit.cs`, para o que não depende de tela
- Modify: `PerfectWorld/ZoneSelfTest.cs`

**Interfaces:**
- Produces: `static class ZoneEdit`: `MoveCorner(area, i, x, z, mapSize)`, `AddCorner(area, afterIndex, x, z)`, `RemoveCorner(area, i)`, `OnTop(file, area)` (prioridade 0 e fim do arquivo), `Below(file, area, neighbors)` (maior prioridade das vizinhas + 1), `Parts(file, id)`, `Neighbors(file, area)`, `SetForParts(file, id, Func<Area,Area>)`, `HeightAt` recebido por parâmetro.

- [ ] **Step 1: Testes**: `canto-na-borda` (para na borda), `por-cima` (depois de `OnTop`, `At` devolve a zona), `por-baixo`, `partes` (mudar nome com **Vale para as N partes** muda todas; a área muda só uma), `id0-ganha-id`.
- [ ] **Step 2: Rodar e ver falhar**
- [ ] **Step 3: Implementar `ZoneEdit`**
- [ ] **Step 4: Implementar os passos no painel**: prévia do nome, arrastar canto, duplo clique na borda, botão direito, **Desenhar de novo**, **Mais uma parte**, pílulas de parte, **Fica por cima** e **Fica por baixo**. O campo de nome recusa aspas e quebra de linha, com até 48 letras.
- [ ] **Step 5: Rodar `--zone-test` e `--zone-shot`.** Expected: `falhas: 0`, captura de zoom alto com cantos do tamanho certo.
- [ ] **Step 6: Commit** `Zonas: nome, cantos e partes`

### Task 8: Passos 3, 4 e 5 (música, regras, marcas)

**Files:**
- Create: `UI/ZoneMusicWindow.cs`
- Modify: `UI/WorldView.ZoneTool.cs`, `lang/*.json`

**Interfaces:**
- Produces: `ZoneMusicWindow(string elementDir, IReadOnlyList<string> now)`, com `List<string> Music`, ouvir e ordenar. Lista vem da pasta `music` do cliente.
- Consumes: `DomainFile` para os nomes de território; prefixo `music\` só no som, e `Music\` fica no texto da música, como na spec.

- [ ] **Step 1:** janela de música com ouvir, ordenar e linha vermelha para arquivo que não existe.
- [ ] **Step 2:** passo 3 com **Repetir** (sempre = 1, nunca = 0, sem a primeira = 2) e som de dia e de noite. O intervalo fica como está.
- [ ] **Step 3:** passo 4: renascer por lista das cidades do mapa ou clique; território só no mundo; **Novatos protegidos de PK**. NPC de território que muda de território vira pergunta (tipo `Ask`).
- [ ] **Step 4:** passo 5 com nome e clique no mapa, e altura pelo terreno.
- [ ] **Step 5:** `--zone-shot` com a zona aberta. Expected: igual ao painel do `zonas-layout.svg`.
- [ ] **Step 6: Commit** `Zonas: música, regras do lugar e lugares no minimapa`

### Task 9: Criar, apagar, salvar, Ctrl+Z e Ver no chão

**Files:**
- Modify: `UI/WorldView.ZoneTool.cs`, `UI/WorldView.cs` (desenho no chão, Ctrl+Z), `UI/WorldMusic.cs`, `lang/*.json`
- Modify: `PerfectWorld/ZoneSelfTest.cs`

**Interfaces:**
- Consumes: `ZonePair.Save`, `Snapshot`, `Restore`, `UseClientAsTruth`, `ZoneRules.Check`, `ZoneRules.Holes`, `ZoneRules.FreeId`.
- Produces: `static Area ZoneEdit.New(file, name, points, instance, Area? below)` com ID livre, origem da instância, prioridade 0, fim do arquivo, música, cidade e território da zona de baixo.

- [ ] **Step 1: Teste `nova-herda`**: zona nova sobre outra herda música, cidade e território, e `At` devolve a nova.
- [ ] **Step 2: Rodar e ver falhar**
- [ ] **Step 3:** criar do zero: clique por canto, clicar no primeiro ou Enter fecha.
- [ ] **Step 4:** **Como vai ficar** em frases, **Salvar zona** cinza com o motivo acima, perguntas para os `Ask`.
- [ ] **Step 5:** **Excluir esta zona**: pergunta parte ou inteira, aviso vermelho e área hachurada quando `Holes > 0`.
- [ ] **Step 6:** mensagens de `SaveResult`, tabela **Ao salvar**, e Salvar / Descartar / Cancelar ao trocar de zona, de mapa ou fechar.
- [ ] **Step 7:** Ctrl+Z por passo antes de salvar; depois de salvar, `Restore`. Atualizar `WorldMusic` e `_tpPrecinct`.
- [ ] **Step 8:** **Ver no chão** e **Voltar ao mapa**; clicar no chão escolhe a zona.
- [ ] **Step 9: Rodar `--zone-test`, `--zone-shot`, `--domain-test`.** Expected: `falhas: 0`, sem arquivo real alterado.
- [ ] **Step 10:** registrar em ENGINE-KB §E6 e §E7, PW-KB §S36, `docs/AFAZERES.md` e na lista de testes de jogo do usuário (seção **Testes** da spec).
- [ ] **Step 11: Commit** `Zonas: criar, excluir, salvar, Ctrl+Z e Ver no chão`
