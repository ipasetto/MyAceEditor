# Criar e editar quests no Mundo 3D — especificação

Combinado com o usuário em entrevista (28/10/2026). Regras do projeto valem em tudo: textos via `Loc.T` com en/ru/zh-CN, uma pasta de PW para todas as abas, todas as versões do jogo, editar o par cliente+servidor, round-trip byte-idêntico antes de gravar, backup + escrita atômica.

## Base (antes de qualquer tela)

1. **tasks.data completo em todas as versões.** Hoje: v93 completo (`TaskSchema93`), textos/diálogos em qualquer versão (`TaskTexts`). Falta o bloco fixo de cada versão (campos, premissas, objetivos, prêmios). Referência: `tasks/ATaskTemplFixedData.cs` do RAE (sTASKedit), com os limites por versão. Critério: ler e regravar idêntico **todos** os tasks.data do disco (v93, v128, v129, servidores).
2. Mesmo leitor para cliente e servidor (o par usa o mesmo tasks.data).

## Fluxo na tela

- Clicar num **NPC** no mundo → painel **"Quests deste NPC"** sobe de baixo para cima (minimizável), na mesma tela: quests que ele dá e que ele recebe, e o botão **Criar quest**.
- Clicar numa quest da lista → abre a **edição** no mesmo painel (criar e editar são a mesma tela).
- Criando ou editando: **painel à direita** com os passos como atalhos (NPC que dá, objetivo, NPC que recebe, requisitos, prêmios, textos, resumo). Pode pular entre eles; vale até **Concluir** ou **Cancelar**.
- Campos de mundo são escolhidos **clicando no mundo**: NPC que dá/recebe, monstro alvo, lugar (área) a alcançar. Tudo continua editável à mão.
- **Requisitos** e **prêmios**: todas as opções que o formato tem. Escolhe o tipo (lista suspensa ou caixas de seleção, vários ao mesmo tempo) e depois informa o valor, item ou quantidade.
- **Textos** começam em branco, com dica do que cada campo é (descrição, aceitar, recusar, prêmio, diálogos).
- **ID novo** = próximo livre (mostrado e editável).
- Se algo não estiver claro durante o uso, o painel pergunta e informa os campos e possibilidades.

## Gravar

- Vínculos preparados separadamente (serviço de quest dos NPCs no elements, item de quest novo, drop no monstro) e aplicados juntos no **Salvar** — cliente apenas.
- **Servidor** só com "Salvar e enviar ao servidor". Enquanto houver mudança não enviada: brilho verde sutil nesse botão + dica dizendo o que falta enviar.
- Antes de aplicar: **validação** (o que falta ou está incoerente) e **resumo final** legível.

## Tipos de quest — da mais fácil para a mais difícil

| # | Tipo | O que envolve |
|---|---|---|
| 1 | Falar com NPC (recado) | NPC que dá → NPC que recebe; sem objetivo |
| 2 | Ir até um lugar | área no mapa (região a alcançar) |
| 3 | Matar monstros | monstro(s) + quantidade |
| 4 | Coletar item que já existe | item + quantidade (drop/mina/loja já existentes) |
| 5 | Entregar item | item levado de um NPC a outro (item dado ao aceitar) |
| 6 | Coletar item de quest novo | criar item de quest + drop no monstro só durante a quest |
| 7 | Combinados | matar + coletar, vários objetivos |
| 8 | Cadeias | quest anterior/seguinte, subquests (pai/filhos) |
| 9 | Com tempo | tempo limite, horário, diária/repetível |
| 10 | Especiais | escolta/proteger NPC, PQ/eventos, por facção/casamento |

Prêmios e requisitos completos entram desde o tipo 1 (são os mesmos campos para todos os tipos).

## Tipo 2 — Ir até um lugar (combinado em 29/10/2026)

- Área marcada **arrastando no chão** (retângulo); a altura é preenchida sozinha e os números (mín./máx. X, Y, Z) ficam editáveis.
- **Várias áreas** quando a versão aceita (até 8 até a v174, 16 depois); nas antigas, uma só com aviso.
- No mundo aparece só a área da quest que está sendo criada ou editada (caixa translúcida).
- **Como termina** fica a cargo do painel: a quest vira uma lista de **etapas** em ordem (como o jogo faz: quest principal com o NPC que dá e o prêmio, e cada etapa é uma subquest): chegar ao lugar (conclui ao chegar), falar com um NPC (prêmio entregue por ele), ir a outro lugar, matar monstros (entra com o tipo 3), e o que mais o formato tiver conforme os outros tipos forem feitos.
- Uma etapa só "falar com NPC" continua sendo o recado do tipo 1 (quest sem subquests).
- Vários lugares (usuário 29/10): escolher a **ordem das etapas** — em ordem, em qualquer ordem (todas) ou basta uma. Várias áreas dentro de uma etapa = chegar em qualquer uma.
- **Garantia ao salvar** (usuário 29/10): o botão Concluir só fica ativo quando não há pendência; o rodapé mostra quantas são e quais (o resumo lista todas). A validação cobre IDs livres da principal e das etapas, NPCs, falas, áreas válidas, tag do mapa conhecida, limite de áreas da versão e vagas nos serviços dos NPCs.
