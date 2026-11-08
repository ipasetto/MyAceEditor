# Terreno e textura do chão — estudo (08/11/2026)

Estudo para a ferramenta de terreno do Mundo 3D (passo 7 do roteiro em `docs/MUNDO-3D-EDITOR.md`). Não é o desenho
final: termina com as decisões para o usuário.

## 1. O que já sabemos (PW-KB §S6 e linhas 603–618)

| Parte | Cliente | Servidor | Já lemos? |
|---|---|---|---|
| Altura do chão | `<mapa>_N.t2bk` "BKBT" v5/v6: 256 blocos por pedaço de 512 m, cada um 33×33 floats em metros (zlib, 1 amostra de sobreposição), linha 0 ao norte | `map/<N>.hmap`: 513×513 floats (valor × 800 = metros), feito do `.t2bk` | sim (o Mundo 3D desenha o terreno; hmap conferido) |
| Textura do chão | lista de texturas no `.trn2` (+0x48); `<mapa>_N.t2mk` "BKMT" v2: 16 áreas de 256 m por pedaço; camadas de 28 B; máscara zlib 256×256 bytes (1 m/px) por camada; 1ª camada = base sem máscara | nada | sim (desenhado) |
| Luz e sombra do chão | `<mapa>_N.t2lm` (dia) / `.t2lm1` (noite) "ML2T" v2: 16 áreas, .dds 256×256 (1 m/px); a sombra das construções está pintada aqui | nada | sim (desenhado) |
| Grama | áreas de grama seguem a altura do chão no jogo (`altura = chão − 0,02`) | nada | sim (pincel pronto) |
| Colisão de objetos | — | `map.bht` (só brushes de construções/modelos/árvores; não tem o chão) | sim |
| Passagem a pé dos NPCs | — | `movemap/<N>.rmap` (bits por célula; formato parcial) e `.dhmap` (não estudado) | parcial |
| Água | — | `watermap/<N>.wmap` | formato conhecido |
| Voo | — | `airmap/<N>.octr` + `spmap.conf` | não estudado |

Editor oficial (fonte em `D:/Perfect World/Estudo PW/CPW_Dev/CElement/CElementEditor`): `Terrain.cpp`,
`A3DTerrain2ForEditor`, `A3DTerrain2MaskForEditor`, `AutoMask`, `Brush`, `ExpTerrain.cpp` (exporta .t2bk/.t2mk),
`LightMapGen.cpp` + `LightMapMerger*` (luz e sombra), `PathMap/` (`NPCMoveMap`, `PassMapGenerator`,
`TerrainObstructGenerator`, `WaterAreaMap`, `PlayerPassMapGenerator`) e `SPOctree/` (voo), `ExpGameDataDlg.cpp`
(exporta os mapas do servidor).

## 2. O que cada pincel precisa

### 2.1 Pincel de altura (subir, descer, alisar, nivelar)
- Mexe nos floats do `.t2bk` (bloco por bloco; recomprimir só os blocos tocados, o resto byte a byte).
- Vizinhos: a amostra de borda é repetida entre blocos e entre pedaços: o pincel tem que gravar os dois lados.
- No mesmo salvar: refazer o `map/<N>.hmap` do servidor dos pedaços tocados (fórmula conhecida, conferida).
- Efeitos colaterais a tratar: grama e árvores acompanham sozinhas (usam a altura na hora); construções e NPCs não:
  oferecer "assentar no chão" para os objetos da área mexida.
- **Passagem dos NPCs (.rmap/.dhmap) fica errada** se o relevo mudar muito: precisa estudar o formato e o
  `NPCMoveMap`/`PassMapGenerator` para refazer (ver 3).

### 2.2 Pincel de textura do chão
- Mexe nas máscaras do `.t2mk` (256×256 por camada e área); textura nova no mapa = entrada nova na lista do `.trn2`
  e camada nova na área (até o limite que o cliente aceita: conferir `A3DTerrain2Mask`).
- Só o cliente muda (o servidor não sabe da textura). É o mais seguro para começar.
- JAV de texturas do `textures.pck` (`Textures\Maps\...`) com prévia, estrela e recentes.

### 2.3 Luz e sombra do chão (`.t2lm`)
- Depois de mexer em altura ou mover construções, a sombra antiga fica no chão. Refazer exige calcular a luz
  (`LightMapGen.cpp`): sol da área + sombra dos objetos. É o mais pesado; pode ficar para depois com uma opção simples
  (clarear/escurecer à mão com pincel, ou apagar a sombra de um objeto movido).

## 3. Servidor depois de mexer no relevo
- `hmap`: simples (refazer dos blocos tocados).
- `rmap`/`dhmap`: precisam de estudo do formato (temos o fonte do gerador oficial em `PathMap/`). Sem isso, mudar muito o
  relevo deixa NPCs andando "no relevo antigo". Proposta: liberar o pincel de altura só com aviso e limite de altura
  até o gerador estar pronto, ou estudar o gerador primeiro.
- `octr` (voo): estudar só se o relevo mudar acima do teto de voo.

## 4. Proposta de etapas (para aprovar)
1. **Textura do chão** (só cliente): pincel com JAV de texturas, força, tamanho, borracha (volta a camada de baixo),
   Ctrl+Z por pincelada, salvar com backup da ferramenta. Menor risco.
2. **Altura** (cliente `.t2bk` + servidor `.hmap`): subir, descer, alisar, nivelar, com a grama e as árvores
   acompanhando e "assentar objetos" na área.
3. **Passagem dos NPCs**: estudar `.rmap/.dhmap` + gerador oficial e refazer só os pedaços tocados.
4. **Luz e sombra**: pincel simples de luz e "apagar sombra" de objeto movido; cálculo completo depois.

## 5. Decisões para o usuário
- Começar pela textura (etapa 1) e deixar a altura para depois do estudo da passagem dos NPCs, ou fazer a altura já com
  aviso de que os NPCs podem andar no relevo antigo até a etapa 3?
- Na textura: borracha volta para a camada de baixo (como o editor oficial) — está bom?
