# Catálogo de todos os mapas (construções e árvores) — desenho

Aprovado pelo usuário em 08/11/2026 (opção "índice"). Faz parte da ferramenta Objetos do mundo do Mundo 3D
([2026-11-07-objetos-do-mundo-design.md](2026-11-07-objetos-do-mundo-design.md)).

## Objetivo
Na pílula **Construções** e na pílula **Árvores**, oferecer o que existe em **todos os mapas e instâncias** do cliente,
não só o que está carregado perto da câmera, para o usuário montar o mapa com qualquer peça do jogo.

## O que o estudo mostrou (PW-KB §S, 08/11)
- 40.079 `.bmd` em 89 mapas, todos no mesmo `litmodels.pck`; texturas no `building.pck` (comum a todos os mapas): uma
  construção de uma instância funciona em outro mapa. A luz vem embutida nos vértices (a cópia leva a luz de origem).
- Peças só de colisão (`bCollideOnly`) ficam fora (o jogo não desenha).
- O cliente acha o `.bmd` pelo nome gravado no `.ecbsd` e carrega pela referência no bloco do `.ecwld`, sem conferir pedaço
  nem mapa de origem.
- Árvores: 79 tipos (`.spt`) em 22 mapas; tronco/folhas e o "mapa composto" das folhas ficam no `trees.pck` (comum). A
  lista de tipos do mapa é lida do offset do cabeçalho (`dwTreeOff`, `iNumTreeType`; `CECScene::LoadTreeTypeData`, lista
  sem limite): dá para acrescentar tipos regravando a lista no fim do arquivo.
- Servidor: árvores não mudam nada; construção com colisão acrescenta brushes no `map.bht` do mapa de destino.
- Processamento: ler e descompactar é trabalho de disco e processador (a placa de vídeo só desenha). Usa vários núcleos.

## Desenho
1. **Índice das construções** (`PerfectWorld/WorldEdit/BuildingIndex.cs`), por cliente:
   - para cada mapa com `.ecwld`, as construções com referência (as órfãs ficam fora);
   - de cada `.bmd`: forma (assinatura estável = vértices, índices e tamanho local, a mesma da lista "Por perto"),
     tamanho, se tem colisão, se é só colisão;
   - cache por arquivo em `%AppData%\MyAceEditor\building-index-<cliente>.json` (chave = caminho + tamanho e posição no
     pacote): depois da primeira vez só relê o que mudou; as referências de cada mapa são relidas (rápido);
   - em segundo plano com progresso; vários núcleos (um leitor do pacote por núcleo); erro num arquivo = arquivo fora
     da lista e no log, nunca derruba o índice.
2. **JAV de construções**: grupo "Por perto" (o de hoje) + um grupo por mapa/instância (nome do jogo) com as formas daquele
   mapa; formas iguais em vários mapas viram uma linha com "aparece N× em M mapas"; tamanho no nome; busca; prévia 3D
   (carrega o `.bmd` na hora); estrela (lista "building", pela assinatura). Enquanto o índice não fica pronto, o grupo
   "Por perto" já funciona e a janela diz "Lendo os outros mapas… N%".
3. **Pôr uma construção de outro mapa**: mesma inserção (prévia seguindo o mouse, um clique, Ctrl+Z). No Salvar: `.bmd`
   novo na pasta do pedaço de destino, ornamento novo no fim do `.ecbsd` (40 B copiados do ornamento de origem, no
   `.ecbsd` do mapa de origem), referência nova (ID novo, 8 B) nos blocos que a caixa cobre, brushes no `map.bht` do
   destino se a original tiver colisão. Mesmo salvar tudo-ou-nada, conferências e backup da ferramenta.
4. **Árvores de todos os mapas**: a JAV de árvores lista os tipos de todos os mapas (agrupados pelo mapa de origem; os do
   mapa aberto primeiro), com prévia e estrela. Plantar um tipo que o mapa não tem acrescenta o tipo à lista do mapa no
   Salvar (lista inteira regravada no fim do `.ecwld`; cabeçalho aponta para ela; teste de regravar e reler igual).

## Regras de sempre
Ctrl+Z/Ctrl+Y; Esc; um por clique; salvar tudo-ou-nada com confirmação visual; backup por ferramenta; recarrega após
backup; textos via `Loc.T` (pt-BR, en, ru, zh-CN); tratamento de erro em tudo.

## Testes
`--objects-test`: índice de um conjunto pequeno (assinatura igual à da lista "Por perto"), plano de cópia vinda de outro
mapa (ornamento novo, referência nos blocos, brushes), tipo de árvore novo (regrava, relê, cliente acha o tipo).
`--world-shot dftcat`: JAV com grupos por mapa, prévia de construção de outro mapa, inserção de um clique.
