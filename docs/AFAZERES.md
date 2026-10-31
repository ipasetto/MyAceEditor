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
[ ] editores especiais: addons, mascaras (classes) c/ checkbox, bool como checkbox, cor, probabilidades
[ ] editar descricao do item (exige gravar configs.pck)
[ ] rotulos PT melhores p/ campos (ex. "Magico defences 1 min")
[x] nomes de skill (skillstr.txt) e de quest nos vinculos (falta clicar e ir a quest)
[ ] icones de monstro/NPC
[x] converter elements entre versoes (Salvar em outra versao) | [x] traduzir com outro elements.data | [ ] importar/exportar item avulso entre arquivos

[x] desfazer/refazer + historico (#1) | [x] procurar erros + corrigir probabilidades (#4) | [x] alterar campo em lote (somar/multiplicar/sequencia/aleatorio/substituir)
[x] tooltip da lista estilo RAE + vinculos por espaco de ID | [x] tipos/subtipos com vinculos e duplo clique

## FASE 3 — outros arquivos
[x] tasks.data v93 completo: 21.777 quests, 238 campos, textos, dialogos; leitura/gravacao byte-identica (§E3c)
[x] aba Quests: arvore + Dados + Textos + Dialogos
[ ] quests: editar premios/itens/monstros pedidos (hoje preservados como bytes) | criar/clonar/excluir quest | [x] outras versoes na aba Quests (TaskLayout: v128/129/187/198 testadas; campos com chaves m_..., sem rotulo PT)
[x] traduzir a partir de outro cliente (elements + quests + falas do aipolicy + pacotes de interface/ajuda quando o jogo e o mesmo programa)
[x] GShop (cliente+servidor juntos, tela igual a do jogo; falta validar no jogo e desfazer)
[ ] AI Policy (PW-KB §S18) | [x] NPCGen: ler/gravar identico, aba com pontos, editar qtde/renascer/monstro, Mudar ID (PW-KB §S22) | [x] Mundo 3D (terreno por altura, camera livre, marcadores, botao direito: copiar/novo ponto/mover) | [x] modelos de monstro/NPC, construcoes, texturas e luz do chao, agua, mapa pintado (M) | [x] arvores (.ecwld + nossa versao do SpeedTree: tronco, galhos, folhas viradas p/ camera) | [x] frondes em lamina | [x] frondes em extrusao + cores pelos materiais do .spt | [x] agua do cliente (bsdata: cor; sem plano onde o cliente nao tem, ex. lava da a09) | [ ] conferir arvores com prints do jogo (pinheiros escuros em leque, copas muito densas?) | [ ] grama, animacao | [ ] objetos e gatilhos (hoje so preservados)
[x] extrator .pck nativo (aba Extrator PCK) | [ ] gravar .pck (para interface/configs)

## QUESTS NO MUNDO 3D (spec docs/QUESTS-3D.md)
[x] etapa 1: tasks.data completo em todas as versoes (TaskLayout) | [>] etapa 2: telas no Mundo 3D
[x] aba Quests: todas as versoes, rotulos/dicas PT+en/ru/zh | [x] Mundo 3D: filtro por tipo, barra do NPC, ver quests do NPC
[~] criar quest tipo 1 (recado) pelo Mundo 3D + dar quest existente (falta testar no app e no jogo) | [x] requisitos/premios por lista de opcoes + "outro campo" | [x] brilho do "Salvar e enviar ao servidor" (tudo, sessao) | [~] tipo 2 ir ate um lugar com etapas (em ordem/qualquer ordem/basta uma, varias areas; testado no arquivo nas 4 versoes, falta testar no app e no jogo) | [ ] tipos 3-10 (matar monstros entra como etapa) | [ ] editar quest existente pelo painel
[!] recados do tipo 1 criados ANTES de 29/10 tinham a opcao aceitar/concluir apontando para a quest modelo: recriar
[ ] depois da 1a etapa das quests: arvores, grama e objetos no Mundo 3D (.ecwld/.spt) em todas as versoes (usuario 29/10)
[ ] futuro (usuario 29/10, "nao agora"): editor de mapa que cria o cenario (arvores, grama, objetos no .ecwld)

## FASE 4 — engine
[ ] corrigir automaticamente o par (exportar loja/elements/tasks p/ os 2 lados)
[ ] racas iniciais (exe + createrace.xml + mascara; PW-KB §S12) | jogadores na TW (§S25)
[x] janelas do jogo dentro da engine: aba Interface (ver/editar posicao, tamanho, texto; salva em interfaces.pck.files) | [ ] gravar no .pck
[ ] deploy de servidor | editor de mapa (PW-KB §S22) | skills

## ONDE PARAMOS (26/10/2026, painel do servidor) - RETOMAR AQUI
Pedido do usuario: "verificacao de versao" + "painel para administrar o servidor num form (como o iweb)".
[x] Verificacao: versao do elements compilada no elementclient.exe x gs x arquivos (ProgramVersion.cs)
[x] Painel "Administrar..." na aba Servidor (ServerAdminWindow): visao geral, contas, personagens, no jogo, mundo, mapas - leituras testadas no programa real
[x] gs01 caia apos editar a loja: itens com preco 0 (gs recusa a loja, erro -31). Editor agora bloqueia salvar/enviar loja assim; Verificacao compara tambem com o servidor local
[x] Painel: "Dar CASH por ID" (conta ou personagem) na aba Contas
[ ] usuario: corrigir os 2 itens de preco 0 no gshop do 144 (posicoes 2 e 382) e reenviar
[x] CASH "nao caia": chega, mas o authd demora (~4 min; so comeca 10 min apos ligar). Painel avisa e mostra pendente
[x] crash ao fechar o inventario pelo X: Win_Engrave fora do .dcf do interfaces.pck -> Verificacao acusa e Corrigir registra (testado em copia)
[x] Corrigir aplicado no 144 pelo programa real (backup interfaces.pck.20261026-122856.bak = original)
[x] editor de interface agora grava no interfaces.pck (antes so na pasta .files) + bug do texto (String/string) corrigido
[ ] usuario: testar no jogo fechar o inventario pelo X e uma janela editada
[x] (ver acima) aplicar o Corrigir no cliente 144
[x] release v0.2.0-beta (26/10): README PT/EN com novidades e prints novos (NPCGen, 3D, Servidor, painel, Verificacao, interface); aba Verificacao e painel traduzidos para EN; 39 textos EN que faltavam
[ ] BUG ABERTO: Mapas -> "Iniciar selecionado" na is05 nao sobe (log /home/logs/gs-is05.log vazio). Proximo: rodar "cd /home/gamed && timeout 40 ./gs is05" em primeiro plano p/ ver o erro (usuario interrompeu antes); depois corrigir pwctl gs-start
[ ] Mapas: a lista perde a selecao ao atualizar (DataSource refeito) -> manter a linha
[ ] testar com o usuario no jogo: aviso geral, EXP em dobro, gold (usecash), banir/expulsar/calar, GM liga/desliga, criar conta/senha pelo painel
[ ] nomes dos mapas em portugues (instance.txt e chines)
[ ] posicao do personagem em coordenadas do jogo (hoje X/Z do servidor)
[ ] proximas etapas do painel: editar personagem (nivel, dinheiro, teleporte; PutRole*), enviar item por correio (SysSendMail), clas, chat (world2.chat)
[ ] aviso de versao tambem na aba Servidor + "qual pacote de servidor usar"
Pendencias antigas: VRAM ~1,3 GB | AI Policy x Policy.cpp | arvores e grama | entradas das 19 instancias so por teleporte | botao "Montar dados do servidor" (usuario: "agora nao")

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
- [~] travadas perto da Cidade do Dragao (pontos perto em cache, carga limitada a ~6 ms/quadro; falta o usuario confirmar) (muitos NPCs): otimizar (instancias por modelo, limitar nomes/modelos, carregar em segundo plano)
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
- [ ] travadas na Cidade do Dragao: nomes novos limitados a 4/quadro; usuario confirmar
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
- [ ] path.sev (pathman.cpp): desenhar no 3D o caminho de patrulha dos grupos (iPathID)
- [ ] arvores/grama (depois): leitor do .ecwld = EC_SceneBlock.cpp; arvores reais = SpeedTreeRT.h + speedtreert.dll

## BUGS + SERVIDOR LOCAL (25/10/2026 tarde)
- [x] b31..b35 usam o terreno b30 (MapTags); a19 cores das construcoes (cor de dia); catapultas/bandeiras de pe (pele na pose padrao)
- [x] seletor de mapa so troca ao confirmar; rodape com a coordenada do jogo
- [x] aba Servidor (WSL): instalar, iniciar, parar, estado, conta, enviar arquivos e reiniciar
- [x] instalacao testada de verdade (25/10): servidor no ar com uma conta GM de teste
- [ ] entrar no jogo pelo servidor Local (usuario)
- [ ] instancias no servidor local (hoje sobe so gs01, como o start.sh do pacote)
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
- [ ] NAO LIDOS do .ecwld: efeitos .gfx (3.519 no world), bichos/critters (1.347), curvas bezier (42.790), sons (1.044), scenelights.dat
