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
       [ ] terreno: pincel de altura (puxar, empurrar, alisar)
       [ ] textura do chao: pincel por camadas
       [ ] grama: pincel de grama e flores
       [ ] arvores: plantar, mover, apagar (+ colisao da arvore no map.bht)
       [ ] construcoes e objetos: girar, colocar novos, apagar (mover ja existe)
       [ ] servidor: gerar de novo map.bht / rmap / dhmap depois de mexer em terreno ou objeto solido
           (obrigatorio antes de liberar terreno e objetos; sem isso o servidor anda no relevo antigo)
       [ ] passagem dos NPCs e voo (mapa de passagem, octree)
[ ] 3. Painel do GM: mandar item pelo correio (SysSendMail)
Depois (sem data): ferramenta de portabilidade (1.5.5 parecendo versao antiga), so apos entrevista com o usuario.

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
