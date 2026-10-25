# Testes pendentes (para o usuário)

Marque com `x` o que funcionou e anote o que não funcionou (com print e, no Mundo 3D, a coordenada do rodapé).
Trabalhe numa cópia da pasta do jogo/servidor até tudo estar conferido.

## 1. Salvar
- [ ] Mudar algo no elements, numa quest, na loja e no npcgen → **Ctrl+S** mostra os 4 arquivos numa confirmação só e grava todos (com cópia de segurança ao lado).
- [ ] **Arquivo → Salvar automaticamente (a cada 5 min)**: ligar, mudar algo, esperar 5 min → rodapé mostra "Salvo automaticamente…".
- [ ] Fechar o programa com algo não salvo → pergunta Salvar tudo / Sair sem salvar / Cancelar.
- [ ] A loja só salva sozinha se o gshopsev do servidor já foi escolhido uma vez.

## 2. Monstro/NPC novo
- [ ] Barra de cima **✚ Monstro/NPC novo** → escolher o tipo, **Escolher…** o modelo de base (prévia 3D aparece), nome e ID sugeridos → **Criar** → abre o item novo no editor.
- [ ] No Mundo 3D, botão direito no chão → **Criar monstro/NPC NOVO aqui** → depois de criar, o ponto já aparece no lugar.
- [ ] Salvar e conferir no jogo que o monstro novo nasce (precisa do servidor com o elements e o npcgen novos).

## 3. Listas e prévias
- [ ] Lista de monstros/NPCs mostra miniaturas do modelo 3D (aparecem aos poucos, sem travar a rolagem).
- [ ] Duplo clique no campo **file_model** de qualquer item → lista de modelos com prévia 3D; arrastar gira.
- [ ] Seletor de monstro/NPC/coletável (novo ponto, trocar) → prévia gira arrastando.

## 4. Mundo 3D — visual
- [ ] Caixa de correio e outros objetos presos a ossos ficam em pé (ex.: Ir para `-2526 4090`).
- [ ] Rochas/pilares do cânion inteiros, sem placas soltas no ar (Ir para `-2810 620 -2300` e olhar ao redor; também `-2851 499 -2560`).
- [ ] Se ainda houver algo flutuando: anotar a coordenada.
- [ ] Instância **b30**: o chão aparece e os NPCs estão em cima dele (tecla **T** esconde/mostra o terreno — confira se não ficou escondido).
- [ ] Cores dos nomes: cidade em branco; verde = evento com data; lilás = gatilho manual/fase de DG; vermelho = guarda; ciano = coletável.
- [ ] Seletor **Mapa:** mostra os nomes (world — Mainland, a05 — Firecrag Grotto…) e troca de mapa.

## 5. Mundo 3D — fluidez
- [ ] Cidade do Dragão (muitos NPCs): andar e girar sem travadas.
- [ ] Teleportar (mapa **M**, botão direito) para `-665.7 -2806.08` (digite no **Ir para**): chão e construções aparecem em ~1 s sem congelar.

## 6. Mundo 3D — edição
- [ ] Clique ao lado de um NPC não seleciona; em cima seleciona.
- [ ] Esquerdo segurado em cima do NPC e arrastado → muda a posição; Ctrl+Z desfaz.
- [ ] **Delete** (ou botão direito → Excluir) exclui com confirmação; Ctrl+Z traz de volta.
- [ ] **Ctrl+C** no selecionado, mouse no chão, **Ctrl+V** → cópia no lugar.
- [ ] Botão direito → **Editar … no software** → o editor vem para a frente no item.
- [ ] Mudar o nome no editor e voltar ao 3D → o nome novo aparece.
- [ ] Botão direito → Novo monstro/NPC/coletável → o seletor fica na frente do 3D.

## 7. No jogo (quando o servidor estiver pronto)
- [ ] Ponto criado, movido, clonado e excluído aparece/some no lugar certo.
- [ ] NPC novo e coletável novo funcionam (clicar, coletar).
- [ ] Monstro criado pelo assistente nasce com o nome novo.
