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

## §E3b TIPOS DE CAMPO + VINCULOS (FieldKinds.cs) [T]
regras NOSSAS pelo nome do campo (nao copiadas): ItemRef AddonRef RecipeRef MonsterRef SkillRef TaskRef Probability ClassMask File Color Plain
v60: ~3.600 campos classificados (ItemRef 831, TaskRef 1050, SkillRef 731, Probability 395, AddonRef 286, RecipeRef 136...); conferido c/ PW_types.csv do Rody: 0 conflitos nos 185 campos de mesmo nome [T]
par referencia->probabilidade: ProbabilityFor (drop_matters_N_id -> drop_matters_N_probability; addons_N_id_addon -> addons_N_probability_addon)
RefIndex: IDs essence (+monstros) / addon / recipe / monster; indice reverso UsedBy (quem usa o item) construido sob demanda
UI: coluna Vinculo na grade (nome + bloco + %, classes por extenso; duplo clique navega) | aba Vinculos (quem usa) | tooltip c/ drops icone+nome+% (ex. monstro 986: 32 drops resolvidos) [T]
pendente: SkillRef/TaskRef so dizem "habilidade"/"quest" (sem nomes ainda: skillstr.txt / tasks.data); id_strategy -> aipolicy; icones de monstro

sim/nao (IsFlag) [T]: FlagFields.cs = 253 pares (bloco|campo) ft_bool da lista do Rody (configs/PW/PW_types.csv). So campos com tipo especial entram nela; campos comuns (ex.: price) nao. Substituiu regra por nome (cobria 37 de 106 nomes). Rody PW_FieldSets.csv vazio na copia: sem lista de agrupamento da grade [A].

## §E3c tasks.data (TasksFile.cs + TaskSchema.cs) — v93 COMPLETO [T]
esquema v93 escrito a partir do estudo do Rody V22 (reducao automatica dos ifs de versao: scratch v93_reduce.py; codigo nosso, declarativo)
quest = 58 fases do bloco fixo (238 campos rotulados PT, editaveis no lugar) + arrays variaveis (horarios, chaves, expr PQ, contrib, regioes, itens pedidos/dados, membros, monstros/itens a obter, expressoes, textos de quest, locais) + premios AWARD_DATA (sucesso, falha, 2× por razao [5 escalas], 2× por itens [5 escalas]) + 4 pwstr (descricao/ok/no/tributo) + 5 talk_proc (id, texto 64ch, janelas{id,pai,texto,opcoes{param,texto 64ch,id}}) + i32 nSub + subquests
textos: UTF-16 XOR (ushort)ID (nome, pwstr, dialogos, opcoes). pwstr vazio = len 0; terminador \0 so se o original tinha (NullFlags); textos fixos de 64ch carregam lixo apos o \0 -> guardar Raw e reusar se nao mudou [T]
prova (--tasks): 9.298 topo / 21.777 total; cada quest termina no offset da proxima; textos recodificados identicos 21.777/21.777; regravar = IDENTICO; editar descricao OK [T]
gravacao: reconstroi tabela de offsets; bloco fixo + textos originais (se nao mudaram) + filhos
UI aba Quests: arvore (subquests ao expandir, cores ^RRGGBB), Dados (grade agrupada, nomes de item/monstro/quest), Textos, Dialogos (arvore janelas/opcoes editavel), Salvar c/ backup
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
PerfectWorld/CfgElementsReader.cs | parser elements.data guiado por .cfg (blobs, TALK_PROC, Id/Name, Raw + layout)
PerfectWorld/ElementsWriter.cs | gravacao byte-identica + ElementsLayout + salvar c/ backup atomico
PerfectWorld/FieldKinds.cs | tipos de campo + RefIndex (vinculos)
PerfectWorld/VersionCheck.cs | verificacao do par cliente x servidor
PerfectWorld/TasksFile.cs | tasks.data (arvore, gravacao c/ tabela de offsets)
PerfectWorld/TaskSchema.cs | esquema v93 (campos, premios, textos, dialogos)
PerfectWorld/GameConfigs.cs | item_color + item_ext_desc do configs.pck
PerfectWorld/ElementsCfg.cs | parser .cfg (CfgBlock/CfgField, TypeSize)
PerfectWorld/FieldLabels.cs | rotulos PT + grupos (Basico Requisitos Ataque Defesa Durabilidade Economia Atributos/Addons Modelo/Arquivos Outros)
PerfectWorld/PckArchive.cs | leitor nativo .pck/.pkx
PerfectWorld/DdsDecoder.cs | DDS DXT1/3/5 + RGB(A)
PerfectWorld/IconAtlas.cs | icones do surfaces.pck (ivtrm+ivtrf, fallback unknown.dds)
PerfectWorld/IconLoader.cs | icones de pasta extraida (alternativa; TGA proprio, unsafe)
PerfectWorld/ElementsReader.cs ElementsSchema.cs ElementsBlocks.cs ElementsProgram.cs FORMAT.md | parser antigo via RE (superado; usado por --inspect)
PerfectWorld/cfg/ | .cfg v60 v61 + README + cfg_walk_poc.py
PerfectWorld/server_ref/ | block_order.tsv struct_fields.txt elementdataman_members.tsv
Model/DataModel.cs | Module>ItemList(Name Group Kind Items)>Item(Id Name Fields)>Field(Type Value)
Store/ModuleStore.cs Changes/ChangeSet.cs | estado, load/save, mudancas
fluxo: EditorForm -> ModuleStore/CfgElementsReader -> elements.data(+.cfg)

## §E7 UI/FEATURES PRONTAS [T]
- [T] Desfazer/refazer (Changes/EditHistory.cs; issue #1): Editar > Desfazer (Ctrl+Z) / Refazer (Ctrl+Y, Ctrl+Shift+Z) / Historico de acoes (voltar/avancar ate um passo); cobre campo da grade, nome, lote, distribuir 100%, corrigir erros, novo/clonar/excluir (1 ou em lote); ate 5000 passos; limpo ao abrir arquivo. Teste: 7 passos (inclui lote de 50 e exclusao) desfeitos -> Build() byte-identico ao original; refazer ok. NAO cobre: edicao de quests (tasks.data), aplicacao de mudancas da IA (ChangeSet), favoritos removidos. Ctrl+Z dentro de caixa de texto/celula em edicao fica com a caixa.
- [T] PerfectWorld/IdSpaces.cs: espaco de ID por bloco (PWKB§S28); usado no Procurar erros (ID repetido so dentro do espaco) e no RefIndex (addon inclui MONSTER_ADDON; receita inclui gravacao/atributo aleatorio).
- [T] Ferramentas > Procurar erros (Ctrl+E; PerfectWorld/ErrorFinder.cs + UI/ErrorsWindow.cs; CLI --errors <elements.data>): ID inexistente (em bloco nenhum), probabilidade fora de 0..1, grupo que nao soma 100% (grupo = mesmo nome trocando o ultimo numero; so conta como distribuicao se soma 1 em >=90% dos itens do bloco), ID repetido entre itens de inventario (blocos com pile_num_max). Corrigir: so probabilidades (limita a 0..1 / reescala proporcional). Base oficial 1.4.4 v60: 0 problemas.
- [T] Alterar campo nos selecionados (UI/FieldEditors.BulkValue/BulkApply): definir, somar, multiplicar, sequencia inicio+passo, aleatorio min-max (prévia = gravado, mesma semente), substituir texto; prévia antes/depois; também no clique direito da grade. Paridade RAE "modificar valores em lote" (PWKB§S27).
layout = modelo do Rody's Angelica Editor (decisao do usuario; arvore em cascata abandonada). Rody: exe nativo x86 sem metadados .NET (protegido/empacotado) -> layout copiado do print, sem RE
esquerda (fundo preto): seletor de bloco "[NNN] Nome (qtd)" (1a opcao = ★ Favoritos; lembra o ultimo; padrao WEAPON_ESSENCE) | filtro tipo/subtipo (so blocos *_ESSENCE c/ id_major_type/id_sub_type) | busca nome/ID + caixa "todos os blocos" | ListView VIRTUAL owner-draw: ID | icone 24px + nome na cor de grade (★☆»«*) | clique na coluna ordena | rodape: Adicionar | ir para ID (Enter) | Excluir
direita: cabecalho icone+nome+ID | grade de campos agrupados/rotulados (addons vazios ocultos) | edicao inline
geral: tooltip estilo jogo no hover | menu de contexto clonar/novo/excluir/favoritar | novo item c/ proximo codigo | auto-recarrega ultima pasta | icones nativos do surfaces.pck | scrollbar escura (SetWindowTheme DarkMode_Explorer) | combos escuros owner-draw
abas: Elements (pronta) | Quests (v93 completa §E3c) | GShop/AI Policy (vazias) | Verificacao (§E5b) | Pacotes .pck (alternativa sPCK)
detalhe do item: cabecalho escuro (icone|ID|nome cor oficial) | abas Valores (sub-abas por grupo: Todos/Basico/.../Queda/Habilidades/Modelo; coluna Vinculo) | Vinculos (N) | Buscar (campo op valor) | Descricao e cor (§E3d)
lista: coluna ∞ = nº de vinculos | barra: ◀ ▶ historico | status: total/bloco/selecionado
diagnostico: --snapshot <png> abre o editor fora da tela c/ a ultima pasta e salva print (verificacao visual sem abrir janela)
 --snapshot <png> Elements <BLOCO> <ID|*> [grupo|shop|desc]: "*" = item mais preenchido do bloco; 6o arg escolhe sub-aba de grupo, aba Loja ou Descricao [T]
melhorias de UI (out/2026, ideias de §E9b) [T por print]:
 - Exibir > "Mostrar campos sem uso no bloco": por padrao esconde campos zerados em TODOS os itens do bloco + reserved_*; linha no fim "N campos sem uso ocultos" (setting hide_unused)
 - sub-abas com ocupacao: Atributos (25/64), Queda (32/32), Habilidades (2/48)
 - coluna Vinculo: barra verde + % para probabilidade; amostra + #RRGGBB para cor; menu de contexto "Distribuir 100% entre as probabilidades preenchidas"
 - nomes: habilidades pelo skillstr.txt do configs.pck (ID*10 = nome) e quests pelo tasks.data ("Ira (habilidade 41)", "Bilhete da Sorte (quest 3462)") -> issue #10
 - FieldKind.ServiceRef: id_*_service do NPC aponta para o registro NPC_*_SERVICE (antes era tratado como quest por conter "task" = ERRO corrigido); rotulo "Servico: Loja", link e seletor do bloco certo
 - rotulos: defesa/dano por elemento (Defesa de Metal...), drops/habilidades/quests numerados ("Drop 3 (item)"), ~70 campos de monstro/NPC, 122 blocos com nome PT/EN
 - aba "Loja (como no jogo)" em NPC_SELL_SERVICE (UI/ShopPreview.cs): paginas com titulo e ocupacao, grade 8x4 de icones, hover = nome, clique abre o item
 - aba Descricao: resumo estilo tooltip do jogo (faixas min-max, requisitos, preco, atributos por origem com %), rolagem volta ao topo
 - duplo clique em file_icon: seletor em grade de todos os icones do atlas (FieldEditors.IconPicker; desenho sob demanda) [sem verificacao visual: janela modal]
 - titulo da janela com versao e data do elements.data; status com campos e bytes do registro; coluna Nome da lista ocupa o espaco (sem rolagem horizontal)
 - proc_type bits 0,1,2,4 renomeados como restricao ("Nao pode trocar"...) — estavam invertidos (PWKB§S26)
 pendente conhecido: Tests/audit_localization.py procura dicionario Tokens em FieldLabels.cs (foi movido p/ FieldTokens.cs) -> script quebrado, nao relacionado a estas mudancas

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

### §E9b INTERFACE DOS EDITORES (prints do RageZone, out/2026) [P] — ideias para a NOSSA UI (so observado; nada copiado)
Angelica Core Editor 2026.6.1 (familia Rody/RAE; thread 1271952; pacote de origem duvidosa, NAO baixar):
- janela: menu Arquivo/Editar/Ferramentas/Favoritos/Links/Ajuda; a direita "Modo escuro" e menu "Desenvolvimento"; barra: voltar/avancar, abrir, salvar, Importar, Exportar, desfazer/refazer, "Octets"/ver XML, seletor "Servidor"
- esquerda: combo de bloco; busca + caixinha (= buscar em todos os blocos) + lupa; lista ID | Nome | coluna de vinculos; rodape "+ Adicionar" + numero (quantidade a adicionar) + botao + "Excluir"; status "item nao selecionado | tamanho do item"
- direita: abas Valores | Referencias (n) | Buscar | Descricao e cor (= nosso layout atual)
- Configuracoes (abas Interface/Parametros/Icones/Arquivos/npcgen+precinct/Servidor/Adicional): idioma (inclui Portugues Brasil), destacar monstro acima do nivel N, mostrar modelo (ski), nome de receita pelo item, **mostrar campos desconhecidos** (desligavel -> resolve "campos demais"), max de IDs no tooltip, refino padrao +12
VAE Vietnam Angelica Editor (base sELedit; thread 1269297):
- subabas por familia com ocupacao no titulo: Main | Model | Craft Addons (7/32) | Random Addons (7/32) | Unique Addons (5/16) | Hide Addons; NPC: Page 7 (0/32)
- faixa min-max numa linha ("Damage High 1954 - 1964", "Durability 430 - 430"); enum com nome ("Short Range Mode: Melee (assassin)"); ref de refino com descricao ("Refine Method (3744) - Physical attack +44")
- cor: amostra + hex; caminho de imagem: preview ao passar o mouse; titulo da janela: caminho + versao + timestamp do elements
- aba Descricao: barra de cores (Reset/Remove/Blue/Gold/Purple/Orange/White/Gray/Green/Red/paleta) que insere ^RRGGBB + tooltip do item IGUAL AO JOGO (nome, soquetes, +refino, atributos, requisitos, addons coloridos, preco, descricao, referencias com barra de %)
- preview da loja do NPC como no jogo (paginas 1-8, grade de icones, custo); aba "Itens recentes"; campo "Nota do item"; status: total, ID, selecionados, indice do campo, "buscar no PWDB"
- preview 3D (GFX/modelo) com animacao/efeito/skill
- menu Ferramentas: editores de quests, GShop, NPCGen, AI Policy, Configuracoes
- NPCGen: visualizador de mapa (mundo 8192x11264, ~29 mil marcadores), zoom, Full Width/Fit All, filtros NPC/Mob/Resource/Dynamic, rotulos/tooltips, minimapa; clicar no ponto abre painel X/Y/Z, Random X/Y/Z, quantidade, gatilho, Aplicar/Reverter; circulo = area de spawn
sEledit / sELedit++ (osudesu, base Wrechid; thread 1264402):
- recursos: elements.data COMPRIMIDO, float guardado em int32 exibido certo, desfazer/refazer, busca em registros e campos, balanceador automatico de % (probabilidades somam 100%), mascaras (classe/equip), cor de int32 editavel, timestamp -> data/hora, icones por nome sem caminho e sem diferenciar maiusculas, links entre listas ate 1.8.0 v531, tasks ate 194, varios idiomas
- grade com colunas Nome | Tipo (int32/float) | Valor; probabilidade em % com barra verde; addon com descricao ("3230 - Ataque +80")
- rodape "Set Value" + "Set Value To Selected" (mesmo campo em varios itens)
- janela "Icon selection": grade de todos os icones, destaque ao passar, clique escolhe
- seletor de quest (le tasks.data): busca id/nome, icone por tipo; campos de quest no NPC mostram nome (= issue #10)
- seletor de mascara de classe: 1 caixa por classe (14), numero da mascara, botoes none/all, Enter=OK Esc=cancelar
sELedit++ (Wrechid, thread 1163866; imagens externas quebradas): miniaturas na lista (pesa no desempenho), tema configuravel por arquivo theme.txt (escuro/cinza/azul), descricoes; pedidos da comunidade: "Auto Next ID" (ao importar/clonar, renumerar a partir de um ID), exportar a lista inteira, traduzir o elements.data, cor do item junto do ID, refino +12, barra voltar/avancar/abrir/importar/exportar; "faz igual ao RAE" (o RAE e a referencia de usabilidade da comunidade)
Gshop Editor (Luka, thread 1121787; imagens 404): gshop.data 1.3.6 ate 1.5.3, abrir/salvar/CONVERTER entre versoes, gerar gshop.sev (servidor) a partir do cliente, importar varios itens do elements.data, clonar/editar/excluir em lote, icones do surfaces.pck, janela de escolha de icone, definir timestamp. Bug relatado: elements.data aberto por outro processo -> abrir com FileShare.ReadWrite
New EL Editor (yangyxd, thread 1249464; zh/en): barra Abrir/Salvar/Salvar como/Importar (adicionar)/Importar (substituir)/Exportar (selecionados | esta pagina); lista com caixinhas, selecionar tudo/inverter/contador; campos em FORMULARIO DE 3 COLUNAS (cabe tudo numa tela); rodape "80 campos, 628 bytes"; versao no titulo; direita: abas Buscar | Alterar em lote | Corrigir probabilidades; busca global lista "(bloco) [id] nome"; tema claro/escuro
UIEdit (pdev, thread 927695): editor WYSIWYG do interfaces.pck (XML + surfaces), preview aproximado do jogo; fluxo: extrair interfaces.pck e surfaces.pck, apontar a pasta, editar
EasyUIEditor (jstvibex, thread 1266961): interfaces.pck em 3 modos (Previa como no jogo | Edicao visual | XML com analise de erro e recurso faltando), modelos de controles/dialogos, desfazer/refazer, camadas, explorador com favoritos, busca por nome de arquivo e por conteudo, MCP + skills do Claude para edicao assistida por IA
DataBrowser (hollyland, thread 1235492): arvore dos arquivos de mapa (bsdata, .t2bk, precinct.clt, region.clt, litmodels .bmd); precinct: regiao (id, prioridade, protecao PK), teleporte (alvo, coords), musica (loop, tempo, lista de sons), poligono X/Y/Z, exportar (ref. issue #31)
AipolicyEditor 11.0 (Kn1fe, thread 1147344; todas as versoes incl. 1.5.5; en/ru/zh): UI plana; esquerda lista de controladores (ID + botoes +/-); TRIGGERS (VERSION n) com nome; condicao desenhada como BLOCOS ("VAR(1) x CONSTANT(100) / VAR(2)", "&&"); chaves liga/desliga Active | Run | AttackValid; OPERATIONS (VERSION n) "[14] active_controller", "[2] talk"; grade de propriedades agrupada (Operation: Text, AppendDataMask | Target: type, occupation); valores enumerados (ex.: calc_var); busca por padrao com resultados "Controller X, Trigger Y, Operation Z" que levam ao ponto; remover controladores vazios; TRADUZIR o aipolicy aberto usando outro arquivo; exportar/importar
Universal Data Editor Tool (LegalSin, thread 1155623, licenca propria): um so programa para elements/gshop(1,2)/tasks/npcgen/aipolicy (PW completo; FW/JD/HTK/ESO em andamento) -> mesma ideia do nosso editor unico
World Targets Editor (Luka, thread 1124793): world_targets.sev/.clt (teleportes do mundo), edicao multipla, converter .clt <-> .sev, pegar coordenada do jogo, pontos no mapa
Perfect World Interface Translator (pdev, thread 947912): traduz XML da interface com integracao ao tradutor Yandex; arrastar e soltar arquivos
010 editor Templates (thread 1222468): templates binarios para PW/Swordsman/JD (ex.: litmodel_*.bmd) -> fonte para formatos de mapa
sTASKedit C# (Ronny/porte C#, thread 982771): arvore de quests colorida por tipo + busca "ID ou nome" + Find Next; quest em abas Geral | Locais | Basico I+II | Requisitos | Quest publica | Recompensas | Conversa; Geral: ID, Tipo (combo), limite de tempo, nome, texto do autor, datas de falha/validade em grade (A M D H M dia), FLAGS como caixinhas (ativar 1a subquest, pode desistir, repetivel, repetivel apos falha, falha ao morrer, no sucesso/falha do pai) -> modelo para issues #15/#16
Ground Breaking Tool (Steve Langley, thread 1163031): gera .dhmap/.rmap a partir dos mapas do CLIENTE e exporta .hmap/.wmap/.bht (sem precisar de arquivos de servidor, exceto npcgen/aipolicy); UI 3D: malha andavel (verde) e de colisao (vermelha), perfis, abas Setor | DHMAP | RMAP, lista de modelos com acao usar/omitir, "Gerar mapa inteiro"/"Gerar setor", posicao do cursor (ref. issue #31 e servidor de teste)
Simple Element Editor (thread 1057841): edicao/exclusao/clonagem em massa, importar/exportar itens em massa, "adicionar item com ID livre", conversao de versao, paginas proprias por bloco
Npcgen Editor v1.7.4 atualizado por Haly (thread 1223806; print com dados do PW BR): caminhos no topo (elements.data, npcgen.data, surfaces.pck) + "Show map"; abas com contagem: Mobs and Npcs 1/21892 | Resources 1/6309 | Dynamic Objects 1/2560 | Triggers 1/6003 | Search | ERRORS | Options and saving; barra Export/Import/Line up/Move objects; painel: Location, Type, X/Y/Z, Group type, Scatter, Auto respawn, ValidOnce; Mobs/Npcs: ID, Amount, Respawn time, Death amount, Aggression, Path type, Speed, Water/Turn offset, Group, Ask/Need help, Corpse(s), flags de faccao; "Opcoes para adicionar nova existencia" (gatilho, local, tipo, ID, quantidade, respawn) + "Insert Coordinates from game"; tooltip rico do NPC (tipo, monstro de origem, hello msg, quests); seletor "Choice Item" por tipo (038 Monster | 057 NPC | 078 Mine), busca Next, OK=Enter, Cancel=Esc; integra com o RAE aberto (API) e Ctrl+Shift+G "ir ao NPC"
Angelica PCK Viewer (Rust, thread 1263732): arvore + breadcrumb, lista ordenavel (nome, tamanho, comprimido, tipo), busca global substring/regex, multisselecao, PREVIEW de texto (XML/Lua/HLSL/CFG/INI/CSV com GBK), imagens (DDS DXT1-5/BC1-7, TGA, BMP, PNG, JPEG, HDR com zoom), audio (WAV/MP3/OGG com forma de onda); extrair, importar por arrastar (substitui mesmo caminho), excluir com reescrita so do indice + compactacao em segundo plano, cache de indice, PCK v2.1/2.2/2.3 e .pkx divididos, tema escuro/claro (Ctrl+D), atalhos Ctrl+O/A/E, F5, Del (ref. issue #13 "Gravar .pck")
Iconlist Editor C# (thread 1249930): abrir/ver/editar pacotes de icones DDS + txt, renomear/adicionar icones, exportar PNG/BMP/JPG, dividir pacote em arquivos e juntar arquivos num pacote
sTASKedit 2.191 modificado (thread 1269915, ate 1.8.0 v531): multisselecao na arvore (Ctrl/Shift/Ctrl+A) com clonar/excluir em lote; planos: edicao em lote com confirmacao, painel "N selecionadas", resumo de diferencas entre quests, exportar selecionadas em .data/JSON/CSV
Editor web "PW Mirage" (beta11n, thread 1172741, 1.3.6): NPC (nome, local, loja, receitas), receitas (4 alvos, 8 materiais), itens; clonar/buscar/excluir; listas personalizadas + recentes; HISTORICO completo com envio a cada 5 min e desfazer qualquer mudanca; fluxo publicar -> discutir -> aprovar -> entra no jogo + launcher que baixa as mudancas (ref. issue #1 desfazer/historico)
"New of Editors PW" (autor BR, thread 1268109, UI escura com textos PT): (1) editor de dominios/Guerra Territorial: barra Abrir/Salvar/TRADUZIR NOMES/Exportar SEV/Adicionar/Duplicar/Excluir; tabela ID|Nome|Nivel|Recompensa|X|Y; abas Domain | TW Map | Battletime | Battletime 2 | File; mapa com poligonos, nomes, IDs, centros, pontos; mover XY/dominio, editar pontos; horarios (dia da semana, hora, minuto); status "52 dominios, 20 horarios, versao 2"; (2) editor de Precinct: versao/timestamp, Abrir/Salvar texto/Salvar servidor/Exportar mapas/Pasta de imagens/DESFAZER/REFAZER/Adicionar e excluir zona/ponto/NPC; mapa com zonas e NPCs rotulados; lista "382 zonas | 187 seguras | 195 PK" com filtros; abas Pontos | NPCs | Detalhes; grade de propriedades (posicao, musica, som noturno, protecao PK, prioridade); "arraste os pontos brancos para editar X/Z" (ref. issue #31)
pwAdmin "Server Control" (thread 1238276): SERVICOS = memoria e swap (total/usado/livre) + cada processo (logservice, authd, uniquenamed, gacd, gfactiond, gdeliveryd, glinkd, gamedbd, gs) com caminho, quantidade, status Online/Offline, botoes Iniciar/Backup; MAPAS = atraso (s), "parar todos com calma", lista de mapas online e de mapas disponiveis (gs01 World, is01 ...), Iniciar/Parar -> modelo da nossa pagina de gerenciamento
Painel web moderno (thread 1268473): menu Registrar, contas, cash, vouchers, personagens, GM (enviar itens com gerador de hex, logs, console, bans IP/HWID), Servidor, Site, Editor; Elements Editor web: blocos com contagem, busca, NPC_SELL_SERVICE alterna "Shop grid" (loja como no jogo, icones) | "Raw fields"; paginas p1..p8 com ocupacao; mudancas pendentes ate "Generate & Apply", "Revert all"
Forum iwebGM (BR, PWKB§S27): checklist de paridade com o RAE (edicao em massa, mesclar versoes, modificar valores em lote, editor de probabilidades, gerador de octets, editor visual de loja/forja, correcao automatica de erros, traducao automatica, desfazer ilimitado); Emulate Manager: tela inicial com idioma em botoes (PT/EN/RU/ZH); IconlistEditor 2.1: troca de idioma em tempo real; editor de interface Angelica: clicar no visual leva a linha do XML; WinPck Ultra: tema escuro, varias abas de .pck, chaves pela interface, ir para offset; sGSHOPedit: nomes/descricoes pelo PWDatabase, exportar cliente+servidor; Npcgen2CoordData: exportar coordenadas em texto; editor de mapa: mover/girar/escalar litmodels e arvores
Npcgen Editor (Luka, thread 1124227; imagens removidas): abrir/editar/salvar/CONVERTER versoes (v6, v7, v10, v11), edicao multipla, carrega elements.data (varias versoes) para escolher o objeto, surfaces.pck para mostrar no mapa, nomes/imagens de objetos dinamicos (arquivo DynObjectInfo), PEGAR COORDENADAS DO JOGO e adicionar objeto por atalho, Maps.conf (id do mapa -> largura,altura), mover cima/baixo, busca por ID/nome/gatilho, alinhar objetos, procurar erros, exportar/importar entre versoes, tema escuro/claro, ru/en

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
- round-trip no PKG144 (PWKB§S14): 41.679 itens, sem mudanca = identico; trocar 1 nome muda so 31 bytes do registro e relê OK [T]
- tasks v93 do PKG144: 21.777 quests, regravacao identica [T]
- verificacao do par PKG144 (--check): v60/v93 iguais, aipolicy v1 1.819 politicas iguais, criptografia off; avisos so de copias antigas de elements.data (a29/a32/a33) [T]
- dicionario de tokens (FieldTokens.cs + en.json): 218 palavras sem uso nos .cfg do repo removidas; restam 253 [T]

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
