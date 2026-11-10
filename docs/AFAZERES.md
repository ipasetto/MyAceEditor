# AFAZERES — o que falta (ler 1o; curto de proposito)
# legenda: [x] feito [~] parcial [ ] falta | detalhes: docs/kb/ENGINE-KB.md (§E7) | regras: docs/REGRAS-DO-JOGO.md
# PUBLICO (GPL-3.0): issues no GitHub (gh CLI, conta ipasetto); roadmap da comunidade em docs/ROADMAP.md (manter em sincronia)

## FOCO: SO A VERSAO 1.5.5 (decisao do usuario 05/11)
Outras versoes: o que rodar, rodou; nao testar nem corrigir. GM: painel separado, lado a lado com o jogo.
Consolidado em 07/11: tudo abaixo de "Pronto" foi conferido pelo usuario no app e no jogo.

## FALTA
[x] 1. elements.data: leitura confirmada ate o ultimo byte em 07/11 (1.5.5 v156: 231/231 blocos; 1.4.4 v60: 150/150;
       regravacao identica). Os ~1,4% eram do leitor antigo, ja trocado. Issue #21 fechada. ENGINE-KB §E3.
[ ] 2. Mundo 3D como editor de mapas (roteiro em docs/MUNDO-3D-EDITOR.md, passos 6 a 8):
       Objetos (construcoes, arvores, grama, modelos, luzes): desenho aprovado 07/11 em
       docs/superpowers/specs/2026-11-07-objetos-do-mundo-design.md
       [x] etapa 1 no codigo (07/11): mover, girar e apagar construcoes e modelos, Salvar tudo ou nada; falta testar no jogo
       [x] etapa 2a (07/11): Ctrl+C/Ctrl+V de construcoes e modelos + tamanho da construcao; falta testar no jogo
       [x] 3 arvores (08/11): mover, apagar, copiar/colar, plantar pelos tipos do mapa; so .ecwld (sem colisao no servidor); falta testar no jogo; um por clique, recarrega apos backup, caixa da arvore inteira (08/11)
       [x] 2b catalogo das construcoes por perto com previa, estrela e outro pedaco (08/11) | [x] arvores e construcoes de todos os mapas e instancias (08/11) | [x] 4 luzes da cena e nome das construcoes (08/11; falta testar no jogo) | [x] 5 pincel de grama e flores (08/11; falta testar no jogo)
       Sombra no chao = pintada no mapa de luz do terreno (.t2lm): pincel de Sombra e luz da pilula Terreno (09/11)
       [x] terreno: pincel de altura (subir, descer, alisar, plainar) + .hmap do servidor (08/11; falta testar no jogo)
       [x] agua: escolher, nova, nivel, tamanho, cor, apagar, copiar/colar; cliente (bsdata/.ecbsd/.ecwld) + servidor (watermap) (08/11; falta testar no jogo; espuma da margem nova e passagem dos monstros de agua depois)
       [x] passagem dos NPCs: .rmap refeito onde o chao mudou ao salvar (08/11; .dhmap e regeracao completa com colisao depois)
       [x] nomes das texturas no idioma do MyAce + Renomear (F2) no glossario do cliente (08/11)
       [x] texturas novas: escolher de outros mapas do cliente; entram no fim da lista do .trn2 ao salvar (08/11; falta testar no jogo)
       [-] luz e sombra do chao automaticas: removidas a pedido do usuario (09/11); conta-gotas I na textura e na grama
       [x] passagem dos NPCs ao mexer construcoes (08/11; .dhmap/andares depois)
       [-] filtro de efeitos visuais: removido a pedido do usuario (09/11); o gatilho fica so visual
       [x] textura do chao: pincel por camadas (08/11; falta testar no jogo; texturas novas no .trn2 depois)
       [x] grama: pincel de grama e flores (feito: item 5 acima)
       [x] arvores: plantar, mover, apagar feitos; colisao no servidor nao existe no 1.5.5 (sem .tbh), mas a passagem dos monstros (.rmap) tem o circulo de cada arvore: refeito ao salvar (09/11; falta testar no jogo)
       [x] construcoes e objetos: girar, colocar novos, apagar (etapas 1/2a/2b)
       [x] Edicao de Mundo: Objetos e Terreno numa ferramenta (pilulas), grama na pilula Terreno, um Salvar, Ctrl+Z em ordem, backup por sessao (09/11)
       [x] pincel de sombra e luz do chao (.t2lm/.t2lm1): Sombra, Luz, Pegar tom, Borracha; previa ao vivo; so blocos mexidos (09/11; TESTADO NO JOGO 09/11: aparece; backup por sessao tambem funcionou)
       [x] pendencias 09/11: sombra nao some na troca dia/noite; conta-gotas pega sombra ou luz (amostra do tom); clique nas criaturas/NPCs de volta (lista do NPCGen sem janela); Ver colisao so do escolhido (pecas solidas reais); aviso do salvar agrupado (max. 12 linhas)
       [x] PASSAGEM PELA MALHA (09/11): construcoes = triangulos da malha (parede na altura do corpo, piso deitado: anda-se em cima da ponte); arvores = tronco e raizes ate a metade da altura + 0,5 m (galhos nao barram) (falta testar no jogo)
       [x] pendencias 09/11 (2a rodada): passagem refeita tambem nas construcoes COPIADAS e sem colisao (era a causa dos monstros atravessando pedra e ponte); paineis de baixo sem tremer; tipo de arvore certo depois de salvar; Ctrl+Z/Ctrl+Y do ultimo Salvar; arrastar ao vivo; sem botao Dar nome; Ver colisao a vista
       [x] testado no jogo 09/11: construcoes e arvores com raizes contornadas, arrastar ao vivo, Ctrl+Z do Salvar
       [x] pontes (09/11): o tabuleiro vale para os monstros de chao, o fundo fecha nas bordas (servidor so tem 1 piso por metro; aquaticos/voadores passam por baixo); Ver colisao desenha pecas com ate 512 lados
       [x] testado no jogo 10/11: ponte (litmodel_680) salva de novo, a criatura atravessa por cima sem afundar
       [x] Caminhos sem travadas ao desenhar a patrulha (raio do mouse uma vez por quadro) (09/11)
       [x] 10/11: menu de ferramentas com ladrilhos baixos (nome; descricao na dica), recolhe se cobrir o painel de baixo; criatura/NPC com Mover V / Girar R no painel (conferido pelo usuario 10/11)
       [x] RELEASE v1.0.1 (10/11, a pedido do usuario): menu de ferramentas baixo, Mover V / Girar R nas criaturas
       [x] RELEASE v1.0.0 publicada no GitHub a pedido do usuario (09/11): .zip com o .exe comprimido, lang, PerfectWorld\cfg, Server, plugins
       [x] servidor: hmap, rmap (terreno e construcoes), dhmap (piso sob chao mexido), map.bht das construcoes, watermap (08/11)
           [x] pontes novas como caminho de monstro (.rmap + .dhmap) e voo dos monstros (.octr) ao mexer construcoes (09/11; falta testar no jogo)
       [-] luzes da cena: removidas (o cliente 1.5.5 nao usa; usuario 09/11)

## PRONTO (conferido pelo usuario)
- Elements: leitura completa e gravacao identica, lista e detalhe no estilo Rody, vinculos, editores de duplo clique,
  drops/addons com barras, lote, busca avancada, trocar ID em todos os arquivos, descricao e cor, converter versao,
  traduzir e auto-tradutor, desfazer/refazer.
- Quests: aba Quests (tasks.data completo) e quests pelo Mundo 3D (recado, ir ate um lugar, matar, obter, entregar,
  esperar, editar, excluir).
- GShop, NPCGen, AI Policy (anuncio ao morrer / ao entrar em combate incluso), extra_drops.sev.
- Mundo 3D: terreno, construcoes, arvores, grama, agua, ceu, instancias; pontos do npcgen; caminhos e patrulhas;
  eventos; zonas (precinct); territorios / guerra territorial (domain.data + domain.sev); teleportes; NPCs e monstros
  (criar, editar, drops); cenario (efeitos, sons, bichos); mover construcao; Esc sai de toda ferramenta; janela de salvo.
- Interfaces: editor, criar interface, importar/exportar, trocar com outro cliente, telas de carregamento; .pck/.pkx.
- Servidor local (WSL): instalar, iniciar, enviar, Administrar (todas as instancias sobem, is05 inclusa); Verificacao do par e do log do cliente; Plugins.
- Painel do GM: comandos testados no jogo (nivel, moedas, EXP, teleporte, item, eventos, chi, aviso, banir...).

## FORA DO ESCOPO (decisao do usuario 07/11; nao reabrir)
Paridade restante com o Rody, servidor remoto por SSH, raças iniciais, regras dos executaveis do servidor, skills.

## REGRAS RAPIDAS
recurso limitado: nada pesado sem autorizacao | backup + escrita atomica + round-trip antes de editar formato |
editar o PAR cliente+servidor | codigo nosso (outros editores = referencia de estudo) | atualizar este arquivo ao
concluir/iniciar etapa | commit e push ao fim de cada tarefa, nunca release
