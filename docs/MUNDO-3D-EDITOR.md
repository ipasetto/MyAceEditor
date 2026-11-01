# Mundo 3D como editor de mapas — o que existe nos editores estudados e o nosso roteiro

Base de estudo (31/10/2026). **Só referência de como as coisas funcionam**: o código é nosso, escrito do zero, no padrão
do MyAceEditor (WinForms + OpenTK, UiKit, Ctrl+Z em tudo, `docs/REGRAS-DO-JOGO.md`). Detalhes de formato na PW-KB §S32.

| Fonte | O que é | Como usamos |
|---|---|---|
| `Estudo PW\CPW_Dev\CElement\CElementEditor` | editor **oficial** de mapas da Perfect World (MFC, 442 arquivos, código-fonte) | ler para entender formatos e regras; nada copiado |
| `PW\Angelica\AngelicaCore_V2\Editor\src_deobf` | Angelica Core (C#, família do RAE): editor de mapa 3D em SharpDX e demais módulos; mapa em SOURCE-MAP §M19 | referência de formatos e funções; código nosso |

## 1. O que o editor oficial faz (pelos menus e janelas dele)

### Arquivo / projeto
- Mapa dividido em **projetos** (blocos do mundo), área de trabalho, abrir/salvar/salvar como.
- **Exportar dados do jogo**: gera os arquivos que o cliente e o servidor leem (`.ecwld`, `.ecbsd`, terreno, npcgen,
  precinct, region, path.sev, map.bht, mapas de passagem). Exportar/importar só a parte de IA (pontos de NPC/monstro).
- Exportar recursos usados, vista de cima do mapa (imagem), lista de luzes, texturas de céu usadas.
- Ajustar a altura de todos os objetos, recortar heightmap automático, rearrumar o mapa, mapa aleatório.
- Conferências: "a área de jurisdição cobre o mapa inteiro?", contagem de monstros, triângulos, modelos mudados.

### Vistas e câmera
- 4 vistas: frente, cima, lado e perspectiva; arame (wireframe); reiniciar câmera; recarregar texturas.

### Objetos da cena (painel "Objetos" → Adicionar / Modificar)
| Objeto | Para que serve | Arquivo que muda |
|---|---|---|
| Modelo estático | casas, pedras, pontes | `.ecwld` + `.ecbsd` (cliente), colisão do servidor |
| Modelo animado / dinâmico | moinhos, portões, objetos que o servidor liga/desliga | `.ecwld`; objetos dinâmicos (lista própria, ID + caminho) |
| Árvore (modelo marcado como árvore) e grama | vegetação, com tipos, densidade, tamanho, maciez, distância de vista | `.ecwld` (árvores), `.ecbsd` (grama) |
| Água | lago/rio com altura e área | `.ecbsd` |
| Luz | luz pontual com cor e alcance | `.ecwld`, mapa de luz |
| Som | som ambiente em uma área | `.ecwld` |
| Efeito (gfx) | fogo, brilho, fumaça | `.ecwld` |
| Grupo de criaturas | pássaros, peixes, bichos de enfeite | `.ecbsd` |
| Caminho (bezier) | rota de patrulha de monstro/NPC | `path.sev` (servidor) |
| Área (caixa) | neblina, céu, música, luz da área | `.ecbsd` |
| **Gerador de monstros/NPC** | ponto ou área onde nascem, quantidade, tempo de renascer, altura, agressivo/passivo, grupo, pedido de ajuda, caminho, tempo para sumir | `npcgen.data` (servidor) |
| NPC fixo | NPC parado num lugar | `npcgen.data` |
| Coleta | minério/erva: tipo, quantidade, renascer, altura | `npcgen.data` |
| **Controlador** (gatilho) | liga/desliga geradores por data, hora, dia da semana, espera e tempo ativo | `npcgen.data` (controladores) |
| **Jurisdição (precinct)** | nome da área, música dia/noite, ponto de renascer, NPCs da área | `precinct.clt` (cliente) + `precinct.sev` (servidor) |
| Faixa (region) | zona segura, PK, portal/teleporte | `region.clt` + `region.sev` |
| Área de instância | entrada/limite de masmorra | `region.sev` / instância |
| Nuvens e céu | caixas de nuvem, céu em cubo/esfera (6 imagens), dia/noite | `.ecwld`, configurações de luz |
| Marcador (dummy) e indicador de coordenada | referências de posição | só no editor |

Toda operação tem **mover / girar / escalar / selecionar vários / copiar / colar / apagar** com desfazer
(`SceneObjectMoveOperation`, `UndoMan`...).

### Terreno (painel "Terreno")
- Altura: puxar/empurrar, alisar, ruído, esticar, zerar; importar/exportar heightmap; emendar mapas vizinhos.
- Pintura de textura por camadas (máscara) e alisar a máscara; luz do terreno (lightmap) e dia/noite.
- **Passagem dos NPCs**: marcar pontos andáveis/não andáveis, cascos (convex hull) bloqueados, calcular o mapa de
  passagem, semente; **passagem do jogador** e de construções; gerar o mapa de passagem no espaço (voo); juntar mapas.
- Ferramentas: achar monstro, inserir monstros de uma lista no mapa, exportar NPCs para texto, `Coord_data.txt`.

### Regra que vem do editor oficial
Mudar **terreno ou objetos sólidos** no cliente obriga a gerar de novo os mapas de colisão/passagem do **servidor**
(`map.bht`, `.rmap/.dhmap`, octree de voo). Sem isso o servidor anda e bate no relevo antigo. Mudar só NPCs/quests/áreas
não precisa (par cliente + servidor, PW-KB §S7).

## 2. O que o Angelica Core (ACE.PWMap) mostra
Editor de mapa em DirectX com histórico de mudanças (adicionar, apagar, clonar, propriedade, terreno),
exportar para cliente e para servidor, gerar a octree de passagem, leitores de bht/rmap/dhmap/wmap/ecwld/ecbsd/trn2/bmd.
Confirma que o caminho do editor oficial é possível fora dele.

## 3. O nosso Mundo 3D hoje
| Já temos | Arquivo |
|---|---|
| Terreno (.trn2), água, céu, dia/noite (L), neblina (N), modelos de construção (.bmd), árvores (.spt), grama | `UI/WorldView.cs`, `WorldTrees.cs`, `WorldGrass.cs` |
| Pontos do npcgen: ver, filtrar, achar, criar, copiar, mover com setas X/Y/Z, girar, apagar, desfazer | `UI/NpcGenView.cs`, `PerfectWorld/NpcGenFile.cs` |
| Eventos (controladores do npcgen) na legenda | `NpcGenFile.cs` |
| Mapa da tecla M, teleporte da câmera, entrada das instâncias (region.sev) | `InstanceMap.cs`, `RegionFile.cs` |
| Quests: ver quests do NPC, criar (recado, etapas falar / ir até um lugar com áreas marcadas no chão), dar quest existente, editar, excluir | `WorldQuestPanel.cs`, `WorldQuestEditor.cs`, `QuestBuilder.cs` |
| Modelos de NPC/monstro parados (regra: estáticos) | `UI/WorldModels.cs` |

## 4. Roteiro (do mais fácil e útil ao mais difícil)
Cada passo: ler → provar gravação **idêntica** ao original → só então editar; Ctrl+Z; funcionar em todas as versões.

1. **Quests no 3D, tipos 3+** — [x] matar monstros, obter itens, entregar itens a um NPC, esperar (testados em 4 versões + tela). Escolher o ponto de coleta no mundo para "obter itens" [x]. Item que só cai na quest (na etapa de matar) [x]. Repetir (diária/semanal/mensal/anual) e horário em que a quest existe [x]. Proteger/escoltar NPC (objetivos 8 e 9): ignorados (decisão do usuário; nenhum cliente usa): matar monstros (clicar no gerador no mundo), coletar (clicar na coleta), entregar item,
   escoltar (caminho). Usa o que já lemos do npcgen.
2. **Geradores completos no 3D** — [~] painel com todos os campos do monstro/NPC e área de nascimento desenhada no chão prontos; falta arrastar a área no 3D para mudar o tamanho: painel de propriedades como o oficial (quantidade, renascer, altura, agressivo,
   grupo/ajuda, caminho, sumir) e **área** de nascimento desenhada no chão; coleta igual.
3. **Controladores (eventos)**: editar data/hora/semana/espera/ativo e ligar geradores a eles, vendo no mundo.
4. **Caminhos (`path.sev`)**: ver as rotas de patrulha no chão, editar pontos (formato `SevBezier`), ligar a geradores.
5. **Áreas (`precinct` / `region`)**: ver e editar caixas/polígonos de nome de área, música, renascer, zona segura,
   portais — sempre o par `.clt` (cliente) + `.sev` (servidor).
6. **Objetos do cenário (`.ecwld` / `.ecbsd`)**: primeiro ler tudo (luzes, sons, efeitos, criaturas, caixas de área,
   água), depois mover/girar/apagar/clonar objetos e plantar árvores/grama.
7. **Terreno**: pincéis de altura e de textura; com isso, **gerar de novo bht/rmap/dhmap do servidor** (passo
   obrigatório antes de liberar edição de terreno ou de objetos sólidos).
8. **Passagem e voo**: ver e corrigir o mapa de passagem; gerar octree de voo.

Antes de cada passo: consultar `/pw-engine` (PW-KB §S32 e SOURCE-MAP para o arquivo do editor oficial que mostra o formato).
