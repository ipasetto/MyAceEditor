# PARIDADE com o Rody's Angelica Editor (RAE) — checklist
# fonte: textos de interface do RAE (Lang/MainWindow.csv, 287 entradas) | [x] temos [~] parcial [ ] falta | prioridade: A alta, B media, C baixa
# regra: mesma funcao/organizacao, codigo NOSSO. Atualizar ao concluir.

## 1. ARQUIVO
[x] abrir diretorio do jogo | [x] abrir elements.data | [x] salvar (backup+atomico) | [x] salvar como
[ ] A salvar tudo (elements+tasks+gshop+configs de uma vez) | [ ] B salvar versao cliente (elements sem dados de servidor)
[ ] B perfis (abrir/salvar perfil = conjunto de arquivos) + recentes | [ ] B arquivos recentes | [ ] C fechar arquivo
[ ] A salvar item_color.txt / item_ext_desc.txt (exige gravar configs.pck) | [ ] C abrir local do arquivo
[ ] C protecao de elements/tasks/PCK (nao prioritario; anti-edicao)

## 2. EDITAR
[ ] A desfazer / refazer + historico de acoes
[~] A pesquisar (temos busca nome/ID + Buscar por campo) | [ ] A pesquisa avancada (todas as listas, varios criterios) | [ ] A substituir (valor em massa)
[ ] A copiar/colar itens (entre listas e entre arquivos, com dependencias, ID unico automatico, com desc e cor)
[ ] B copiar nome/modelo/drops/valor/pagina/tudo | [ ] B importar/exportar itens (arquivo)
[ ] A escanear lista/elements por erros (+ corrigir) | [ ] B corrigir IDs de quest in/out dos NPCs via tasks.data | [ ] C corrigir tipos invalidos de monstros
[ ] B alterar valores global (mesmo campo em muitos itens)

## 3. LISTA DE ITENS (menu de contexto)
[x] seletor de blocos agrupado por categoria | [x] adicionar | [x] clonar | [x] excluir | [x] favoritar | [x] ordenar por ID/nome (coluna)
[~] A selecao multipla (Ctrl/Shift/Ctrl+A) + lote: [x] clonar (Ctrl+N) [x] excluir (Del) [x] alterar campo [x] copiar IDs [ ] copiar/colar itens
[ ] B mover itens (cima/baixo/topo/fim) | [ ] B ordenar selecionados (ID/nome/valor, cresc/decr)
[ ] A mudanca de ID avancada / refatorar ID (trocar em elements+tasks+gshop+npcgen) | [ ] B definir ID unico | [ ] B copiar IDs
[ ] B adicionar ao GShop / procurar no GShop | [ ] A traduzir itens selecionados (Ctrl+T)
[ ] B descricao: anexar texto, mudanca avancada, limpar, salvar | [ ] B criar receita/roupa/ovos/NPC/quest a partir dos selecionados
[ ] C gerar dados de pedra | [ ] C salvar icones dos selecionados
[x] nome colorido (cor oficial) | [ ] B definir tag de cor | [ ] B renomear em massa | [ ] B itens com nome similar

## 4. DETALHE DO ITEM
[x] Valores com sub-abas (conjunto de campos) | [x] coluna de vinculo (nome/%/classes) | [x] Referencias/Vinculos (quem usa) | [x] Buscar | [x] Descricao e cor
[~] A editores por duplo clique: [x] mascara de classes (checkbox) [x] addons (lista c/ descricao e filtro por tipo, multi preenche slots seguintes) [x] item/monstro/receita (seletor c/ busca e icone) [ ] protecao de item (proc_type mask) [ ] imunidades (mask) [ ] cor [ ] bool [ ] enums
[x] descricao de addon (texto como no jogo, §E3e) 
[x] A links clicaveis nos vinculos | [x] A "dropado por" no item (monstros + chance, link) | [x] efeito do addon no bloco de addons
[ ] B alterar valor (Ctrl+E) / limpar campos / copiar-colar campos / pesquisar iguais / nao iguais / itens com este ID
[ ] B definir tipo do campo / lista vinculada (custom pelo usuario)
[ ] B preview de modelo 3D / de receita
[ ] B editar descricao (item_ext_desc) e cor (item_color) do item

## 5. FERRAMENTAS
[x] abrir/ler PCK (nativo) | [ ] A descompactar/compactar PCK (gravar)
[ ] B unificar elements.data (join) | [ ] B unificar item_ext_desc | [ ] B unificar tasks (novas quests, mascara, tipo)
[ ] B geradores de mascara: classes / protecao de item / imunidades de monstro | [ ] C gerador de octets
[ ] B exportar lista / NPCs p/ NPCGen / monstros p/ AIPolicy / tabela / addons / icones .jpg
[ ] B traducao de elements com outro elements (localizacao) | [ ] A auto-tradutor (nosso diferencial)
[ ] C permissoes do elements | [ ] C limpar cache de icones | [x] favoritos

## 6. EDITORES SEPARADOS
[~] Quests (tasks.data) — falta: premios/listas editaveis, criar/clonar/excluir, outras versoes
[ ] A GShop (gshop.data) | [ ] A NPCGen (npcgen.data) | [ ] B domain2.data | [ ] B dynamicobjects / path.data
[ ] A AI Policy (nao esta no menu do RAE, mas no pacote: NextAIPolicyEditor2)

## 7. SERVIDOR
[x] verificacao do par cliente x servidor (nosso diferencial)
[ ] B enviar elements/tasks/gshopsev para o servidor + reiniciar GS + comando custom + console (SSH)

## 8. INTERFACE
[x] voltar/avancar | [x] favoritos | [x] tooltip c/ drops | [ ] B separadores/abas configuraveis | [ ] B atalhos (links) | [ ] C idiomas
