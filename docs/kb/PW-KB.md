# PW-KB — Perfect World (Angelica engine) client+server
# Base de conhecimento consolidada (fontes: partes A-K do estudo + §S17 client source 1.5.2). Estado FINAL: contradições já resolvidas (ver §Z).
# Formato: linhas "chave: valor" ou "a | b | c". Busque por ID de seção (ex: §S3). Texto em pt-BR telegráfico.

## §L LEGENDA
conf: [C]=codigo-fonte lido | [A]=arquivo real lido (conf/sh/sql/xml) | [B]=binario (readelf/nm) | [P]=print/mapa mental | [F]=fonte forte (wiki/README/oficial) | [M]=medio (doc gerada IA, guia de forum c/ arquivo mostrado) | [W]=fraco (comentario/anuncio) | [I]=inferencia | [?]=duvida aberta
escopo: PKG144=pacote terceiros "PW Server 1.4.4 v60" (nao e 1.4.4 puro; v60=rev. do pacote) | SRC153=zip "source_server_153" (nome do zip, nao provado 1.5.3; copias de 2013-09/2015) | 2007=guia 1.2.6 | geral
regra: o que vale p/ 1 pacote = hipotese p/ outras versoes. Layout interno de gs/exe/offsets MUDA POR BUILD.
nao incluido de proposito: senhas, chaves, bytes de patch, links de download.

## §0 ÍNDICE
S1 visao geral | S2 client fs | S3 .pck e formatos | S4 daemons+portas | S5 boot/deploy/rede logica | S6 gs.conf+mapas | S7 arquivos de dados (espelho c/s) | S8 protocolo/login/seguranca | S9 storage gamedbd + tabelas | S10 structs personagem/conta | S11 SQL (authd/MySQL) | S12 GM/debug/classes/rates | S13 codigo-fonte | S14 versoes | S15 ferramentas | S16 IDs (refino/addons/pets) | S17 codigo-fonte do CLIENT (1.5.2) | S18 aipolicy | S19 gshop | S20 UI XML | S21 tasks (sTASKedit) | S22 npcgen+mapas server | S23 elements (sELedit) | S24 operacao/seguranca/erros | S25 constantes em binario + skillstr | Z resolvidos | Q abertos | R fontes

## §S1 VISÃO GERAL
jogo: MMORPG chines; lancamento jan/2006 China [F] (fontes populares dizem 2005 [W]); empresa Perfect World Co. fundada 2004, Pequim.
motor: Angelica (A3D), proprietario [F]. Mesmo motor: Jade Dynasty, Forsaken World, Ether Saga (sPCK modos -pw -jd -fw -eso -rwpd). "element"=prefixo do projeto (elementclient.exe). "Element3D" so fonte fraca [W].
3 blocos: client (pasta element/) | servidor (daemons 32-bit + banco) | versoes (client e server precisam casar: build/dados).
publicadores: Beijing PW (CN), Games-Masters (EU), Arc/PWE (NA), Astrum (RU), Level Up (BR). Mesma versao sai com nome/data diferente por regiao.
fontes dos mapas mentais: rbb138 (RaGEZONE mai/2010, 3 niveis); B1 = Diego Martins 2025 (MindMeister) [I].

## §S2 CLIENT (pasta element/)
exe: elementclient.exe | elementskill.dll | launcher/ | patcher/{skin/, server/{updateserver.txt,serverlist.txt}} | config/{element,launcher,patcher/version.sw}
cmdline client (PWI 1.4.0) [W]: game:cpw, console:1; console abre Shift+~
crypto client: element/dbserver.conf [GameClient] type=tcp + isec,iseckey,osec,oseckey (ver §S8)
pastas: data/ maps/ userdata/ bbsfiles BBSPages cache cursors font fonts help info logs(tasks.txt) music reportbugs screenshots(.bmp)
data/: aipolicy.data elements.data gshop.data gshop1.data(loja evento) gshop2.data tasks.data domain.data(guerra territorio)
maps/<mapa>/: .ecwld .ecbsd .trn2(+.t2bk .t2mk .t2lm .t2lm1 por quadrado [?funcao exata]) region.clt precinct.clt script/config/global_api.lua
  region.clt=espelho de region.sev (zonas seguras, portais/teleporte); precinct.clt=espelho de precinct.sev (nome de area, respawn, musica)
userdata/: server/serverlist.txt | character/customize | currentserver.ini | systemsettings.ini
raiz client ainda: nomes de pasta chineses sao reais (装备 形象 通用装备 场景动画模型 场景活物 百宝阁 图标 个性化 变形丸 头发模型库 时装头饰)

### pck (todos em element/)
configs.pck | models.pck | gfx.pck | sfx.pck(.wav) | surfaces.pck | interfaces.pck | textures.pck | shaders.pck | facedata.pck | fonts.pck | grasses.pck[?: grama ou so textura] | litmodels.pck(.bmd por mapa: arvores/casas e posicao) | loddata.pck(dungeons) | building.pck(mox,dds) | trees.pck(.cfg; por arvore .spt+.dds)
conteudo custom pode ir em QUALQUER .pck se caminho no .ecm/elements.data bater (CPW patcher recomenda models.pck).

### configs.pck (arquivo: funcao)
actions_npc.txt/actions_player.txt: nomes de animacao | angelica.cfg: motor | badwords.txt | broadcast.ini | buff_str.txt | calendar.ini
console_cmd.txt: comandos GM/debug (client) | coord_data.txt: nome+bordas mapas/minimapas | dynamicobjects.data | element_client.cfg: titulo da janela
fixed_msg.txt | font.ini | guide_area.txt | guide_feature.txt(lista instancias p/ guia) | iconsound.txt
instance.txt: lista de instancias; ID tem que bater com gs.conf (senao client fecha sem erro); nomes aparecem no teleporte de grupo/zona
item_desc.txt | item_ext_desc.txt | loginpage.ini(pagina exibida ao carregar) | petwords.txt | rare_item.conf(IDs que geram aviso vermelho no chat mundial)
scenectrl.ini(camera login) | server_error.txt | skillstr.txt | task_err.txt | uiconfig.ini

### interfaces.pck
legado: .dtf(botoes/imagens/fundo/links) .stf(textos) [.htf .gcf]. Novo: XML (a partir de 1.4.2 ou 1.4.4 [?]). Util: interfaces/version01/ (raiz ignorada). 1.5.0: 524 xml na raiz, 1117 arquivos, 7 subpastas.
pastas: face(s) terrain(sem uso) wiki version01
janelas (xml): character, create, createinfo0-9, createrace(escolha de raca), select, bag, equiprefine/equipslot/equipup/equipbind/equipdye/enchase/engrave/damagerep, chat/chatbig/guild*/mail*/trade/wedding, shop/qshop(loja jogo)/backshop(evento)/bshop/auction*, map/automove/gmaptravel/quest, login/loading/setting_*/gmconsole, petlist/petdetail0-3/skill_0-9/elf_*, addexp/setexp(hiper) addexp2(meditacao 1.4.5+), force, pop(antibot so CN)
inativas: escmenu findplayer gmconsole2; nao usadas versao RU: addid agreement pop

### surfaces.pck
iconset (atlas de icones) [A-PKG144, lido pela engine]: iconlist_<x>.dds + iconlist_<x>.txt (GBK): l1 largura l2 altura l3 linhas usadas l4 colunas, depois 1 nome por icone na ordem da grade. Itens: ivtrm (masc) / ivtrf (fem), DXT1 4096x2048, icones 32x32, 128 colunas. file_icon do elements.data casa pelo NOME do arquivo. ~8,7% dos file_icon do PKG144 nao existem no pck -> client mostra unknown.dds [I]. string:N do elements = GBK (confirmado: caminhos de icone) [A]
pastas: addexp button expression extra faces force home iconset(icones skill/item; .dds+.txt) ingame(minimapas dungeon, emoticons, mapa-mundi, teleporte) maps minimaps(4 .dds=1 quadrado) pet pq version01 window 百宝阁(preview loja cash) 图标/个性化(.dds/.tga)
raiz: iconlist_guild.dds/.txt logo.dds procbar-16-9.tga 任务信息2.tga 载入{16-9,1024,1280}-(1-15).jpg(loading)

## §S3 .PCK E FORMATOS
pck: "Angelica file packages v2008" [F]; varia por jogo/regiao (sdPck_china existe). Guarda caminhos internos; usa zlib [F-autoangel][C]; "criptografia" = so XOR com chave em offset da tabela e tamanho de entrada; CONTEUDO dos arquivos nao e criptografado [C-uPCK]; cabecalho/tabela/rodape: ver subsecao abaixo.
limite 2GB (int32): excedente vai p/ .pkx; leitor deve ler PAR .pck+.pkx (autoangel read_pck(pck,pkx)); juntar: copy /b a.pck+a.pkx b.pck
substituir arquivo no pck deixa blocos mortos (arquivo cresce) -> reempacotar. Sem desfragmentacao.
fluxo editar: desempacotar -> pasta <nome>.pck.files/ -> editar -> reempacotar (sPCK -pw -x / -c; pwpack -c ... -z 0-9). Nomes chineses: sPCK -ap = base64 (.b64.files).
patches: .cup (oficial; sPCK decodifica), .xup (7z c/ nomes base64, sPatch/CPWCreator). Provavelmente troca arquivos dentro dos pck [I].
modelos (cadeia): elements.data(client) guarda caminho do .ecm -> .ecm aponta .smd (+ .gfx/.wav por setas) -> .smd -> .ski(malha) + .bon(esqueleto) + .stck(animacoes) ; texturas .dds em pasta textures ao lado.
  smd v5: .ski+.bon | v6: .ski + muitos .bon (1 por animacao) | v8: .ski + .bon(offset) + pasta de .stck (1 por animacao)
  autoangel le: .bon .ski .smd .ecm .stck .gfx
gfx: .gfx em qualquer pasta; textures/ (.dds .bmp .tga); .sgc(sgc/) .att(skillattack/) .pll(变形丸/ [?])
facedata: .dds terminados a01/b01, .ski, 头发模型库, 时装头饰 [?hierarquia]
elements.data: VERSIONADO (layout muda; configs por versao detectadas pelo cabecalho). Declaracoes XML p/ v.60,62,63,65-70,73,76,77,80,81,84-88. Organizado em LISTAS numeradas (ex: 004 armas WEAPON_ESSENCE, 010 ornamentos DECORATION_ESSENCE [W]). Numeracao de lista difere client x server (pets: server 93/client 90; ovos pet: server 91; era 2010 [W]). Layout/cabecalho: ver §S17 [C].
escala de versao de dados usada por ferramentas (ham/pw-api): 07,63,69,70,80,85,88,101,145,156 [F]. = numeros entre parenteses no forum.
trn2/hmap: ver §S6 (terreno).

### formato .pck/.pkx (fonte uPCK C#: PCKStream PCKFileEntry PCKKey PCKZlib ArchiveEngine) [C]
visao: [cabecalho 12B][blobs dos arquivos, um apos o outro][tabela de arquivos][rodape 280B v2 / 288B v3]. Tabela fica NO FIM; rodape aponta p/ ela. Sem alinhamento/padding entre blobs.
cabecalho (12B): int32 FSIG_1 | uint32 tamanho total do pck(+pkx) (gravado so no fim, offset 4) | int32 FSIG_2. Leitor NAO usa o cabecalho.
rodape v2 (280B), da frente p/ tras: int32 ASIG_1 | int16 2 | int16 2 | uint32 (offsetTabela ^ KEY_1) | int32 0 | texto "Angelica File Package, Perfect World." (37B) | 215B zero | int32 ASIG_2 | int32 nArquivos | int16 2 | int16 2.
leitura a partir do FIM: -8=nArquivos(int32) | -4=versao(int16; ==3 -> v3, senao v2) | v2: -272=offsetTabela uint32 ^ KEY_1 | v3: -280=offsetTabela int64 ^ KEY_1 (rodape v3 = 288B; so esses 3 campos sao lidos).
tamanho da tabela = tamanhoTotal - offsetTabela - 280 (v2) ou - 288 (v3); entradas sequenciais, sem indice/hash.
registro da tabela: int32 (tam ^ KEY_1) | int32 (tam ^ KEY_2; ESCRITO mas IGNORADO na leitura) | <tam> bytes de entrada. Entrada com tam < 276 (v2) / 288 (v3) = comprimida em zlib e inflada p/ o tamanho cheio; senao crua.
entrada v2 (276B crua): path[260] (cp936/GBK, terminado em \0; '/' vira '\') | uint32 offset | int32 size | int32 csize | int32 0.
entrada v3 (288B crua): path[260] | int32 (ignorado) | int64 offset | int32 size | int32 csize | 8B nao lidos. v3 existe p/ offsets >4GB; uPCK so LE v3, ESCREVE sempre v2.
arquivo: seek(offset), le csize bytes; csize < size -> zlib inflate p/ size bytes; senao armazenado cru. Compressao: zlib COM header (zlib.net ZOutputStream, nivel 0-9); so guarda comprimido se ficar menor que o original. Falha de inflate e engolida (try/catch vazio -> buffer zerado do tamanho esperado).
chaves: KEY_1 KEY_2 ASIG_1 ASIG_2 FSIG_1 FSIG_2 = int32 fixos em PCKKey.cs (valores omitidos de proposito); construtor aceita chaves custom -> chaves/assinaturas variam por jogo/regiao (explica sdPck_china e PCKs de Jade Dynasty/Forsaken World/Ether Saga) [I]. KEY_1 mascara offset da tabela + tamanho de entrada; KEY_2 so aparece na escrita.
.pkx: continuacao LOGICA do .pck. Limite do .pck = 2147483392 B (0x7FFFFF00); excedente vai p/ .pkx (mesmo nome, ".pck"->".pkx"; ignorado se extensao .cup). Offsets sao logicos sobre o par concatenado: Position < len(pck) -> pck; senao pkx[Position - len(pck)]; leitura que cruza a fronteira continua no inicio do pkx. Offset v2 = uint32 (teto 4GB logicos). Quirk uPCK: leitura com Position == len(pck) exato devolve zeros (bug de borda).
unpack (_Unpack): le versao(-4) e nArquivos(-8), le tabela, cria <nome>.pck.files/<path>, inflate em ThreadPool (leitura sequencial, escrita paralela).
pack (_Compress): escreve cabecalho, comprime arquivos em paralelo mas grava blobs NA ORDEM (ManualResetEvent encadeado), depois tabela + rodape v2 (versao gravada 2/2), volta ao offset 4 e grava tamanho total. Path gravado como GB2312, relativo, com '\'.
ParseBase64Path: segmentos de caminho em base64 UTF-8 sao decodificados (= sPCK -ap, nomes chineses; pasta .b64.files).
leitor minimo (resumo): 1) abrir pck (+pkx logico) 2) ler int16 @-4 (versao) e int32 @-8 (n) 3) offsetTabela @-272 u32 (v2) ou @-280 i64 (v3), XOR KEY_1 4) ler [offsetTabela, fim-280|288) 5) n vezes: tam = i32 ^ KEY_1, pular 4B, ler tam B, inflar se tam < 276|288, parsear 6) por arquivo: ler csize @offset, inflar se csize < size.
nao verificado: significado do par int16 (2,2) / valor 3 do v3; 4B ignorados e 8B finais da entrada v3; rodape v3 completo; se PCK de outras regioes usa o mesmo layout; origem exata dos valores das chaves. uPCK e ferramenta de TERCEIROS (nao e o AFilePackage oficial) -> tratar como [C-terceiros], confirmar vs autoangel-rs.

## §S4 SERVIDOR: DAEMONS E PORTAS
convencao conf: bloco [XxxServer]=escuta, [XxxClient]=conecta. Nome do bloco = nome do manager (rpcalls.xml) + Server|Client; numero final=instancia (GLinkServer1..4).
todos 32-bit Intel 80386 [B]; precisa i386 libs em 64-bit (libstdc++6:i386, libxml2:i386 p/ gacd). Dependencias: libstdc++.so.5, libpcre.so.0, libxml2.so.2, libtask.so (gs). Runtime citado: libcrypto 0.9.8.

daemon | lang | porta(escuta) | quem conecta | papel
glinkd | C++ dyn | 29000-29003 (GLinkServer1-4; max_users=3000, halflogin_users=6000) | client do jogo | gateway do client; estado de login; cripto/compress; versao em gamesys.conf
glinkd | | 29301-29304 (GProviderServer1-4) | gs (GProviderClient2-5) | link gs<->glinkd
gdeliveryd | C++ static | 29100 (GDeliveryServer) | glinkd(GDeliveryClient), iweb | HUB: login/sessao, chat, auth, banco, nomes, cla, anti-cheat; maior codigo (937 arq)
gdeliveryd | | 29300 (GProviderServer id=0) | gs (GProviderClient0) | gs plugado no delivery
authd | JAVA (mauthd/*.class; jar jio, mysql-connector 5.1.7/5.1.10, sqljdbc) | 29200 (GAuthServer) | gdeliveryd(GAuthClient) | contas/cash/GM-perm/anti-vicio; suporta MySQL ou MSSQL (authd.conf ismssql); gauthd C++ do source = esqueleto
gamedbd | C++ static (WDB) | 29400 (GameDBServer) | gdeliveryd gfactiond iweb cashstat gs(gmserver.conf) pwAdmin | dados de personagem/clã/correio/leilao; API orientada a RPC (198 rpcs)
uniquenamed | static | 29401 | gdeliveryd gfactiond iweb | nomes unicos (4 tabelas)
gfactiond | dyn | 29500 (GFactionServer), 29600 (GProviderServer p/ gs GProviderClient1) | gdeliveryd glinkd iweb | clas/fortalezas
gacd | dyn (libxml2) | 29702 (GAntiCheaterServer), 29712 (GACControlServer) | gdeliveryd(GAntiCheatClient); 29712 conectante desconhecido | ANTI-CHEAT (confirmado: protocolos ACQuestion/ACAnswer/ACReport, coleta plataforma/mouse/threads, ACRemoteCode/Exe). Dados: charfont.dat charlib.dat hzfont.dat Questions.data Questions2.data gacd.xml (Questions = banco de perguntas [I])
gs (gamed) | dyn | 10901.. por mapa (ver §S6); 12341 TCP + /tmp/foo unix (MsgTCPSession/UNIX, entre gs [I]) | | mundo: mobs, combate, quests (libtask.so), IA
logservice | dyn | 11100 UDP, 11101 TCP | todos (blocos Logclient*) | logs
iweb | Tomcat/Java (porta painel 22552 [W]) | | | painel admin
rollback | 32-bit | | | variante do gs p/ restaurar estado (gs_rollback.conf mundo gs44) [I fraca]
extras de dev (source): gclient gdbclient gacdclient logclient dbtool. Outros jogos da familia: toplist (Zhu Xian).
dbtool: dbtool -r -s <orig> -d <dest> repara/copia tabela; cashstat: conecta gamedbd:29400 (cashstat.conf)
proxy entre daemons: possivel trocando porta em gamesys.conf (ex: gamedbd 29400->39400 + proxy em 29400); vale p/ todos EXCETO glinkd [W].

### fluxo (arestas)
client -29000-> glinkd ; glinkd -29100-> gdeliveryd ; glinkd -29500-> gfactiond
gs -29301..29308-> glinkd ; gs -29300-> gdeliveryd ; gs -29600-> gfactiond ; gs -29400-> gamedbd
gdeliveryd -29200-> authd(Java+MySQL) ; -29400-> gamedbd ; -29401-> uniquenamed ; -29500-> gfactiond ; -29702-> gacd
gfactiond -29400-> gamedbd ; -29401-> uniquenamed ; iweb -> 29100 29400 29401 29500 ; todos -> logservice
gmserver.conf (gs=cliente): PKG144 count=10 (2 fixos: delivery, faction + 8 links 29301-29308; glinkd so define 4). Guia 2007 citava 13.

## §S5 BOOT, DEPLOY, REDE LÓGICA
rede logica: servicos acham-se por APELIDOS de host (/etc/hosts). PKG144 hosts: dbserver database auth audb manager aumanager link1-4 game1-4 delivery gdelivery backup gmserver nfsroot gamedbserver GameDB GameDBClient -> 127.0.0.1. (Zhu Xian: +promote umanager)
gs.conf usa apelido de MAPA: listen_addr=WORLD01:10901; gsalias.conf [AddrAlias] mapeia WORLD01=game1, INSTANCE06=game2, BATTLEGROUND01=game3...; hosts mapeia game1->IP. Distribuir mapas = editar gsalias.conf+hosts.
ordem start.sh PKG144: iweb(Tomcat) > logservice > uniquenamed > authd(java mauthd/build/authd.sh) > gamedbd > gacd > gfactiond > gdeliveryd > "glinkd gamesys.conf 1"(1=indice do link; 2-4 comentados) > "gs gs01"(espera 60s; instancias comentadas). Cada um recebe gamesys.conf como arg. Logs /home/logs/<nome>.log.
ordem guia 2007 (1.2.6, 7 pastas, sem authd/logservice): gamedbd gdeliveryd gfactiond gacd glinkd gs01 depois gs arena01-04, is01...
stop.sh: pkill -9 em tudo (bug: 'logservices' c/ s) ; codigo usa SIGUSR1 p/ parar gamedbd.wdb/uniquenamed.wdb com graca -> kill -9 explica scripts dbrepair.sh/dbrec.sh.
gs 1 processo por mapa (antigo); 1.5.3: um gs hospeda varios mapas: "gs gs01 gs.conf gmserver.conf gsalias_wallow.conf is21 is22 is61" (lista is01..is75; reduz RAM).
distribuicao oficial: arvore cnet; cpackage.*.tar.gz -> srcfiles/; script de empacote sobre XML de topologia; servicerun (multi-maquina). Multi-servidor e o caso previsto.
PKG144 layout: SQL/{PW_tabels.sql(cria db 'pw' utf8), PW_procedures.sql(tabelas+21 procs)} etc/{authd.conf GMserver.conf iweb.conf table.xml hosts gmopgen.xml ssl/} home/{gacd gamed gamedbd gdeliveryd(filters,domain.sev,auctionid.txt,menuid.txt,sysauctionlist.txt,webtradeid.txt=contadores[I]) gfactiond glinkd(patch,replacestr.pl[?]) logservice uniquenamed mauthd *.sh} usr/java(JDK6u26 32b) usr/local/jakarta(Tomcat/iweb) var/www/register/status.php lib/
gdeliveryd+gfactiond: arquivo filters (palavras sensiveis) em GBK; encoding errado => "Cannot load sensitive words" (checar `file`, converter iconv).
pacotes SO: apache2 mysql-server php phpmyadmin java(openjdk/JDK); ubuntu 10.04/12.04 comuns; Docker (SealinRepo) 1.5.5: 1 container/servico, MySQL por subsistema, Ubuntu 18.04.
2GB RAM bastam p/ 1 mundo sem dungeons (teste).

### conf por servico
gacd: gamesys.conf (nao precisa mudar) | gamed: gs.conf gmserver.conf gsalias.conf (1.5.3: gsalias_wallow.conf) ptemplate.conf rates.conf gs_rollback.conf | gamedbd: gamesys.conf cashstat.conf | gdeliveryd/gfactiond/glinkd/uniquenamed/logservice(logservice.conf): gamesys.conf | authd: authd.conf + build/table.xml (senha do DB) [W]
glinkd: chave version (HEX, 1 byte por componente: 10404=0x10404=1.4.4; 1.5.3 esperado 10503 [I]; exemplo no source=10204 nunca atualizado); chave compress (MPPC)
gamedbd gamesys.conf: [storage] homedir=dbhome cachesize errpfx checkpoint_interval=60 compress=1 | [storagewdb] homedir=dbhomewdb tables=<31 nomes> | datadir=dbdata logdir=dblogs backupdir | [gamedbd] noimportclsconfig rolenamesuffix factionnamesuffix familynamesuffix | [Role] delete_timeout | [GameServerRegion] | bloco por ocupacao (pos nascimento, hp mp vitality energy strength agility, velocidades, ganho/nivel)
uniquenamed: mesmo esquema 2 blocos storage; tabelas em uname/dbdata
[GDeliveryServer] (1.5.1): tcp 29100 | zoneid=1 aid=1 freeaid=1 district_id=districtzone | max_player_num=6000 | so_sndbuf/so_rcvbuf=65536 ibuffermax/obuffermax/accumulate=1048576 tcp_nodelay=0 listen_backlog=10 | pvp battlefield sellpoint freecreatime = 0/1 | max_name_len=32 | table_name=rolename.txt name_charset=ASCII table_charset=GBK | recalloldplayer=1 token_login_support=true lan_ip_list is_central_ds=false (delivery central p/ multi-servidor) | mtrace=/tmp/m_trace.link | au_cert, shared_key (mesmo do authd; padrao trivial)
authd.conf: [Enthrallment] playtime=10800 resttime=18000 (3h/5h) enable_enthrallment=false | [GAuthServer] enable_enthrallment ismssql isabroad lastloginlimit allow_all_user allow_same_zoneid matrixpasswd_log no_anti_wallow_aids shared_key forbidips | [ThreadPool]
versao do servidor: glinkd/gamesys.conf linha version. build (numero entre parenteses): 1.4.5(63,69) 1.4.6(70,80,85) 1.5.0(88) 1.5.1(101) 1.5.3(145) 1.5.5(156). Docker diz 1.5.5 "build 2567" = outra escala [Q].

## §S6 GS: gs.conf, MAPAS, TERRENO
gs arg = nome da secao do mapa (gs01, is01, arena01, bg01). 
gs.conf secoes globais:
 [General] world_servers instance_servers AntiWallow AntiCheat(false no PKG144) logic_threads(8) expire_item
 [Template] Root itemDataFile QuestPackage QuestPackage2 GlobalData PolicyData DropData RareItem NPCGenFile RegionFile RegionFile2 PathFile RestartShell MallData Mall2Data LuaData CollisionFile CollisionElement
   -> arquivos em gamed/config/: elements.data tasks.data dyn_tasks.data world_targets.sev aipolicy.data extra_drops.sev rare_item.conf npcgen.data precinct.sev region.sev path.sev restart2 gshopsev.data gshopsev1.data global_api.lua map.bht wmgj.nmd
   gs/template/ no codigo: elementdataman(elements.data) itemdataman npcgendata el_region el_precinct pathman globaldataman extra_drop_table
 [MoveMap] Path WaterPath AirPath | [WallowLight][WallowHeavy] (exp sp item money task_* em %, 100=sem reducao) | [WallowTime] TimeLight=2 TimeHeavy=4 TimeClear=5 (horas [I]) | [DoubleExp] first_rest_time rest_period rest_time_unit max_rest_time
 4 secoes por mapa: [World_<n>]|[Instance_<n>] (index tag cid base_path limit grid base_region local_region) ; [Terrain_<n>] ; [MsgReceiverTCP_<n>] listen_addr=<APELIDO>:<porta> ; [MsgReceiverUNIX_<n>] /tmp/gssock_<n>
 PKG144: 45 secoes; world_servers=17 (gs01 arena01-04 is01 is02 is12 is13 is18-22 is32-34); instance_servers=27 (is05-11 is14-17 is23-29 bg01-06 is31 is35 is38); [World_gs02__] reserva.
convencao nomes: gs01=mundo principal | is##=instancia/dungeon | arena## | bg##=campo de batalha guerra territorio | ms##,random## modos especiais.
tag (ID de instancia, = instance.txt do client): gs01=1, is##=100+n, arena##=200+n, bg## ~230+n [bg nao conferido]. IDs de dungeon MUDAM entre versoes/servidores (ex: Batalha da Dinastia is43-46 em 1.5.2 vs is70/71 em lista 2013).
portas por mapa PKG144: WORLD01=10901 WORLD02=10902 INSTANCE<nn>=10910+nn ARENA<nn>=10940+nn
cid (coluna): "0,2,13" normal; "14,2,13" arenas/is01 (14=PK sem nome vermelho + renasce sozinho; 17=igual+selo); demais colunas sem explicacao [Q]
base_path (pasta do mapa em gamed/config/): gs01->world | is##->a## | arena##->b## | bg0n->b(29+n). Client usar mesmos nomes: nao verificado [Q].
world=mapa persistente; instance=dungeon/battleground com instancias [I]. Codigo: instance/{instance_manager battleground_manager countrybattle_manager trickbattle_manager faction_world_ctrl parallel_world_manager(copias do mesmo mapa)}
flags limit= (22 no codigo, 11 no PKG144; separadores ; , \n):
 nofly nomount nothrow noduel no-bind no-market lowjump no-multi-exp clear-ap(zera chi) allow-root(permite iniciar/entrar) use-save-point(+save_point=<mundo>,x,alt,z) height-limit(+height_limit, padrao 800.0) anti-cheat profit-time profit-time2(zona controle de ganho) online-award
 commondata(=common_data_notify: gs envia ao client lista de "dados comuns" do mapa, vars alteradas por IA op_set_common_data/op_add_common_data)
 gm-free(campo gmfree, efeito nao verificado) ctrlid-open-instance nothrow-anyway can-reenter noauto-resurrect
pasta de mapa no server (gamed/config/<mapa>/; 46 pastas: world a01-a29 a31-a35 a38 b01-b04 b30-b35):
 map/<n>.hmap (altura; world: 88 arq x 1.052.676 B = 513*513*4 float) | map.bht (colisao; world 195MB) | movemap/<n>.rmap .dhmap movemap.conf (113) | watermap/<n>.wmap (90) | airmap/<n>.octr spmap.conf (18) | npcgen.data(=fonte de posicoes de spawn) path.sev precinct.sev region.sev | xo.sev[?]
 formatos rmap/dhmap/wmap/octr/bht nao estudados [Q]
 terreno gs01: nAreaWidth=nAreaHeight=512 vGridSize=2.0 nNumCols=8 nNumRows=11 nNumAreas=88 vHeightMax=800; area=1024x1024u; mundo 8192x11264; base_region={-4096,-5632},{4096,5632}
gamed/config/ raiz extra: dyn_tasks.data task_npc.data domain.data ExtDataID.dat global_api.lua restart2 wmgj.nmd + copias de npcgen/path/precinct/region [?uso]. gshopsev2.data NAO existe no PKG144 (so gshopsev e gshopsev1).
nomes mapa client x server: instance.txt (client) lista; mapas client em maps/<nome> (nome pode diferir do server).

## §S7 ARQUIVOS DE DADOS: ESPELHO CLIENT x SERVER
client | server(gamed/config) | relacao
elements.data | elements.data | mesmo conteudo/versao; listas com numeracao diferente (client tem info visual/caminhos)
tasks.data | tasks.data | iguais (quests; baus tambem sao quests)
aipolicy.data | aipolicy.data | IA mobs/NPC (falas/acoes)
region.clt | region.sev | espelho
precinct.clt | precinct.sev | espelho
gshop.data gshop1.data | gshopsev.data gshopsev1.data | COMPLEMENTARES, nao identicos (sGSHOPedit usa banco proprio e exporta 1 arq p/ cada lado)
rare_item.conf | rare_item.conf | igual
instance.txt (configs.pck) | tag em gs.conf | IDs precisam casar
- | path.sev npcgen.data world_targets.sev extra_drops.sev | so server (ProjectAtlas edita path.sev lendo coords do client em execucao via offsets.txt por versao)
Divergencia client/server em itens/quests/loja => erro ou comportamento estranho [geral].

## §S8 PROTOCOLO, LOGIN, SEGURANÇA
IDL: cnet/rpcalls.xml (558KB, set/2014) + rpcalls.xsl. Totais: 698 protocol (opcodes 1-9001) | 617 rpcdata | 237 rpc (tipos 15-8201) | 56 state | 13 service.
services: glinkd(4 managers,25 estados,27 prot) gdeliveryd(13 mgrs,13 estados,304 prot,20 rpc) gauthd(7 prot; estado GAuthServer 28 prot/11 rpc) gamed(1 mgr GProvider->GProviderClient,147 prot) gamedbd(GameDBServer 198 rpcs/GameDBClient 177; 4 prot,6 rpc) uniquenamed gfaction(4 mgrs,45 prot) gacd(GAntiCheater->ACServer, GACControl->ACControlServer; 34 prot) gacdclient gdbclient gclient(153 prot) logservice/logclient(opcodes 59-62)
gdeliveryd managers: GDelivery GAuth GProvider GameDB UniqueName GRoleDB + 7 nao lidos
tipos de dado: byte int32 string Octets(blob) CUInt32(compacto var). Transporte TCP binario.
login (estados glinkd, timeout s):
 1 GLoginServer(60) aceita Response KeepAlive ACReport ACAnswer DomainLogin KeyReestablish
 2 GResponseReceive(60) | 3 GKeyExchgSend(60) KeyExchange | 4 GKeyExchgSucc(60)
 5 GVerifyMatrix(180) MatrixResponse (cartao-matriz)
 6 GRoleList(60)->GRoleListReceive(120) RoleList
 7 GSelectRoleServer(86400) CreateRole DeleteRole SelectRole UndoDeleteRole Set/GetCustomData Set/GetHelpStates WebTradeRole* GetPlayerIDByName SSOGetTicket
 8 GSelectRoleReceive(120) | 9 GReadyGame(120) EnterWorld
 ACReport/ACAnswer aceitos em TODOS estados pre-jogo (anti-cheat dentro do glinkd). Estado limita protocolos [I].
opcodes: Challenge=1 (nonce version algo edition exp_rate) | KeyExchange=2 (nonce blkickuser) | Response=3 (identity response use_token cli_fingerprint) | PlayerLogin=65 | SelectRole=70 | EnterWorld=72 | AnnounceProviderID=73 (id left right top bottom worldtag edition = gs anuncia limites do mapa e tag) | KeepAlive=90 | KeyReestablish=1105 | `edition`=string hex das versoes de dados (ver §S17)
rpc gamedbd (decimal/hex): GetUserRoles 3401/0xD49 | GetRoleData 8003/0x1F43 | GetRoleBase 3013 | GetRoleStatus 3015 | GetRoleEquipment 3017 | GetRoleTask 3019 | GetRoleStorehouse 3027 | GetRolePocket 3053 | GetRoleId 3033 | PutUser 3001 | GetUser 3002 | DBCreateRole 3007 | DBDeleteRole 3008 | DBUndoDeleteRole 3009 | PutRole 3024 | PutRoleForbid 3030 | GetRoleForbid 3031 | TransactionAcquire/Commit/Abort (casam c/ PageLogger prepare/commit/abort do WDB [I])
 pwAdmin usa GetUserRoles depois GetRoleData (2 passos) em gamedbd:29400.
seguranca (share/io/security.h): NullSecurity(0) Random(1) [client §S17 diz RANDOM=0 NULL=1, SHA256=7: conferir] ARCFourSecurity/RC4(2) MD5Hash(3) HMAC_MD5Hash(4) SHA1Hash CompressARCFourSecurity/DecompressARCFourSecurity. Java: com.goldhuman.Common.Security.
 cripto C<->glinkd: embutida, DESLIGADA por padrao. Client element/dbserver.conf [GameClient] + servidor glinkd/gamesys.conf em CADA GLinkServer*: isec/iseckey (entrada) osec/oseckey (saida); espelhados (entrada server = saida client). So tipo 2 funciona na pratica [W]. Sem arquivo/chave igual => client nao entra. Barra bots OOG; chaves extraiveis do exe. Truque: GLinkServer extra sem cripto restrito por IP (padrao nao tem whitelist).
 compressao: MPPC (streamcompress.h, janela 8192) ligada pela chave compress do glinkd.
 handshake detalhado/selecao de algo: nao lido [Q].
 implementacoes independentes do protocolo: pwi_oog (C++ authd PWI), Catzilla/AuthDaemon (C#), Kn1fe/GAuthDaemon (C++), pw-packet-stream-reader (Pascal; despeja apos decifrar).
auth/cash protocolos (classes authd Java): UserLogin UserLogout KickoutUser AddCash UseCash GetAddCashSN GMKickoutUser GMShutup GRoleForbid QueryUserPrivilege QueryUserForbid GQueryPasswd GChangePasswd MatrixPasswd Cert{Request,Challenge,Answer,Finish,Key} Enthrallment* AddictionControl
familias de recursos (rpcalls): conta/cash (AddCash UseCash Billing* Coupon* GiftCodeRedeem SellPoint TouchPoint) | mercado (Auction* SysAuction* WebTrade* PShop* Stock*) | cla/guerra (Faction* FactionFortress* FactionResourceBattle* Battle* CountryBattle* KE*=eleicao do rei TankBattle*) | social (Friend* ChatRoom* Mail* PlayerGivePresent Ref*) | multi-servidor (CentralDelivery* PlayerChangeDS GDataExchg* FetchPlayerData SwitchServer* RemoteLogin* GetRemoteRoleInfo) | externo (GameTalk* SNS* GPhoneReceive=servidor movel(40 prot) IWeb*) | GM (GM* DomainCmd Report2GM ForbidUser) | AutoTeam* PlayerProfile*

## §S9 STORAGE DO gamedbd/uniquenamed
motores (escolhidos na compilacao, share/storage/storage.h): BDB (-DUSE_BDB, db_cxx-4.2, binario gamedbd) | CDB (-DUSE_CDB, nao lido) | WDB (-DUSE_WDB, binarios gamedbd.wdb/uniquenamed.wdb; padrao do Makefile). PKG144 usa WDB (simbolos WDB::StorageEnv, Compressor, GNET::__db_helper::PageFile/PageLogger; sem assinatura BDB; logs log.<hex-timestamp>). WDB espelha codigos DB_* do BDB. clsconfig.bdb.old + noimportclsconfig = legado BDB importado. Ferramentas: share/storage/{convert recover tools storagetool}.cpp.
dirs: dbhome(env) dbhomewdb dbdata/<tabela>/ dblogs backup. Erro "Initialize storage environment failed" = caminho errado em gamesys.conf ou base corrompida -> dbtool -r.
PKG144 31 tabelas: auction city equipment friends messages status user clsconfig factioninfo inventory rolename storehouse userfaction base config factionname mailbox sellpoint task translog waitdel gtask order shoplog syslog userstore webtrade webtradesold serverdata factionfortress factionrelation
uniquenamed tabelas: config uidrole unamefaction unamerole
SRC153 42 tabelas (GTableDefinition) = as 31 + 11 novas: force friendext globalcontrol rolenamehis kingelection playershop weborderitem playerprofile uniquedata recalluser mappassword
tabela -> tipo: user:User | base:GRoleBase | status:GRoleStatus | task:GRoleTask | inventory:GRolePocket | equipment:GRoleEquipment | storehouse:GRoleStorehouse | mailbox:GMailBox | friends:GFriendList | messages:MessageVector | factioninfo:GFactionInfo | order:StockOrder | syslog:GSysLog | config:Octets | factionname,waitdel,rolename:int | clsconfig:GRoleTableClsconfig(=molde de personagem inicial por classe: version base status inventory equipment storehouse) | auction:GAuctionDetail | userfaction:GUserFaction | shoplog,sellpoint,translog:char(logs/indices) | city:GTerritoryStore | gtask:Octets | userstore:GUserStorehouse | webtrade,webtradesold:GWebTradeDetail | serverdata:GServerData | factionfortress:GFactionFortressDetail | factionrelation:GFactionRelation | force:GForceGlobalDataList | friendext:GFriendExtra | globalcontrol:GGlobalControlData | rolenamehis:Octets | kingelection:KingElectionDetail | playershop:PShopDetail | weborderitem:WebOrderItemDetail | playerprofile:PlayerProfileData | uniquedata:GUniqueDataElem | recalluser:char | mappassword:MapPasswordData
exclusao de personagem: agendada (GRoleBase.delete_time), desfazivel (UndoDeleteRole), tabela waitdel, [Role] delete_timeout.

## §S10 STRUCTS (SRC153; tipos: i=int u=uint c=char uc=uchar s=short f=float O=Octets blob V=vector)
nota: blobs sao opacos no rpcalls.xml; formato interno decidido por cgame/gs/player.cpp (NAO lido) [Q]. Quase toda struct tem `version` (versionamento) e `reserved*`.
User(23; tabela user, NAO e a users do MySQL): logicuid u, rolelist u(mascara slots), cash i, money i, cash_add u, cash_buy u, cash_sell u, cash_used u, add_serial i, use_serial i(controle do usecash), exg_log V<StockLog>, addiction O, cash_password O, autolock V<GPair>, status c, forbid V<GRoleForbid>, reference O, consume_reward O, taskcounter O, cash_sysauction O, login_record O, mall_consumption O, reserved32 s
GRoleBase(21; base): version c, id u, name O, race i, cls i, gender uc, custom_data O(aparencia), config_data O, custom_stamp u, status uc, delete_time i, create_time i, lastlogin_time i, forbid V, help_states O, spouse u, userid u, cross_data O(servidor cruzado), reserved2-4 uc
GRoleStatus(51; status): version c, level i, level2 i(cultivo), exp i, sp i, pp i[?], hp i, mp i, posx f, posy f(altura), posz f, worldtag i(=tag gs.conf), invader_state i, invader_time i, pariah_time i, reputation i, custom_status O, filter_data O, charactermode O, instancekeylist O, dbltime_{expire,mode,begin,used,max} i, time_used i, dbltime_data O, storesize s, petcorral O, property O, var_data O, skills O, storehousepasswd O, waypointlist O, coolingtime O, npc_relation O, multi_exp_ctrl O, storage_task O, faction_contrib O, force_data O, online_award O, profit_time_data O, country_data O, king_data O, meridian_data O, extraprop O, title_data O, reincarnation_data O, realm_data O, reserved2-3 c
  padrao: cada recurso novo = novo blob aqui (evolui sem migrar banco) [I]
GRolePocket(6; inventory): capacity u, timestamp u, money u, items V<GRoleInventory>, reserved1-2 i
GRoleInventory(10; item unico p/ bolsa/equip/armazem/quest): id u, pos i, count i, max_count i, data O(refino/addons do item [I]), proctype i(bind etc), expire_date i, guid1 i, guid2 i, mask i[?]
GRoleEquipment(1): inv V<GRoleInventory>
GRoleStorehouse(10): capacity u, money u, items V, size1 uc, size2 uc, dress V(aba roupas), material V, size3 uc, generalcard V(cartas), reserved s
GUserStorehouse(7; userstore, armazem da conta): capacity i, money u, items V, reserved1-4 i
GRoleTask(4): task_data O, task_complete O, task_finishtime O, task_inventory V
GRoleForbid(4; mesma p/ conta e personagem): type uc, time i, createtime i, reason O  [valores de type: Q]
GRoleData(6)=base+status+pocket+equipment+storehouse+task. GRoleDetail(35)=visao total (status inventory equipment storehouse task cash cla aliancas/hostilidades logs loja armazem conta). GRoleInfo(27)/RoleInfo(23)=resumo p/ lista de personagens. GTableDefinition(42).
dinheiro: money em Pocket, Storehouse e User; cash SO na conta (User).
nao extraidas: GFactionInfo GUserFaction GFactionRelation GFactionFortressInfo GMail GMailBox GFriendInfo GAuctionDetail [Q]

## §S11 SQL (authd; MySQL ou MSSQL)
divisao: MySQL/MSSQL = conta, GM-perm, cash, bans, sessao. gamedbd(WDB) = personagem/item/cla/correio/leilao. (era 2007-2010: MSSQL; PKG144: MySQL; authd suporta ambos via ismssql)
PW_procedures.sql: users(ID name passwd Prompt answer truename idnumber email mobilenumber province...; campos de nome real/documento = heranca registro CN [I]) | auth(userid zoneid rid) | forbid(userid type ctime forbid_time reason gmroleid) | iplimit(uid, ate 3 pares ipaddr/ipmask, enable, lockstatus) | point(uid aid time zoneid zonelocalid accountstart lastlogin enddate; online/zona via recordonline/recordoffline [I]) | usecashnow, usecashlog(userid zoneid sn aid point cash status datas)
21 procs: acquireuserpasswd addForbid addGM adduser adduserpoint addUserPriv changePasswd changePasswd2 clearonlinerecords deleteTimeoutForbid delUserPriv enableiplimit lockuser recordonline recordoffline remaintime setiplimit updateUserInfo usecash (+demais). Corpos nao lidos [Q].
online/roles/uwebplayers/files = extras do painel pwAdmin (nao do jogo). roles = copia p/ site; files = MD5+versao p/ download (equiv. web de updateserver.txt).
pwAdmin: 1.5.1 precisa importar SQL de roles alem do esquema.

## §S12 GM, DEBUG, CLASSES, TAXAS
GM perms: tabela auth (userid, zoneid=1, rid); 1 linha por permissao; "52 por GM" mas lista tem 51 [?]. addGM concede: 0-11 (ferramentas GM em jogo: ver ID, invis/invul, online, chat, teleportes, speedhack, follow) | 100-105 (ban jogador/chat/trade/venda, chat GM vermelho, desligar gs) | 200-214 (criar mobs/spawners, dispel summon, GMMaster, double exp/drop/dinheiro/SP, ActivityManager, desligar trade/leilao/correio/faccao) | 500-518 (admin painel web: info personagem, logs, reiniciar, consultar conta, presente, pontos, kick, trocar senha; forum diz 501-518). Limitar GM = apagar linha rid.
debug: ptemplate.conf [GENERAL] debug_command_mode=active|1 ; logic_level_limit=150 ; allow_login_class_mask. Com debug ligado QUALQUER jogador usa comandos de debug (gs nao checa GM; handler de GM checa). Patches de gs por build prendem a GM. flag de GM no struct do personagem tem offset diferente por build.
comandos: "d <n> args" (n=numero; d 2000=+1 nivel; d 19 x y z=teleporte; GM d 201-221 (C2S GM_*=200..221, ver §S17)) = c2s p/ gs (d_c2scmd) ; "d_<nome>" = maioria exibicao client (d_fps d_showpos d_showid d_npcid d_query d_cameramode d_boundbox d_title d_relogin d_getservertime...); d_delcmd = servidor (guerra territorio/leilao). Numeracao depende do build (d 10850 falhou na 1.4.2). Lista completa nao incluida.
classes: ordem em gamedbd occupations (PKG144, 10): SWORDSMAN MAGE NEC HAG ORGE ASN ARCHER ANGEL BLADE GENIE (bit n = posicao n). allow_login_class_mask: PKG144=1023 (247 comentado); 255=8 primeiras; 219=bits0,1,3,4,6,7=SWORDSMAN MAGE HAG ORGE ARCHER ANGEL = 6 classes/3 racas [I]. BLADE/GENIE nascem em pos inicial diferente (indica pacote mais novo que 1.4.4 puro).
reduzir racas (5->3) exige 3 camadas coerentes: constante no elementclient.exe (offset por build; file vs IDA diferem 0x400000) + interfaces/version01/createrace.xml + mascara ptemplate.conf. Servidor barra raca removida mesmo com client alterado.
dados de classe em 2 lugares: gamedbd/gamesys.conf (posicao/atributos iniciais) e gamed/ptemplate.conf (attack_speed attack_range crit_rate...) [nao comparados].
taxas: PKG144 em rates.conf (ChaosRates, [ChaRatesCfg] exp sp item money task_*) E ptemplate.conf (exp_bonus money_bonus sp_bonus drop_bonus); unidades nao explicadas (10000/7500/100000) [?]. 1.5.1: taxas fora do ptemplate [W]; alternativa: multiplicar linhas do elements.data.
anti-vicio ("wallow"/enthrallment): authd conta tempo (3h jogo/5h descanso); gs aplica redutor leve/pesado (gs/wallow/addiction.cpp: online_sum offline_sum logout_time; LIGHT_PANISH_TIME WEIGHT_PANISH_TIME). PKG144 neutralizado (enable_enthrallment=false, % =100). `gsalias_wallow.conf` = variante c/ wallow (nao existe no PKG144).
anti-cheat: gacd + flag anti-cheat por mapa + AntiCheat global.

## §S13 CÓDIGO-FONTE (zip source_server_153; CVS; 83.146 entradas 1,13GB)
camadas: share/ (io storage rpc jio(Java) pyio common util cache perf application... ; io tem protocol netio pollio security mppc login rpc timermanager) | cnet/ (daemons) | cgame/ (gs) | cskill/ (skill expr gen header simulator; 316 arq nao lido)
linhas: cnet 183k | cgame 236k | share 27k | cskill 37k. .hpp/.hrp = 1 por protocolo/rpc (>48k na arvore).
cnet arq/pasta: gdeliveryd 937 | glinkd 398 | gamed 267(cola de rede do gs, nao o gs) | gamedbd 246(1 .hrp por rpc) | gclient 138 | gfaction 137 | gacd 122 | gdbclient 71 | gacdclient 31 | uniquenamed 22 | gauthd 20 | logservice/logclient 12 | dbtool 1
build: Makefile.tmpl (antigo, cita linkd deliveryd authd client qqdbd inexistentes) ; MakeGame compila glinkd gdeliveryd gauthd gamed gamedbd gdbclient gfaction. Passos: ./rpcgen na raiz cnet; opgen.pl em gfaction/operations. glinkd -v "CNET 3.2.2". Deps: make gcc g++ libssl-dev libcrypto++-dev libpcre3. Compila facil so em distros antigas (Ubuntu 8.04 32b/Fedora Core 2/CentOS citados; discutido). glinkd compila; gdeliveryd com ajustes; gs/client exigem muito ajuste (fontes incompletos; gerar state, callids).
scripts: begingame(gdeliveryd glinkd gfactiond gamedbd.wdb uniquenamed.wdb) killgame/stopgame.
cgame/gs (567 arq): raiz 120 .cpp + item(90) pathfinding(60) template(26) instance(25) task(13) ai(5) mobile(4) wallow(3) io(2). Irmaos: collision(39) include(30) io(16) common(15) libgs(8). Configs exemplo: gs.conf gs2.conf gmserver{,2,3}.conf gsalias.conf ptemplate.conf.
 familias: objetos(object actobject npc player matter substance petnpc siegenpc guardnpc) | gerentes(worldmanager objmanager aiman petman titlemanager weddingmanager autoteamman meridianmanager fateringmanager dpsrankmanager mapresman) | *_filter (buffs/estados: skill mount fly invisible pk_protected wedding...) | player* (cmd faction team trade stall mall duel battle countrybattle force title fatering reincarnation giftcard dailysign trashbox) | globais(global_controller global_drop global_manager global_message public_quest random_maze multi_exp_ctrl online_award antiwallow serverstat threadusage) | rede(netmsg usermsg userlogin uniquedataclient serviceprovider)
 outras copias: game_qq (linhagem antiga: authd deliveryd linkd qqdbd trade; plataforma "QQ"? [I]) | netgame (=nome original do cgame [I]) | 20141231/PWSL | release_20130703..20150316 (timeline de features nao comparada) | init | .7z
 versao 1.5.3 do zip NAO provada (datas set/2015 + nome).

## §S14 VERSÕES (nomes/anos da tabela [P] = provavel regiao BR; PWI difere)
ver | nome(print) | classes/racas | obs
1.2.6 | Imperio Ancestral 2008 | | 1a expansao; PWI "Lost Empire" dez/2008
1.3.1 | Vale Primordial 2009
1.3.6 | | 3 racas 6 classes | "PW classico", mais usada em privados [W]
1.3.9, 1.4.1 | | 3r/6c
1.4.2 | Mar em Furia 2010 | 4r/6c | PWI Rising Tide dez/2009 (raca Tideborn, recasting); XML UI possivelmente aqui
1.4.4 | Genesis 2011 | 5r/8c (PKG144 libera 10) | PWI Genesis mar/2011
1.4.5 Submundo 2012 | 1.4.6 Ascensao da Dinastia jan/2013 | 1.5.0 Ascensao Espiritual nov/2013
1.5.1 | Novos Horizontes 2014 (PWI dez/2013)
1.5.3 | Alvorecer Sombrio 2015 | 6r/10c | skills God/Evil
1.5.5 | Elysium 2016 (PWI abr/2016) | armas G17, casas
1.5.9 | Redencao 2019 (PWI set/2018) | 1.6.2 | 6r/12c
PWI extras sem mapeamento: Age of Spirits 05/2009, Descent 02/2012, Sirens of War 11/2012, Eclipse 12/2014, War Front 11/2015, Neverfall 04/2017, Wonderland 01/2018, Wings of Rebirth 05/2019, ate Song of the Surging Waves 12/2024. Mapa versao->expansao p/ 1.4.5-1.5.3 nao feito [Q].
fonte: forum mmorpg-devs.ru ativo (ultimo post out/2026).

- [A] PKG144 (pacote "PW_ServerFiles_1.4.4v60", cliente+servidor juntos): server `home/`: gacd, gamed (gs, gs.conf, gmserver.conf, libtask.so, core), gamedbd (+cashstat, clsconfig, dbtool), gdeliveryd, gfactiond, glinkd, uniquenamed, logservice, mauthd. `etc/`: authd.conf, GMserver.conf, iweb.conf, table.xml, gmopgen.xml, ssl/. `gamed/config/a01` e `a02`: mapas (.hmap/.dhmap/.rmap), npcgen.data.
- [A] glinkd do PKG144 reporta versao do jogo 10404 (=1.4.4); cliente elementclient.exe. elements.data do cliente = v60 (0x3000003C), tasks.data = v93.

## §S15 FERRAMENTAS
pck: sPCK(-pw..., SourceForge pw-tools; extrai/compacta/add/.cup/.xup) pwpack/PWpack Fast-Packer pPck/sdPck(Kn1fe, C#) uPCK(C# WinForms, zlib.net; le v2/v3+.pkx, grava so v2; fonte lido: §S3) autoangel-rs(Smertig; Rust/Python/WASM; pck+pkx, elements.data por versao, modelos; demos web)
dados: sELedit(elements.data; 1.2.6-1.4.5) sTASKedit(tasks.data) sNPCedit/NPC Edit 2(npcgen.data) Dynamic objects editor sGSHOPedit(gshop; 1.4.2+) NextAIPolicyEditor2/AipolicyEditor(aipolicy) ProjectAtlas(path.sev) DataTranslator(elements+tasks) Npcgen2CoordData(npcgen->coord_data.txt) Skill Octet Editor / pw Octets Editor v1.5 (octetos equip/ovos/voos/skills) PW-IISET(icones) GfxEditor+SkillGfxComposer
servidor/client: CPW patcher/CPWCreator(updateserver) pwAdmin(painel PHP, shadowvzs) iWeb PWGen PWoe dbtool
versoes pagas de editores existem (alexdnepro, gouranga). 1.5.x provavelmente exige ferramentas mais novas.

## §S16 IDs UTEIS (era 2010; validar no elements.data real; listas de pet NAO valem p/ 1.5.2, ver §S17)
refino por rank r=1..15 (addon id): soqueira 1691+r | arma fisica 1711+r | arma magica 1751+r | arma distancia 1731+r | armadura pesada 1771+r | leve 1791+r | magica 1811+r. Forum escreve soqueira rank9=1600 (provavel typo de 1700 [?]).
addons: armas (cast speed -3..-15%, atk fis/mag, atributos +10..19, intervalo -0.05..-0.1s, crit 1-4%, skills por addon); armaduras (def +94..313, res elem +250..750, vida +60..180, mov +0.1..1.5m/s, esquiva +110..142); pedras tem 2 grupos (arma/armadura); prob. do stat em receita pode ser 0 e ainda funcionar.
adicionar pet (2010): 1) desempacotar configs.pck, pasta configs/Pets/<x>/ com modelo; ajustar caminho .smd no .ecm 2) server elements.data lista 93: clonar pet, novo ID 3) client lista 90 idem; no client campo modelo = caminho do .ecm (nao .smd) 4) lista 91 (ovos): clonar ovo, novo ID, campo apos icone = ID do pet 5) reempacotar e vender o ovo em NPC. Caminhos em chines (sELedit suporta Unicode). Com CPW patcher, por em models.pck.

## §S17 CLIENT SOURCE (zip source_client_153) [C]
escopo: SRC-CLI = zip "source_client_153": 10.870 arquivos, 388MB (so ~64MB sao fontes). Pastas: CElementClient(1930 src/507k linhas) CElementSkill(2875/694k) CCommon(100/50k) CElementEditor(491/135k) CElementData(144/44k) CTaskTemplEditor(126/22k) CEleDataMan CElementIconTool CElementHintTool(79/26k) CFWEditorLib(236/22k) CRandomMap_WYD(13/11k) ClientSimulator(71/24k) ClientTryLogin(43/12k) ClientTools(PWDownloader FileExtract) CCHBasedCD CBin(Interfaces Shaders) Doc
identidade: GAME_VERSION=0x00010502 (=1.5.2, NAO 1.5.3) | GAME_BUILD=2457 | ELEMENTSKILL_VERSION=15 | ELEMENTDATA_VERSION=0x3000007f | task_templ_version=121 (EC_Game.cpp, ExpTypes.h, TaskTempl.cpp)
linhagem: Doc/ tem manual "[完美国际]" (Perfect World Intl) 2010-2013 e doc de compilacao (2013-08): "internacional usa engine/SDK antigo, versao paga usa novo" => arvore = linha PWI [I forte]. Fontes em GBK (converter p/ ler).
=> glinkd "version" esperado p/ este client = 10502 (hex) [I]; GAME_BUILD 2457 = escala propria; relacao com "build 2567" (Docker 1.5.5) nao provada [Q].

### arranque do client
ordem: abre 15 pck (configs facedata gfx grasses help\script interfaces building models sfx shaders surfaces textures trees litmodels loddata) via OpenFilePackageInGame; falha = erro "pacote danificado" EXCETO com cmdline sepfile (le arquivos soltos). Pastas fora de pck: cursors data help\voice maps userdata\{userpic,character,server} music info bbsfiles font physx\v2.8.4 reportbugs. Arquivos soltos na raiz (lista fixa): dbserver.conf elementclient.exe elementskill.dll ftdriver.dll zlibwapi.dll unicows.dll perfectworld.version arcsdk.dll gt_*.exe logiccheck.dll pwprotector.exe etc.
depois: rede (dbserver.conf via GNET::Conf; chave [GameClient] compress) > configs\element_client.cfg + client_id.cfg + userdata\SystemSettings.ini > GT overlay (opcional) > StartLogin.
dados lidos: configs\{uiconfig.ini qshopitem.ini fixed_msg.txt item_desc.txt item_ext_desc.txt skillstr.txt buff_str.txt DynamicObjects.data fonts.txt item_ext_prop.txt item_color.txt Coord_data.txt petautoskill.txt petwords.txt} data\elements.data data\tasks.data (MD5 de ambos calculado/logado)
cmdline (key:value): user pwd area token token2 role agent gt coreclient arcasia exportserver miniclient logiccheck luadebug rtdebug rtdebug_hide sepfile rendernofocus nocheck facepill nocooldown ; nocheck = ignora divergencia de "versao pequena" (edition)
estados do GameRun: GS_NONE GS_LOGIN GS_GAME. Gerentes: MAN_PLAYER NPC MATTER ORNAMENT SKILLGFX ATTACKS DECAL (Tick/Render/ProcessMessage). Mensagens internas MSG_* (100+ gerais, 200+ ghost, 220+ host).
ui: 405 classes Dlg*; AUI; temas (d_theme); "^ffffff" nos textos = cor padrao da janela, nao branco puro (CECGameUIMan::ReplaceColor).

### version/edition (resolve [?] edition de §S8)
Challenge.version = GAME_VERSION; Challenge.edition = string hex "%x%x%x%x" = ELEMENTDATA_VERSION + task_templ_version + gshop_timestamp + gshop_timestamp2 (versao dos dados elements/tasks/gshop). Client compara os dois; divergencia = msg "versao errada" (server mais novo) ou "server update" e fecha a sessao.
Challenge.nonce carrega Attr do servidor (load, lambda, flags doubleExp/doubleMoney/doubleObject/doubleSP/freeZone/sellPoint/battle) + createtime; usado tambem no PING da lista de servidores (estado PINGING: conecta, le Challenge, fecha). Challenge.exp_rate = taxa de exp.
login: Response = identity + resposta ao nonce. Variantes: algo 0 (HMAC-MD5), algo!=0 (SHA256), use_token 1/2 (token), USB-key HID; cli_fingerprint = client id (client_id.cfg).
KeepAlive(90) a cada 15 ticks em STATE_KEEPING. GameClient: CLOSED CONNECTING PINGING KEEPING CLOSING.
estados do client (state.cxx): LoginClient(6000) DataExchangeClient(7200) GLoginClient(6000) GSelectRoleClient(86400) GDataExchgClient(6000) [unidade nao verificada]
opcodes conferidos: Challenge=1 KeyExchange=2 Response=3 OnlineAnnounce=4 GameDataSend=34 SelectRole=70 EnterWorld=72 KeepAlive=90 MatrixChallenge=551 MatrixResponse=552 ACReport=5001 ACRemoteCode=5003 ACQuestion=5031 ACAnswer=5032 (+~387 ids em Network/callid.hxx; 435 arquivos em Network/)
security (gnsecure.h do client): RANDOM=0 NULL=1 ARCFOUR=2 MD5=3 HMAC_MD5=4 COMPRESS_ARC4=5 DECOMPRESS_ARC4=6 SHA256=7. [CONFLITO c/ §S8 (Null=0 Random=1): conferir share/io/security.h]. MPPC janela 8192 confirmado no client.
cross-server: CECCrossServer tipos CT_GAME_TO_SPECIAL / GAME_TO_ORIGINAL / LOGIN_TO_SPECIAL; guarda isec/osec da sessao p/ reconectar; protocolos ChangeDS / PlayerChangeDS (casa com §S8 multi-servidor).
serverlist.txt: linha SEM TAB = nome de grupo; linha = Nome<TAB>portaMin-portaMax:host[:linha]<TAB>zoneid[<TAB>&1 novo/hot][<TAB>&2 servidor central cross]. Busca: ..\patcher\server\serverlist.txt depois userdata\server\serverlist.txt. Export gera serverlist.sev (zoneid+nome; rejeita duplicados). userdata\currentserver.ini [Server] CurrentServer/CurrentServerAddress/CurrentLine.

### comandos do jogo (dentro de GameDataSend=34)
S2C: 397 comandos (0=PLAYER_INFO_1 ... 396=PRAY_DISTANCE_CHANGE; SELF_INFO_1=8 OBJECT_MOVE=15 OWN_IVTR_DATA=42 TASK_DATA=105 SERVER_TIME=114 PLAYER_CASH=253 COMMON_DATA_NOTIFY=266)
C2S: 0..179 (NUM_C2SCMD=180; PLAYER_MOVE=0 LOGOUT=1 USE_ITEM=40 CAST_SKILL=41 TASK_NOTIFY=49 DEBUG_DELIVER_CMD=104 DEBUG_GS_CMD=105 MALL_SHOPPING=106); GM_* = 200..221 (GM_MOVETO_PLAYER=201 ... GM_OPEN_ACTIVITY=220 GM_CHANGE_DS=221) [corrige "GM d 201-222" de §S12]
d_delcmd = DEBUG_DELIVER_CMD(104) e d_c2scmd = DEBUG_GS_CMD(105) [I]

### console d_* (Doc/完美国际命令行.txt)
ativos: d_cameramode d_boundbox d_rtdebug(0|1|2|list|show|hide <PROTO>) d_npcid d_goto d_go d_c2scmd d_delcmd d_viewradius d_relogin d_skill d_render_water|grass|forest d_turnaround d_testdist d_gfx d_showpos d_a3dstat d_gamestat d_treelod d_fps d_playerradius d_showid d_skipframe d_settimeofday d_getservertime d_task(so build DEBUG) d_mipmapbias d_trncull d_title d_namepos d_createtime d_lastlogintime d_money d_query(nome->id em elements.data) d_querynpc d_queryrservice d_querymodel d_queryskill d_theme d_uidebug d_rendernofocus d_ie d_enableie d_showfortress d_ignoreurl d_showdialog d_come(simula protocolo recebido) d_ignoreprotocol d_factionpvpstatus d_debugfog
invalidos: d_runspeed d_fly d_render_outline d_trnlayer d_modelupdate d_minidump d_gscmd

### elements.data (CCommon/elementdataman.cpp) [C] (resolve cabecalho de §S3/Q2)
layout: u32 version(0x3000007f) | time_t timestamp | listas em ORDEM FIXA: cada uma = u32 count + count*sizeof(struct) bruto (structs de ExpTypes.h) | lista talk_proc = size_t count + registros com save/load proprio | 2 blocos de assinatura do exportador no meio (tag 0xab7689dd + nome do PC com XOR; tag 0xee35679f + info de hardware) que o loader sempre le e ignora
load falha (-1) se version != ELEMENTDATA_VERSION => mudar layout = mudar versao.
DATA_TYPE (198 listas, 0..197; 198=MAX): 1 EQUIPMENT_ADDON 4 WEAPON_ESSENCE 7 ARMOR_ESSENCE 10 DECORATION_ESSENCE 13 MEDICINE 16 MATERIAL 18 DAMAGERUNE 20 ARMORRUNE 22 SKILLTOME 23 FLYSWORD 24 WINGMANWING 28 ELEMENT 29 TASKMATTER 36 STONE 37 MONSTER_ADDON 39 MONSTER_ESSENCE 40-56 NPC_*_SERVICE 58 NPC_ESSENCE 59 TALK_PROC 60-67 FACE_* 70 RECIPE 71 ENEMY_FACTION 72 CHARRACTER_CLASS_CONFIG 78 PLAYER_LEVELEXP 80 MINE 82-84 FASHION 91 SUITE 94 PET_TYPE 95 PET_ESSENCE 96 PET_EGG 97 PET_FOOD 107 SKILLMATTER 109 DESTROYING 120 GOBLIN 130 MULTI_EXP 132 GOD_EVIL_CONVERT 133 WEDDING 138-141 FACTION_FORTRESS/BUILDING 145 ENGRAVE 151 FORCE 158 COUNTRY 166 MERIDIAN 170 TITLE 174 PLAYER_REINCARNATION 178 PLAYER_REALM 179-180 CHARIOT 181-185 POKER 188 GT_CONFIG 191 FACTION_PVP 192 UNIVERSAL_TOKEN 197 SIGN_AWARD
espacos de ID: ESSENCE=0 ADDON=1 TALK=2 FACE=3 RECIPE=4 CONFIG=5. ELEMENTDATA_NUM_PROFESSION=12; ELEMENTDATA_DOMAIN_COUNT=52 (territorios).
=> numeracao da era 2010 em §S16 (pets 90/93/91) nao vale p/ 1.5.2: PET_ESSENCE=95 PET_EGG=96.

### tasks.data (Task/*) [C]
cabecalho: u32 magic 0x93858361 | u32 version(121) | u32 item_count | item_count*u32 offsets | templates (LoadFromBinFile). Versao divergente = erro (-2 pack mais novo, -3 mais antigo).
limites: ativas 175 | concluidas 2040 | storage 32x10 | simultaneas 30 | entregas 5. Explica blobs GRoleTask (task_data/task_complete/task_finishtime) de §S10.

### mapas (CCommon/EC_WorldFile.h EL_Region.h EL_Precinct.h) [C] (parcial Q6)
.ecwld: ident 'ECWD', versao 12 | header: largura/comprimento do mundo (m), tamanho do bloco, grid por bloco, n blocos, linhas/colunas, offsets de arvores e grama, flags(editor/home), 60B reservados | tabela de offsets por bloco | blocos (arvores, agua, grama, ornamentos, box areas, efeitos GFX, modelos EC, critter groups, beziers, sons)
.ecbsd: ident 'BSDF', versao 12 (dados compartilhados por bloco; ecwld referencia por export-id+offset). Massa de dados: versao 2.
carga (EC_World::LoadWorld): <mapa>.trn2 (terreno) > .ecwld (cena) > navigate.dat > precinct.clt > region.clt > scenelights.dat ; extras .t2hc, maps\world\world.gld (grama), trees\trees.cfg
region.clt: versao 5 | header (versao, n regioes, n caixas de transporte, timestamp) | tipos: 0=PK 1=SANCTUARY | transport box = instancia destino, instancia origem, limite de nivel, volume 3D (portal/teleporte de §S6)
precinct.clt: versao 7 | header (versao, n, timestamp) | nome, prioridade, instancia origem/destino, domain id (guerra de territorio), flag PK-protect, sons dia/noite, musicas + intervalo/loop, marcas
Home: pasta Home/ = gerador procedural do "lar" do jogador (terreno Perlin, estradas, florestas, agua, construcoes; MSG_ENTERAUTOHOME). CRandomMap_WYD = gerador de mapas aleatorios (random_maze no gs).

### skills (CElementSkill) [C]
2852 headers skill<id>.h (ids 1..2865) gerados por CPPGEN; mesmo codigo compila p/ client (elementskill.dll) e server (#ifdef _SKILL_SERVER); Skill<id>Stub com State1..N (tempo, Calculate, Interrupt...). Tipos: 1 ataque 2 bless 3 curse 4 summon/item 5 passiva 6 arma 7 vida 8 teleporte 9 producao 10 bless pet 11 bless neutro. Alcance: 0 ponto 1 linha 2 esfera propria 3 esfera alvo 4 cone 5 self. Requisitos: LearnRequirement (nivel sp dinheiro classe rank realm), UseRequirement (mp ap forma arma flecha ambiente hp combo); pet e goblin tem os seus.
client confere ELEMENTSKILL_VERSION vs dll na inicializacao.

### anti-cheat no client (defence/) [C, so nivel alto]
modulos: Collector (coleta processos/janelas, mouse, pilha, integridade de imagem, checagem de APIs) + NetDLL (executa blob recebido do servidor = ACRemoteCode). Casa com gacd (ACQuestion/ACAnswer/ACReport) de §S4. VerifyDefenceThread chamado do tick da rede. Overlays: GT (chat) e Arc.

### Doc/ (20 docs internos 2010-2013, CN)
GT chat | linha de comando | otimizacao de UI/memoria e client | export serverlist.sev cross-server | codigo de conduta | upgrade de engine e escala de modelo | arrastar-soltar | console (docx+xlsx) | transformacao de jogador | tema de UI | wiki | cast em movimento | auto-pathfinding (+bugs) | manual de manutencao (petwords.txt/CECPetWords, RECALL_PET com motivo) | compilacao (SDK velho x novo)
Doc/软件技术申请/: 2 pedidos de patente (exibicao circular de personagens; geracao aleatoria de fases 3D) + demo OnScreenModelDisplay (inclui .zip e configs.pck/building.pck de 2015-01, nao analisados)

## §S18 aipolicy.data (fonte: AIPolicyEditor C# decompilado; doc AIPolicyEditor-detalhamento.md) [C-terceiros]
uso: IA scriptada de mobs/NPCs (server; client tem copia, §S7). Liga ao mob: elements lista MONSTER (idx sELedit 38) campo 64 = id da policy [I forte, §S23]
modelo: arquivo > Policy(uID) > Trigger ("SE condicao ENTAO operacoes") > CondTree + Operacao[] (cada uma c/ Alvo)
binario LE: i32 uVersion | i32 nPolicy | Policy{ i32 nVersion, i32 uID, i32 nTrig, Trigger{ i32 dwVersion, i32 uID, bool bActive, bool bRun, bool bAttackValid, byte[128] nome GBK, CondTree, i32 nOp, Op{ i32 tipo, <param SEM tamanho>, Target{ i32 tipo, <param> } } } }
CondTree (pre-ordem): i32 tipo | i32 paramSize (lido e descartado) | param | marcadores: 1=filho esq 2=filho dir 3=fim folha 4=fim c/ filhos
RISCO: operacao NAO tem tamanho -> layout errado de 1 tipo desalinha o resto. Validar por round-trip byte a byte.
dwVersion e POR TRIGGER (novo trigger copia o do 1o trigger da 1a policy). Limiares: >=1 O_ACTIVE_CONTROLLER +bool bStop+3B | >=4 o_set_global +bIsValue | >=7 o_summon_monster enum iDispearCondition | >=9 o_play_action (nome[128]+loop,interval,playtime) | >=17 O_TALK_TEXT +i32 uAppendDataMask | >=24 O_SUMMON_MINE +bool+3B
condicoes 0..40: 0 time_come(id) 1 hp_less(f%) 2 start_attack 3 random(f) 4 kill_player 5 NOT 6 OR 7 AND 8 died 9-12 + - * / 13 > 14 < 15 == 16 var_global 17 const 18 be_hurt(lo,hi) 19 reach_end(path) 20 history_stage 21 history_value 22 stop_fight 23 local_var 24 reach_end_2 25 has_filter 26 room_index 27 players_in_radius(f) 28 players_in_region(caixa) | 29-40 sem nome (29 data Y/M/D/h/m, 30 dia-semana/h/m [I]); 31 33 40 sem param
operacoes 0..79: 0 attack 1 use_skill 2 talk 3 reset_hate 4/5/6 run/stop/active_trigger 7/8 create/kill_timer 9 flee 10-12 hate first/last/50% 13 skip 14 active_controller 15 set_global 16 revise_global 17 summon_monster 18 walk_along 19 play_action 20/21 history 22 faction_pvp_points 23 calc_var(dst=src1 op src2; Add Sub Mul Div Mod) 24-35 variantes c/ VarType (summon_2 walk_2 skill_2 controller_2 deliver_task summon_mine summon_npc tasks em regiao/hate contagem de players) | 36-79 so estrutura (unkN); o_62 SEM classe [?]. Tabela completa: doc fonte §5
alvos 0..18: 0-2 hate first/second/others 3 most_hp 4 most_mp 5 least_hp 6 occupation_list(mascara §S24) 7 self 8 killer 9 birthplace_faction 10 hate_random 11 nearest 12 farthest 13 hate_first_redirected 14-18 so estrutura
enums: VarType 0 Global 1 Local 2 Const 3 Random | SummoneeDisappear 0 Never 1 FollowSummoner 2 FollowTarget 3 Either | Patrol 0 StopAtEnd 1 Return 2 Loop | Speed 0 Begin 1 Slow 2 Fast 3 End | Chat 0 Normal 1 Faction 2 Anon 3 Broadcast 4 Instance 5 System 6 InstanceCenterScreen 7 Whisper
O_TALK_TEXT: i32 tamanho + UTF-16LE c/ \0; canal = prefixo de 2 chars no texto ($F $A $B $I $S $X $W; sem=Normal). AppendDataMask: Name=1 LocalVar0=2 LocalVar1=4 LocalVar2=8 TalkingName=0x10
controller = (uID, uIDType) 8B; O_64 = bool+3B + 10 ctrl fixos (84B); controllers = ativadores de grupos de spawn [I] (casa c/ trigger do npcgen §S22 [I])

## §S19 GSHOP (fonte: sGSHOPedit C++/CLI decompilado; doc pw-gshop-ui.md) [C-terceiros]
client gshop.data/gshop1.data: i32 timestamp | i32 nItens | item[1380B] | categoria[8 fixas]
 item client [T 24/10, PKG144 + server 1.4.4]: @0 shop_id @4 cat @8 sub (i32) | @12 surface 128 BYTES GBK (Surfaces\百宝阁\*.dds, figura grande) | @140 item_id @144 amount | @148 4 sale_option x32B (price end_time duration(s) start_time control_type day status flags) | @276 descricao 512 chars UTF-16 (1024B, ^cor, 
 quebra) | @1300 nome 32 chars UTF-16 (64B) | @1364 gift_id gift_amount gift_duration log_price = 1380B. Servidor 164B = mesmos numeros na mesma ordem (shop,cat,sub,id,amount,4 opcoes,gift x3,log_price); regravar o servidor a partir do cliente da identico. Precos vistos: 10..80000 (unidade [I] centavos de ouro). status visto 0-3 (0 normal, 1-3 novo/quente/presente? [I]); control_type sempre -1; duration 604800 (7 dias) ou 0 (permanente). gshop1 (Butique de Eventos) pode estar vazio (so 8 categorias).
 categoria: nome[128] UTF-16 | i32 nSub | nSub x nome[128]
server gshopsev.data/gshopsev1.data: i32 timestamp | i32 nItens | item[164B] (mesmos numericos, SEM textos/surface/categorias)
juncao: shop_id (unico) + item_id (= id elements.data). Strings UTF-16LE fixas zero-padded. timestamp unix UTC = provavel gshop_ts de Challenge.edition (§S17) [I forte]
control_type: client -1 => grava price/expire/duration/start; outro => esses 4 zerados no client (valor real so no server) [C]. status: 0 Normal 1 New? 3 Hot? 4 Sale? [I]. day/flags [?]
remover item: cat=sub=-1 (nao exportado). novo: shop_id=max+1
legado 1.3.6: item 1288B (4x(price,ign,duration), status unico; sem gift/log_price/control_type)
item_ext_desc.txt: UTF-16, `item_id "texto"`, ignora / e #
erro 1451 no gshop = item sem duracao permitida [W]; preco 0.00 no gshop => "versao antiga" [W]

## §S20 interfaces.pck XML (fonte: UIEdit C# WPF github perfectdev/UIEdit; doc pw-gshop-ui.md) [C-terceiros]
XML UTF-16LE (parte c/ BOM FF FE; preservar). NAO e XML valido: atributos c/ < > crus (escapar p/ parsear, restaurar ao salvar)
raiz: Name Width Height + Resource/FrameImage@FileName (moldura). Controles filhos: Name x y Width Height (px absolutos)
tags: IMAGEPICTURE | STILLIMAGEBUTTON (Hint@String, Text@String FontSize Color OutlineColor InnerColor, FrameUp/Down/OnHoverImage) | RADIO (Normal/CheckedImage) | SCROLL (Up/Down/Scroll/BarFrameImage) | LABEL (Text, Color, TextUpperColor/TextLowerColor=gradiente [I]) | EDIT
cor = "R,G,B,A" 0-255. FileName relativo a raiz de surfaces (.dds/.tga). Moldura = 9-slice (cantos copiados, bordas/miolo esticados)
existem e UIEdit NAO le: SoundEffect Command FontName FontShadow FontBold Outline [I]; lista completa de tags do client [Q]

- [T 24/10] interfaces.pck do cliente 1.4.4 BR tem DUAS interfaces: raiz interfaces\*.xml (antiga, o jogo nao usa) e interfaces\version01\*.xml (a usada). 533 XML em version01, todos UTF-16 com BOM. Tipos: IMAGEPICTURE 4412, LABEL 2952, STILLIMAGEBUTTON 1936, EDIT 778, CHECK 212, RADIO 181, LIST 91, TEXT 88, PROGRESS 77, COMBO 29, SLIDER 24, SCROLL 12, TREE 9, WINDOWPICTURE 4, SUBDIALOG 4, CONSOLE 1, VERTICALTEXT 1.
- [T] Fontes do jogo vem com o cliente: element\font (e fonts, data): fzxh1jw.ttf = 方正细黑一简体 (texto normal), fzl2jw.ttf = 方正隶二简体 (titulos), fzlbjw.ttf = 方正隶变简体. Nao tem letras acentuadas. FontSize do XML ~ altura em px (BR abreviou "C. Tudo" p/ caber 48px com FontSize 10 e manteve "Consertar").
- [T] Rotulo (LABEL) sem Width/Height: x/y e a ancora (Align 0 esquerda, 1 centro, 2 direita). Texto NAO e cortado na caixa: traducoes maiores que a caixa passam por cima no jogo (ex. shop.xml "Moeda(s)" bold em 29px) = defeito do cliente, nao do editor. Textos "0000"/"1234"/"物品描述" sao exemplos que o jogo troca; controles empilhados no mesmo lugar (img_new/hot/gift, Btn_Time1..3) o jogo mostra um por vez; x=-2000 = escondido.
- [T] STILLIMAGEBUTTON TextOffsetX/Y desloca o texto (ex. abas da loja com icone: X=12). Imagens com Frames="n" = tira animada (usar 1o quadro). .bmp da interface usam magenta (255,0,255) como transparente. Cor malformada existe (ex. "25color").
- [W] sPCK extrai com o caminho completo: interfaces.pck -> interfaces.pck.files\interfaces\version01\... (confirmado na pasta do usuario).

## §S21 tasks.data — visao do sTASKedit (doc pw-tasks-editor.md) [C-terceiros] (complementa §S17 tasks)
header 12B: i32 magic 0x93858361 (editor aceita 0) | i32 version | i32 count | count x i32 offset | corpos. Escrita: reserva tabela, grava corpos, volta ao byte 12 e grava offsets
corpo da quest = [bloco fixo por versao (logic_read_task_data_struct, ~40 sub-blocos, arrays c/ contagem)] + [textos: descricao, ok, no, tributo (pwstr) + 5 talk_proc: delv, unqualified, delv_item, exe, award] + [i32 nSub + subquests recursivas] [C-Rody V22 tasks/ATaskTempl.cs] -> o bloco fixo NAO pode ser pulado sem o esquema completo (Rody V22 removeu o modo "adivinhar o fim")
corpo: u32 ID @0 (v>=153: 2 bytes antes) + nome 30 chars UTF-16 (60B) @4, cada char XOR (ushort)ID; padding = 0^ID [T no PKG144 v93]
PKG144: tasks.data v93, 9.298 quests de topo, offsets crescentes; nomes com cor ^RRGGBB no inicio (ex. "^ffffffO Renascer dos Alados") [T]
corpo da quest v93 (layout completo, PROVADO no PKG144: 21.777 quests, cada uma termina no offset da proxima) [T]: 58 fases do bloco fixo (campos + contadores + ponteiros mortos de 4B) -> arrays variaveis na ordem: assinatura(60B se HasSign) | horarios (n×(tm 24B inicio + tm 24B fim)) | chaves (n×9B) | expr PQ (n×(64B + 64×8B)) | contrib monstros (n×16B) | regioes entrega/entrar/sair (n×24B) | itens exigidos/dados (n×17B) | membros (n×32B) | monstros (n×30B) / itens a obter (n×17B) | expressoes (n×(64+512B)) | textos de quest (n×128B) | locais alcancar/sair (n×24B) -> premios: AWARD_DATA ×2 + RATIO_SCALE ×2 (i32 n, 5 floats, n×AWARD) + ITEMS_SCALE ×2 (i32 n, i32 item, 5 i32, n×AWARD) -> 4 pwstr + 5 talk_proc -> i32 nSub + subquests. AWARD_DATA v93 = 153B fixos + arrays (cand: bool+n+n×17B; monstros invocados se n>0: 6B+n×16B; ranking PQ se n>0: 1B+n×21B; chaves n×9B; display n×4B; expr; textos n×128B). Todos os textos XOR (ushort)ID. ~424 nomes de campo (grupos: basico, entrega, tempo, premissas, itens dados, requisitos de conclusao, eventos, falha, UI, casa/PQ/torre)
arvore: sub-quests recursivas (m_ulParent FirstChild NextSibling PrevSibling). Premio sucesso/falha, tipo 0-5, variantes normal/razao/itens/carrier/evento
limiares versao tasks: <80 1 regiao | >=69 timetable 24 | >=80 listas de regioes | <=119 mascara classe 1023 (10 bits), >=120 4095 (12) | >=140 TaskEvent + 6 faixas de razao
limiares elements usados pelo editor: >=87 lista de titulos (169) | >=191 listas >=58 deslocam +1 | <52 mascara 255
faixas validas (deteccao de valor desconhecido): m_ulType 0-110 | m_enumMethod 0-5,8,10-28 | FinishType 0-3 | AvailFrequency 0-4 | AwardType 0-5 | MsgChannel 0-15 | Period 0-8,20-22,30-32,40-41 | FactionRole 2-6 | Force 0/1004-1006 (tabela completa: doc fonte §6)
codificacao: data empacotada (bit31) = [hora,dia,mes,0x80|(ano-2000)] | cor ^RRGGBB | coords server->exibicao X=400+x*0.1 Y=y*0.1 Z=550+z*0.1 | strings UTF-16
nomes de NPC/mob/mina = listas elements 38/57/79; titulos 169 (indices sELedit, §S23)

## §S22 SERVER: npcgen.data + formatos de mapa (fonte: sNPCedit, sMAPtool C++/CLI decompilados; doc sMAPtool-sNPCedit-detalhamento.md) [C-terceiros] (resolve parte de §Q6)
npcgen.data: i32 version | i32 nCreatureSet | i32 nResourceSet | [v>=10] i32 nDynamic, i32 nTrigger | corpo: creatures > resources > dynamics > triggers
 CreatureSet (71B v>=10 + grupos): spawn_mode(0 Ground 1 Free3D) nGroups pos[3f] rot[3f] spread[3f] tipo(Mob/NPC) i32 3bool i32 [v>=10 trigger +2 i32]
 CreatureGroup 60B (64B v>=11): id(=elements) amount respawn(unid [?]) + unknowns
 ResourceSet 42B (31B v<10): pos/spread 5f, nGroups, ..., trigger | ResourceGroup 20B: unk id respawn amount unk
 Dynamic 24B: id pos[3f] 3B trigger 1B | Trigger 199B: id, unk, nome[128] GBK, flags, inicio+fim {ano mes dia_semana(-1=todo) dia hora min}, duration
 trigger em set/dynamic = link p/ Trigger.id (agendamento de spawn por data/hora). sNPCedit promove v<10 -> v10 ao salvar
path.sev: i32 version | i32 n | path{ i32 unk(=3) i32 id i32 -1 i32 -1 | i32 nPts | pt 6f (pos+direcao) | i32 nSeg | seg 6f(2 pts controle) + i32 start i32 end f length }
.hmap: 513x513 float (z*513+x); altura_m = valor*800. Vem do client .t2bk: 256 blocos de 33x33 floats (zlib), sobreposicao de 1 amostra, /800
.t2bk: i32 ? | i32 versao | i32 count(256) | i32 ? | offsets[count] | bloco{ zone, gpos, f, pixel_size, 4 tamanhos, [v>5 i32 extra], hmap+tile2/3/4 zlib }. sMAPtool rebaixa v>5 -> 5 p/ server
.rmap: 6 i32 (Version ByteCount LineSize W H LineCount) + f + linhas de bits (da ultima p/ a 1a); bit 1 = celula transitavel [I]
.wmap: i32 version | 2f (w,h) | i32 n | superficie 5f (centro x,y, meia-largura x,y, altitude)
.t2mk: header 5 i32 + posicoes + blocos; sub-blocos zlib; semantica [?]
bsdata/*.dat: header 12B, entradas por tag (0x10002000/0x10003000 = grade), tabela de offsets no fim + N; sMAPtool converte 3000->2000 p/ server [I]
region.clt/precinct.clt -> .sev: sMAPtool parseia forma TEXTO UTF-16 dos .clt (entrada nao vista [Q]); §S17 descreve o .clt binario
zlib: blobs c/ header de 2B (pular p/ deflate cru)
nomes mob/NPC: npcdb.txt (UTF-16 id<TAB>nome); posicao do player lida da memoria do elementclient via offsets.txt (cadeia de ponteiros por build)
- [T] layout conferido byte a byte nos 49 npcgen.data do servidor 1.4.4 (config\, world\, aNN\, bNN\; versoes 5, 6 e 10): cabecalho v<=5 = 3 i32 (versao, nCriaturas, nRecursos); v6 +nObjetos; v>=7 +nGatilhos. CreatureSet 59 B (v<7) / 71 B: local i32, nGrupos, pos/dir/raio 3x3f, tipo i32, groupType i32, 3 bytes (initGen autoRevive validOnce), genId i32, [v>=7 gatilho, lifeTime, maxNum]. Grupo 60 B (v11 +refreshLower): id qtde renascer(s) mortes agressivo offAgua(f) offTerreno(f) faccao ajudante aceita 4 bytes caminho tipoCaminho velocidade tempoCorpo. ResourceSet 31 B (v<10) / 42 B (+dir1 dir2 rot bytes, gatilho, maxNum); item 20 B (tipo=80 em todos, id, renascer, qtde, altura f). Objeto 24 B (id pos 3f, dir1 dir2 rot, gatilho i32, escala byte). Gatilho 195 B (v7) / 199 B (+duracao): id gmId nome[128] GBK autoStart esperaInicio esperaFim 2 bytes inicio{ano mes(0-11) dia_semana dia hora min, -1 = qualquer} fim{...}.
- [C-terceiros] sNPCedit "Get Coordinates" (pasta "Editor NPC 1.5.5 v156", fonte decompilada junto): offsets.txt UTF-16 = blocos "Name=<build>" + linha com cadeia de ponteiros hex + 6 linhas hex (dirX dirY dirZ posX posY posZ). Leitura: acha o processo elementclient, p=0; para cada elo p = ler_i32(p + elo); depois floats em p+offset. 1.5.5 v156: cadeia 00E444A4 1C 34, dir 6C/70/74, pos 3C/40/44 (endereco absoluto, cliente 32 bits). Vale so para aquele build; 1.4.4 precisa achar a propria cadeia [?]. Preenche X/altura/Z e direcao do ponto selecionado.
- [C-terceiros] sNPCedit le nDinamicos/nGatilhos so se v>=10 (erra os v6 do 1.4.4, que ja tem nDinamicos) e grava v<11 como 10; quase todos os campos do grupo/gatilho como unknown_N. Nosso leitor (v5/v6/v10 conferidos) cobre mais. npcdb.txt dele = 12815 nomes PT-BR id<TAB>nome (nos tiramos do elements).
- [T] terreno do cliente (element\maps\<mapa>): <mapa>.trn2 "TRN2" v1: +8 largura f (8192), +12 altura f (11264), +16 lado do bloco f (64), +20 celulas/bloco (32), +0x38 linhas de pedacos (11), +0x3C colunas (8), +0x40 celulas/pedaco (512), +0x44 celula f (2 m). <mapa>_N.t2bk (N = linha*colunas+coluna+1, linha 0 = norte, z = +5632): "BKBT" v5, nBlocos 256, flags, offsets[256]; bloco 32 B = i32, i16 linha GLOBAL, i16 coluna GLOBAL, f, i32, 4 tamanhos; alturas = zlib (pular 2 B) 33x33 floats em METROS, linha cresce para o sul. Conferido: altura dos 12398 pontos "no chao" do npcgen bate com mediana 0,10 m (interpolado). Mundo inteiro carrega em 0,46 s. .t2mk/.t2lm/.ecwld ainda nao lidos. Minimapa: surfaces.pck surfaces\minimaps\<mapa>\LLCC.dds (DXT 256x256, peca de 512 m, LL = linha a partir do norte, CC = coluna a partir do oeste; world = 16 colunas x 23 linhas = 368) [T visual: neve sobre a area dos Duendes Glaciais]. Mapa pintado do jogo (tecla M): surfaces\maps\LLC.dds, 11 linhas x 8 colunas de 1 km (LL linha a partir do norte, C coluna), com nomes das regioes ja desenhados (PT no cliente BR); visao geral em surfaces\ingame\world.dds [T visual].
- [T] texturas do chao: <mapa>.trn2 +0x48 offset da lista de texturas (i32 n + GBK, "Textures\Maps\DDS...", no textures.pck), +0x50 offset da lista de nomes dos pedacos. <mapa>_N.t2mk "BKMT" v2: nAreas 16 (4x4 de 256 m, linha 0 = norte), nCamadas, ?, offsets[nAreas], camadas 28 B (textura global, ?, lado da mascara 256, ?, escala u, escala v, ?); area = nUsadas + (camada, offset absoluto, tamanho); mascara zlib (pular 2 B) 256x256 bytes (1 m/px), 1a camada = base sem mascara. Conferido visualmente (neve onde ha Duendes Glaciais).
- [T] luz do terreno: <mapa>_N.t2lm "ML2T" v2 (t2lm1 = noite): nAreas 16, lado 256, ?, tamanho, offsets[16] -> cada area = .dds 256x256 (1 m/px); o jogo multiplica a cor do chao x luz x 2 (sombras de arvores visiveis).
- [T] agua: servidor gamed\config\<mapa>\watermap\N.wmap = i32 versao, 2f lado (1024), i32 n, n x (centro x, z relativos ao centro do pedaco, meia-largura x, z, altura; mundo 1.4.4 = 216 m). watermap.conf: 8 x 11 pedacos de 1024 m.
- [T] modelos de monstro/NPC: file_model (.ecm, TEXTO "MOXTVersion: 21", SkinModelPath: ...SMD) -> .SMD binario "MOXBDMSA" v5 (osso .bon, .phy, acoes; NAO lista os .ski) -> o .ski de mesmo nome na pasta + os "AddiSkinCount/AddiSkinPath:" do .ecm (caminho relativo a Models\; ex.: besouros-rei 天牛王 nao tem .ski com o nome do .SMD, so o adicional; 1997/2009 .ecm de criaturas resolvem, os 12 restantes nao existem no models.pck). .ski "MOXBIKSA" v8/v9: 10 i32 (versao, nPele, nRigidas, 2 tipos, nTexturas, nMateriais, nOssos, ?, ?) + 60 B | [v9 nomes dos ossos] | texturas (i32 n + GBK, .dds na pasta ou "textures\) | materiais ("MATERIAL: " + 1 B + 17 floats + 1 B) | malha com pele: nome, textura, material, nV, nI, vertices 48 B (pos, 3 pesos, 4 ossos u8, normal, uv), indices u16 | rigida: nome, osso, textura, material, nV, nI, vertices 32 B (pos, normal, uv). 15355/15356 .ski do models.pck lidos ate o ultimo byte (o que sobra e vazio, v0).
- [T] construcoes: litmodels.pck litmodels\<mapa>\<pedaco>\litmodel_N.bmd (world.ecbsd "MOXBFDSB" lista "10\litmodel_219.bmd" e BSData\N.dat). .bmd "MOXB" v0x10000002: escala 3f, direcao 3f (padrao 0,0,1), cima 3f (0,1,0), posicao no mundo 3f, nMalhas; malha: versao (0x10000004), nome[64], textura[256] (building.pck), nV, nTri, vertices 24 B (pos local, cor BGRA com luz embutida, uv), indices u16 x3, normais, extras variaveis (proxima malha achada pela versao). v0x80000001: 1 byte + 0x10000002 depois da versao, resto 5 B adiante, malhas 0x10000005. 40079/40079 lidos. Luz: textura x cor x 2.
- [T] world.ecwld "ECWD" v12: arvores (.spt SpeedTree, 59 tipos), grama (.mox, 242), efeitos .gfx, sons .wav, alguns .ecm; ainda nao lido. Mapa pintado do jogo e minimapa: ver acima.
- [T] esqueleto .bon "MOXBEKSA" v5: +0x0C nOssos, +0x10 nJuntas, ossos a partir de 0x64: i32 n + nome GBK, i32 ?, i32 pai (-1 raiz), i32 ?, i32 nFilhos, matriz relativa ao pai 16f (vetor-linha), matriz inicial 16f, filhos[nFilhos]; depois as juntas. 6885/6885 lidos. Malha rigida do .ski (osso = indice; v9: indice na lista de nomes do .ski -> nome no .bon) fica no espaco do osso: posicao = v x (relativa x pai x ...). Ex.: caixa de correio (osso raiz gira -90 em X, modelo feito com Z para cima).
- [T] texturas do cenario (building.pck) e de modelos guardam muitas vezes outra coisa no alfa (brilho): so usar teste de alfa quando o alfa e "recorte" (quase so 0/255 com buracos). Usar sempre apagava faces de pilares de canion (objetos "flutuando").
- [T] gatilho do npcgen com AutoStart=1 e sem agenda = liga com o mapa (NPCs de cidade); com data/hora = evento; AutoStart=0 sem agenda = acionado por GM/script/fase de DG.
- [?] world.ecwld: tabela de tipos de arvore (+0x24 offset, 59 nomes .spt + folhas .dds) e grama (+0x28, 242 .mox); registros de 60 B com contadores; layout dos blocos com posicoes ainda nao fechado. Arvores = SpeedTree .spt (trees.pck), formato binario proprio.
- [T] "local" 0 = no chao, 1 = livre: no mundo 1.4.4 o valor 1 tem 562 dos 581 monstros de agua e 481 dos 524 de ar (inhabit_type 1/2).
- [T] tipo 0 = pontos de monstro, 1 = de NPC; mas o tipo 0 do mundo tem 615 NPC_ESSENCE (guardas etc.) -> trocar ID procura em todos os pontos de criatura. Item de coleta = MINE_ESSENCE (5198 no mundo). 5 IDs de NPC sem registro no elements (tipo 1).
- [T] renascer em segundos (comuns: 30, 10, 15, 2000; ha negativos -7/-1 [?]). Mundo 1.4.4: 16457 pontos (13784 monstro, 2673 NPC), 3054 coleta, 1501 objetos, 2682 gatilhos; config\npcgen.data e world\npcgen.data diferem.

## §S23 elements.data — visao do sELedit (doc analise_arquivos_pw.md §1) [C-terceiros] (complementa §S3/§S17)
header lido como i16 Version + i16 Signature (= u32 0x3000003C -> versao 60, assinatura 0x3000); bytes fixos antes de cada lista = "listOffset" do .cfg (timestamp entra ai) [I forte]
.cfg: linha1 = n listas | linha2 = indice da lista de conversa | por lista: nome / tamanho do offset ou AUTO / campos ; / tipos ;. Tipos: int16 int32 int64 float double byte:N string:N(GBK) wstring:N(UTF-16LE). Arquivo: configs\PW_*_v{Version}.cfg
listas especiais 20 e 100: tag 4B + tamanho N 4B + N bytes (+4B finais so na 20) = blocos de assinatura do exportador (§S17)
lista de conversa (padrao idx 58, = TALK_PROC): sem count, 1 blob. Dialog{ i32 id, nome[128] UTF-16, i32 nQ } Question{ i32 id, i32 control, i32 textLen(chars), texto, i32 nChoices } Choice{ i32 control, texto[132] UTF-16 }. sELedit acha o fim procurando "facedata\" (heuristica)
indices sELedit (0-based) = DATA_TYPE-1 de §S17 [I forte]: 3 armas 6 armaduras 9 adornos 38 monstros(campo 64 = aipolicy) 57 NPCs 79 minas 169 titulos; elements >=191 desloca +1 a partir de 58
.rules (conversao entre versoes): SETVERSION|n SETSIGNATURE|n REMOVELIST:i REPLACEOFFSET:i|hex REMOVEVALUE:lista:campo
join de EL: casa por campo 0 (ID): MISSING / novo / DIFFERENT, c/ backups e LOG.TXT
validacoes sELedit (indices hardcoded 1.3.6): soma de probabilidades=1 (armas/armaduras/adornos), skills invalidas em monstros, task overflow (listas 45/46 [?])
xref: configs\references.txt (lista;campo,campo)

- [T] food_mask (PET_ESSENCE) bits: 0 grama, 1 carne, 2 fungos, 3 frutas, 4 agua/oleo (v60: cavalos 29, lobos 26, caranguejos mecanicos 16; = tela do RAE).
- [T] inhabit_type valores usados v60: 0 terra (4383 monstros), 1 agua, 2 ar, 3 = anfibios (tartarugas, ra), 6 = Goshiki/espiritos; nomes 3..6 (terra+agua, terra+ar, agua+ar, todos) [I].
- [T] combined_services (NPC_ESSENCE) 32 bits com nomes do RAE conferidos nos NPCs v60: 4 criar cla, 6 correio, 7 leilao, 9/10 incubar/restaurar mascote, 11 resultado guerra territorial (teleportadoras), 14 refino (anciaos), 15 pintar roupa (tecelas), 23 reparar (ferreiros/alfaiates), 26 casamento; bits 2 e 8 sem uso. Bit 31 ligado = int negativo.
- [T] NPC_MAKE_SERVICE pages_N_id_goods_M = IDs de RECEITA (todas as versoes); NPC_SELL_SERVICE usa pages_N_goods_M_id (item) — mas em 22 cfgs antigos a loja tambem usa id_goods: o tipo depende do bloco.
- [T] file_model = "Models\..." .ecm; no models.pck o caminho e "models\..." minusculo (4628 .ecm no 1.4.4; 390/400 caminhos do elements achados).
- [T] Pontos de teleporte com nome: element\maps\world\world_targets.txt (UTF-16): ID<TAB>"nome"<TAB>mapa<TAB>"x,y,z"<TAB>regiao (61 no 1.4.4); IDs de id_to_discover (NPC) e targets_N_idTarget (NPC_TRANSMIT_SERVICE); servidor tem world_targets.sev.
- [T] RECIPE_ESSENCE materials_1..32 podem estar espalhados (ex. receita 27 usa slots 2, 7, 11); a janela de fabricar (interfaces\version01\produce_icon.xml: Btn_Set1..8 paginas, U_01..U_32 receitas, Item_01..08 + Txt_no materiais, Item_out/Txt_out, Txt_Gold, Txt_SkillName/Level) mostra 8 — nossa previa compacta em sequencia [I].
- [T] Habilidades de monstro (v60): 379 distintas usadas, nenhuma das 693 de classe (descricao de personagem tem "Nv. %d"/"Custo em MP"); ovos de mascote usam 48 (22 com descricao de personagem, sao skills de mascote). Seletor abre nas usadas pelo bloco.
- [T] skillstr.txt: "ID*10 \"nome\"" e "ID*10+1 \"descricao\"" (descricao pode ter varias linhas; 1789 habilidades com nome no 1.4.4).
## §S24 OPERACAO, SEGURANCA, ERROS (PDFs PWDevGroup + Protegendo Servidor + guia andrey.pw; doc analise_arquivos_pw.md §3-6) [W/M]
classes (mascara, confirmada em sELedit+aipolicy+tasks): BM=1 WIZ=2 PSY=4 VEN=8 BAR=16 AS=32 AR=64 CLE=128 SE=256 MY=512 | modernas: Duskblade 0x400 Stormbringer 0x800 Technician 0x1000 Edgerunner 0x2000 [C]
 nomes internos (§S12): SWORDSMAN=BM MAGE=WIZ NEC=PSY HAG=VEN ORGE=BAR ASN=AS ARCHER=AR ANGEL=CLE BLADE=SE GENIE=MY [I forte]
contas-molde de personagem inicial (userid): 16 BM 17 MY 18 SE 19 WIZ 20 PSY 23 VEN 24 BAR 27 AS 28 AR 31 CLE; editar e `./gamedbd gamesys.conf exportclsconfig` (=tabela clsconfig §S9) [W]
isec/osec: chaves CRUZADAS (iseckey de um lado = oseckey do outro); vale glinkd<->client e gdeliveryd<->authd(29200) [M]. Chaves de tutorial sao publicas: gerar proprias
exemplo GLinkServer1 1.5.5: version=10505 so_sndbuf/rcvbuf=12288 ibuffermax=16384 obuffermax=65536 accumulate=131072 compress=0 [M]
firewall: so 29000 publica; 22/80/8080(iweb) restritos por IP; MySQL fechado [M]
erros: "connect to GAuth failed" = isec/osec diverge gdeliveryd x authd | "versao antiga" = .data mal editado / timestamp / gshop preco 0 / nao salvou par server+client | erro entre servidores = chaves, libs, /etc/hosts -> logs/world2.err | Report Bug em cidade = dynamic object inexistente no client | logs: world2.err, logservice/logs/world2.formatlog (peer=IP), gsis01.log
config: pvp em gdeliveryd gamesys.conf | nomes so ASCII: name_charset/table_charset (gdeliveryd, gfactiond) | relogio do jogo = relogio do Linux | barra de chi: Octets property ultimos 8 hex = 8f010000 [W]
client: console `elementclient.exe game:cpw console:1` | logo login surfaces\logo.dds | radio configs\broadcast.ini | Molebox empacota exe
patcher skin: patcher\skin\mainuni.xml (<SkinApp>: Style Option SkinWindow{Button ComboBox ListBox ProgressBar Browser Slider Scroll Event} Command Art); layout por imagem-mapa (MapColor r,g,b) [C-SkinDesigner]
instalacao (guia): Ubuntu 14/Debian 8 + libs i386; editar /etc/hosts, glinkd gamesys.conf IP, table.xml (senha DB), iweb .config.jsp, register.php, patcher serverlist.txt; start/stop/chmod.sh [W]

## §S25 CONSTANTES EMBUTIDAS NOS BINARIOS + skillstr (fonte: editores gDeliveryd/gFactiond 1.4.8 e skill_translator; doc PW-ferramentas-gDeliveryd-gFactiond-skillstr.md) [C-terceiros]
regras de jogo HARDCODED no ELF (nao em conf): editaveis so por patch de offset (int32 LE). Offsets valem SO p/ o build 1.4.8 do autor [C]; nao conferidos vs ELF real [?] -> NAO usar no PKG144 sem achar por padrao/simbolo
gdeliveryd 1.4.8: apostas TW inicio/fim/final/entrega recompensa 0x25F658/668/678/688 (stride 16) | recompensa batalha das dinastias item 0x26CB58, qtd 0x415838 [?destoa], bind 0x26CB6C | correio tempo 0xF80D + 0xF7F9 (= valor-1; desalinhados = imediatos de instrucao x86 [I])
gfactiond 1.4.8: bloco contiguo 0x10C3BC-0x10C3EC: custo nivel 1-3 (BC C0 C4) | ? (C8 CC) | cargos mestres marechais majores capitaes membros (D0-E0) | membros nivel 1-3 (E4 E8 EC) | tempo no cla antes da TW 0x89D9E (imediato [I])
=> como achar no PKG144: gs/gdeliveryd/gfactiond nao-stripped c/ DWARF (§S5) -> procurar o simbolo/funcao em vez de offset fixo [I]
skillstr.txt (configs.pck): UTF-16; skill_translator gera skillstr.tab "ID<TAB>nome<TAB>descricao" ordenado; formato de entrada exato [?] (parser divide por aspas, 4 tokens por entrada)

## §S26 FORUM RAGEZONE (secao Perfect World > Ajuda; relatos de usuarios = [W] ate testar)
- [W] v531 elements.data (1.8.0): carga no gs falha em MONSTER_ESSENCE (descompressao MPPC "buffer overflow"). Relato: tamanho de struct de monstros/alguns blocos menor que o real; aumentar datasize resolve. Fonte: thread 1244530. -> conferir tamanhos no .cfg por versao.
- [W] Par cliente x servidor: cliente de outro servidor pode ter exe modificado; usar cliente do mesmo pacote do servidor. Fonte: thread 1273415 (post #9). Confirma a verificacao do par (ENGINE-KB §E5b).
- [W] VM Debian 11 (VirtualBox) com 8 versoes PW (1.3.1 a 1.5.5) em /opt; so uma roda por vez; todas usam o mesmo authd, logservice e SQL; troca por script. Fonte: "Multiple PW Servers in One VM" (56 respostas). Alternativa a considerar para a nossa VM (hoje Ubuntu 16.04).
- [W] "elements_13 => elements_10" (1240078): post lido sem dados tecnicos (opiniao sobre versoes).
- [W] Estrutura das tabelas do elements.data a partir do binario gs: `gdb` no ELF do gs, `file gs` e `ptype elementdataman` lista os arrays (equipment_addon_array, weapon_essence_array, ...); depois `ptype <NOME_DO_ARRAY>` por tabela. Fonte: thread 1239040 ("Structure for elements.data", 12 respostas). Confirma o caminho ja usado em ENGINE-KB §E3 (DWARF do gs).
- [W] Servidor PW 155 v156 no Ubuntu 22.04: bibliotecas incompativeis; sugestao de outro usuario: Ubuntu 14.04. Fonte: thread 1225694. Nossa VM esta planejada em 16.04 (ainda nao testada); 14.04 e alternativa se o 16.04 falhar.
- [W] elements.data -> XML e de volta: usuario converteu o arquivo inteiro (325 MB, 7,6 mi linhas), versionou no git, renomeou 500+ itens e recompilou; 1.5.5 cliente+servidor funcionaram. Bytes fora dos campos ("offset2", hex) regravados como estavam. Fonte: thread 1208384. Ideia para a engine: diff/historico por versao (ENGINE-KB §E8).
- [W] "Nao abre elements.data": causa comum = cfg corrompido/errado; cfg de versao do sEledit resolveu para outro usuario. Fonte: thread 1230744. Reforca verificar cfg por versao (ENGINE-KB §E3).
- [W] has_guid (sim/nao do item): item carrega identificador global unico. Na instancia do item: guid1 = timestamp unix, guid2 = ID unico. Fonte: thread 1120709 (post de avalanche3). Responde "Tem GUID" (issue #7).
- [W] material_need em armas/armaduras = quantidade de pedras por tentativa de refino; levelup_addon (armas #28, armaduras #37) liga ao calculo do refino. Fonte: thread 1120709. Nao verificado no nosso codigo.
- [W] Structs do gs v60 extraidas do binario (gdb, lista de classes WEAPON_ESSENCE, ARMOR_ESSENCE, MONSTER_ESSENCE, NPC_ESSENCE, FASHION_ESSENCE, ...). Fonte: thread 832723 ("Element struct ver 60", loko9988, 2012, anexo gs_AStruct_elem_lists_126.zip). Campos ja vistos: has_guid, proc_type, force_all_magic_defences (ARMOR), fly_mode (FLYSWORD), no_auto_fight/aggressive_mode/combined_switch (MONSTER). Confrontar com PW_1.4.4_v60.cfg antes de usar.
- [W] Structs do NPCGen (gs): NPCGENFILEHEADER(6/7), NPCGENFILEAIGEN, NPCGENFILEAREA(7), NPCGENFILECTRL(8), NPCGENFILEDYNOBJ(9/10), NPCGENFILERES, NPCGENFILERESAREA(6/7). Fonte: thread 830074 ("NPCGEN struct", 2012). Util para o editor de NPCGen (issue #19).
- [W] GShop (gshop.sev, server): _GSHOP_ITEM {local_id, main_type, sub_type, id, num, buy[4]{price, end_time, time, start_time, type, day, status, flag}, idGift, iGiftNum, iGiftTime, iLogPrice}. Fonte: thread 830080 (loko9988, 2012). Confrontar com §S19 (sGSHOPedit).
- [W] precinct.sev header: PRECINCTFILEHEADER {dwVersion, iNumPrecinct} (+ dwTimeStamp na v5). Fonte: thread 830075.
- [W] "AI struct PART 1/2" (thread 830083) = classes de runtime do aipolicy (ai_policy, ai_task...), nao o layout do arquivo. Usar §S18 para o aipolicy.data.
- [W] elements.data da PWI ("Epic"): so a versao e as contagens de itens das primeiras 40 listas vem "criptografadas"; sELedit++ contorna com arquivo elements.list.count (versao + total por lista). Fonte: thread 1163866 (Wrechid, post v2.3.5.0). Relevante p/ ler clientes oficiais.
- [W] Constantes de .pck por jogo Angelica (thread 1146288): PW/Zhuxian v0x20002 = HeadChk 0x4DCA23EF/0x56A089B7, TblChk 0xFDFDFEEE/0xF00DBEEF, NdxXor 0xA8937462/0xF1A43653 (= KEY_1/KEY_2 que ja usamos). Forsaken World, Ether Saga, Saint Seiya, Hot Dance Party e Swordsman (v0x20003) tem TblChk/NdxXor proprios (lista no thread). Sao constantes do formato de arquivo, nao chaves de rede.
- [W] tasks.data v118: estrutura pedida no forum (thread 991322), sem layout publicado no trecho lido; task_char = byte[]/string.
- [W] VM com varias versoes (Wrechid, thread 1213091, VirtualBox OVA Debian 11): 8 versoes (1.3.1..1.5.5) em /opt; so uma roda por vez; authd + logservice + SQL compartilhados; script "server" troca as libs por versao ("server start 131", "server status", "server pwAdmin start"); pwAdmin :8080/pwadmin, phpMyAdmin :99, register.php; contas valem em todas as versoes, personagens nao (gamedb/uniquenamed por versao). Mesma ideia: "PW 155 in VBOX Debian11" (1213018), "PW 173 in VBOX on Debian 12" (1268373), "PW 155 Out of the Box" (1154029).
- [W] Instalador Ubuntu 20.04 para 1.5.1/1.5.5 (crucifix, thread 1237480): installer.sh instala dependencias + MariaDB (credenciais geradas em arquivo no /root); depois editar (1) /etc/hosts com 127.0.0.1 para localhost, manager, aumanager, auth, audb, link1, link2, game1..game4, delivery, database, backup, gmserver; (2) table.xml (etc/; na 155 em home/authd) com url jdbc:mysql://localhost:3306/pw + usuario/senha; (3) register.php com o banco; subir: cd /home; ./chmod.sh; ./server start; para pouca RAM, reduzir a lista de mapas. -> base para o gerador de configs e a pagina de gerenciamento (ENGINE-KB §E8).
- [W] Lista de pacotes/ferramentas de servidor (thread 1221728, sticky): Debian/Ubuntu com PHP 5.6, MySQL 5.7, phpMyAdmin 4.9 e OpenJDK 6 para 1.1.6..1.6.0; script de instalacao 1.1.6..1.5.3 (Altruist); "Server Setup Simplified" (crucifix). MySQL 5.7 = o do Ubuntu 16.04 (nossa VM).
- [W] Significado de campos (comunidade, versoes 1.3.x, posicoes de outros editores -> casar pelo NOME antes de usar; thread 711347 "Explaination | Elements.data | Rows"):
  MONSTER: elemento 0 nenhum/1 metal/2 madeira/3 agua/4 fogo/5 terra; domavel = id do ovo de pet (= id_pet_egg_captured); agressivo (relato diz "0 = sim", conferir com aggressive_mode); hp, defesas por elemento, exp, sp, dinheiro medio +- variacao, precisao/evasao, ataque min/max
  NPC: tipo 3214 normal / 3216 guarda; pode ser morto; ponto seguro; cor do nome; servicos (vender, consertar, purificar, quests dar/receber, skills, cura, teleporte, deposito, desmontar, autenticar, resetar atributo, pets, vincular/destruir vinculado) = "0 nao, ID do servico = sim"
  pergaminho de quest: ate 8 pares (quest disparada, probabilidade), "usar automaticamente ao pegar" (= use_on_pick), pilha, preco, protecao
  moda: tipo (saia, sapato...), nivel, corantes necessarios, sexo 0 masc/1 fem; medicamento: nivel, recarga, hp/mp por tempo
  armas/armaduras/ornamentos: cor do nome 0 cinza/1 verde/2 dourado/3 laranja (campo ainda nao identificado no v60); armadura local 0 elmo+capa/1 saia/2 luvas/3 calca/4 bota/6 manto
  regra: o ULTIMO campo quase sempre e a protecao (trocar/cair etc.) = proc_type
- [W] Existe "v60 Documented config (sELedit)" (Souris, thread 850735): cfg v60 documentado (listas da 1.3.6 completas, novas parciais); anexo so logado; resposta aponta a thread 832723 para o resto.
- [W] Montagem servidor 1.4.4 (asgborges, thread 827248, "qualquer Linux"): apt-get openssh-server, openjdk-6-jre (ou -headless), apache2, mysql-server, php5, php5-mysql, php5-cgi, phpmyadmin, mc; descompactar na raiz; glinkd/gamesys.conf: [GLinkServer1..4] address = IP do servidor; /etc/hosts com o nome da maquina; chmod 777 + rodar /root/chmod.sh; editar /etc/gmopgen.xml, /etc/table.xml, register/config.php, register/status.php; banco: importar mysql_tables.sql e depois mysql_procedures.sql trocando o delimitador para $$. ATENCAO: Ubuntu 16.04 nao tem php5 nem openjdk-6 nos repositorios padrao -> 14.04 e mais compativel com esses guias [I].
- [W] Servidor em Docker: (a) zentinel, thread 1203681: 1.5.5 build 2567, um container por processo (authd, gacd, gamed, gamedbd, gdeliveryd, gfactiond, glinkd, logservice, uniquenamed) + MySQL separado, base i386/ubuntu:18.04, porta 29000 publicada, "make up-perfectworld", fuso horario por variavel (eventos dependem da hora), sem pwAdmin; (b) EndyBH, thread 1178719: imagem unica Ubuntu 14.04 com o pacote "PW 155 Out of the Box", portas 8080, 29000, 80, 3306; cliente aponta para IP:29000. -> alternativa a VM no Windows (Docker Desktop) para o nosso teste local.
- [W] Docker 1.4.1 em Ubuntu 22.04 (shadowvzs, thread 1223056): MariaDB, PHP 8, OpenJDK 11 com script install_into_docker.sh; Windows = Docker Desktop; "docker run -it -v C:\pasta:/home/pw -p 8080:8080 -p 29000:29000 -p 8081:80 -p 3306:3306 --privileged ubuntu:jammy"; depois "commit" do container vira imagem pronta; cliente aponta para o IP do host. => versoes antigas NAO exigem Ubuntu antigo se houver script de adaptacao [I].
- [W] WSL2 (thread 1253502): wsl --install; wsl --set-default-version 2; wsl --import <Nome> <pasta> <distro.tar.xz> --version 2 (CentOS 7, Debian 11, Debian 12); wsl -d <Nome> -u root. Outra via de servidor local no Windows.
- [W] "PW Server Ultimate Manager V6.9" (excelled, thread 1268311, bash, EN/PT-BR): painel de CPU/RAM/swap/disco + status de cada mapa com iniciar/parar; monitor de log colorido (verde/amarelo/vermelho); firewall iptables com lista de IPs liberados; trocar porta do glinkd (edita gamesys.conf + firewall); backup do gamedbd; limpeza/arquivo de logs; watchdog que reinicia se o gs01 cair; webhook do Discord; criar swap. -> lista de requisitos da nossa pagina de gerenciamento (ENGINE-KB §E8).
- [W] gs nao sobe por causa da gshop (erro -31 "falha ao iniciar dados do Bai Bao Ge", thread "Gs crash (gshop.data problem)"): (1) fim do periodo antes do inicio; (2) o mesmo item com periodos sobrepostos; (3) o mesmo item com periodo e tambem vendido sem periodo. -> validacoes do futuro editor de GShop (issue #18).
- [W] "Server network error"/"authd: could not find manager" na 1.4.4: faltam entradas no /etc/hosts (manager, aumanager, auth, audb, link*, game*, delivery, database, backup, gmserver) ou usuario/senha errados no table.xml (ha mais de uma copia, ex. /etc/ e /mauthd/etc/); npcgen e gshop invalidos tambem derrubam. "Client version too low" e gs offline apos trocar elements.data: copiar o MESMO arquivo para cliente e servidor (= verificacao do par).
- [W] Item some ao tirar do correio/enviar: mascara (mask) ou octetos (hex) do item errados.
- [W] 1.4.4: mudar a gshop no servidor impede o login do cliente ate atualizar o cliente (thread 827590, Romulan). Confirma checar o par gshop (ENGINE-KB §E5b).
- [W] proc_type por bit (uniq, thread 1121806, testado por ele): 0 nao cai ao morrer | 1 nao pode largar | 2 nao pode vender | 3 reservado (moda/montaria; sem efeito em outros) | 4 nao pode trocar | 5 nao pode refinar (?) | 6 vincula ao equipar | 7 ? | 8 some ao sair da area | 9 usado ao pegar | 10 cai ao morrer | 11 perde ao desconectar | 12 nao pode consertar | 13 ? | 14 nao pode guardar no armazem da conta. Exemplos: 19 = item de missao (sem queda/troca), 64 = vincula ao equipar, 32791 = vinculado a alma, 8 = montaria/voo, 23 = genio/ovo. => bits 0,1,2,4 sao RESTRICOES; nomes do enum do cliente (PROC_DROPPABLE etc.) enganam. Corrigido em BitMasks.cs [T: rotulos].
- [W] Clientes novos (PWBR/PWI/PWRU 172+) geram elements.data que o gs da versao 172 nao le (filtros de ID nao declarados no codigo-fonte). Fonte: thread 1238529. Sem resposta conclusiva no trecho lido.

## §S27 FORUM iwebGM / SuporteGM (BR, secao Editores; quase todo conteudo tecnico oculto: exige login e as vezes reagir/responder) [W]
- [W] Mapa versao do jogo -> versao do elements.data (lista de .cfg publicada, out/2026): 1.8.0=v531, 1.8.4=v585, 1.8.5=v591, 1.8.7=v600/v604, 1.8.8=v611/v614, 1.8.9=v618/v622, 1.9.0=v624. Ha tambem "Estrutura de tasks.data ate v247" (template 010 Editor; oculto).
- [W] RAE (Rody's Angelica Editor) e VENDIDO (licenca permanente R$ 690 / mensal R$ 44, topico 181) -> so referencia de UI/recursos, nunca copiar. Recursos anunciados: PW 1.1.6-2.0.0 (+FW, JD, ESO, HoTK); icones e imagens da loja; edicao em massa; importar/exportar/mesclar entre versoes; modificar valores em lote (recalcular, intervalo, aleatorio, multiplicar, substituir texto); busca/troca ilimitada; editor de probabilidades com calculo inteligente; gerador de octets (XML); editor visual de lojas e forjas; achar e corrigir erros (probabilidade errada, item inexistente); integracao com pwAdmin e sTASKedit; traducao automatica de itens selecionados (Bing/Yandex); desfazer ilimitado; protecao de elements.data.
- [W] Limite maximo de refino e uma constante no binario gs (alteravel por editor hexadecimal; topico 178). Bytes do padrao nao registrados aqui (regra do projeto).
- [W] pwAdmin em PT: configuracao em /pwserver/jakarta/webapps/pwAdmin/WEB-INF/.pwadminconf.jsp (topico 179).
- [T] Grupos que SEMPRE somam 1 (100%) no elements.data oficial v60 (todos os itens): armas/armaduras drop_/make_probability_socket#, probability_addon_num#, addons_#_probability_addon, rands_#_probability_rand (+uniques_# em arma); ornamentos addon_num/addons/rands; MONSTER_TYPE addons; monstro aggro_strategy_#, skill_hp75/50/25_#, probability_drop_num#, drop_matters_#_probability; RECIPE targets_#; TASKDICE task_lists_#; MINE materials_# e probability#; ENGRAVE addon_num_#/addons_#. Slot vazio pode ter chance 1 (normal). IDs de tabelas de rosto/config repetem IDs de itens (espacos separados). NPC_MAKE_SERVICE goods = IDs de receita; MONSTER_TYPE addons = MONSTER_ADDON.
- [W] Boss em horario: no NPCGen criar gatilho com "Start on schedule"/"Stop on schedule" (topico 279).

## §S28 ESPACOS DE ID + pasta do UDE/RAE net481 (D:/Perfect World/PW/Angelica/net481; so estudo, nada copiado) [T]
- [T] O jogo indexa registros do elements.data em tabelas de ID separadas; o mesmo numero pode existir em espacos diferentes. essence = itens + *_MAJOR/SUB_TYPE + MONSTER_TYPE/ESSENCE + NPC_*_SERVICE/NPC_ESSENCE (+EASY/FAST_PRODUCE_ITEM, EXTR_PROP_SKILL_CONFIG); addon = EQUIPMENT_ADDON + MONSTER_ADDON; recipe = RECIPE_*, ENGRAVE_*, RANDPROP_*, STONE_CHANGE_RECIPE*, HOME_FORMULAS_PRODUCE_*, LOANS_PRODUCE_*, LIB_PRODUCE_RECIPE; config = *_CONFIG (+ STORY_BOOK/BLACK_WHITE_LIST/SPECIAL_CAMP_REGION_MAJOR_TYPE); face = FACE_*, HIGH_POLY_*, COLORPICKER, CUSTOMIZEDATA; home = HOME_ITEM_*/HOME_*_ENTITY; talk = TALK_PROC. Regra em PerfectWorld/IdSpaces.cs, conferida contra os 406 blocos da lista do UDE (0 divergencias); base oficial v60: 0 IDs repetidos dentro de um espaco.
- [W] UDE = "Universal data Editor.exe" (.NET Framework 4.8.1, WPF+WinForms, com .pdb) = build do Rody's Angelica Editor. Nao abre aqui: nenhum erro no log do Windows (sai sozinho); arquivos com marca "baixado da internet"; tem verificacao de licenca/assinatura -> nao contornar (regra do projeto).
- [W] Menus do RAE (Lang/MainWindow.csv) = checklist de paridade: Arquivo (projeto .rae, abrir pasta do cliente, salvar versao cliente, protecao do elements, salvar item_color/item_ext_desc); Editar (desfazer/refazer + historico de acoes, busca avancada, substituir, copiar nome/modelo/drop/pagina, colar com dependencias e ID unico automatico, importar/exportar, procurar erros na lista/no arquivo todo, corrigir quest in/out de NPC pelo tasks.data, corrigir tipo de monstro invalido); Ferramentas (geradores de mascara de classe/proc_type/imunidade, mesclar elements/tasks/item_ext_desc, exportar NPCs p/ sNPCgen e monstros p/ aipolicy, alterar itens por ID); por item: traduzir (Ctrl+T), descricao (anexar texto, n. de tinturas), criar receitas/fashions/ovos de mascote/quest de presente/NPC a partir dos selecionados, adicionar/achar na loja; grade: mover/trocar campos, selecionar campos parecidos, achar iguais/diferentes, achar itens com este ID, limpar pagina; Navegar voltar/avancar com lista; Servidor (salvar e enviar elements.data, reiniciar GS, enviar tasks/gshopsev, comando livre, console putty).
- [W] Envio ao servidor no UDE: pasta ssh/ com pscp/plink/putty + script que mata o gs, espera 5 s e sobe "./gs gs01" com log (caminho /pw/gamed). Modelo p/ nossa automacao (WinSCP/ssh).
- [W] UserData do UDE: limites de campo (EQUIPMENT_ADDON num_params 1..5; MONSTER drop_times 0..20, skills_#_level e skill_hp#_#_level 0..15); lista de campos-chave do monstro p/ o resumo (nivel, vida, defesas/elementos, imunidade, exp, sp, dinheiro, precisao/esquiva, dano fis/mag, IA, skills por HP, drops, highest_frequency, invisible/uninvisible_lvl, no_auto_fight, drop_mines); mundo->pasta do mapa (1=world, 101=a01...) e tamanhos de mapa (world 8192x11264; instancias 512..3072); campos com "$" = qualquer numero.
- [W] Rules/*.rules do UDE: conversao entre versoes do elements com comandos SETVERSION, SETSIGNATURE, REMOVELIST, REPLACEOFFSET, REMOVEVALUE (v12=v7, v29=v27, v69=v63, v115..v191 = v87...).

## §S29 FORUM DYNDEV.RU (RU; secoes Documentacao 149 + Desenvolvimento 145 topicos, lidos 24/10; quase tudo aberto) [W]
- [W] npcgen.data: header v1 {iNumAIGen, iNumResArea}; v6 +iNumDynObj; v7+ +iNumNPCCtrl. AIGEN (por spawn): dwID, dwNum, iRefresh, dwDiedTimes, dwAggressive, fOffsetWater, fOffsetTrn, dwFaction, dwFacHelper, dwFacAccept, bNeedHelp, bDefFaction, bDefFacHelper, bDefFacAccept, iPathID, iLoopType, iSpeedFlag, iDeadTime. AREA: iType, iNumGen, vPos[3], vDir[3], vExts[3], iNPCType, iGroupType, bInitGen, bAutoRevive, bValidOnce, dwGenID; v7 +idCtrl, iLifeTime, iMaxNum. RES (recurso): iResType, idTemplate, dwRefreshTime, dwNumber, fHeiOff; RESAREA: vPos, fExtX, fExtZ, iNumRes, bInitGen, bAutoRevive, bValidOnce, dwGenID; v6 +dir[2], rad; v7 +idCtrl, iMaxNum. DYNOBJ: dwDynObjID, vPos, dir[2], rad; v9 +idController; v10 +scale. CTRL (gatilho): id, iControllerID, szName[128], bActived, iWaitTime, iStopTime, bActiveTimeInvalid, bStopTimeInvalid, ActiveTime, StopTime; v8 +iActiveTimeRange.
- [W] gshop item: local_id, main_type, sub_type, id, num, buy[4]{price, end_time, time, start_time, type, day, status, flag}, idGift, iGiftNum, iGiftTime, iLogPrice. precinct.sev header {dwVersion, iNumPrecinct}; v5 +dwTimeStamp. precinct.clt: id instancia, id zona, n. linhas de borda, n. NPC/ligacoes, som ambiente (0 nao; 1-4 dia/noite), musicas, instancia de renascer, segundos p/ expulsar quem sai do grupo (+liga), tag e grupo do world_targets.
- [W] Monstro (por nome do campo v60): id_strategy = tipo de combate (0 corpo a corpo, 1 distancia parado, 2 distancia recuando, 3 combo, 4 nao se move); show_level = forca exibida (3 = muito forte); attack_speed = segundos entre golpes (0.2 = 5 por s); aggressive_mode 0 passivo / 1 agressivo; inhabit_type 0 terra, 1 agua, 2 ar; immune_type bits fisico 1, metal 2, madeira 4, agua 8, fogo 16, terra 32, atordoamento 64..., tudo = 16383; probability_drop_num0..3 = chance de cair 0..3 itens; drop_times = quantas rolagens. Aplicado em PerfectWorld/FieldValues.cs.
- [W] NPC: mascara de funcoes (sELedit linha 38) - bit 1048576 refinar/transferir genio, 1073741824 banco sem senha, -1 = todas. Mascara de slot de equipamento = 2^slot (slot via comando d_uidebug no cliente). Chance de 4 furos so precisa no elements do SERVIDOR.
- [W] gs.conf [World_isXX] limit= nofly, nomount, no-bind, noduel, nothrow, height-limit, lowjump, clear-ap, allow-root, no-multi-exp, gm-free, use-save-point (renasce onde entrou), anti-cheat, commondata. instance.txt (configs.pck) precisa bater com gs.conf, senao o cliente fecha ao entrar na instancia.
- [W] Servidor 1.4.x em VPS (guia antigo, Ubuntu 10.04/12.10 x32): apt openjdk-6-jre apache2 mysql-server php5 php5-mysql php5-cgi phpmyadmin (+tofrodos unzip mc openssh-server); importar o .sql; trocar a senha do MySQL em /etc/table.xml, iweb .config.jsp, register.php; editar glinkd/gamesys.conf, mauthd/etc/table.xml, /etc/gmopgen.xml; chmod.sh e start.sh em /home; .data do servidor em /home/gamed/config. Ferramentas: PuTTY (terminal) + WinSCP (envio).
- [W] Debug: ptemplate.conf [GENERAL] debug_command_mode = active -> comandos d_c2scmd (ex.: 2000 +1 nivel, 10800 ID cria item, 10802 ID qtd 0 0 cria monstro, 8900 ID teleporta ao monstro/NPC, 10852 mostra IDs, 2002 8510 120 reinicia em 2 min). Valem para qualquer personagem -> so em servidor de teste.
- [W] gamedbd: ./gamedbd gamesys.conf listrolebrief | listuserbrief | listfaction | listfactionuser | listcity | query roleid | tablestat | exportclsconfig ... Backup: copiar /home/gamedbd e /home/uniquenamed + mysqldump, tar com data (cron).
- [W] configs.pck: badwords, broadcast, buff_str, console_cmd, coord_data (bordas de mapas), element_client.cfg (windows_title), fixed_msg, guide_area/feature, iconsound, instance, item_desc, item_ext_desc ("ID ^cor texto", aspas obrigatorias), loginpage.ini, petwords, rare_item.conf (anuncio em vermelho), scenectrl.ini, server_error, skillstr, task_err. interfaces.pck: desde 1.4.4 a UI usa XML em version01 (inventory_m/f.xml etc.).
- [W] Editores citados: sELedit Rody66 Edition (origem do RAE: substituir com faixas e filtro, checar ID unico, Ctrl+S com .bak, exportar/importar item pelo n. na lista, abrir por argumento, Favoritos, busca no pwdatabase, busca global por tudo (modelo, drop), busca ciclica, incrementar valor (IDs em sequencia), valor = outro campo (Name := ID), maiusculas/inteiro/Replace, copiar/colar modelo/drop/campos escolhidos com Ctrl+Shift+C/V, mover itens cima/baixo/topo/fim, ID unico automatico ao clonar a partir do ultimo ID ou 30001); Logic replace do sELedit (lista/item/campo + antigo/novo + recalculo aritmetico); sNPCedit (captura de coordenadas do cliente pelo offset); PathEditor (mapa com caminhos, captura de coordenadas); UIEdit (WYSIWYG da interface, codigo aberto); IconListEditor; Editor de AI deluxe (codigo aberto); npcgen jolan (adicionar em massa com coordenadas aleatorias, achar monstro no elements, ir ao gatilho com duplo clique); TaskEditor (XP/SP/dinheiro de todas as quests de uma vez, localizar quests com outro tasks.data); Tasks Level Changer (nivel em massa); gsconform1 (patch p/ gs ler elements de cliente).

## §S30 SERVIDOR NA PRATICA (dyndev.ru secao Servidor 98 topicos + RageZone Showcase 41 topicos, lidos 24/10) [W]
- [W] Montagens antigas: Debian 6 / Ubuntu 10-12 32 bits (ou x64 + ia32-libs), apache2 + mysql-server + php5 (+php5-mysql, phpmyadmin) + Java 6 (iweb) + tofrodos (fromdos nos .sh). Banco: importar tabelas + procedures (.sql; no phpMyAdmin trocar ";" por "$$" nas procedures). Senha do MySQL em /etc/table.xml, authd/etc/table.xml (e copia do servidor). IP em glinkd/gamesys.conf (trocar 0.0.0.0). Scripts start/stop/kill (fromdos + chmod). Versoes prontas como imagem VirtualBox (.vdi Debian) e coLinux; rede da VM: se eth0 vira eth1, copiar o MAC em /etc/udev/rules.d/70-persistent-net.rules.
- [W] Montagem 1.4.4 "pwserver" (Sem): tudo em /pwserver (sem /home), banco "pwserver", 3 lugares de senha, 1 so IP no gamesys.conf.
- [W] Operacao: gamedbd/gamesys.conf delete_timeout = segundos ate apagar personagem (604800 = 7 dias). Vaipar personagens = parar servidor e apagar gamedbd/dbhomewdb + uniquenamed/unamewdb; contas = recriar banco (exige vaipar personagens). Itens dos jogadores em gamedbd/dbhomewdb/dbdata (equipment, inventory, storehouse). gamedbd "read <tabela> <roleid>" devolve o registro bruto (mesma estrutura de GetRoleBase). Backup = copiar gamedbd + uniquenamed + mysqldump (cron). Reerguer o mundo: script no cron a cada 5 min checa se o gs caiu. Instancias e TW podem ser ligadas/desligadas por script (cron) p/ eventos. Firewall: iptables liberando so as portas do jogo/ssh/web e DROP no resto.
- [M] Instalador moderno (RageZone "Universal Installer v2", 2025): app Windows (WPF) com IP do servidor + usuario/senha SSH + versao (ex. 1.5.5) + senha do banco (em branco = gera) -> "Begin Installation" roda tudo por SSH (apt, i386, MySQL, PHP, painel Laravel) mostrando o log, e "Install PW-Panel". MODELO p/ o nosso #27 (usuario so informa o acesso; o software faz o resto).
- [M] Paineis: pwAdmin remix (contas, personagens com local/itens/propriedades, configuracao, controle: lista de servicos logservice/authd/uniquenamed/gacd/gfactiond/gdeliveryd/glinkd/gamedbd/gs com estado online/offline + iniciar/backup, mapas online/disponiveis com iniciar/parar, memoria). "Online Editors + DX11" (2026): painel web com DB/web/jogo em maquinas separadas, varios servidores, editor de elements + loja em grade por pagina (p1..p8, "Shop grid"/"Raw fields", Generate & Apply, Revert all), enviar item por correio com gerador de octetos (nao quebra item), agendador de mensagens/abrir mapas, logs, banimento por IP/HWID, deteccao de cfg.
- [M] Ferramentas vistas: visualizador 3D (Rust/Bevy) le direto do .pck v2/v3 (+.pkx): SKI 8/9/100/101, BON, STCK v1/v2, SPHY, ECM, DDS/TGA, nomes GBK; MorzEditor + MorzE NPCGen Editor (print do usuario): grade de spawns com X/Y/Z, direcao, dispersao, botao "inserir coordenadas do jogador" (le do cliente em execucao) e grupo com quantidade/respawn/agressao/velocidade/mortes/tempo de vida/caminho/tipo de ciclo.

## §Z RESOLVIDOS (use SEMPRE estes; ignore versoes antigas)
- lancamento: jan/2006 (nao 2005)
- daemons: 9 servicos incl. gacd (=ANTI-CHEAT); authd=Java (nao nativo); clsconfig=TABELA do gamedbd (nao arquivo solto); gamesys.conf existe em quase todos
- banco: SQL = contas/perm/cash/bans/sessao; gamedbd = personagem. MSSQL era so em guias antigos; authd suporta ambos
- storage: BDB|CDB|WDB por compilacao; PKG144=WDB (nao BDB)
- gshop* e gshopsev* NAO sao espelho byte a byte
- opcodes 0xD49/0x1F43 = rpcs GetUserRoles/GetRoleData
- version em glinkd e HEX (10404=1.4.4); numeros entre parenteses = escala de build de dados
- MPPC confirmado no codigo; wallow=antivicio; tag=100+n/200+n; base_path=a##/b##
- numero de racas/classes: 3 camadas (exe+xml+mascara)
- tabela de versoes: nomes/anos regionais
- 1.4.4 "8 classes" da tabela [P] vs 10 no PKG144 = pacote adaptado
- client do zip = 1.5.2 build 2457 (GAME_VERSION 0x10502), linha PWI [I]; nome "153" nao confirmado
- Challenge.edition = hex(ELEMENTDATA_VERSION, task_templ_version, gshop_ts1, gshop_ts2)
- elements.data: version+timestamp+listas (u32 count + structs brutos) em ordem fixa; tasks.data: magic 0x93858361
- GM C2S = 200..221; opcodes GameDataSend=34, ACQuestion=5031, ACAnswer=5032
- .pck: conteudo NAO criptografado; XOR so em offset da tabela e tamanho de entrada; zlib; v2 (entrada 276B, offset u32) e v3 (288B, offset i64); .pkx = continuacao logica a partir de 0x7FFFFF00; detalhes §S3 [C-uPCK]

- elements header: u32 0x3000003C = i16 versao 60 + i16 assinatura 0x3000; 2o u32 = timestamp, lido como listOffset do .cfg (§S23)
- classes: mascara BM1 WIZ2 PSY4 VEN8 BAR16 AS32 AR64 CLE128 SE256 MY512; nomes internos de §S12 mapeados em §S24
- indice de lista sELedit = DATA_TYPE-1; monstro campo 64 = id aipolicy (§S23 §S18)
- glinkd version=10505 = 1.5.5 (confirma esquema hex §S5)
- formatos .rmap .wmap .hmap path.sev .t2bk npcgen.data: agora em §S22 (era §Q6)
- edition na pratica: par real PKG144 tinha gshop.data do cliente (ts fev/2017, 498 itens) != gshopsev.data do servidor (ts jan/2012, 507) -> login recusado; elements/tasks/aipolicy iguais [T]
- nome de quest no tasks.data = UTF-16 XOR (ushort)ID (§S21) [T]; vale para TODOS os textos da quest (descricao, dialogos, opcoes) [T]
- cor oficial do nome do item: configs/item_color.txt (indice) + tabela l_aNameCols do cliente: 0 ffffff 1 8080ff 2 ffdc50 3 aa32ff 4 ff6000 5 ffffff 6 b0b0b0 7 00ffae 8 ff0000 [C][A]
- configs/item_ext_desc.txt: UTF-16, "ID<TAB>\"texto\"", cores ^RRGGBB, \r literal = quebra (PKG144: 9.681 descricoes) [A]

## §Q ABERTOS (proximos passos sugeridos, por valor)
1 [PARCIAL: lido via uPCK, §S3] falta: confirmar vs AFilePackage oficial/autoangel-rs; chaves por jogo/regiao; campos ignorados da entrada v3; rodape v3 completo; significado do int16 de versao
2 [parcial: §S17] conteudo dos structs de ExpTypes.h por lista vs declaracoes XML de terceiros
3 formato dos blobs de GRoleStatus/GRoleInventory.data: cgame/gs/player.cpp (task blobs: ver limites em §S17)
4 framing de pacote/handshake/selecao de algo: share/io, pwi_oog, pw-packet-stream-reader
5 glinkd/patch, replacestr.pl, gamedbd.conv, a.txt, b.txt, opt/admin.txt, xo.sev; porta 29712 do gacd; formato Questions*.data
6 formatos .rmap .dhmap .wmap .octr map.bht; trn2 (.t2bk .t2mk .t2lm .t2lm1 .t2hc) [ecwld/ecbsd/region/precinct: §S17]
7 structs de cla/correio/amigos/leilao; valores de GRoleForbid.type; campos [?] (pp level2 mask proctype edition)
8 corpos das procedures (recordonline recordoffline addGM usecash); table.xml gacd.xml gmopgen.xml config.xml
9 comparar gs.conf/gmserver*.conf; release_20130703 vs 2015; cskill; game_qq; PWSL
10 build 2567 vs 156; mapa versao->expansao; client usa mesmos a##/b## ; grasses.pck; 1.4.2 vs 1.4.4 p/ XML UI
11 forum nao lidos: compat client x CPW, "Замена iweb"(isec/osec), gamedbd cai/fix, debug page 2, Debug поиск, ligar/desligar zonas restritas, copia de dungeons, lista skills/bonus de item/tasks.data edit

## §R FONTES (curtas)
forum: mmorpg-devs.ru/forums/perfect-world.15/ (docs 2010-2026; PW Документация, PW Сервер)
ragezone: PW 1.5.1 Server Setup; PW 1.4.4 Basic Setup; PURE PW 1.5.3 files; client structure (rbb138); elitepvpers guia 2007
codigo/doc: github Smertig/autoangel-rs (docs/ bloqueado), sourceforge pw-tools (sPCK), lugovov/pw-elements-data-declarations, shadowvzs/pwAdmin (+DeepWiki), SealinRepo/perfectworld (Docker 1.5.5), Kn1fe/*, emudevs/sGSHOPedit, pdaniil/ProjectAtlas, packagist ham/pw-api
geral: Wikipedia PW/Jade Dynasty/PW company, StrategyWiki, comunicados PWE/Arc 2010-2011
docs de estudo originais (detalhe completo; fora do repo, resumidos nas secoes indicadas): AIPolicyEditor-detalhamento.md (§S18) pw-gshop-ui.md (§S19-20) pw-tasks-editor.md (§S21) sMAPtool-sNPCedit-detalhamento.md (§S22) analise_arquivos_pw.md (§S23-24) PW-ferramentas-gDeliveryd-gFactiond-skillstr.md (§S25)
pacotes locais lidos: "PW Server 1.4.4 v60" (zip terceiros) ; "source_server_153" (zip CVS) ; "source_client_153" (client 1.5.2, §S17) ; "source_uPCK" (C#, extrator/empacotador .pck, §S3)
13 [novo] aipolicy: semantica c_29-40, o_36-79 (o_62 sem classe), t_14-18, bRun/bAttackValid | gshop: day/flags/control_type | tasks: ordem binaria do ATaskTempl (tasks.dll ou CTaskTemplEditor do SRC-CLI) | npcgen: unknowns, unidade de respawn | .clt texto do sMAPtool, t2mk, bsdata | tags UI nao lidas pelo UIEdit | offsets gdeliveryd/gfactiond no build do PKG144 (achar via DWARF) | formato real do skillstr.txt
12 [novo] formato .pck lido via uPCK (§S3); AFilePackage original (SDK Angelica) nao visto | structs dos 397 S2C/180 C2S | serverlist.sev binario | CBin/Interfaces vs interfaces.pck | ClientSimulator/editores nao analisados | conflito enum Security (Null/Random) | relacao GAME_BUILD 2457 x build 2567/156
