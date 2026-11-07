# Objetos do mundo no Mundo 3D — criar, editar e apagar — desenho

Data: 07/11/2026 · Versão do jogo: só 1.5.5 · Status: aguardando revisão do usuário
Entrevista com o usuário em 07/11/2026; abordagem escolhida: rascunho em memória.

## 1. O que o usuário pediu
- Ferramenta **leve**: o trabalho pesado roda em segundo plano; a tela nunca espera.
- **Nada é gravado nem enviado** até o Salvar.
- **Primeiro visual**: mexer vendo o mundo.
- Ao abrir a ferramenta, já dá para editar o pedaço do mapa onde a câmera está **e os 8 vizinhos (3x3)**; ao andar, os
  novos vizinhos entram sozinhos.
- Cada objeto mostra as informações dele **como num programa de modelagem 3D**.
- Funções principais: **criar, editar, apagar**.
- **Tratamento de erro em tudo** (regra do usuário de 07/11).

## 2. Escopo
| Entra | Fica de fora |
|---|---|
| Construções e estruturas (`.bmd`: casas, pontes, muros, pedras grandes) | Efeitos e sons: continuam como já são editados hoje nesta ferramenta |
| Árvores (`.ecwld`, uma a uma) | Pincel de árvores (espalhar em área) |
| Grama e flores (pincel + borracha) | Terreno (relevo) e textura do chão: ferramenta própria, depois |
| Modelos decorativos (ECmodels: moinho, bandeira, portão), sempre parados | Animação dos modelos |
| Luzes do cenário (lugar, cor, alcance, quando acende) | Modelos trazidos de outro programa (.obj) |
| Mundo principal e todas as instâncias do seletor de mapa | Outras versões do jogo |

## 3. Como se usa
### Abrir
- O ladrilho **"Objetos do mundo"** do menu flutuante do Mundo 3D (`Tool.Scene`, hoje com botões cinza de "Próximas
  ferramentas") abre a ferramenta; o painel toma o lugar do menu, com a seta de voltar (regra do Mundo 3D).
- Em segundo plano, o editor abre os 9 pedaços em volta da câmera no **rascunho**; um aviso discreto "Carregando
  objetos… 4/9" não trava nada. Ao andar, os vizinhos novos entram; pedaços longe **e sem mudança** são soltos.

### Mexer no que existe
- O clique pega **qualquer objeto à vista, de qualquer tipo** (construção, árvore, modelo, luz e também efeito, som e
  bichos, que já estão nesta ferramenta).
- O escolhido ganha **contorno** e o **cartão de informações**: tipo, nome e arquivo, posição X/Y/Z, giro, escala,
  tamanho (largura × altura × profundidade) com a caixa desenhada no mundo, quantas vezes o modelo aparece no mapa e o
  interruptor **Colisão** (padrão: a mesma dos objetos iguais que já existem).
- Os números do cartão e o que se vê andam juntos.
- **Setas X/Y/Z, anel de giro** (os mesmos dos NPCs), **"Grudar no chão"** (liga/desliga), **Delete** (apaga na hora;
  Ctrl+Z traz de volta), **Ctrl+C / Ctrl+V** (copiar do mundo).

### Inserir
- Os botões cinza viram as pílulas **Construções | Árvores | Grama | Modelos | Luzes**: a pílula diz **o que vai ser
  inserido**.
- Construções, Árvores, Modelos e Luzes: escolher a pílula abre o **catálogo** daquele tipo (a JAV,
  `UI/ItemSearchWindow.cs`, com a prévia 3D que ela já tem); depois **cada clique no chão coloca um**, até Esc ou botão
  direito.
- **Grama**: o clique vira pincel (tipo, tamanho, densidade) com borracha.
- **Luzes**: o cartão mostra cor, alcance e quando acende; a prévia mostra a luz acesa (passa para a noite, tecla L,
  se precisar).
- Saem do painel: "Pincel de textura do chão" (vai para a ferramenta de terreno) e "Muro e muralha" (vira construção
  comum no catálogo).

### Ajudas visuais
Contorno do selecionado; interruptor **Ver colisão** (caixas onde o jogador bate).

### Regras de sempre
Ctrl+Z/Ctrl+Y em tudo; Esc cancela o passo em andamento e depois sai da ferramenta (trabalho não salvo pergunta se
abandona); textos via `Loc.T` em pt-BR, en, ru e zh-CN; espaço livre embaixo; padrão para leigos.

## 4. Por dentro
### Rascunho (`PerfectWorld/WorldEdit/`)
| Peça | O que faz |
|---|---|
| `WorldDraft` | pedaços abertos; por pedaço, os arquivos lidos e a lista de objetos por tipo (id estável, modelo, posição, giro, escala, colisão); estado "só olhar" com motivo |
| `DraftOps` | cada mudança é um passo pequeno com o seu desfazer (inserir, mover, girar, escalar, apagar, colisão, pincelada); o Ctrl+Z guarda só a diferença |
| `DraftChecks` | as conferências da seção 5 |
| `DraftSaver` | monta todos os arquivos em memória, confere, grava tudo ou nada, refaz a colisão do servidor |
| Formatos | reaproveita `EcwldFile`, o leitor de `.ecbsd`, o de `.bmd` (`WorldBuildings`/`BuildingSave`), `BhtFile`, `PckWriter`, `BackupStore`, `AtomicFileSave`; ganha os que faltam (luzes, grama) |

### Desenho
`WorldBuildings`, `WorldTrees`, `WorldGrass` e `WorldModels` passam a ler do rascunho. Montar malhas e miniaturas roda
em segundo plano; a tela só troca o que ficou pronto.

### Tela
`UI/WorldView.ObjectTool.cs` (painel, pílulas, cartão, pincel). Catálogo = JAV. Janela de salvo = `SavedDialog`.

## 5. Salvar e conferir
1. **Resumo em frases**, por pedaço ("3 casas movidas, 12 árvores novas, 1 luz apagada em world 12_08…").
2. Montar todos os arquivos em memória.
3. Conferências:
   - **Bloqueiam**: objeto fora da borda do mapa ou do pedaço; pedaço acima do limite de objetos/tamanho do formato.
   - **Avisam** (com botão "levar até ele"): objeto flutuando ou enterrado; objeto sólido em cima de NPC, ponto de
     nascer, teleporte ou caminho de patrulha.
4. Gravar **tudo ou nada**: cópia de segurança (`BackupStore`) de cada arquivo, escrita segura; se qualquer um falhar,
   todos voltam ao que eram. Inclui o `map.bht` do servidor quando um objeto sólido mudou.
5. `SavedDialog` com o resumo e a oferta de enviar ao servidor.

## 6. Tratamento de erro
- **Abrir**: pedaço que não lê vira "só olhar" com o motivo; os outros seguem. Falta de memória: solta pedaços longe
  e avisa.
- **Catálogo**: modelo com arquivo faltando ou ilegível aparece apagado, com a dica.
- **Cada passo** é conferido antes de entrar no rascunho (valores fora da faixa, objeto sem arquivo); recusa com
  frase simples.
- **Salvar**: tudo ou nada (seção 5); arquivo preso pelo jogo ou por outro programa, disco cheio ou servidor fora
  viram mensagem com o que fazer; as mudanças continuam no rascunho.
- **Log**: detalhe técnico (arquivo, posição, exceção); para o usuário, frase curta.

## 7. Etapas
Cada etapa começa pelo estudo do formato e pela prova de **regravação idêntica** antes de editar (regra do projeto),
e consulta antes a PW-KB (§S6 colisão, §S17 `.ecwld`/`.ecbsd`, §S32 editor oficial) e o SOURCE-MAP (§M17 CElementEditor).
1. **Base**: rascunho com 3x3 em segundo plano; seleção de qualquer tipo; cartão de informações; mover, girar e
   apagar construções e modelos que já existem (mover construção já existe e entra no rascunho); Ver colisão; Salvar
   tudo ou nada com as conferências. Nesta etapa a escala e a colisão aparecem no cartão só para leitura.
2. **Inserir construções e modelos** pelo catálogo e por copiar; escala; interruptor de colisão.
   Estudar: como nasce uma construção nova (cópia do `.bmd` + entrada no `.ecbsd` + brushes no `map.bht`); se o
   formato aceita escala; limite de objetos por pedaço.
3. **Árvores** uma a uma. Estudar: colisão de árvore no `map.bht`.
4. **Luzes**. Estudar: formato das luzes (`scenelights.dat` e luzes do `.ecwld`) e a opção "quando acende".
5. **Pincel de grama**. Estudar: gravação das áreas de grama (`ECBSDFILEGRASS7` + mapa de bits no `bsdata`).

## 8. Conferência
- Linha de comando por etapa: abrir um mapa, aplicar mudanças sem tela, salvar numa cópia e reler (mesma ideia de
  `--zone-test` e `--world-shot`).
- Captura de tela fora da janela (`--world-shot`) para o painel e as pílulas.
- No jogo (usuário): objeto novo aparece no lugar, colisão bate, nada some depois de reiniciar o servidor.

## 9. Registro
ENGINE-KB §E6/§E7 (arquivos e ferramenta), PW-KB (cada fato novo de formato, na seção certa), `docs/MUNDO-3D-EDITOR.md`
(passo 6 do roteiro), `docs/AFAZERES.md`.
