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
[ ] quests: editar premios/itens/monstros pedidos (hoje preservados como bytes) | criar/clonar/excluir quest | outras versoes (so v93 completa)
[ ] auto-tradutor (quests + elements + configs.pck)
[x] GShop (cliente+servidor juntos, tela igual a do jogo; falta validar no jogo e desfazer)
[ ] AI Policy (PW-KB §S18) | [x] NPCGen: ler/gravar identico, aba com pontos, editar qtde/renascer/monstro, Mudar ID (PW-KB §S22) | [x] Mundo 3D (terreno por altura, camera livre, marcadores, botao direito: copiar/novo ponto/mover) | [x] modelos de monstro/NPC, construcoes, texturas e luz do chao, agua, mapa pintado (M) | [ ] arvores e grama (.ecwld/.spt), animacao | [ ] objetos e gatilhos (hoje so preservados)
[x] extrator .pck nativo (aba Extrator PCK) | [ ] gravar .pck (para interface/configs)

## FASE 4 — engine
[ ] corrigir automaticamente o par (exportar loja/elements/tasks p/ os 2 lados)
[ ] racas iniciais (exe + createrace.xml + mascara; PW-KB §S12) | jogadores na TW (§S25)
[x] janelas do jogo dentro da engine: aba Interface (ver/editar posicao, tamanho, texto; salva em interfaces.pck.files) | [ ] gravar no .pck
[ ] deploy de servidor | editor de mapa (PW-KB §S22) | skills

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
- [ ] arvores e grama: fechar o layout do world.ecwld (posicoes) e ler SpeedTree .spt (ou desenhar substitutos)

## MUNDO 3D / LISTAS — rodada 3 (25/10/2026 noite)
- [x] assistente: ID novo automatico (proximo livre, ja ao abrir), campo bloqueado
- [x] Ctrl+Z depois de salvar (salvar no mesmo arquivo nao recarrega)
- [x] nome/modelo mudados no editor aparecem no 3D sem salvar
- [x] miniaturas entram em lote e aparecem sem passar o mouse; .ecm com AddiSkinPath (besouros-rei) agora tem modelo
- [x] seletor de blocos com busca, categorias e icones
- [x] 3D sem selecao: centro do mapa, do alto; instancia: perto do 1o NPC (entrada aproximada)
- [ ] arvores/grama "do jogo": .spt e SpeedTree procedural (precisa do runtime), .tbh e so casca de colisao; usuario escolher o caminho
- [ ] instancias: a26 (Nirvana) texturas, a19 cor, b30 catapultas abaixo do chao; entrada exata da instancia (tabela de entradas do servidor)
