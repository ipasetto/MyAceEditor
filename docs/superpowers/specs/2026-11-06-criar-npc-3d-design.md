# Criar NPC, criatura, vendedor e fornalha no Mundo 3D (teste 28)

Pedido do usuário: ferramenta 100% intuitiva no Mundo 3D. Vendedor abre no centro da tela a janela de venda como no jogo;
fornalha abre a janela de fabricação. O usuário clica nos espaços, escolhe os itens e vê as informações flutuantes como no
jogo. Depois escolhe o lugar no mundo.

## Fluxo

Novo ladrilho no menu do Mundo 3D: **Criar**. O painel segue o padrão das ferramentas (quadros, seta de voltar).

1. **O que criar:** Criatura, NPC, Vendedor ou Fornalha.
2. **Quem é:** janela de escolha com ícone, dica e modelo 3D. Em NPC, Vendedor e Fornalha o escolhido é a base (aparência) e
   o nome é editável. Em Criatura o escolhido entra no mundo como está, sem registro novo.
3. **Loja ou fabricação:** janela do jogo no centro (`shop.xml` ou `produce_icon.xml`), desenhada com as texturas do cliente.
   - Clique no espaço: escolhe o item (Vendedor) ou a receita (Fornalha). Botão direito tira. Passar o mouse mostra a dica.
   - Abas = páginas, com título editável.
   - Fornalha: receita nova com produto, quantidade, materiais, preço e tempo; ou uma receita que já existe.
4. **Onde fica:** o botão pede um clique no chão. Esse clique cria tudo e põe o ponto no NPCGen.

## Registros criados (elements.data e npcgen)

| Tipo | Registros |
|---|---|
| Criatura | ponto no npcgen |
| NPC | NPC_ESSENCE (cópia limpa da base, `ClearNpc`) + ponto |
| Vendedor | + NPC_SELL_SERVICE ligado em `id_sell_service`; `id_buy_service` usa um serviço de compra existente |
| Fornalha | + NPC_MAKE_SERVICE em `id_make_service` + RECIPE_ESSENCE para cada receita nova |

## Regras do gs (fontes)

- Serviço inválido no NPC: o gs pula o NPC (`npcgenerator.cpp`).
- Soma das chances dos `targets` da receita tem de ser 1: senão o gs ignora a receita (`recipe_manager`).
- `id_skill` da receita tem de ser igual a `id_make_skill` do serviço: senão o jogador recebe `ERR_SKILL_NOT_AVAILABLE`
  (`produce_provider::TryServe`). Receitas novas usam a habilidade do serviço.
- Material com quantidade 0 é ignorado com aviso; material repetido gera aviso. O editor bloqueia os dois.
- Até 8 páginas × 32 espaços. IDs novos por `IdRefactor.NextFreeId`.

As checagens ficam em `NpcBuilder.Validate` (antes de criar) e em `ItemRules` (ao salvar).

## Desfazer

Enquanto o rascunho está aberto, Ctrl+Z e Ctrl+Y valem na janela. Depois de criado: o ponto sai pelo Ctrl+Z do Mundo 3D e os
registros do elements pelo Ctrl+Z do editor (cada um tem a sua pilha).

## Testes

`--npc-builder-test` cria os quatro tipos na memória e confere as regras. Teste na tela: criar um vendedor e uma fornalha.

## Mudança de 06/11 (teste 28)

A ferramenta passou a se chamar **NPCs e monstros**. Ela abre com quem está selecionado no mundo, **Criar novo** e **Editar o selecionado**, que fica apagado sem seleção. Editar usa os mesmos passos; monstro abre no Elements. Na edição, loja, fabricação e teleporte são linhas separadas: cada uma entra e sai sozinha. Tirar um serviço só desliga o NPC, e o registro fica para quem mais usa.
