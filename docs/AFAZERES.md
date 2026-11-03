# AFAZERES — mapa de etapas (ler 1o; curto de proposito)
# legenda: [x] feito [~] parcial [ ] falta [>] em andamento | detalhes: docs/kb/ENGINE-KB.md (§Exx)
# PUBLICO (GPL-3.0): tarefas = issues #1-#32 no GitHub (gh CLI logado, conta ipasetto); roadmap p/ comunidade em docs/ROADMAP.md (manter em sincronia c/ este arquivo); guia em CONTRIBUTING.md
# PARIDADE: checklist completo das funcoes do Rody em docs/PARIDADE-RODY.md (ver antes de escolher a proxima tarefa)
# META: app fazer 100% do Rody's Angelica Editor, codigo nosso, interface parecida (§E8)

## FASE 1 — base
[x] elements.data leitura ~98,6% (§E3)            [ ] cauda Territory War ~1,4% (§Q1)
[x] elements.data gravacao byte-identica (§E3)    [x] .pck/.pkx nativo + icones (§E4)
[x] verificacao par cliente x servidor (§E5b)     [x] tipos de campo + vinculos (§E3b)
[x] docs publicas: README, ARQUITETURA, KBs, skill

## FASE 2 — paridade Rody (Elements)
[x] lista plana: bloco | filtro tipo | busca | ID+icone+nome colorido | ordenar
[x] detalhe: Valores (coluna Vinculo) | Vinculos (quem usa) | tooltip c/ drops
[x] tela Rody: cabecalho escuro ID+nome (cor oficial item_color) | sub-abas por grupo (Todos/Basico/.../Queda/Habilidades/Modelo) | col. ∞ vinculos | ◀ ▶ | Descricao e cor (item_ext_desc) | Buscar por campo | barra de status
[~] editores especiais: [x] mascaras (#8) [x] bool como checkbox (#7, campos conhecidos) [x] probabilidades (ChanceEditor) | [ ] addons, cor
[x] editar descricao e cor do item (ItemTexts, grava no configs.pck; #14)
[x] rotulos PT melhores p/ campos (#9)
[x] nomes de skill (skillstr.txt) e de quest nos vinculos (falta clicar e ir a quest)
[x] icones de monstro/NPC (miniaturas do modelo 3D; #11)
[x] converter elements entre versoes (Salvar em outra versao) | [x] traduzir com outro elements.data | [ ] importar/exportar item avulso entre arquivos

[x] desfazer/refazer + historico (#1) | [x] procurar erros + corrigir probabilidades (#4) | [x] alterar campo em lote (somar/multiplicar/sequencia/aleatorio/substituir)
[x] tooltip da lista estilo RAE + vinculos por espaco de ID | [x] tipos/subtipos com vinculos e duplo clique

## FASE 3 — outros arquivos
[x] tasks.data v93 completo: 21.777 quests, 238 campos, textos, dialogos; leitura/gravacao byte-identica (§E3c)
[x] aba Quests: arvore + Dados + Textos + Dialogos
[ ] quests: editar premios/itens/monstros pedidos (hoje preservados como bytes) | criar/clonar/excluir quest | [x] outras versoes na aba Quests (TaskLayout: v128/129/187/198 testadas; campos com chaves m_..., sem rotulo PT)
[x] traduzir a partir de outro cliente (elements + quests + falas do aipolicy + pacotes de interface/ajuda quando o jogo e o mesmo programa) | [x] 31/10 textos das janelas .xml do interfaces.pck por janela+controle, qualquer versao (copia 155<-144: 5.498 textos) | [ ] usuario: testar no jogo uma janela traduzida
[x] GShop (cliente+servidor juntos, tela igual a do jogo; falta validar no jogo e desfazer)
[ ] AI Policy (PW-KB §S18) | [x] NPCGen: ler/gravar identico, aba com pontos, editar qtde/renascer/monstro, Mudar ID (PW-KB §S22) | [x] Mundo 3D (terreno por altura, camera livre, marcadores, botao direito: copiar/novo ponto/mover) | [x] modelos de monstro/NPC, construcoes, texturas e luz do chao, agua, mapa pintado (M) | [x] arvores (.ecwld + nossa versao do SpeedTree: tronco, galhos, folhas viradas p/ camera) | [x] frondes em lamina | [x] frondes em extrusao + cores pelos materiais do .spt | [x] agua do cliente (bsdata: cor; sem plano onde o cliente nao tem, ex. lava da a09) | [ ] conferir arvores com prints do jogo (pinheiros escuros em leque, copas muito densas?) | [ ] grama, animacao | [ ] objetos e gatilhos (hoje so preservados)
[x] extrator .pck nativo (aba Extrator PCK) | [x] gravar .pck v2/v3 (PckWriter; #13) | [x] gravar pacote dividido .pck+.pkx (01/11, so a ultima parte muda)

## QUESTS NO MUNDO 3D (spec docs/QUESTS-3D.md)
[x] etapa 1: tasks.data completo em todas as versoes (TaskLayout) | [>] etapa 2: telas no Mundo 3D
[x] aba Quests: todas as versoes, rotulos/dicas PT+en/ru/zh | [x] Mundo 3D: filtro por tipo, barra do NPC, ver quests do NPC
[~] criar quest tipo 1 (recado) pelo Mundo 3D + dar quest existente (falta testar no app e no jogo) | [x] requisitos/premios por lista de opcoes + "outro campo" | [x] brilho do "Salvar e enviar ao servidor" (tudo, sessao) | [~] tipo 2 ir ate um lugar com etapas (em ordem/qualquer ordem/basta uma, varias areas; testado no arquivo nas 4 versoes, falta testar no app e no jogo) | [x] etapas matar monstros, obter itens, entregar itens a um NPC, esperar (31/10; testado no arquivo nas 4 versoes + captura; falta testar no app e no jogo) | [ ] demais objetivos (ver docs/MUNDO-3D-EDITOR.md) | [x] editar e excluir quest pelo painel do NPC (so quests no formato do assistente; Ctrl+Z; testado no arquivo nas 4 versoes, falta testar no app e no jogo)
[!] recados do tipo 1 criados ANTES de 29/10 tinham a opcao aceitar/concluir apontando para a quest modelo: recriar
[ ] depois da 1a etapa das quests: arvores, grama e objetos no Mundo 3D (.ecwld/.spt) em todas as versoes (usuario 29/10)
[~] editor de cenario (01/11): .ecwld regrava identico em 332/332 mapas; efeitos/sons/bichos editaveis | [~] construcoes: escolher e mover no 3D (pendente ate salvar); [~] colisao do servidor: map.bht lido/regravado igual 68/68 e deslocar brushes pronto (BhtFile); [x] achar os brushes de cada construcao (caixa igual aos brushes do .bmd) e gravar no Salvar (.bmd + gemeas + map.bht + ornamento; testado em memoria a05/a01/world, falta testar no jogo); [ ] movemap/airmap (caminho dos NPCs) | [x] gravar construcao (.bmd + .ecbsd + map.bht) | [x] 01/11 Caminhos e areas: area x patrulha, desenhar/mover/apagar caminho, salvar path.sev (falta testar no jogo) | [x] ida e volta x circulo (PathLoop) no painel | [x] 01/11 patrulha de voador/nadador com altura | [x] 01/11 ANUNCIO reaproveitavel (AiAnnounce: ao morrer / ao entrar em combate; clona a politica; canal por anuncio; Mundo 3D -> Anuncios) [ ] testar no jogo | [ ] anuncio em evento com horario | [ ] chance de nascer (fase 2: sorteador + controlador, usa o anuncio) | [ ] Objetos do mundo: arvores (selecionar/mover/apagar/plantar + colisao de arvore no map.bht), pedras e objetos, muro, pincel de grama/flores, pincel de textura do chao | [ ] girar construcao | [ ] paleta de edicao: plantar arvores/pedras, pincel de terreno (ladrilhos quadrados, usuario 01/11) | [ ] modelos EC e arvores | [ ] plantar arvores/grama

## FASE 4 — engine
[x] corrigir automaticamente o par (Verificacao > Corrigir, --fix-pair; #26)
[ ] racas iniciais (exe + createrace.xml + mascara; PW-KB §S12) | jogadores na TW (§S25)
[x] 01/11 area Interfaces na barra lateral (acima do Extrator PCK, que so extrai) | [x] janelas do jogo dentro da engine: aba Interface (ver/editar posicao, tamanho, texto; grava no .pck) | [x] 31/10 (estudo PIE 2.0 + AUIEditor): lista de controles, dica, esconder, busca nos textos, .dcf da janela | [ ] aviso de texto maior que o controle | [ ] selecao multipla + alinhar | [ ] aba XML com Aplicar | [ ] modo .dcf (posicao na tela) | [ ] adicionar/copiar controle (ENGINE-KB §E9b)
[x] 31/10 arquivos de interface: exportar/importar pasta com conferencia, trocar com outro cliente (tudo ou parte + imagens), telas de carregamento (classica e 1600), 3 erros do editor corrigidos (texto com < > nas aspas, <text> minusculo, SetRect) | [ ] usuario: testar no jogo uma tela de carregamento trocada, uma janela importada e uma troca parcial | [x] backup do .pck no armazem (BackupStore.Adopt corrigido)
[ ] futuro (usuario 31/10): trocar o fundo ANIMADO do login — cena maps\login + camera (SceneCtrl.ini / scenectrl.lua), efeito gfx\程序联入\登录界面1.gfx, background.jpg, CG .wmv no 1.7; tudo mapeado em PW-KB §S20e
[ ] PLUGINS de evento (docs/PLUGINS.md: programa C# no servidor + tela Plugins acima do Extrator; monstro raro, chefe errante, mercador/colheita/rodizio) | [ ] deploy de servidor | editor de mapa (PW-KB §S22) | skills

- [>] 02/11 Servidor local POR VERSAO (prioridade do usuario): codigo pronto (1 distro por pasta, sem instalar versao por cima, so 1 no ar). Distro antiga PWServer155 excluida a pedido. FALTA: usuario instalar 155 (D:\PWServer155, pacote pw_server_162-155) e 144 (D:\PWServer144v60) pela aba Servidor e reportar erros; itens do Testes.txt: 1 (Verificacao com as pastas do Servidor: feito ao abrir cliente), 2/8 (Corrigir resolver tudo, ate sem servidor), 3 (favoritos/3D do 155 aparecendo no 144: configuracoes globais), 4 gshop 144, 5 plugin padrao desativado, 6 botao "zerar" no menu Arquivo, 7 campos de quest sem dado escondidos e icone principal so onde existe.
- [~] 02/11 noite: 144 instalado pelo usuario sem erros [T]. Feito no codigo (falta testar na tela): pergunta de configurar servidor na 1a abertura, janela Instalar com disco/memoria, WSL ausente (instalar/Verificacao/Corrigir), varios servidores da mesma versao, Verificacao+Corrigir automaticos apos instalar.
- [~] 02/11: janela "Servidor e pastas" pronta (captura ok); 144 corrigido (envio com o cliente certo, gs no ar, loja ts 1793026156) - falta o usuario confirmar que entra no mundo.
- [ ] Servidor por IP/SSH (VM, outro PC, hospedagem): tipo "Outro Linux" ja existe em "Servidor e pastas" (so arquivos); falta conectar com ssh.exe/scp.exe do Windows (IP, porta, usuario, CHAVE - nunca senha gravada): Enviar = scp dos arquivos do envio, Iniciar/Parar/Estado/Administrar = ssh + pwctl (levar o pwctl na 1a conexao; conferir se funciona num servidor nao instalado pelo programa).
- [>] 02/11 crash ao entrar no mundo no 144 (cliente PW 144 BR): servidor ok; suspeita = dados de cliente (elements BR convertido v61->v60, configs.pck BR) x exe v60; teste proposto = usar o "client files" do pacote 1.4.4 (mesmo exe) com backup.
- [ ] Verificacao ler o log do CLIENTE (element\logs\EC.log + ec_build*.dmp): mostrar crash do jogo (excecao, modulo+offset, ultimo arquivo que faltou) e avisar quando o cliente jogado nao e o aberto no editor (02/11: servidor recebeu "Perfect World 144" e o jogo era "PW 144 BR").
## ONDE PARAMOS (26/10/2026, painel do servidor) - RETOMAR AQUI
Pedido do usuario: "verificacao de versao" + "painel para administrar o servidor num form (como o iweb)".
[x] Verificacao: versao do elements compilada no elementclient.exe x gs x arquivos (ProgramVersion.cs)
[x] Painel "Administrar..." na aba Servidor (ServerAdminWindow): visao geral, contas, personagens, no jogo, mundo, mapas - leituras testadas no programa real
[x] gs01 caia apos editar a loja: itens com preco 0 (gs recusa a loja, erro -31). Editor agora bloqueia salvar/enviar loja assim; Verificacao compara tambem com o servidor local
[x] Painel: "Dar CASH por ID" (conta ou personagem) na aba Contas
[x] usuario: corrigir os 2 itens de preco 0 no gshop do 144 (posicoes 2 e 382) e reenviar (usuario confirmou 01/11)
[x] CASH "nao caia": chega, mas o authd demora (~4 min; so comeca 10 min apos ligar). Painel avisa e mostra pendente
[x] crash ao fechar o inventario pelo X: Win_Engrave fora do .dcf do interfaces.pck -> Verificacao acusa e Corrigir registra (testado em copia)
[x] Corrigir aplicado no 144 pelo programa real (backup interfaces.pck.20261026-122856.bak = original)
[x] editor de interface agora grava no interfaces.pck (antes so na pasta .files) + bug do texto (String/string) corrigido
[x] usuario: testar no jogo fechar o inventario pelo X e uma janela editada (usuario confirmou 01/11)
[x] (ver acima) aplicar o Corrigir no cliente 144
[x] release v0.2.0-beta (26/10): README PT/EN com novidades e prints novos (NPCGen, 3D, Servidor, painel, Verificacao, interface); aba Verificacao e painel traduzidos para EN; 39 textos EN que faltavam
[ ] BUG ABERTO: Mapas -> "Iniciar selecionado" na is05 nao sobe (log /home/logs/gs-is05.log vazio). Proximo: rodar "cd /home/gamed && timeout 40 ./gs is05" em primeiro plano p/ ver o erro (usuario interrompeu antes); depois corrigir pwctl gs-start
[ ] Mapas: a lista perde a selecao ao atualizar (DataSource refeito) -> manter a linha
[x] testar com o usuario no jogo: aviso geral, EXP em dobro, gold (usecash), banir/expulsar/calar, GM liga/desliga, criar conta/senha pelo painel (usuario confirmou 01/11)
[ ] nomes dos mapas em portugues (instance.txt e chines)
[ ] posicao do personagem em coordenadas do jogo (hoje X/Z do servidor)
[ ] proximas etapas do painel: editar personagem (nivel, dinheiro, teleporte; PutRole*), enviar item por correio (SysSendMail), clas, chat (world2.chat)
[ ] aviso de versao tambem na aba Servidor + "qual pacote de servidor usar"
Pendencias antigas: VRAM ~1,3 GB | AI Policy x Policy.cpp | arvores e grama | entradas das 19 instancias so por teleporte | botao "Montar dados do servidor" (usuario: "agora nao")

## README (31/10)
[x] README.md so em ingles, novo do zero, 12 prints em docs/img/en (README.en.md removido)

## ESTADO GIT
repo PUBLICO github.com/ipasetto/MyAceEditor (GPL-3.0), branch main. Usuario faz "Push origin" (force push bloqueado p/ mim).

## REGRAS RAPIDAS
recurso limitado: nada pesado sem autorizacao | backup + escrita atomica + round-trip antes de editar formato | editar o PAR cliente+servidor | codigo nosso (Rody = referencia de estudo) | atualizar este arquivo ao concluir/iniciar etapa

## MUNDO 3D / NPCGEN — pedidos do usuario (25/10/2026, retomar apos 11:40)
- [x] botao "Mundo 3D" tambem na barra de cima (ao lado de Excluir); na aba NPCGen o botao aparece cortado (ajustar barra)
- [x] Ctrl+Z nao funciona dentro da janela do Mundo 3D
- [x] trocar "Coleta (minas)" por "Coletaveis"
- [x] "Novo ponto aqui" tambem para NPC (nao so monstro); seletor com icones e previa 3D do modelo para escolher
- [x] seletor de mapa no topo (mundo, instancias, DGs…)
- [x] Espaco sobe e Z desce a camera (como voo no jogo)
- [~] objetos flutuando: pontos "no chao" agora desenhados na altura do terreno; se ainda houver construcoes flutuando, mandar print com a coordenada
- [x] clonar ponto/NPC direto no 3D
- [x] travadas perto da Cidade do Dragao (pontos perto em cache, carga limitada a ~6 ms/quadro; falta o usuario confirmar) (muitos NPCs): otimizar (instancias por modelo, limitar nomes/modelos, carregar em segundo plano) (usuario confirmou 01/11)
- [x] indice/legenda no canto superior esquerdo (fundo preto translucido): verde = ligado a evento/gatilho, branco = NPC de missao/venda ou monstro normal, vermelho = guardas, etc.
- [x] botao direito no NPC: "Editar" leva ao item no editor; mudancas no editor atualizam o 3D na hora (e vice-versa)
- [x] DESCARTADO: animacao dos modelos
- [ ] a26 do servidor 1.4.4 nao bate com a a26 do cliente (pontos de x -4286 a 4453 num mapa de 1 km): descobrir qual mapa do cliente corresponde

## MUNDO 3D — rodada 2 (25/10/2026 tarde)
- [x] clique preciso (escolha pela tela, 12-40 px do modelo); Delete/menu Excluir; Ctrl+C/Ctrl+V; janela sem dono (Editar traz o editor); seletor aberto pelo 3D fica na frente do 3D
- [x] previa 3D gira arrastando (seletor de ponto e duplo clique em file_model no editor)
- [x] barra escura, campos pretos; legenda + atalhos recolhivel (H); sem hastes (ponto pequeno so p/ quem nao tem modelo)
- [x] verde = evento agendado, lilas = gatilho sem agenda (fases de DG); mapa preso as bordas, sem nome do selecionado, aviso "botao direito para teleportar"
- [x] carga em segundo plano (WorldLoader): alturas, chao, mascaras, luz, minimapa, modelos, construcoes; terreno em malhas por area/peca com indices; corte do que esta atras/longe; teleporte para a cidade carrega em ~1 s
- [x] terreno longe mais fino (16 m) para nao parecer que objetos flutuam; T esconde o terreno (ver dentro de cavernas); "no chao" so segue o terreno a ate 3 m
- [x] objetos flutuando: o teste de alfa apagava faces de pilares (textura com especular no alfa); agora so recorte de verdade (DdsDecoder.IsCutout) — paredoes de canion inteiros
- [x] caixa de correio deitada: malha rigida em Z para cima, girada por regra (SkiModel.StandUp) ate ler o .bon
- [x] gatilho que liga sozinho com o mapa (AutoStart) = branco; lilas so p/ gatilho manual/fase
- [x] "Ir para" aceita coordenadas (x z ou x y z); nomes das instancias no seletor de mapa
- [x] lote de construcoes/modelos/chao nao se perde por um arquivo ruim; construcoes na fila prioritaria
- [ ] b30: dados batem (pontos 196,8 m, chao 196,0 m); conferir no programa se o chao aparece (T esconde o terreno)
- [x] travadas na Cidade do Dragao: nomes novos limitados a 4/quadro; usuario confirmar (usuario confirmou 01/11)
- [x] ler o esqueleto (.bon) para malhas rigidas (BonFile; 6885/6885)
- [x] miniaturas 3D nas listas para quem nao tem icone (ModelThumbs, segundo plano)
- [x] assistente "Monstro/NPC novo" (barra e botao direito no 3D, ja posiciona o ponto)
- [x] Salvar tudo (Ctrl+S) + salvar automatico a cada 5 min (Arquivo) + aviso ao fechar
- [ ] servidor: o software faz o maximo (para testar no jogo)
- [x] arvores: posicoes do .ecwld e geracao propria do .spt (29/10) | [ ] frondes (13000-14000), grama (.ecwld/.mox), objetos/ECmodels do .ecwld

## MUNDO 3D / LISTAS — rodada 3 (25/10/2026 noite)
- [x] assistente: ID novo automatico (proximo livre, ja ao abrir), campo bloqueado
- [x] Ctrl+Z depois de salvar (salvar no mesmo arquivo nao recarrega)
- [x] nome/modelo mudados no editor aparecem no 3D sem salvar
- [x] miniaturas entram em lote e aparecem sem passar o mouse; .ecm com AddiSkinPath (besouros-rei) agora tem modelo
- [x] seletor de blocos com busca, categorias e icones
- [x] 3D sem selecao: centro do mapa, do alto; instancia: perto do 1o NPC (entrada aproximada)
- [ ] arvores/grama "do jogo": .spt e SpeedTree procedural (precisa do runtime), .tbh e so casca de colisao; usuario escolher o caminho
- [ ] instancias: a26 (Nirvana) texturas, a19 cor, b30 catapultas abaixo do chao; entrada exata da instancia (tabela de entradas do servidor)

## CODIGO-FONTE 1.5.3 (25/10/2026) — PW-KB §S13 §S18 §S22
- [x] npcgen: limiares de versao do oficial (coleta v6/v7, objeto v9/v10); 49/49 identicos
- [x] region.sev lido; Mundo 3D abre a instancia na entrada (26 de 45 mapas)
- [x] a26: so a copia do mapa do cliente; terreno-tampa escondido; entrada na altura do piso
- [ ] aipolicy: conferir os 16 layouts "inferidos" com Policy.cpp e dar nome aos campos
- [x] path.sev (pathman.cpp): caminho de patrulha desenhado no 3D (P) | [ ] editar pontos do caminho
- [ ] arvores/grama (depois): leitor do .ecwld = EC_SceneBlock.cpp; arvores reais = SpeedTreeRT.h + speedtreert.dll

## BUGS + SERVIDOR LOCAL (25/10/2026 tarde)
- [x] b31..b35 usam o terreno b30 (MapTags); a19 cores das construcoes (cor de dia); catapultas/bandeiras de pe (pele na pose padrao)
- [x] seletor de mapa so troca ao confirmar; rodape com a coordenada do jogo
- [x] aba Servidor (WSL): instalar, iniciar, parar, estado, conta, enviar arquivos e reiniciar
- [x] instalacao testada de verdade (25/10): servidor no ar com uma conta GM de teste
- [ ] entrar no jogo pelo servidor Local (usuario)
- [~] instancias no servidor local: Administrar -> Mapas e instancias (ligar/desligar, estado, padroes gs01+is61) — usuario testou a lista; falta conferir o Estado
- [~] 31/10: backups fora do cliente, varios campos de uma vez, Ctrl+C/V de addons, mapa M das instancias (3D e GM), teleporte d 8888 corrigido, abas do GM reorganizadas — falta o usuario testar
- [ ] aipolicy: conferir os 16 layouts "inferidos" com Policy.cpp
## MUNDO 3D / INSTANCIAS — onde paramos (30/10/2026)
- [x] camera na entrada (8 m atras, olhando os pontos de criatura); CameraStarts.cs com as posicoes que o usuario conferiu (world, a05, a12, a39, a41, a49, a61, a62, a66)
- [x] tecla Espaco/qualquer tecla presa (confere GetAsyncKeyState a cada quadro)
- [x] camera nao passa mais por baixo do chao (so onde o terreno e tampa da instancia; a20)
- [x] construcoes com faces de tras descartadas como no jogo (FrontFace CW; a08: plano bege de 51x25 m cobria a parede); recorte segue dos 2 lados
- [x] texturas .tga nas construcoes; superficie .sdr sem textura nao e desenhada (domos da a33)
- [x] agua do cliente so nas celulas da mascara
- [x] a26 planos azuis, a17 faixa marrom, a23 paredes marrons esticadas = pecas SO DE COLISAO (.bmd v0x80000001 com bCollideOnly = 1; o jogo nao desenha). Agora puladas; --lit-list lista essas pecas por mapa
- [x] ceu = geometria exata do A3DSkySphere (Angelica2.2; PW-KB §ecbsd). a33 roxa = a propria textura [ ] a33 conferir com o jogo; a23: plano verde liso no alto a direita (conferir)
- [x] tetos (a35, a24, a27) = CEU da area: WorldSky le as caixas de area do .ecbsd (ECBSDFILEBOXAREA11 + 6 textos de ceu) e desenha a cupula (topo + 2 metades) com textures\sky\...; area da camera = caixa que a contem, senao a padrao mais perto. So dia. a15: mancha clara pequena na borda
- [x] texturas .bmp/.jpg/.png nas construcoes (a35: e\4ad.bmp)
- [ ] a21 arvores; bambu deitado (angulo do nivel 1 77..130 tomado a partir do eixo do pai). Pista (30/10): o nivel 0 do bambu tem angulo +90 e o das outras arvores -90; no SpeedTreeRT (FUN_100056d0) cada galho, tronco incluso, gira pelo proprio angulo no inicio (local_60). So virar o tronco para baixo nao bastou (colmos ainda 50..103 graus do alto): falta ler como o angulo e a gravidade negativa (-1) entram ali
- ferramentas: --instances, --world-shot (nome "ov" = camera do Mundo 3D), --bmd-check <pck> <mapas> [uv], --lit-list, --scene-models; MYACE_HIDE=terrain|buildings, MYACE_HIDETEX=<trecho>, MYACE_BCULL=cw|ccw|off (teste)
## PEDIDO 30/10 (painel do GM, NPC novo, drop, quest seguinte, selecao multipla)
- [x] painel do GM: barras de chi (d 1991 / d 1992) e eventos (GM_ACTIVITY_CONFIG + gatilho do npcgen pelo gdeliveryd). Testar no jogo. Tudo de GM: PW-KB §S31
- [~] monstro de teste so dropa moedas ("Ourico Teste" 61642): dados certos (3 itens x 10 sorteios do ID 6 a 100%, cliente = servidor); causa provavel = penalidade de nivel do PARAM_ADJUST_CONFIG (40+ niveis acima: item 20%, moeda 100%). Confirmar o nivel do personagem; depois mostrar a chance real por morte no painel de drop. Diagnostico: --drops <elements> [antigo]
- [x] Mundo 3D "Novo NPC aqui…" cria NPC_ESSENCE novo pelo assistente (copia a base; nome "NPC - ID", sem hello_msg e sem nenhum servico: EditorForm.ClearNpc); "NPC que ja existe aqui…" mantem o jeito antigo. Testar no app
- [ ] Mundo 3D quest seguinte (usuario: subquest). O assistente ja cria etapas "+ Falar com NPC" em ordem (= subquests, WorldQuestEditor passo Etapas); a opcao "Nova quest ao concluir" (QuestOptions newTask) so pede ID. Falta saber: a quest ja estava salva? Se sim, precisa abrir quest existente no assistente e acrescentar etapa (hoje ele so cria)
- [~] selecao multipla + Delete: feito em drops/addons (ChanceEditor: Shift/Ctrl+clique, Ctrl+A, Delete; Ctrl+Z volta tudo), pontos do NPCGen (lote desfeito num Ctrl+Z) e criaturas de um ponto. Lista principal ja era. Falta perguntar quais outras listas (quest, gshop, loja do NPC…)
## ARVORES / BAMBU (30/10)
- [x] algoritmo do galho lido na lib de depuracao do SpeedTree (AngelicaSDK2): ver PW-KB §S13 (linhas [C 30/10] SpeedTreeRT galhos / Compute / gravidade / geradores)
- [x] SptGrower.Grow reescrito com sistema de eixos por vertice no espaco do SpeedTree (Z para cima -> nosso (x, z, -y)); bambus de pe, 0 erros em 144/155/1.5.5/180
- [~] conferido com as fotos do jogo de 29/10 (mundo): palmeiras de tronco curvo e pinheiros/folhosas retos batem com o novo. [ ] falta foto do jogo com bambu e scotchpine (a21)
## MUNDO 3D MAIS PERTO DO JOGO (pedido 30/10)
- [x] entrada da a26 em 338.76, 5, -169.58 (CameraStarts)
- [x] neblina por area (.ecbsd ECBSDFILEBOXAREA11), tecla N; luz da area nas arvores
- [x] setas X/Y/Z + anel de giro no ponto SELECIONADO (WorldView DrawGizmo/GizmoDrag; NpcGenView RotatePoint). [ ] testar no app
- [x] grama no Mundo 3D (1.5.5; WorldGrass + MoxModel). [ ] conferir nas outras versoes (144/155/180) quando o usuario autorizar. Antes: Ja sabido (PW-KB §ecbsd "GRAMA"): tipos no .ecwld, areas ECBSDFILEGRASS7 -> mapa de bits no bsdata, 1 .mox por bit. Falta: ler o .mox (Angelica2.2 A3DFrame::Load / A3DMesh), achar o registro de grama no bsdata N.dat, desenhar (WorldGrass.cs, perto da camera, como as arvores)
- regra: modelos de criaturas ficam ESTATICOS (posicao inicial; usuario 30/10)
- [x] legenda de atalhos em grupos com teclas desenhadas; clique no vazio tira a selecao; botao de fora abre o mundo em 1090.03, 240, 1033.48
- [x] grama sem triangulos brancos (recorte nos mipmaps); arvores de cartoes dos .bmd recortadas. [ ] Besouro Estrela (1063) sem textura: busca de textura do modelo aceita outra extensao e registra no log as que faltam; usuario conferir
- [x] Besouro Estrela branco: pele de mesmo nome do .SMD so entra se o .SMD citar (WorldModels.ResolveSki)
- [ ] ARVORES x JOGO (30/10, prints do usuario): world 1560,220,454 = shingleoak4 (鹅卵石像树大, 3 niveis, tamanho 100 pes, tronco raio 0.1, galhos 10%..100% freq 30, folhas freq 300 = 9.530 cartoes 0.4 = SizeUsed 40/100). No jogo: tronco grosso visivel ate ~10 m, copa alta; no nosso: arbusto colunar com folhas ate o chao. Ja descartado: tamanho da folha (4006 SizeUsed = 4005 x tamanho, mesma proporcao). Suspeitas: posicao/angulo dos galhos baixos, quantidade de folhas, LOD de folhas do cliente. Usuario tambem notou menos arvores e pouca variacao de tamanho/cor de tronco no nosso.
## MUNDO 3D x JOGO (30/10, rodada de 200k) — ESPERANDO O TESTE DO USUARIO
- [x] folhas como o SpeedTree (flores 3000-3002/4000, espacamento 3007, colisao 3008): copas abertas; escala mantida (altura = tamanho)
- [x] grama com a luz do terreno (MODULATE2X) e textura achada pelo nome; [x] Besouro 1063 (pele do .SMD)
- [x] dia/noite (tecla L): neblina, ceu de noite, .t2lm1, grama, cores de noite dos .bmd, luz das arvores e dos modelos
- [x] agua: reflexo do ceu pelo angulo, borda transparente, ondas andando, noite
- [ ] usuario testar no app: arvores (shingleoak4 em 1560,220,454), grama, L de noite, agua; depois conferir 144/155/180 (pedir permissao)
- [x] (30/10 tarde) arvores na escala do SpeedTree (x tamanho), conferido com print em 1064,1113; Ctrl+Z ate o ponto salvo tira a marca do npcgen
- [ ] grama: conferir flores (陆地04/小黄花05) de perto; vento (grama e arvores); [ ] agua: reflexo real (2a camera) e mais transparente/azul como no jogo; [ ] cor do tronco
- [x] 01/11 lidos do .ecwld/.ecbsd: efeitos .gfx, bichos, curvas, sons (tecla O; efeitos/sons/bichos editaveis) | [ ] scenelights.dat
## MUNDO 3D — PENDENTES (organizado 30/10, depois dos testes em 144/155/1.5.5/180)
- [x] testado em 144, 155, 1.5.5 e 180: árvores (100 tipos, 0 erros, mesmas folhas), grama (.mox lidos em todas), áreas, dia/noite (.t2lm1 em todas)
- [x] 155/180: folhas opacas e grama preta (estado do OpenGL dependia das construções) — corrigido; 180: áreas .ecbsd v17 (364 B) — corrigido
- [x] (30/10) pacotes com número no nome (155/180) e .bmd novo do 180 lidos: construções e modelos aparecem; scotchpine e outras 8 árvores sem a copa enterrada
- [x] ~~155 e 180 NÃO têm litmodels.pck/models.pck com o nome certo (litmodels-001.pck + litmodels.pkx; models-005.pck + models.pkx; no 180 building-002.pck, litmodels-020.pck, -013.pkx, -019.pkx1, models-003.pck...): sem construções nem modelos no Mundo 3D dessas pastas. Perguntar ao usuário se são cópias quebradas ou se o editor deve aceitar esses nomes.
- [ ] grama: vento (maciez do tipo); árvores: vento; cor do tronco x jogo
- [ ] água: reflexo real (2ª câmera espelhada, como A3DTerrainWaterRender) e cor mais azul/transparente como nos prints do jogo
- [x] 01/11 efeitos, bichos, curvas e sons lidos e mostrados (O); efeitos e sons: mover/clonar/apagar; bichos: mover; salvar com backup | [ ] scenelights.dat (lâmpadas de noite)
- [ ] usuário testar no app (lista em TESTES-PENDENTES "Mundo 3D — rodada de 30/10")

## Ideias dos utilitarios estudados em 31/10 (PW-KB §S15)
- [ ] Mundo 3D como editor de mapas: roteiro em 8 passos em docs/MUNDO-3D-EDITOR.md (quests 3+, geradores, eventos, caminhos, areas, objetos .ecwld/.ecbsd, terreno + regerar bht/rmap do servidor, passagem/voo)
- [ ] editor da guerra territorial (domain.data + domain.sev, formato em PW-KB; editar os dois juntos)
- [x] tradutor de cliente: nomes dos mapas da guerra territorial (domain*.data, world_targets.txt, GuildMap.dds) + outras imagens com texto (31/10; falta testar aplicando num cliente) (vem do domain.data UTF-16; PW-KB §S32/§S6) — pedido do usuario 31/10
- [ ] ler as notas do porte 1.8.9 (Estudo PW\pw-189-port) e levar para a PW-KB o que valer para o editor (elements v531, tasks v198, aipolicy 1.8.0)
- [ ] ItemRules: avisar quando os addons fixos de movimento de um item somam > 3 m/s (o gs limita a 3 e zera acima de 5; REGRAS-DO-JOGO §2)
- [ ] adicionar icone novo ao iconlist (ivtrm + ivtrf juntos) para itens novos, como o Glue do IconListEditor
- [ ] botao "inserir minha posicao" no NPCGen e nas quests (GamePosition ja le a posicao)
- [ ] gerador XML de skills (UPX): desempacotar e estudar se fizer falta

## TESTES DO USUARIO 01/11 (Desktop\Testes\Testes.txt)
[x] 2 nao perguntar sempre para enviar ao servidor (Preferencias, padrao desligado)
[x] 9 seletor de bloco abre no bloco atual, sem piscar; Favoritos (N) certo
[x] 10 zerar Ovo de pet: coluna significado atualiza (0 = nao capturavel)
[x] 11 Voltar/Avancar em 1 clique
[x] 1/8/12/15 pesquisados (PW-KB §S12/§S21/§S22); 8 virou regra mob-level (> 150)
[~] 3/4/5/14 Caminhos e areas (compilado, falta ver na tela): rotulos x/y fora, linha no chao, so na ferramenta, Patrulhas no Mostrar, 1 caminho por criatura, dica. Falta: conferir porque o monstro nao seguiu o caminho (path.sev salvo+enviado? gs reiniciado? PathLoop)
[ ] (antigo) 3/4/5/6/14 Caminhos e areas: linha do caminho acompanhando o terreno; so com a ferramenta aberta; riscos rosas no Mostrar; patrulha OU area (um so); exigir criatura selecionada; 1 caminho por criatura; tirar rotulos x/y; dica do que fazer; conferir porque o monstro nao seguiu o caminho
[x] 7 Mostrar numa barra inferior que quebra em linhas (conferido por --world-shot)
[~] 13 cor do nome: aba Descricao e cor some em monstro/NPC [x]; tamanho do modelo no 3D: size e so raio de toque, escala vem do .ecm (estudar) [ ]; nome sobre modelo grande (cliente) [ ]
[ ] 12 sorteio de spawn (aipolicy Rand + controller) e aviso no mundo
[x] 9b seletor de bloco abre marcado no bloco atual (conferido por print)
[x] teto de valor: FieldLimits troca pelo maximo (tipo + regras do jogo)
[x] secoes Spawn (npcgen editavel) e Quests (quem usa, abre a quest) no elements
[x] Preferencias: MyAce (editor) x Servidor na barra lateral
[x] Mundo 3D: painel da criatura refeito como ficha (conferido por --creature-shot); falta ver no 3D de verdade
[~] Caminhos e areas em passos + fim do caminho (para / vai e volta / circuito) + "Sem area" - compilado, falta ver na tela
[-] (abandonado pelo usuario 01/11) limite minimo de quests: exige patch do gs E do elementclient.exe (constante inline 20/30); estudar por versao antes

## TESTES 01/11 (4a rodada)
[x] caminhos: nao apaga ao trocar, circuito, Enter, texto vermelho, colisao, NPC comum (compilado; falta ver)
[x] drops/habilidades por slot; Spawn numa linha; cor amarela do besouro; icone da quest no assistente
[ ] preferencia do cliente: esconder Win_PwdHint (patch do elementclient.exe; confirmar com o usuario se e essa janela)
[ ] Spawn: editar "minimo" (RefreshLower) = tempo sorteado

## TESTES 01/11 (5a rodada)
[x] ferramenta Quests no 3D + Salvar e enviar; caminho: marcador, Enter, arvores, NPC comum; Spawn em ficha + Minimo; campos sem uso nas Preferencias; quests sumidas saem dos NPCs; editor de interfaces em janela; icone de cultivo (chave)
[ ] chance de nascer + mensagem ao nascer (aipolicy do monstro: gatilho ativo no inicio, talk "$B..." e parar; chance precisa de um ajudante)
[ ] aviso Win_PwdHint ("Lembre-se") e painel "Aviso" do login: texto editavel pelo editor de interfaces; esconder so com patch do cliente

## CRIAR INTERFACE (pedido 02/11) — FEITO 02/11 (UI/InterfaceDesigner.cs + PerfectWorld/UiDesign.cs; falta testar no jogo) — PW-KB §S20b
- Tela "Criar interface" no editor de interface: desenhar janela propria copiando controles de outras janelas (mesmas imagens do surfaces).
- "Vincular a" uma janela do jogo (ex.: gmconsole.xml): o editor grava com o MESMO nome de arquivo e Win_, confere o contrato (todos os nomes+tipos de controle e Commands do XML original; mostra o que falta) antes de salvar, poe no .dcf se preciso, backup + escrita atomica no interfaces.pck.
- Controles a mais = so enfeite (o exe nao tem codigo para eles); janela com arquivo novo vira CDlgBase (so fechar/ajuda) e nada a abre.

## COMPATIBILIDADE ENTRE VERSOES (pedido 02/11) — PW-KB §S33 e §Q
Pendencias achadas nos testes de 02/11 (--roundtrip/--tasks/--aipolicy/--npcgen/--gshop nos arquivos do acervo):
- [ ] tasks.data v55 (servidor 1.3.6) TRAVA a leitura (nao termina em 5 min): pôr limite/aviso para nao prender a tela e depois o layout das versoes antigas (< 93).
- [ ] gshop.data 1.7.x (1.7.6 e Maori 1.7.2) nao abre: layout V3 do item (~1448 B cliente / ~232 B servidor). Usar o par do 1.7.6 (PW_Server/1.7.6/pw_server_1.7.6_2025) e o fonte 1.7.4 (Estudo PW/174Source.zip, cgame/gs/playermall.cpp). Ver memoria gshop-172-pending.
- [ ] elements.data comprimido MPPC (1.7.x, v386) so leitura: validar a regravacao comprimida.
- [ ] PWSERVER-1.7.6-01.2026-VM.7z ainda nao extraido/inventariado.
- [x] aipolicy 1.7.6 (operacoes 103-105) — feito 02/11.

Documento COMECADO 02/11 (docs/COMPATIBILIDADE.md; preencher os "?"): uma tabela POR FERRAMENTA do editor, marcando cada versao testada:
- Linhas = TODAS as ferramentas/recursos: Elements, Quests (tasks), GShop, AI Policy, NPCGen, Mundo 3D (e cada ferramenta dele), Verificacao, Servidor, Plugins, Painel do GM, Interfaces (editar janelas, Criar interface, importar, trocar com outro cliente, telas de carregamento), Extrator PCK, tradutores (elements, loja, configs/interfaces), icones, Preferencias (cada opcao que mexe em arquivo do jogo) e o que mais existir no menu.
- Colunas = versoes do acervo: 1.3.6, 1.4.4 (v60/v61), 1.5.2, 1.5.5 (v156, 162-155, PWI), 1.7.2 (Maori), 1.7.6, 1.8 (180 BR).
- Celula: OK (testado, regrava identico / funciona) | so leitura | NAO (com o motivo) | nao testado; cada pendencia com link para o item acima ou para o §Q da PW-KB.
- Base: resultados ja na PW-KB §S33/§Q e ENGINE-KB; preencher rodando os testes de linha de comando em cada pasta de §S33 (sem abrir as pastas do usuario de novo para inventario). Atualizar a tabela sempre que uma versao for mexida (regra: toda funcao em todas as versoes).

## PEDIDOS 02/11 (fim do dia, sem tokens) — fazer na proxima sessao
- [~] FEITO no codigo 02/11 (WorldView.OwnPath no arrasto do chao e no aereo; PathUsers no mapa certo), FALTA TESTAR NO JOGO: caminho de monstro voador compartilhado. Ao editar o caminho (patrulha) de um monstro, VARIOS outros seguem o mesmo caminho (o iPathID do grupo no npcgen aponta para o mesmo path; o usuario ja tinha visto isso antes). A edicao de caminho tem que tratar 100%: se o caminho e usado por mais de um ponto/grupo, clonar para um ID novo so deste ponto (com Ctrl+Z) antes de mexer, e avisar. Conferir no editor de PATRULHA e no de AREA (Mundo 3D > Caminhos e areas) e no path.data/npcgen do cliente e do servidor. Testar em todas as versoes e marcar em docs/COMPATIBILIDADE.md.
- [ ] Falas dos monstros (AI Policy) pela janela de anuncios do Mundo 3D: hoje ela so MOSTRA em cinza as falas que a IA do jogo ja tem (combate, vida baixa, sorteio, timer). Fazer: editar texto/canal, tirar e criar falas com outras condicoes (vida abaixo de X%, timer, sorteio, matou jogador), com Ctrl+Z e traduzido.
- [x] FEITO 02/11 (registro do WSL + juncao D:\PWServer -> D:\PWServer155 + editor acha pela chave do WSL): Pasta do servidor que nao renomeia ("aberta em outro programa" com o servidor parado): o WSL/explorer/editor segura a pasta; ver se o editor guarda algum arquivo aberto do servidor (ServerAdmin/LocalServer) e documentar no README o passo (fechar o editor + wsl --shutdown).

