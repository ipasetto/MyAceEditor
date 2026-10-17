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
quests | data/tasks.data | tasks.data | §S7 §S17 §S21 | pendente; header/offsets conhecidos, corpo ATaskTempl [Q]
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

## §E4 .pck/.pkx (NATIVO, IMPLEMENTADO) [T]
PckArchive.cs: le v2/v3 + .pkx logico, tabela XOR KEY_1, entradas zlib (ZLibStream), paths GBK. Chaves (uPCK PCKKey.cs): KEY_1=-1466731422 KEY_2=-240896429 ASIG_1=-33685778 ASIG_2=-267534609 FSIG_1=0x4DCA23EF FSIG_2=1453361591 (PWKB omite de proposito; a engine precisa) [C-uPCK][T]
surfaces.pck do usuario: v2, 5147 entradas, 130MB [T]
icones de item: surfaces\iconset\iconlist_ivtrm.dds (masc) + iconlist_ivtrf.dds (fem): DXT1 4096x2048 | .txt GBK: linhas 1-4 = iconW(32) iconH(32) linhas_usadas(46) colunas(128), depois 1 nome por icone; icone i = col i%128, linha i/128 [T]
file_icon do elements ("Surfaces\图标\通用物品\nome.dds") casa pelo NOME do arquivo no .txt [T]. Cobertura: 15568/17047 = 91,3% [T]; o resto NAO existe no pck (itens sem icone no proprio jogo) -> mostra unknown.dds (1a entrada do .txt), como o client [I]
outros atlas: iconlist_skill _pet _guild _action _elf _faction _pq _calendar, emotions (mesmo formato [I])
DdsDecoder.cs: DXT1/3/5 + RGB(A) 24/32bpp. IconAtlas.cs: carga 0,13-0,3s, recorte 32x32 sob demanda
modos CLI: --pck <arquivo> [filtro] (lista em pck-list.txt) | --icons <elements.data> (cobertura em icons-report.txt + amostras PNG) | --snapshot <png> | --roundtrip <elements.data> (prova da gravacao em roundtrip-report.txt)
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
layout = modelo do Rody's Angelica Editor (decisao do usuario; arvore em cascata abandonada). Rody: exe nativo x86 sem metadados .NET (protegido/empacotado) -> layout copiado do print, sem RE
esquerda (fundo preto): seletor de bloco "[NNN] Nome (qtd)" (1a opcao = ★ Favoritos; lembra o ultimo; padrao WEAPON_ESSENCE) | filtro tipo/subtipo (so blocos *_ESSENCE c/ id_major_type/id_sub_type) | busca nome/ID + caixa "todos os blocos" | ListView VIRTUAL owner-draw: ID | icone 24px + nome na cor de grade (★☆»«*) | clique na coluna ordena | rodape: Adicionar | ir para ID (Enter) | Excluir
direita: cabecalho icone+nome+ID | grade de campos agrupados/rotulados (addons vazios ocultos) | edicao inline
geral: tooltip estilo jogo no hover | menu de contexto clonar/novo/excluir/favoritar | novo item c/ proximo codigo | auto-recarrega ultima pasta | icones nativos do surfaces.pck | scrollbar escura (SetWindowTheme DarkMode_Explorer) | combos escuros owner-draw
abas: Elements (pronta), GShop/Tasks/AI Policy (vazias), Pacotes .pck (alternativa sPCK)
diagnostico: --snapshot <png> abre o editor fora da tela c/ a ultima pasta e salva print (verificacao visual sem abrir janela)

## §E8 ROADMAP (ordem sugerida; custo: B=baixo M=medio A=alto)
META INICIAL (decisao do usuario 2026-10-17): o app fazer 100% do que o Rody's Angelica Editor faz, com CODIGO NOSSO e interface parecida com a dele.
 referencia de estudo: codigo-fonte do Rody em D:/Perfect World/PW/Outros Editores/Rodys Angelica Editor.zip (C# net481, ~1200 .cs; modulos NextAIPolicyEditor2 NpcGenDataEditor PCKManager DDS MppcLibrary RAE_Octets; dados configs/PW/PW_types.csv (tipo/vinculo de cada campo: matters, skills_pw, ft_prob, ft_bool, ft_hex_color, time_seconds, *_ESSENCE...), PW_pt.csv (rotulos PT), PW_enums.json, 134 .cfg, 23 .rules)
 mais referencias (D:/Perfect World/PW/Outros Editores/Angelica Editor/): Rodys_Angelica_Editor_SRC_V22 (mais nova; READMEs V15-V17 em chines: tasks.data v128/v187/v191 completo c/ subtarefas, dialogos, NewID, header v223+ -> base p/ editor de quests e auto-tradutor) | AngelicaEditor_V45/V46/V52 (binarios) | AngelicaCore_V2: codigo RECUPERADO de binario protegido (Enigma -> pe-sieve -> de4dot -> ilspycmd), README diz "private reference, study" -> so estudo, NUNCA codigo no repo; tem verification/ (regressao) e McpServer (87 tools = lista de funcoes de uma engine completa)
 usuario afirma que o material do Rody foi liberado pela comunidade/autor (2026-10-17); mesmo assim: seguir modelos/UI/formatos (ok) e escrever codigo proprio.
 REGRA: consultar so o trecho do passo atual; NUNCA copiar codigo nem arquivos de dados dele p/ o repo (repo publico; parece pasta de trabalho privada de software licenciado). Escrever a nossa versao.
1b aba GShop (formato completo PWKB§S19; exporta client+server) M | aba AI Policy (PWKB§S18) M | npcgen/spawns (PWKB§S22) M
3 checagem version/edition client x server (§E5) B
4 tasks.data / gshop / aipolicy (abas) M cada
5 fechar cauda elements.data (§Q1) B-M
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
