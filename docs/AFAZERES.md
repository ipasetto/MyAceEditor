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
