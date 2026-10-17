# AFAZERES — mapa de etapas (ler 1o; curto de proposito)
# legenda: [x] feito [~] parcial [ ] falta [>] em andamento | detalhes: docs/kb/ENGINE-KB.md (§Exx)
# META: app fazer 100% do Rody's Angelica Editor, codigo nosso, interface parecida (§E8)

## FASE 1 — base
[x] elements.data leitura ~98,6% (§E3)            [ ] cauda Territory War ~1,4% (§Q1)
[x] elements.data gravacao byte-identica (§E3)    [x] .pck/.pkx nativo + icones (§E4)
[x] verificacao par cliente x servidor (§E5b)     [x] tipos de campo + vinculos (§E3b)
[x] docs publicas: README, ARQUITETURA, KBs, skill

## FASE 2 — paridade Rody (Elements)
[x] lista plana: bloco | filtro tipo | busca | ID+icone+nome colorido | ordenar
[x] detalhe: Valores (coluna Vinculo) | Vinculos (quem usa) | tooltip c/ drops
[>] tela 100% Rody: cabecalho ID+nome escuro | sub-abas por grupo (Basico/Habilidades/Queda/Modelo…) | col. nº vinculos | voltar/avancar | Descricao e cor (item_ext_desc do configs.pck) | Buscar por campo | barra de status
[ ] editores especiais: addons, mascaras (classes) c/ checkbox, cor, probabilidades
[ ] nomes de skill (skillstr.txt) e de quest nos vinculos
[ ] icones de monstro/NPC
[ ] importar/exportar item (entre elements de versoes diferentes, .rules)

## FASE 3 — outros arquivos
[>] tasks.data esquema completo v93 (textos, dialogos, subquests, premios) (§E3c)  [~] lista/renomeia quests
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
repo github.com/ipasetto/MyAceEditor, branch main. Usuario faz "Push origin" (force push bloqueado p/ mim).

## REGRAS RAPIDAS
recurso limitado: nada pesado sem autorizacao | backup + escrita atomica + round-trip antes de editar formato | editar o PAR cliente+servidor | codigo nosso (Rody = referencia de estudo) | atualizar este arquivo ao concluir/iniciar etapa
