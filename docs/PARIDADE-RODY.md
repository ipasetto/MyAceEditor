# PARIDADE com o Rody's Angelica Editor (RAE) — checklist
# fontes: textos de interface do RAE (Lang/MainWindow.csv, 287 entradas) + 175 prints da versao 2026.9.1 beta (24/10/2026, usuario)
# [x] temos [~] parcial [ ] falta | prioridade: A alta, B media, C baixa
# regra: mesma funcao/organizacao, codigo NOSSO (prints = so referencia de tela). Atualizar ao concluir.

## 1. ARQUIVO
[x] abrir diretorio do jogo | [x] abrir elements.data | [x] salvar (backup+atomico) | [x] salvar como | [x] salvar em outra versao (converter)
[ ] A salvar tudo (elements+tasks+gshop+configs de uma vez) | [ ] B salvar versao cliente (elements sem dados de servidor)
[ ] B perfis/projetos (nome do projeto, conjunto de arquivos cliente+servidor) + recentes | [ ] B arquivos recentes | [ ] C fechar arquivo
[ ] A salvar item_color.txt / item_ext_desc.txt (exige gravar configs.pck) | [ ] C abrir local do arquivo
[ ] B 1a execucao: perguntar pasta do jogo e associar .data ao programa (abrir pelo Explorer)
[ ] C protecao de elements/tasks/PCK (nao prioritario; anti-edicao)

## 2. EDITAR
[x] desfazer / refazer + historico de acoes
[~] A pesquisar (temos busca nome/ID + Buscar por campo) | [ ] A pesquisa avancada: aba Pesquisar com fonte (lista atual/todas), campo, condicao (=,<,>,contem), valor; resultados com "manter resultados", "corrigir bugs selecionados", "alterar resultados selecionados"
[x] alterar valores em lote (definir, somar, multiplicar, sequencia, aleatorio, substituir texto) | [ ] B + ordenar crescente/decrescente, remover duplicados
[ ] A copiar/colar itens (entre listas e entre arquivos, com dependencias, ID unico automatico, com desc e cor)
[ ] B copiar nome/modelo/drops/valor/pagina/tudo | [ ] B importar/exportar itens (arquivo)
[x] escanear elements por erros (+ corrigir probabilidades) | [ ] B corrigir IDs de quest in/out dos NPCs via tasks.data | [ ] C corrigir tipos invalidos de monstros

## 3. LISTA DE ITENS (menu de contexto)
[x] seletor de blocos agrupado por categoria | [x] adicionar | [x] clonar | [x] excluir | [x] favoritar | [x] ordenar por ID/nome (coluna)
[~] A selecao multipla (Ctrl/Shift/Ctrl+A) + lote: [x] clonar (Ctrl+N) [x] excluir (Del) [x] alterar campo [x] copiar IDs [ ] copiar/colar itens
[ ] B mover itens (cima/baixo/topo/fim) | [ ] B ordenar selecionados (ID/nome/valor, cresc/decr)
[ ] A refatorar ID (mudar ID e trocar todas as referencias: elements+tasks+gshop+npcgen) | [ ] B definir ID unico | [x] copiar IDs
[ ] B adicionar ao GShop / procurar no GShop | [~] A traduzir itens selecionados (Ctrl+T) (temos: traduzir com outro elements.data)
[ ] B descricao: anexar texto (ex.: copyright), mudanca avancada, limpar, salvar
[ ] B criar receita / roupa / quest a partir dos selecionados
[ ] C gerar dados de pedra | [ ] C salvar icones dos selecionados | [ ] C pesquisar no PWDatabase (link externo)
[x] nome colorido (cor oficial) | [ ] B definir tag de cor | [ ] B renomear em massa | [ ] B itens com nome similar

## 4. DETALHE DO ITEM
[x] Valores com sub-abas (conjunto de campos) | [x] coluna de vinculo (nome/%/classes) | [x] Referencias/Vinculos (quem usa) | [x] Buscar | [x] Descricao e cor
[~] A editores por duplo clique: [x] mascara de classes (checkbox) [x] addons (lista c/ descricao e filtro por tipo) [x] item/monstro/receita (seletor c/ busca e icone)
    [ ] protecao de item (proc_type: lista de flags com nomes, ex. "nao pode ser descartado", "nao desaparece ao morrer")
    [ ] imunidades (mask) [ ] comida de mascote (mask) [ ] ambiente de movimento (chao/agua/ceu…) [ ] enums em lista popup "Escolha um valor"
    [ ] cor de personagem (seletor de cor) [ ] bool
    [ ] servicos do NPC: duplo clique abre o seletor ja no bloco do servico; servicos combinados = mascara com checkboxes
    [ ] ponto de TP / cidade: lista de nomes de cidades | [ ] habilidade: lista de skills com descricao
    [ ] seletor de icone em grade (todas as figuras do surfaces.pck; tooltip nome/indice/uso)
    [ ] seletor de modelo (.ecm/.ski) com "so nao usados" e contagem de uso
[x] descricao de addon (texto como no jogo, §E3e) | [x] links clicaveis nos vinculos | [x] "dropado por" no item | [x] efeito do addon no bloco de addons
[x] A barras de probabilidade na propria grade: clicar/arrastar muda, cadeado fixa o slot, os livres se ajustam p/ manter Σ 100%; Σ verde/vermelho no 1o slot do grupo
[x] A editor de addons (janela): aba Chances (soquetes drop/fabricar, qtd de addons, chance de unico) + abas fabricacao/aleatorios/unicos com barras, cadeado, adicionar/remover/ordenar/ajustar 100%/dividir igual; menu de contexto da grade
[x] A editor de drop do monstro (janela): chance de 0/1/2/3 drops + lista com icone/nome/barra/cadeado, adicionar/remover/ordenar/ajustar; botoes "✎ Editor de addons" e "✎ Editor de drops" acima da grade (so aparecem quando o item tem o que editar)
[ ] B editor de probabilidades de receita (itens resultantes com barras)
[ ] B monstro: habilidades por % de HP (75/50/25) com barras de chance
[ ] B barra de valor sob a grade: numero + botoes rapidos (0.1 0.2 0.25 0.33 0.5 … / 0 1 2 5 10 100 1000 …) + slider + Σ
[ ] B menu de contexto do campo: alterar valor (Ctrl+E), mover campos, selecionar campos similares, pesquisar iguais/nao iguais/originais/itens nao encontrados, substituir nos selecionados, copiar/colar/recortar, limpar campos/pagina
[ ] B definir tipo do campo / lista vinculada (custom pelo usuario, manual ou automatica)
[ ] B preview de modelo 3D (janela separada) | [ ] A preview do servico de criar do NPC como a janela do jogo (abas Amuleto/Pingente/Anel/Mats) — ver myaceeditor-game-like-ui
[ ] B editar descricao (item_ext_desc) com botoes de cor nomeados (Azul, Dourado, Roxo, Laranja, Branco, Cinza, Verde) e previa
[ ] C curva de evolucao de mascote (lista de niveis)
[x] tooltip como no jogo | [ ] B tooltip fixavel (F2) + "Ctrl+Shift+G: ir para este item" + barras de chance nas referencias

## 5. FERRAMENTAS
[x] abrir/ler PCK (nativo) + extrair | [ ] A compactar PCK (gravar)
[ ] B unificar elements.data (join) | [ ] B unificar item_ext_desc | [ ] B unificar tasks (novas quests, mascara, tipo)
[ ] C gerador de octets (item → octets, copiar como XML/HTML)
[ ] B exportar lista / NPCs p/ NPCGen / monstros p/ AIPolicy / tabela / addons / todos os icones .jpg
[x] traducao de elements com outro elements (localizacao) | [ ] A auto-tradutor (nosso diferencial)
[ ] C permissoes do elements | [ ] C limpar cache de icones | [x] favoritos (menu Favoritos = atalhos de blocos)

## 6. EDITORES SEPARADOS
[~] Quests (tasks.data) — temos campos/textos/dialogos/listas/premios, clonar/excluir; RAE usa sTASKedit: abas Basic Properties I-V, Special Parameters, Given Requirements I-V, Complete Requirements, Awards (sucesso/falha, Awards I/II), Text Page (talks → janelas → opcoes), Extended Search; falta: mudar qtd de entradas, outras versoes, organizar em abas como o sTASKedit
[x] GShop (gshop.data + gshopsev) com a tela do jogo | [ ] B GShop: inicio/fim de venda (dia da semana/dia/mes), classe necessaria, preco por 10 moedas, "corrigir bugs automaticamente", mostrar itens nao encontrados/nome igual/ID repetido
[ ] A NPCGen (npcgen.data) | [ ] B domain2.data (guerra territorial: tempo de batalha, territorios, pontos) | [ ] B path.data (caminhos) | [ ] B dynamicobjects
[x] AI Policy

## 7. SERVIDOR
[x] verificacao do par cliente x servidor + corrigir (nosso diferencial)
[ ] B enviar arquivos ao servidor por SSH (IP/porta/usuario, caminho remoto por arquivo, compactar antes) + reiniciar GS + comando custom + console — so quando o usuario pedir (myaceeditor-server-simple)

## 8. INTERFACE / CONFIGURACOES
[x] voltar/avancar | [x] favoritos | [x] tooltip c/ drops | [x] idiomas (pt/en/zh, lang/*.json) | [ ] B separadores/abas configuraveis | [ ] B atalhos (links)
[ ] B janela de configuracoes com abas: Interface, Geral (limite de historico/desfazer, acao do duplo clique, mover itens), Icones (tamanho), Arquivos (pasta do cliente, PCKs, arquivos de dados e estado), Servidor, Diverso
[ ] B backups compactados com limite de quantidade | [ ] C editores externos configuraveis
