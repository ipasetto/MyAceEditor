# AFAZERES — mapa de etapas (ler 1o; curto de proposito)
# legenda: [x] feito [~] parcial [ ] falta [>] em andamento | detalhes: docs/kb/ENGINE-KB.md (§Exx)
# PUBLICO (GPL-3.0): tarefas = issues #1-#32 no GitHub (gh CLI logado, conta ipasetto); roadmap p/ comunidade em docs/ROADMAP.md (manter em sincronia c/ este arquivo); guia em CONTRIBUTING.md
# PARIDADE: checklist completo das funcoes do Rody em docs/PARIDADE-RODY.md (ver antes de escolher a proxima tarefa)
# META: app fazer 100% do Rody's Angelica Editor, codigo nosso, interface parecida (§E8)

## 任务点击卡顿（本次）
[x] 任务 ID/搜索索引，后台准备详情，字段32项分批；树节点200项分页、对话按需展开；隐藏页不预建详情。[C]
[x] 搜索200ms合并和取消过期请求；修复编辑归属、旧对话节点标题、无改动文本误标修改及绘制资源释放。[C]
[x] 37个C#文件词法检查、任务路径人工审查、299项固定文案和170 CFG指纹审查通过。[C]
[x] 新增任务索引、1200子任务搜索、改名、重复ID、取消及空任务写回的C#回归案例。[C]
[ ] 缺少tasks.data和.NET/Windows，未执行C#回归、实际任务点击或界面性能测量；详见 docs/任务卡顿修复说明.md。

## 取消启动自动读取（前次）
[x] 文件菜单新增默认关闭的启动自动读取开关；保存用户选择，启动时先检查开关再访问上次文件。[C]
[x] 修改源码的字符串/分隔符、启动调用路径及汉化审查通过；170 CFG 原始指纹保持不变。[C]
[ ] 无 .NET / Windows，本次未编译、未实际重启验证开关；验收见 docs/取消自动读取说明.md。

## 读取进度与防卡顿（前次）
[x] 后台读取/解码/写回校验/资源及引用索引；阶段进度、耗时、限频、取消；成功后统一接管新数据。[C]
[x] 引用字段按 CFG 预编译，绘制只查缓存；字段/关联/任务分批显示，搜索合并、选择事件去重。[C]
[x] 三个真实样本独立参考复测、504 项参考回归、170 CFG 指纹和 298 项固定文案审查通过。[A][C]
[x] 新增取消、分块文件句柄释放、进度单调和反向索引完整发布的 C# 回归案例。[C]
[ ] Windows/.NET 编译、上述 C# 回归及界面响应/取消验收未执行；详见 docs/读取进度与防卡顿说明.md。

## 继续检测与补全汉化（前次）
[x] 三个真实文件参考复测及 504 项参考回归通过；CFG 原始指纹保持一致。[A][C]
[x] 补齐字段查找/状态提示/文件标题/中文确认弹窗；679 个新词汇，总计 1,159；290 项固定文案审查。[C]
[x] 修正职业选择高位掩码保留及“全部职业”误判。[C]
[ ] 无 .NET SDK / Windows：C# 编译、实际界面与缩放仍待验证；详见 docs/继续检测与汉化报告.md。

## CFG 多版本适配（前次）
[x] CFG 结构审查：170 文件 / 164 版本；实现版本、偏移、列表头、MPPC 读取和 EOF 校验 [C]。
[ ] 在 .NET 执行 Tests/CfgRegression；当前没有 SDK。
[x] 真实 v344 / v386 / v531：独立 Python/C++ 参考验证全部记录、对话、ID 和 EOF；修复压缩空列表、8 KB 分段。[A][C]
[x] 485 个数据分类中文名、480 个字段词汇、中文布局/报告、原始字段提示。[C]
[ ] Windows C# 编译、真实文件回归、界面点击验证；详见 docs/真实文件验证报告.md。

## FASE 1 — base
[x] elements.data leitura ~98,6% (§E3)            [ ] cauda Territory War ~1,4% (§Q1)
[x] elements.data gravacao byte-identica (§E3)    [x] .pck/.pkx nativo + icones (§E4)
[x] verificacao par cliente x servidor (§E5b)     [x] tipos de campo + vinculos (§E3b)
[x] docs publicas: README, ARQUITETURA, KBs, skill

## FASE 2 — paridade Rody (Elements)
[x] lista plana: bloco | filtro tipo | busca | ID+icone+nome colorido | ordenar
[x] detalhe: Valores (coluna Vinculo) | Vinculos (quem usa) | tooltip c/ drops
[x] tela Rody: cabecalho escuro ID+nome (cor oficial item_color) | sub-abas por grupo (Todos/Basico/.../Queda/Habilidades/Modelo) | col. ∞ vinculos | ◀ ▶ | Descricao e cor (item_ext_desc) | Buscar por campo | barra de status
[ ] editores especiais: addons, mascaras (classes) c/ checkbox, bool como checkbox, cor, probabilidades
[ ] editar descricao do item (exige gravar configs.pck)
[ ] rotulos PT melhores p/ campos (ex. "Magico defences 1 min")
[ ] nomes de skill (skillstr.txt) e de quest nos vinculos
[ ] icones de monstro/NPC
[ ] importar/exportar item (entre elements de versoes diferentes, .rules)

## FASE 3 — outros arquivos
[x] tasks.data v93 completo: 21.777 quests, 238 campos, textos, dialogos; leitura/gravacao byte-identica (§E3c)
[x] aba Quests: arvore + Dados + Textos + Dialogos
[ ] quests: editar premios/itens/monstros pedidos (hoje preservados como bytes) | criar/clonar/excluir quest | outras versoes (so v93 completa)
[ ] auto-tradutor (quests + elements + configs.pck)
[ ] GShop (cliente+servidor juntos; formato PW-KB §S19)
[ ] AI Policy (PW-KB §S18) | NPCGen/spawns (PW-KB §S22)
[ ] gravar .pck (para interface/configs)

## FASE 4 — engine
[ ] corrigir automaticamente o par (exportar loja/elements/tasks p/ os 2 lados)
[ ] racas iniciais (exe + createrace.xml + mascara; PW-KB §S12) | jogadores na TW (§S25)
[ ] janelas do jogo dentro da engine (interfaces.pck XML; PW-KB §S20)
[ ] deploy de servidor | editor de mapa (PW-KB §S22) | skills

## ESTADO GIT
repo PUBLICO github.com/ipasetto/MyAceEditor (GPL-3.0), branch main. Usuario faz "Push origin" (force push bloqueado p/ mim).

## REGRAS RAPIDAS
recurso limitado: nada pesado sem autorizacao | backup + escrita atomica + round-trip antes de editar formato | editar o PAR cliente+servidor | codigo nosso (Rody = referencia de estudo) | atualizar este arquivo ao concluir/iniciar etapa
