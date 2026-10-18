# ENGINE-KB — MyAceEditor (engine de edicao do Perfect World)
# Base de conhecimento da ENGINE. Complementa (nao duplica) a KB do JOGO: docs/kb/PW-KB.md (= "PWKB"). Visao para humanos: docs/ARQUITETURA.md.
# Formato: igual PWKB. Linhas "chave: valor" / "a | b | c". Busque por ID (ex: §E3). pt-BR telegrafico. Ref cruzada: PWKB§S17 = secao S17 da KB do jogo.
# Estado: vivo. Atualizar SEMPRE que algo novo for provado (protocolo em §U).

## §L LEGENDA
conf: mesma da PWKB§L ([C] codigo [A] arquivo real [B] binario [F] fonte forte [M] medio [W] fraco [I] inferencia [?] duvida) + [T]=testado no nosso codigo contra arquivo real do usuario
alvo do usuario: PKG144 (PWKB§L) = client/server "PW 1.4.4 v60" em D:\Perfect World\Perfect World 144
projeto: C:\Godot\MyAceEditor (isolado; NAO e o jogo C:\Godot\sapo)

## §0 INDICE
E1 visao/capacidades | E2 mapa engine x jogo (onde cada capacidade toca) | E3 elements.data (implementado) | E4 .pck/.pkx (plano nativo) | E5 versao/edition (checagem de erros) | E6 codigo (mapa) | E7 UI/features prontas | E8 roadmap | Z resolvidos | Q abertos | U protocolo de atualizacao | R fontes

## §E1 VISAO
engine (nao so editor): jogo 100% editavel por 1 app desktop nativa.
capacidades-alvo: editar todos os dados client+server | editar/criar skills | gerar arquivos de servidor + deploy automatico | detectar conflitos client x server | checar versao/edition | checar erros de servidor (logs/conf) | abrir janelas do jogo dentro da engine (gshop, inventario, personagem) p/ edicao visual
stack: C# .NET 10 WinForms nativo (WinExe net10.0-windows AllowUnsafeBlocks). SEM web (decisao do usuario). Pacotes: ModelContextProtocol 0.4.0-preview.1, Microsoft.Extensions.Hosting 10.0.0
modos (Program.cs): padrao=UI | --mcp=servidor MCP stdio (agente IA) | --inspect <data> | --testcfg <cfg> <data>
restricoes do usuario: recurso/token limitado -> nada pesado sem autorizacao | icones SEMPRE do jogo (nunca gerados) | tema: paineis branco aco, painel de itens preto (como no jogo)

## §E2 MAPA ENGINE x JOGO (capacidade -> arquivos/sistemas -> secao PWKB)
capacidade | client | server | PWKB | status
itens/equip/NPC/etc | data/elements.data | gamed/config/elements.data | §S7 §S17 | LEITURA ~98,6% [T]; ESCRITA PRONTA byte-identica [T] (§E3)
quests | data/tasks.data | tasks.data | §S7 §S17 §S21 | v93 COMPLETO [T] (§E3c): campos, textos, dialogos, subquests; premios preservados
IA mobs | data/aipolicy.data | aipolicy.data | §S7 §S18 | pendente; formato COMPLETO (mais pronto p/ implementar)
loja cash (gshop) | gshop.data gshop1/2 | gshopsev.data gshopsev1 | §S7 §S19 (NAO espelho) | pendente; formato COMPLETO dos 2 lados
spawns | - | npcgen.data | §S6 §S22 | pendente; formato v7-11 conhecido
regioes/portais/terreno | maps/*/region.clt precinct.clt .t2bk | region.sev precinct.sev .hmap .rmap .wmap path.sev | §S6 §S17 §S22 | pendente
skills | elementskill.dll (CElementSkill) + configs/skillstr.txt | gs (mesmo codigo #ifdef _SKILL_SERVER) | §S17(skills) | pendente; exige recompilar dll+gs -> maior risco
icones | surfaces.pck iconset/ (.dds+.txt) | - | §S2 §S3 | NATIVO PRONTO [T] (§E4)
janelas do jogo | interfaces.pck (XML, ex: qshop bag character) | - | §S2 §S20 | pendente; render: controles + moldura 9-slice + surfaces
inventario/personagem | - | gamedbd (GetRoleData 8003, PutRole 3024) | §S8 §S9 §S10 | pendente; blobs opacos (PWKB§Q3)
deploy/servidor | - | daemons, gs.conf, gamesys.conf, start.sh | §S4 §S5 §S6 §S24 | pendente
regras hardcoded (cla, TW, correio) | - | ELF gdeliveryd/gfactiond | §S25 | pendente; patch por simbolo DWARF + hash, nunca offset fixo
skills (textos) | configs.pck skillstr.txt | - | §S25 | pendente
regra de ouro: mudar dado de um lado sem o outro = erro/comportamento estranho (PWKB§S7). Engine deve editar o PAR client+server junto.

## §E3 elements.data (IMPLEMENTADO) [T]
arquivo do usuario: u32 0x3000003C = i16 versao 60 + i16 assinatura 0x3000 (PWKB§S23) | 2o u32=0x4F614E5B = time_t timestamp (~15/mar/2012) [I forte]
layout (casa c/ PWKB§S17): u32 version | time_t | listas em ordem fixa (u32 count + count*registro fixo) | 2 blocos de assinatura do exportador (tags 0xab7689dd e 0xee35679f; lidos e ignorados) | TALK_PROC variavel
definicao de campos: .cfg da comunidade (sTASKedit/Rody/DataTranslator). Usado: PerfectWorld/cfg/PW_1.4.4_v60.cfg (150 listas). = "declaracoes por versao" de PWKB§S3/§Q2
formato .cfg: linha contagem; por bloco "NNN - NOME" | linha flag | nomes de campo (;) | tipos (;). Tipos: int32/float=4 int16=2 byte=1 wstring:N=N bytes (N/2 chars UTF-16) string:N=N bytes. TALK_PROC = RAW/byte:AUTO
TALK_PROC: u32 count; por item: 4 + 0x80 + M*(8 + len1 + len1*2 + len2 + len2*0x88) [T]
hierarquia: id_major_type/id_sub_type -> listas *_MAJOR_TYPE / *_SUB_TYPE (categoria>tipo>subtipo)
armadilha [T]: ItemList.Name = nome PURO do bloco (sem prefixo numero), senao *_MAJOR_TYPE nao casa
cobertura: ~98,6% byte-perfeito; para em NPC_WAR_TOWERBUILD_SERVICE (cauda guerra territorio) -> §Q1
ref servidor: gs ELF c/ DWARF -> elementdataman 216 arrays + structs (PerfectWorld/server_ref/). Server analisado v145 tem *_ESSENCE ~4B maiores que o v144 do usuario -> para o arquivo do usuario confiar no .cfg v60
numeracao de listas client x server difere (PWKB§S3); conferir antes de editar par

ESCRITA (ElementsWriter.cs) [T]: cada Item guarda Raw (bytes originais do registro); Module.Source = ElementsLayout (bytes do arquivo + CountPos/EndPos de cada bloco lido)
 gravacao: copia byte a byte tudo fora dos blocos (header, blocos de assinatura, TALK_PROC, cauda nao lida ~1,4%) + por bloco: count novo + registros; registro parte do Raw e so recodifica campo cujo valor (texto) difere do decodificado do Raw -> floats/padding de strings preservados
 prova (--roundtrip): sem mudanca = IDENTICO 33.859.144 B (leitura 1,8s, gravacao 0,8s); renomear 1 item = 25 bytes diferentes so naquele registro (1420 B), mesmo tamanho, releitura OK [T]
 salvar na UI: Ctrl+S / Salvar como; backup <arquivo>.<yyyyMMdd-HHmmss>.bak + escrita atomica (tmp + File.Replace) + recarrega o arquivo
 novo item: Raw null -> todos os campos codificados do texto (zeros); clonar copia Raw
 leitor nao corta mais em 5000 itens/lista (cortava blocos grandes; perderia dados ao gravar)
 limites: string:N grava GBK (caracter fora do GBK vira ?), trunca deixando terminador; wstring:N UTF-16
 pendente: gravar o PAR client+server (numeracao de listas difere, PWKB§S3), atualizar timestamp? (§Q2), blocos com count 0 nao aparecem na UI (nao da p/ adicionar o 1o item)

## §E3e CFG 多版本适配（本次修正版）
[C][A-CFG] 随包 170 CFG / 164 版本 v6..623；结构审查 42,866 块；167 无数量警告、3 有字段名/类型数量不一致。只验证 CFG 文件，不等同于真实 elements.data 读取。
[C] CfgCatalog：低 16 位完整版本；分支后缀；同结构别名合并；候选按结构试读；歧义显式选择；无相邻版本回退。
[C] ElementsCfg：固定/AUTO offset、conversation 索引、文件顺序、293_2、UTF-8、uint/byte:N；重复字段生成独立键；缺失类型不猜补。
[C] CfgElementsReader：count / type-count-size / 逐记录 MPPC 索引；计数与边界、解码 ID、EOF 检查；未完整读取不标成功。第二个 u32 不再作为固定签名校验。
[C] 写回：普通完整且无警告格式通过无修改字节比较后可编辑；压缩、部分读取、有警告的格式只读；保留新版 type/size 头。
[C] UI：文件菜单手选 CFG；帮助菜单读取报告；诊断 --cfg-audit/--cfg-match/--cfg-batch。
[C] 新代码：PerfectWorld/CfgCatalog.cs、MppcDecoder.cs；回归入口 Tests/CfgRegression/CfgRegression.csproj；Python 实际配置审查 Tests/audit_cfg.py。
[A][C] 新附件 v344/v386/v531 经独立 Python/C++ 参考读取器逐条验证：385/398/420 块，98,307/105,892/106,019 普通记录，3,411/3,422/3,422 对话，全部 EOF；v344 参考重建字节相同。不是 C# 执行结果，不标 [T]。
[A][C] 修复：空 MPPC 列表只有 count，无 payload 长度；>8192 字节记录支持 ushort 段头及逐段 MPPC；两份压缩附件共 1,437 条分段记录。
[C] BlockLabels.cs 覆盖 485 个分类；FieldLabels 480 词汇；布局/报告中文；空分类可见，字段提示保留原始键。
[C] 回归新增长记录/截断/空列表；test-real-data.cmd 可用真实目录执行 C# 测试。参考工具 Tests/RealFiles，详情 docs/真实文件验证报告.md。
[?] 当前仍无 .NET SDK / Windows GUI：C# 编译、回归、界面未执行；其他真实客户端分支及压缩写回未验证。

### 本轮继续检测与界面汉化
[A][C] v344/v386/v531 独立参考复测全量通过；310,218 普通记录、10,255 对话；参考回归再次 504 项通过。不是 C# 运行结果。
[C] 汉化审查：290 固定文案，108,505 原始字段键，485 中文分类；词汇由 480 补至 1,159；29 原始缩写/参数代号保留。Tests/audit_localization.py 及 docs/汉化审查.json。
[C] UI：FieldEntry 显示中文、检索仍使用 Key；中文文件窗口标题与 ChineseDialogs；原始诊断单列详情；HP/MP 属性文案改为生命/真气。
[C] 职业选择默认 16 槽且保留未显示高位；ClassNames 不再以选择数>=10 推断“全部职业”。
[C] 新文件：PerfectWorld/FieldLexicon.cs、UI/ChineseDialogs.cs、Tests/cfg_original_hashes.json；原始 CFG 指纹来自输入项目包，未改 CFG。
[?] 无 SDK / Windows，编译和实际界面（含 DPI/遮挡）未验证；系统原生文件选择器按钮取决于操作系统语言。详见 docs/继续检测与汉化报告.md。

## §E3b TIPOS DE CAMPO + VINCULOS (FieldKinds.cs) [T]
regras NOSSAS pelo nome do campo (nao copiadas): ItemRef AddonRef RecipeRef MonsterRef SkillRef TaskRef Probability ClassMask File Color Plain
v60: ~3.600 campos classificados (ItemRef 831, TaskRef 1050, SkillRef 731, Probability 395, AddonRef 286, RecipeRef 136...); conferido c/ PW_types.csv do Rody: 0 conflitos nos 185 campos de mesmo nome [T]
par referencia->probabilidade: ProbabilityFor (drop_matters_N_id -> drop_matters_N_probability; addons_N_id_addon -> addons_N_probability_addon)
RefIndex: IDs essence (+monstros) / addon / recipe / monster; CLI UsedBy conserva preparacao sob demanda; UI prepara em background e consulta CachedUsedBy [C; alteracao atual nao executada]
UI: coluna Vinculo na grade (nome + bloco + %, classes por extenso; duplo clique navega) | aba Vinculos (quem usa) | tooltip c/ drops icone+nome+% (ex. monstro 986: 32 drops resolvidos) [T]
pendente: SkillRef/TaskRef so dizem "habilidade"/"quest" (sem nomes ainda: skillstr.txt / tasks.data); id_strategy -> aipolicy; icones de monstro

### §E3f 后台读取与进度（当前实现，未编译）
[C] LoadContext：可组合阶段、同一取消令牌、80ms 限频、单调百分比；文件按 1MiB 只读分块；不是预计剩余时间。
[C] ReadProgressDialog：Shown 异步启动 Task.Run；模态窗口保留消息循环；最新进度槽+100ms 界面计时器，不逐条 Post；Esc/关闭/按钮请求协作取消，完成后再关闭窗口。
[C] ElementsLoadBundle：后台匹配/解码/写回校验/双向索引/所有者表/中文标签预热/图标/描述/同目录任务；只返回数据，控件由 EditorForm 成功后接管。
[C] UI 不在绘制中调用 UsedBy；Prepare 按 CFG 预编译引用和概率字段，完整结束才发布反向索引；取消不发布部分索引。CLI 仍可同步读取。
[C] EditorForm：取消或准备失败保留旧会话；字段和关联每128行、任务每256行分批；异步刷新用修订号丢弃旧结果；搜索180ms合并；控件始终在界面线程操作。
[C] 启动自动读取默认关闭；文件菜单“启动时自动读取上次文件”勾选保存 auto_load_last=1/0；仅值1开启，缺失/其他值均关闭。Shown 在读取 last.txt/访问数据路径前检查开关；旧的上次文件路径不自动启用。--snapshot 同样遵循设置；当前仅源码审查，Windows运行待验证。
[C] 取消贯穿 CFG/记录/字段、MPPC、写回验证、资源包表/分块解压、DDS、附加属性和任务结构；正在进行的系统 I/O/单次库调用仍需返回后检查。
[C] 新文件：PerfectWorld/LoadContext.cs、UI/ReadProgressDialog.cs、UI/ElementsLoadBundle.cs；Tests/CfgRegression 新增取消、句柄释放、进度和索引案例。
[A][C] 当前独立参考复测：v344/v386/v531 全量通过；504 项参考回归；170 CFG 原始指纹不变；298 项固定文字审查。二进制格式规则未变。
[?] 没有 .NET SDK / Windows：未编译、未执行 C# 测试或界面性能测量；不能据参考读取耗时推断应用速度。说明 docs/读取进度与防卡顿说明.md。

## §E3c tasks.data (TasksFile.cs + TaskSchema.cs) — v93 COMPLETO [T]
esquema v93 escrito a partir do estudo do Rody V22 (reducao automatica dos ifs de versao: scratch v93_reduce.py; codigo nosso, declarativo)
quest = 58 fases do bloco fixo (238 campos rotulados PT, editaveis no lugar) + arrays variaveis (horarios, chaves, expr PQ, contrib, regioes, itens pedidos/dados, membros, monstros/itens a obter, expressoes, textos de quest, locais) + premios AWARD_DATA (sucesso, falha, 2× por razao [5 escalas], 2× por itens [5 escalas]) + 4 pwstr (descricao/ok/no/tributo) + 5 talk_proc (id, texto 64ch, janelas{id,pai,texto,opcoes{param,texto 64ch,id}}) + i32 nSub + subquests
textos: UTF-16 XOR (ushort)ID (nome, pwstr, dialogos, opcoes). pwstr vazio = len 0; terminador \0 so se o original tinha (NullFlags); textos fixos de 64ch carregam lixo apos o \0 -> guardar Raw e reusar se nao mudou [T]
prova (--tasks): 9.298 topo / 21.777 total; cada quest termina no offset da proxima; textos recodificados identicos 21.777/21.777; regravar = IDENTICO; editar descricao OK [T]
gravacao: reconstroi tabela de offsets; bloco fixo + textos originais (se nao mudaram) + filhos
UI aba Quests: arvore (subquests ao expandir, cores ^RRGGBB), Dados (grade agrupada, nomes de item/monstro/quest), Textos, Dialogos (arvore janelas/opcoes editavel), Salvar c/ backup
[C; alteracao atual nao executada] TaskIndex 建立一次任务ID字典、先序节点缓存及排序搜索数组；任务字段提示不再逐字段 AllNodes 扫描，也不依赖已加载 elements 才能查任务名。
[C] 任务搜索200ms合并、取消旧任务；显示仅创建200项树节点，通过续载节点按需追加，不再3000条截断；主任务恢复定位可从目标所在分页开始。
[C] ShowQuest 后台准备字段/引用/显示文本，字段32项分批；任务页隐藏时不建详情；文本和5组对话仅在对应页打开时绑定；窗口/选项延迟展开，不使用 ExpandAll。
[C] 文本/对话/字段编辑记录各自归属；切换前提交，清空时屏蔽事件；异步结果用修订号、TasksFile和QuestNode引用确认；对话更新原节点而非刚选中的新节点；未编辑的显示文本不规范化写回。
[C] 任务分组标题共用Font并随窗口关闭释放；主标签绘制及时释放Font/Brush；对话预览只取前50字符再替换换行；深层任务绘制保护空/负宽度。
[?] 当前没有tasks.data、.NET SDK或Windows；上述为源码修复与审查，不是任务真实文件或界面测试结果。新增C#索引回归未执行；docs/任务卡顿修复说明.md。
pendente: editar premios/listas (hoje preservados), criar/clonar quest, outras versoes (so nomes)

## §E3d CONFIGS DO CLIENTE (GameConfigs.cs) [T]
configs.pck: item_color.txt (5.444; "ID<TAB>idx") + tabela oficial l_aNameCols (source_client EC_Game.cpp): 0 branco 1 azul-claro 8080ff 2 amarelo ffdc50 3 roxo aa32ff 4 dourado-escuro ff6000 5 branco 6 cinza b0b0b0 7 verde-agua 00ffae 8 vermelho [C][T]
item_ext_desc.txt (9.681; UTF-16, "ID<TAB>\"texto\"", ^RRGGBB, \r literal = quebra) -> aba "Descricao e cor" [T]
equipamentos geralmente sem entrada (descricao gerada pelos atributos no jogo)

## §E5b VERIFICACAO DO PAR (VersionCheck.cs) [T]
aba Verificacao + --check <element> <home>: elements versao/data, copias de elements por mapa (gamed/config/aNN), tasks versao/nº, gshop×gshopsev e gshop1×gshopsev1 timestamp/nº, aipolicy, glinkd version (hex), isec/osec cruzadas (sem mostrar chaves)
par real do usuario: cliente D:/Perfect World/Perfect World 144/element ; servidor D:/Perfect World/PW_ServerFiles_1.4.4v60/[andrey.pw]_PW_ServerFiles_1.4.4v60/server files/home -> ERRO loja (ts/itens diferentes); avisos: copias a29/a32/a33 antigas (v27/v12), gshopsev1 ausente

## §E4 .pck/.pkx (NATIVO, IMPLEMENTADO) [T]
PckArchive.cs: le v2/v3 + .pkx logico, tabela XOR KEY_1, entradas zlib (ZLibStream), paths GBK. Chaves (uPCK PCKKey.cs): KEY_1=-1466731422 KEY_2=-240896429 ASIG_1=-33685778 ASIG_2=-267534609 FSIG_1=0x4DCA23EF FSIG_2=1453361591 (PWKB omite de proposito; a engine precisa) [C-uPCK][T]
surfaces.pck do usuario: v2, 5147 entradas, 130MB [T]
icones de item: surfaces\iconset\iconlist_ivtrm.dds (masc) + iconlist_ivtrf.dds (fem): DXT1 4096x2048 | .txt GBK: linhas 1-4 = iconW(32) iconH(32) linhas_usadas(46) colunas(128), depois 1 nome por icone; icone i = col i%128, linha i/128 [T]
file_icon do elements ("Surfaces\图标\通用物品\nome.dds") casa pelo NOME do arquivo no .txt [T]. Cobertura: 15568/17047 = 91,3% [T]; o resto NAO existe no pck (itens sem icone no proprio jogo) -> mostra unknown.dds (1a entrada do .txt), como o client [I]
outros atlas: iconlist_skill _pet _guild _action _elf _faction _pq _calendar, emotions (mesmo formato [I])
DdsDecoder.cs: DXT1/3/5 + RGB(A) 24/32bpp. IconAtlas.cs: carga 0,13-0,3s, recorte 32x32 sob demanda
modos CLI: --pck <arquivo> [filtro] | --icons <elements.data> | --snapshot <png> [aba] | --roundtrip <elements.data> | --check <element> <home> | --kinds <cfg> | --refs <elements.data> <bloco> <id> | --tasks <tasks.data> | --configs <configs.pck> [id]
sPCK/pasta extraida: so alternativa (aba Pacotes .pck)

## §E5 VERSAO/EDITION (base da checagem de erros de versao)
client manda Challenge.edition = hex(ELEMENTDATA_VERSION, task_templ_version, gshop_ts1, gshop_ts2) (PWKB§S17). Divergencia = "versao errada"/fecha sessao.
=> engine deve: ler version de elements.data + version de tasks.data (magic 0x93858361) + timestamps gshop, nos 2 lados, e acusar diferenca ANTES do deploy.
glinkd version = HEX (10404=1.4.4) (PWKB§S5). tasks.data versao divergente = erro -2/-3.

## §E6 CODIGO (C:\Godot\MyAceEditor)
arquivo | papel
Program.cs | entrypoint + modos
UI/EditorForm.cs | UI principal (arvore, lista, grade, busca, menus, icones, tooltip, settings %AppData%/MyAceEditor)
UI/ReadProgressDialog.cs UI/ElementsLoadBundle.cs | 后台读取进度窗口、最新状态取样与完整结果接管 [C]
PerfectWorld/LoadContext.cs | 阶段进度、限频、取消和分块读取 [C]
PerfectWorld/CfgElementsReader.cs | parser elements.data guiado por .cfg (blobs, TALK_PROC, Id/Name, Raw + layout)
PerfectWorld/ElementsWriter.cs | gravacao byte-identica + ElementsLayout + salvar c/ backup atomico
PerfectWorld/FieldKinds.cs | tipos de campo + RefIndex (vinculos)
PerfectWorld/VersionCheck.cs | verificacao do par cliente x servidor
PerfectWorld/TasksFile.cs | tasks.data (arvore, gravacao c/ tabela de offsets)
PerfectWorld/TaskIndex.cs | 任务ID、节点缓存、可取消的搜索与名称查询 [C]
PerfectWorld/TaskSchema.cs | esquema v93 (campos, premios, textos, dialogos)
PerfectWorld/GameConfigs.cs | item_color + item_ext_desc do configs.pck
PerfectWorld/ElementsCfg.cs | parser .cfg (CfgBlock/CfgField, TypeSize)
PerfectWorld/BlockLabels.cs | 485 个数据分类中文显示名 [C]
PerfectWorld/FieldLabels.cs | 中文字段标签（1,159 词汇，含 FieldLexicon 补充）+ 分组 (Basico Requisitos Ataque Defesa Durabilidade Economia Atributos/Addons Modelo/Arquivos Outros)
PerfectWorld/PckArchive.cs | leitor nativo .pck/.pkx
PerfectWorld/DdsDecoder.cs | DDS DXT1/3/5 + RGB(A)
PerfectWorld/IconAtlas.cs | icones do surfaces.pck (ivtrm+ivtrf, fallback unknown.dds)
PerfectWorld/IconLoader.cs | icones de pasta extraida (alternativa; TGA proprio, unsafe)
PerfectWorld/ElementsReader.cs ElementsSchema.cs ElementsBlocks.cs ElementsProgram.cs FORMAT.md | parser antigo via RE (superado; usado por --inspect)
PerfectWorld/cfg/ | 本次为 170 个 CFG v6..v623；详见 §E3e [C]
PerfectWorld/server_ref/ | block_order.tsv struct_fields.txt elementdataman_members.tsv
Model/DataModel.cs | Module>ItemList(Name Group Kind Items)>Item(Id Name Fields)>Field(Type Value)
Store/ModuleStore.cs Changes/ChangeSet.cs | estado, load/save, mudancas
fluxo: EditorForm -> ModuleStore/CfgElementsReader -> elements.data(+.cfg)

## §E7 UI/FEATURES PRONTAS [T]
[C; nao executado] 任务点击详情后台准备、树节点分页与对话按需展开、搜索合并/取消及编辑归属保护；新增实现，Windows验收待完成。
[C; nao executado] 后台读取/校验/引用索引/资源准备、中文进度与取消、分批列表和刷新去重；本次新增，Windows 验收待完成，详见 §E3f。
layout = modelo do Rody's Angelica Editor (decisao do usuario; arvore em cascata abandonada). Rody: exe nativo x86 sem metadados .NET (protegido/empacotado) -> layout copiado do print, sem RE
esquerda (fundo preto): seletor de bloco "[NNN] Nome (qtd)" (1a opcao = ★ Favoritos; lembra o ultimo; padrao WEAPON_ESSENCE) | filtro tipo/subtipo (so blocos *_ESSENCE c/ id_major_type/id_sub_type) | busca nome/ID + caixa "todos os blocos" | ListView VIRTUAL owner-draw: ID | icone 24px + nome na cor de grade (★☆»«*) | clique na coluna ordena | rodape: Adicionar | ir para ID (Enter) | Excluir
direita: cabecalho icone+nome+ID | grade de campos agrupados/rotulados (addons vazios ocultos) | edicao inline
geral: tooltip estilo jogo no hover | menu de contexto clonar/novo/excluir/favoritar | novo item c/ proximo codigo | auto-recarrega ultima pasta somente quando habilitado (padrao desligado; alteracao atual [C]) | icones nativos do surfaces.pck | scrollbar escura (SetWindowTheme DarkMode_Explorer) | combos escuros owner-draw
abas: Elements (pronta) | Quests (v93 completa §E3c) | GShop/AI Policy (vazias) | Verificacao (§E5b) | Pacotes .pck (alternativa sPCK)
detalhe do item: cabecalho escuro (icone|ID|nome cor oficial) | abas Valores (sub-abas por grupo: Todos/Basico/.../Queda/Habilidades/Modelo; coluna Vinculo) | Vinculos (N) | Buscar (campo op valor) | Descricao e cor (§E3d)
lista: coluna ∞ = nº de vinculos | barra: ◀ ▶ historico | status: total/bloco/selecionado
diagnostico: --snapshot <png> abre o editor fora da tela c/ a ultima pasta e salva print (verificacao visual sem abrir janela)

## §E8 ROADMAP (ordem sugerida; custo: B=baixo M=medio A=alto)
META INICIAL (decisao do usuario 2026-10-17): o app fazer 100% do que o Rody's Angelica Editor faz, com CODIGO NOSSO e interface parecida com a dele.
 referencia de estudo: codigo-fonte do Rody em D:/Perfect World/PW/Outros Editores/Rodys Angelica Editor.zip (C# net481, ~1200 .cs; modulos NextAIPolicyEditor2 NpcGenDataEditor PCKManager DDS MppcLibrary RAE_Octets; dados configs/PW/PW_types.csv (tipo/vinculo de cada campo: matters, skills_pw, ft_prob, ft_bool, ft_hex_color, time_seconds, *_ESSENCE...), PW_pt.csv (rotulos PT), PW_enums.json, 134 .cfg, 23 .rules)
 mais referencias (D:/Perfect World/PW/Outros Editores/Angelica Editor/): Rodys_Angelica_Editor_SRC_V22 (mais nova; READMEs V15-V17 em chines: tasks.data v128/v187/v191 completo c/ subtarefas, dialogos, NewID, header v223+ -> base p/ editor de quests e auto-tradutor) | AngelicaEditor_V45/V46/V52 (binarios) | AngelicaCore_V2: codigo RECUPERADO de binario protegido (Enigma -> pe-sieve -> de4dot -> ilspycmd), README diz "private reference, study" -> so estudo, NUNCA codigo no repo; tem verification/ (regressao) e McpServer (87 tools = lista de funcoes de uma engine completa)
 usuario afirma que o material do Rody foi liberado pela comunidade/autor (2026-10-17); mesmo assim: seguir modelos/UI/formatos (ok) e escrever codigo proprio.
 REGRA: consultar so o trecho do passo atual; NUNCA copiar codigo nem arquivos de dados dele p/ o repo (repo publico; parece pasta de trabalho privada de software licenciado). Escrever a nossa versao.
1b aba GShop (formato completo PWKB§S19; exporta client+server) M | aba AI Policy (PWKB§S18) M | npcgen/spawns (PWKB§S22) M
4 tasks.data / gshop / aipolicy (abas) M cada
5 fechar cauda elements.data (§Q1) B-M
5b nomes de skill (skillstr.txt) p/ SkillRef B | editar premios de quest M
6 renderizar janelas do jogo a partir de interfaces.pck XML (gshop/qshop primeiro) A
7 deploy servidor (gerar conf, subir, validar portas/ordem PWKB§S4-S5) A
8 inventario/personagem via gamedbd rpc (PWKB§S8-S10; blobs opacos) A
9 skills (dll+gs; PWKB§S17 skills) A+

## §E9 LICOES DOS EDITORES DE TERCEIROS (PWKB§S18-S25) — o que a engine deve fazer MELHOR
todos: decompilados (C++/CLI ou C#), x86, net20-45; NENHUM faz backup confiavel -> engine: backup automatico + escrita atomica (grava tmp, renomeia) SEMPRE
validacao: round-trip byte-identico (ler->gravar->comparar) antes de liberar edicao de qualquer formato. Critico em aipolicy (op sem tamanho)
strings: string:N = GBK (acentos pt-BR viram ?) | wstring:N = UTF-16LE. .NET: registrar CodePagesEncodingProvider p/ GBK (936)
versoes: tratar layout por versao (aipolicy dwVersion por trigger; npcgen v7-11; tasks limiares; elements .cfg por versao)
recursos a copiar: .rules (converter versao EL), join por ID c/ log, xref via references.txt, validacoes (soma de probabilidades, skills invalidas, valores fora de faixa do sTASKedit), busca por valor/tipo (AIPolicy)
par client+server: gshop (1380B x 164B, control_type), elements, tasks -> exportar os 2 lados juntos e checar timestamps (§E5)
patches em binario: tabela de patches c/ verificacao de hash do ELF, achar alvo por simbolo DWARF, nunca offset fixo de outro build
coordenadas: client exibe X=400+x/10 Z=550+z/10 (tasks); path 1px=1un centro; wmap 1px=2un -> unificar num conversor so

## §Z RESOLVIDOS
- formato elements.data do usuario: v60 via .cfg comunidade (nao RE) [T]
- 2o u32 do header = timestamp, nao assinatura [I forte, casa PWKB§S17]
- "blobs especiais" = blocos de assinatura do exportador (PWKB§S17) = listas especiais 20 e 100 do sELedit (PWKB§S23) [I forte]
- header elements: 1o u32 = i16 versao + i16 assinatura (PWKB§S23)
- ItemList.Name sem prefixo numerico (bug de hierarquia corrigido) [T]
- UI nativa WinForms (web/WebView2 abandonado)
- string:N do elements e GBK (era lido como Latin-1: caminhos saiam embaralhados) [T]
- PCK nativo pronto; icones automaticos sem sPCK [T]
- escrita elements.data pronta e provada byte-identica (§E3) [T]

## §Q ABERTOS
1 cauda ~1,4% elements.data a partir de NPC_WAR_TOWERBUILD_SERVICE (guerra territorio; comparar .cfg v60 x v61 / PWKB DOMAIN_COUNT=52)
2 escrita: manter ou atualizar o timestamp (2o u32) ao salvar? hoje mantem (copia intacto)
4 numeracao client x server das listas no PKG144 (p/ editar par)
5 nosso TALK_PROC (4+0x80+M*(...)) x layout Dialog/Question/Choice do sELedit (PWKB§S23): conferir se descrevem o mesmo
6 ordem de implementacao dos novos formatos: aipolicy e gshop ja tem formato completo; tasks depende do corpo ATaskTempl
7 git: repo github.com/ipasetto/MyAceEditor, branch main = historico completo; main remoto antigo (Initial commit + HelloWorld c/ bin/obj) precisa de force push feito pelo usuario; backup local em main-antigo. Repo sera PUBLICO: nada de segredos nas KBs

## §U PROTOCOLO DE ATUALIZACAO (como chegar a 100%)
- fato novo sobre o JOGO -> PWKB (secao §S certa, com tag conf). Fato sobre a ENGINE/nosso codigo -> aqui.
- toda linha nova leva tag conf; [T] so se testado contra arquivo real.
- contradicao resolvida -> registrar em §Z (e corrigir a linha original). Duvida nova -> §Q.
- feature concluida -> mover de §E8 p/ §E7; atualizar §E6 se criar arquivo.
- nao colar dumps; telegrafico; preferir referencia cruzada (PWKB§Sx) a repetir.
- ler por secao (Grep "## §E3"), nunca o arquivo inteiro sem necessidade (economia de token).

## §R FONTES
PWKB (docs/kb/PW-KB.md) | cfg comunidade (sTASKedit/Rody/DataTranslator) | gs DWARF via REA+Ghidra (server_ref/) | forum mmorpg-devs.ru (threads pkx, estrutura do client) | uPCK local (source_uPCK, chaves PCKKey.cs)
