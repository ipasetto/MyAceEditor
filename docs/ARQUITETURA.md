# Arquitetura do MyAceEditor

> Visão geral para quem chega ao projeto. Os detalhes técnicos completos (formatos binários,
> protocolo, IDs, dúvidas em aberto) ficam nas bases de conhecimento em [`docs/kb/`](kb/).

## 1. O que é

O MyAceEditor é uma **engine de edição do Perfect World** (motor Angelica): uma aplicação
desktop nativa em C#/WinForms que lê e edita os arquivos de dados do jogo, **do cliente e do
servidor**, sem depender de ferramentas externas.

A meta de longo prazo é tornar o jogo **100% editável** a partir de uma única janela:

| Fase | Capacidade | Estado |
|---|---|---|
| 1 | Ler e navegar `elements.data` (itens, equipamentos, NPCs, monstros…) | ✅ ~98,6% do arquivo |
| 1 | Ícones reais do jogo, lidos direto do `.pck` | ✅ 91,3% (o resto não existe no jogo) |
| 2 | Gravar `elements.data` (escrita byte-idêntica, backup automático) | ✅ |
| 2 | Vínculos entre itens (drops, receitas, quem usa) | ✅ |
| 2 | Quests (v93): campos, textos, diálogos, subquests | ✅ |
| 2 | GShop, AI Policy, NPCGen (spawns), quests completas | 🔜 formatos já documentados |
| 3 | Checar versão/edition do par cliente + servidor antes de publicar | ✅ |
| 3 | Gerar e publicar arquivos de servidor | 🔜 |
| 4 | Abrir janelas do próprio jogo dentro da engine (loja, inventário…) | 🔜 |
| 4 | Edição de skills | 🔜 |

## 2. Como o Perfect World funciona (resumo)

```mermaid
flowchart LR
    subgraph Cliente["Cliente (pasta element/)"]
        EXE[elementclient.exe]
        DATA["data/*.data<br/>elements · tasks · gshop · aipolicy"]
        PCK["*.pck<br/>surfaces · interfaces · configs · models"]
    end
    subgraph Servidor["Servidor (Linux, daemons 32-bit)"]
        GLINK[glinkd :29000]
        GDEL[gdeliveryd :29100]
        AUTH[authd :29200]
        GDB[gamedbd :29400]
        GS["gs (mapas)<br/>elements · tasks · aipolicy · npcgen"]
    end
    EXE -->|TCP| GLINK --> GDEL
    GDEL --> AUTH
    GDEL --> GDB
    GS --> GLINK
    GS --> GDB
```

Pontos que guiam todo o desenho da engine:

- **Os dados existem em par.** `elements.data`, `tasks.data`, `gshop`/`gshopsev`, `aipolicy.data`
  aparecem no cliente **e** no servidor. Editar só um dos lados causa erro de versão ou
  comportamento estranho, por isso a engine trata sempre o **par** (PW-KB §S7).
- **Versão importa.** O cliente envia ao servidor uma `edition` calculada a partir da versão do
  `elements.data`, do `tasks.data` e dos timestamps da loja. Se não bater, a sessão cai
  (PW-KB §S17).
- **Os formatos não são autodescritivos.** Um `elements.data` é só uma sequência de listas de
  registros de tamanho fixo. Quem diz o que é cada campo é um arquivo `.cfg` por versão
  (PW-KB §S23).

## 3. Camadas do código

```mermaid
flowchart TB
    UI["UI/EditorForm.cs<br/>WinForms nativo"]
    MCP["Tools/*.cs<br/>servidor MCP (modo --mcp)"]
    STORE["Store/ModuleStore.cs<br/>estado · carregar · salvar"]
    MODEL["Model/DataModel.cs<br/>Módulo → Lista → Item → Campo"]
    subgraph PW["PerfectWorld/ (formatos do jogo)"]
        CFG["ElementsCfg + CfgElementsReader<br/>elements.data guiado por .cfg"]
        PCKR["PckArchive<br/>.pck / .pkx"]
        DDS[DdsDecoder]
        ATLAS["IconAtlas<br/>ícones do surfaces.pck"]
        LBL["FieldLabels<br/>rótulos PT + grupos"]
    end
    UI --> STORE
    MCP --> STORE
    STORE --> MODEL
    UI --> ATLAS
    UI --> LBL
    STORE --> CFG
    ATLAS --> PCKR
    ATLAS --> DDS
```

| Camada | Responsabilidade | Arquivos |
|---|---|---|
| **UI** | Janela, lista de itens, detalhes, menus, tooltip | `UI/EditorForm.cs` |
| **Agente (MCP)** | As mesmas operações expostas a um agente de IA | `Tools/*.cs`, modo `--mcp` |
| **Estado** | Módulo carregado, mudanças, salvar | `Store/`, `Changes/` |
| **Modelo** | Representação neutra: Módulo → Lista → Item → Campo | `Model/DataModel.cs` |
| **Formatos PW** | Ler e gravar cada formato do jogo | `PerfectWorld/` |

A regra é: **formatos não conhecem a UI** e **a UI não conhece bytes**. Cada formato novo
(GShop, AI Policy, NPCGen…) entra como um leitor/gravador em `PerfectWorld/` que produz o mesmo
modelo, e a UI ganha a aba correspondente.

## 4. Fluxo ao abrir a pasta do jogo

`EditorForm` procura o arquivo em background com `ReadProgressDialog` e inicia
`ElementsLoadBundle.Read` em `Task.Run`. O bundle usa `CfgCatalog` e
`CfgElementsReader`, valida a regravacao sem alteracoes, prepara `RefIndex`,
icons e configs do cliente e tasks proximas. `LoadContext` distribui progresso
e cancelamento sem conhecer controles. A UI consulta somente o ultimo progresso
num timer de 100ms e adota o bundle inteiro depois de sucesso. Cancelar conserva
o modulo anterior. Esta alteracao atual e revisada no codigo, ainda sem execucao
WinForms neste ambiente.

A última pasta aberta fica salva em `%AppData%\MyAceEditor`. A abertura automatica
fica desligada por padrao; so `auto_load_last.txt` com valor `1` habilita a leitura
no evento Shown. O menu Arquivo permite alternar e persistir a opcao. Com a opcao
desligada, Shown retorna antes de consultar o ultimo arquivo de dados.

## 5. Formatos implementados

### `elements.data`
- Cabeçalho: `int16` versão + `int16` assinatura (ex.: `0x3000003C` = versão 60) e timestamp.
- Depois, listas em ordem fixa: `uint32 count` + `count` registros de tamanho fixo.
- Os campos de cada lista vêm de um `.cfg` da comunidade (`PerfectWorld/cfg/`).
- Textos: `wstring:N` = UTF-16LE, `string:N` = **GBK** (caminhos de arquivo em chinês).
- Hierarquia de tipos: `id_major_type`/`id_sub_type` apontam para as listas `*_MAJOR_TYPE`/`*_SUB_TYPE`.

### `.pck` / `.pkx` (pacotes Angelica)
- `[cabeçalho 12B][arquivos][tabela][rodapé 280B]`; a tabela fica no fim, com offset protegido
  por XOR; cada entrada (caminho GBK, offset, tamanho) pode estar comprimida com zlib.
- `.pkx` é a continuação lógica de um `.pck` acima de 2 GB.

### Ícones
- `surfaces\iconset\iconlist_ivtrm.dds` (+ `ivtrf`): atlas DXT1 4096×2048 com ícones de 32×32.
- O `.txt` ao lado lista os nomes na ordem da grade; o campo `file_icon` do item aponta para um
  desses nomes.

Formatos já documentados e prontos para implementar (GShop, AI Policy, NPCGen, mapas do
servidor, XML de interface): ver PW-KB §S18–§S22.

## 6. Princípios de engenharia

1. **Round-trip antes de editar.** Um formato só ganha edição depois de provar que lê e grava o
   arquivo **byte a byte idêntico**.
2. **Backup e escrita atômica, sempre.** Nenhum editor de terceiros estudado faz isso direito
   (ENGINE-KB §E9).
3. **Editar o par.** Cliente e servidor são exportados juntos, com checagem de versão.
4. **Ícones e textos do próprio jogo.** Nada gerado artificialmente.
5. **Nativo e leve.** Sem web, sem dependências pesadas; abre e navega 40 mil itens na hora.

## 7. Base de conhecimento

| Arquivo | Conteúdo |
|---|---|
| [`kb/PW-KB.md`](kb/PW-KB.md) | O **jogo**: estrutura do cliente, daemons e portas do servidor, protocolo de login, formatos binários, banco de dados, versões, ferramentas |
| [`kb/ENGINE-KB.md`](kb/ENGINE-KB.md) | A **engine**: o que já está implementado, mapa capacidade → arquivo → seção da PW-KB, roadmap e dúvidas em aberto |
| [`../.claude/skills/pw-engine/SKILL.md`](../.claude/skills/pw-engine/SKILL.md) | Skill do Claude Code que ensina o agente a consultar e atualizar as KBs |

As KBs usam formato telegráfico e **etiquetas de confiança** em cada fato: `[C]` código-fonte
lido, `[A]` arquivo real, `[B]` binário, `[T]` testado no nosso código, `[I]` inferência,
`[?]` dúvida. Contradições resolvidas vão para a seção `§Z` e dúvidas novas para a `§Q`.

## 8. Rodar e compilar

```bash
dotnet run
```

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```

Modos de linha de comando (diagnóstico):

| Modo | O que faz |
|---|---|
| `--inspect <elements.data>` | Relatório do parser |
| `--testcfg <cfg> <data>` | Valida um `.cfg` contra um `elements.data` |
| `--pck <arquivo.pck> [filtro]` | Lista o conteúdo de um pacote |
| `--icons <elements.data>` | Mede a cobertura de ícones e salva amostras |
| `--snapshot <saida.png>` | Abre o editor fora da tela e salva um print |
| `--mcp` | Servidor MCP por stdio para agentes de IA |


## CFG 多版本适配后的读取流程（C# 运行验证待完成）

EditorForm / CLI 先调用 CfgCatalog，按完整版本号查找并试读同版本分支，再由 CfgElementsReader 按 CFG 偏移和选定列表布局生成 Module。ElementsLayout 保存配置路径、完整性、警告与回写校验状态。只有普通格式完整读取且无修改重建字节一致后，ElementsWriter 才开放保存。压缩格式使用 MppcDecoder 解码，目前只读。

MPPC 空列表不读取 payload 长度；长记录先按 8 KB 封装解码，必要时尝试连续位流，均验证消费长度。BlockLabels 统一中文分类名，FieldLabels 负责字段标签；原始 CFG 名称与二进制键保持不变。

实际配置审查见 [CFG 多版本说明](CFG_多版本说明.md)；v344/v386/v531 已经独立参考读取器全量验证，见 [真实文件验证报告](真实文件验证报告.md)。C# 实现尚未在本次环境编译或运行。

### 汉化显示与原始数据键

FieldLabels 与补充 FieldLexicon 只生成界面文字；FieldEntry 单独保存 Key，因此查找时不使用翻译文字作为数据键。BlockLabels 生成中文分类名。ChineseDialogs 提供应用自有中文按钮和错误概述，原始诊断信息保留在只读详情中。CFG 原始指纹与文字覆盖由 Tests/audit_localization.py 检查；此流程不替代 Windows 窗口运行验证。

### 后台准备与界面接管

`PerfectWorld/LoadContext` 只携带进度与取消令牌，CLI 原有无上下文调用仍可同步运行。各阶段使用子区间；字节/记录进度按固定权重汇总，资源未知总量时显示滚动进度。`ReadProgressDialog` 的工作线程仅覆盖一个最新状态，界面计时器读取该状态，避免进度消息挤满队列。窗口的异步事件保持正常消息循环；代码不使用阻塞的任务等待或 `Application.DoEvents`。

`ElementsLoadBundle` 返回新 Module、完整 RefIndex、所有者表和资源，后台阶段不访问控件。`EditorForm` 直到工作完成且未被取消才接管。引用字段和对应概率名按 CFG 每块编译一次，`CachedUsedBy` 只查缓存，不触发全数据扫描。中文标签缓存使用线程安全字典，可在后台预热。

字段/关联列表每 128 行、任务树每 256 行让出一次界面线程；任务筛选在后台完成，TreeNode 仍在界面线程创建。修订号及数据引用检查阻止过时的刷新覆盖新选择。搜索输入合并 180ms 内连续变化，初始化时屏蔽重复选择事件。保存写入仍使用原有校验、备份和原子替换；本次没有把保存按钮流程改成异步。

新增取消与索引回归随源码提供，但本环境无法编译或运行；验收见 [读取进度与防卡顿说明](读取进度与防卡顿说明.md)。

### 任务点击与详情生命周期（本轮源码更新）

TasksFile 解析结束后创建 TaskIndex：保存先序任务节点缓存、按 ID 排序的主任务/搜索行和 ID 字典。UI 的任务字段提示直接查询字典，未加载元素数据时也可以查询任务名称。搜索读取模型当前名称，200ms 合并输入并取消旧请求；工作线程只筛选模型，不创建控件。

任务树、子任务和对话使用200项续载节点，点击后插入下一段；对话窗口/选项仅在展开时生成。ShowQuest 在任务页可见时才用后台任务准备字段值和提示，返回界面后每32项让出消息循环。文本控件和五组对话根节点延后到相应页签打开时绑定，不再 ExpandAll。

每个编辑区记录所属 QuestNode，切换任务前提交旧编辑内容，清空控件时屏蔽事件，过期后台结果不得覆盖新选择。显示换行有独立基线，用户未修改时不把显示规范化写回数据。任务分组标题共享并释放字体；页签绘制及时释放画刷和字体。当前无 tasks.data / .NET / Windows，这些流程尚未实际运行，验收步骤见 [任务卡顿修复说明](任务卡顿修复说明.md)。
