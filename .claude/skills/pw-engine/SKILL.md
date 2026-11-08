---
name: pw-engine
description: Arquitetura do Perfect World (client+server, protocolo, formatos), mapa das fontes C++ do jogo (servidor 1.5.3, cliente 1.5.3, engine Angelica) e da engine/editor MyAceEditor. Use SEMPRE (regra do usuario) antes de qualquer trabalho que envolva Perfect World, elements.data/tasks.data/gshop/aipolicy/npcgen, .pck/.pkx, servidor/daemons/gs, o projeto MyAceEditor, parser .cfg, itens/skills/NPCs/quests, GM, ou icones/UI do jogo.
---

# PW Engine — mapa para pular direto a informacao

> **OBRIGATORIO sempre que esta skill for chamada (regra do usuario, 01/11):**
> 1. Tudo o que for feito segue as regras do §4 e o layout do §4b (inclusive o padrao do Mundo 3D abaixo), sem excecao.
> 2. No fim da resposta, listar de forma clara **o que falta**: do trabalho atual + os itens abertos do `docs/AFAZERES.md`.
> 3. Seguir SO o limite de tokens que o usuario pedir no chat (parar ao concluir ou ao passar dele); sem parada fixa a cada 100k (usuario 08/11).

> FOCO DO USUARIO (05/11): so a versao 1.5.5. Outras versoes: o que rodar, rodou; nao reconferir.

**Regra do usuario: consultar esta skill antes de mexer em qualquer coisa de PW.** Ela nao e para ler inteira: e um
indice. Cada camada abaixo diz onde dar o Grep. Custo alvo: 1-3 Greps pequenos por pergunta.

## 1. Por onde comecar (ordem barata)
| Pergunta | Onde | Como |
|---|---|---|
| O que esta feito / falta? | `C:/Godot/MyAceEditor/docs/AFAZERES.md` | ler o topo |
| Regra que derruba o gs / item que nao funciona | `C:/Godot/MyAceEditor/docs/REGRAS-DO-JOGO.md` | ler a tabela da secao |
| Fato do JOGO (formato, campo, daemon, protocolo, GM, mapa) | `docs/kb/PW-KB.md` | Grep `## §0` (indice) -> Grep `## §S<n>` |
| Onde no NOSSO codigo / o que ja existe | `docs/kb/ENGINE-KB.md` | `§E2` capacidade->arquivo->§PW-KB, `§E6` arquivos, `§E7` features |
| Onde no codigo C++ do JOGO (gs, daemons, cliente, engine) | `SOURCE-MAP.md` (nesta pasta) | Grep `§M0` (assunto -> arquivo:simbolo) ou o nome do arquivo; depois Grep o simbolo no arquivo |
| Arquitetura para humanos | `docs/ARQUITETURA.md` | so ao mudar camada/fluxo |
| Mundo 3D como editor de mapas (o que o editor oficial faz, roteiro) | `docs/MUNDO-3D-EDITOR.md` | ler a secao do passo atual |

Caminhos `docs/...` sao relativos a `C:/Godot/MyAceEditor`. Copia versionada desta skill: `C:/Godot/MyAceEditor/.claude/skills/pw-engine/`
(a do usuario em `~/.claude/skills/pw-engine/` e copia dela; ao mudar uma, copiar para a outra).

## 2. Fontes do jogo (D:/Perfect World/source) — resumo; detalhe no SOURCE-MAP.md
- `source_server_153/cgame/gs` = gs (mapa, itens, addons, IA, GM, instancias, quests). `gs/template` = elementdataman/itemdataman.
- `source_server_153/cnet` = daemons (gdeliveryd, gamedbd, glinkd, gauthd, gfaction, uniquenamed, logservice, gacd) + `rpcalls.xml`.
- `source_client_153/CElementClient` = cliente (EC_* jogo, Dlg* janelas), `CElementData` = templates do elements.
- `Angelica2.2_src/Angelica2.2` = engine (A3D, AFile .pck, AUI).
- `D:/Perfect World/PW_Server` e `D:/Perfect World/PW_Clientes` (PW-KB §S33): servidores 1.3.6, 1.4.4v60, 1.5.5 (pw_server_162-155 = o do WSL, par do cliente PWI 1.5.5; PW_ServerFiles_1.5.5v156, par do 155 BR) e 1.7.6 (servidor extraido + cliente 1.7.6 RU com Wine), fonte do servidor 1.5.2 (source_server_152, com cskill), clientes 144/155/180 BR e PWI 1.5.5 com versao de cada arquivo. Consultar §S33 antes de abrir essas pastas.
- `D:/Perfect World/Estudo PW` (PW-KB §S32): `CPW_Dev/CElement` = cliente + ferramentas OFICIAIS (CElementEditor = editor de mapas 3D, SOURCE-MAP §M17; CTaskTemplEditor = editor de quests, §M18; Doc = comandos de console GM); `174Source.zip` = servidor 1.7.4 (nao extraido; tar.exe do Windows). `D:/Perfect World/PW/Angelica/AngelicaCore_V2/Editor/src_deobf` = Angelica Core (C#, familia RAE): referencia de formatos e funcoes, SOURCE-MAP §M19 (estudar; codigo nosso).
- Pastas com data (cgame_2014..., cnet_2015..., release_...) = mesmas fontes em outras datas: usar as sem data.
- O gs do servidor local (`/home/gamed/gs` no WSL) vem COM simbolos: `objdump -t -C /home/gamed/gs | grep <Nome>` acha a funcao no binario.
- Textos do cliente em GBK (cp936). Regerar o mapa: `python -I .claude/skills/pw-engine/mkmap.py` (quando entrarem fontes novas).

## 3. Como ler sem gastar
1. Nunca ler PW-KB (~40k tokens) nem SOURCE-MAP (~35k tokens) inteiros: Grep o cabecalho `## §...` ou a palavra.
2. ENGINE-KB e pequena: pode ler inteira quando for mexer no codigo.
3. Codigo C++: Grep do simbolo com `-n` e ler so ~40 linhas em volta.

## 4. Regras do projeto
- OBRIGATORIO (usuario 07/11): TRATAMENTO DE ERRO EM TUDO. Validar entrada e regras do jogo ANTES de agir; arquivo, rede, servidor e processo sempre em try/catch; mensagem curta em palavras simples (Loc.T) com o que fazer + detalhe tecnico no log; nunca deixar arquivo meio gravado nem operacao meio enviada (backup + escrita atomica; conferir a resposta do servidor); nada falha em silencio.
- OBRIGATORIO (usuario 08/11): TODA FERRAMENTA tem, sem excecao: (1) confirmacao de abandonar a edicao nao salva (Esc, seta voltar, botao fechar e fechar a janela); (2) Ctrl+C/Ctrl+V do que ela mexe, com previa seguindo o mouse e UM objeto por clique (depois do clique sai da insercao e o novo fica escolhido); (3) botao de fechar clicavel com o mouse em janelas e paineis; (4) Esc cancela o passo em andamento e depois sai; (5) Ctrl+Z/Ctrl+Y em toda acao; (6) tratamento de erro em tudo (item acima) e Salvar tudo-ou-nada; (6b) SALVAR COM CONFIRMACAO VISUAL: todo salvar mostra uma janela clara de sucesso com o resumo do que gravou (SavedDialog.Show em UI/UiKit.cs), nao so texto na barra ou botao mudando de cor; se o arquivo ainda tem que chegar ao servidor, a janela oferece 'Enviar ao servidor agora' e arquivo gravado fora do editor principal chama ServerNotice.Notify(arquivo) para acender o brilho do enviar; (7) BACKUP POR FERRAMENTA: um Salvar = um conjunto (PerfectWorld/BackupSets.cs) que volta todos os arquivos juntos no Restaurar backup; (8) recarrega sozinha depois de qualquer backup restaurado (evento BackupSets.Restored); (9) teste de linha de comando nunca mostra pergunta na tela (WorldView.Headless, MessageBox da classe) e encerra o processo no fim. (10) com a ferramenta aberta o menu do clique direito do Mundo 3D (copiar coordenada, monstro/NPC novo aqui) NAO abre (usuario 08/11); a roda do mouse nao muda campos digitaveis (so clicar e digitar). (11) LAYOUT CONFERIDO EM CAPTURA antes de entregar (usuario 08/11): nenhum botao colado no outro (margem entre botoes e entre grupos), nenhum texto ou botao cortado (largura/altura que caiba o texto mais longo, inclusive en/ru/zh), botoes do mesmo grupo com o mesmo tamanho, tecla de atalho embaixo do nome, e o painel da ferramenta desce ate o fim da janela (sem rolar a toa).
- OBRIGATORIO: respeitar docs/REGRAS-DO-JOGO.md (ID unico, loja <= 65534 no 1.5.x, subtipos, configs de ID fixo, referencias das quests, trava de velocidade...). Checagens em PerfectWorld/ItemRules.cs e QuestRules.cs rodam ao salvar; nunca desligar. Regra nova vai no doc e no codigo.
- Recurso/token do usuario e limitado: nada pesado (RE profunda, fetch grande, loops longos) sem autorizacao explicita.
- Nativo C#/WinForms (sem web). Icones e textos sempre do proprio jogo. Telas no padrao UiKit (barra lateral, barra de comandos, cartao de estado, pilulas, secoes). Toda acao com Ctrl+Z.
- Editar dado do jogo = editar o PAR client+server (PW-KB §S7).
- Formato so ganha edicao depois de round-trip byte-identico; backup (fora do cliente, BackupStore) + escrita atomica sempre.
- Repositorio publico: nunca gravar senhas, chaves de rede (isec/osec), dados pessoais ou bytes de patch nas KBs.

## 4b. Layout das telas (REGRA DO USUARIO: toda tela nova ou mexida segue isto; pecas em UI/UiKit.cs)
- MUNDO 3D (usuario 01/11): filtros "Mostrar" ficam numa barra propria EMBAIXO que quebra em linhas (usuario 01/11, antes em cima e nao cabia), em grupos pontos | so com gatilho | mundo (grama, patrulhas); abrir ferramenta fecha os paineis de baixo e a legenda, titulo com icone; a legenda/atalhos so abre pelo botao
  Atalhos de cima (nada flutuando por padrao). Ferramentas = menu FLUTUANTE a direita (nao colado em cima nem embaixo),
  ladrilhos RETANGULARES, UM POR LINHA (icone grande a esquerda, nome + para que serve; usuario 01/11); o painel de uma ferramenta mostra SO o que serve para ela (ex.: area do monstro so em Caminhos e areas; gizmo de mover escondido la); clicar numa ferramenta liga a visao do que ela mexe no mundo e o painel dela TOMA O
  LUGAR do menu, com seta "voltar" no topo. Nada de menu lateral fixo.
  Caminhos (usuario 01/11): patrulha de QUALQUER criatura se marca com um CIRCULO + setas X/Y/Z no lugar do modelo (fica so o nome) e o botao 'Marcar ponto aqui' (Espaco) que vai ligando os pontos; clicar num ponto e arrastar as setas muda ele.
- Pensado no usuario, o mais intuitivo possivel; textos simples, sempre via Loc.T (pt-BR/en/ru/zh-CN).
- PENSAR NO MAIS LEIGO (usuario 06/11): regras em docs/superpowers/specs/2026-11-06-teleportes-leigos-design.md. Falar do que o jogador ve, nao do arquivo; o que so funciona junto e um passo so; passos numerados que abrem aos poucos com visto; nada grava antes do botao de salvar, sempre a vista, com resumo em frases; criar e editar sao a mesma tela; escolher no mundo e na lista.
- Areas de uma janela: barra lateral `NavBar` (icone + nome, dica diz para que serve), nunca abas em cima.
- ESC NO MUNDO 3D (usuario 06/11): toda ferramenta sai com Esc e volta a navegacao principal; passo em andamento cancela antes; trabalho nao salvo chama a confirmacao de abandonar (EscapeStep, LeaveTool).
- Comandos: barra estilo Word `UiKit.Ribbon/Cmd/Menu` (icone em cima, texto embaixo, dica; acao principal em azul).
  Botoes soltos: `UiKit.Decorate` (plano, icone pela funcao; cuidado: "mundo" contem "undo").
- Estado que liga/desliga (servidor, jogo): `StatusCard` fixo no topo (bolinha + titulo + detalhe), atualizado sozinho.
- Sub-paginas: pilulas `SegmentBar`. Muitos campos: agrupados por funcao, `SectionList` vertical (icone + contagem) +
  titulos de secao na grade (`SectionTag`/`PaintSection`), colunas proporcionais nome | valor | significado.
- Preferencias: tela `PreferencesWindow` (menu Preferencias); 1 linha por opcao = icone, nome, explicacao, estado REAL
  lido ao abrir e interruptor `ToggleSwitch`; opcao nova = mais um `Pref` em `EditorForm.OpenPreferences`
  (secao, icone, nome, explicacao, ler, aplicar, pergunta antes se derruba algo).
- Telas vazias dizem o que fazer; listas com nome em destaque + 2a linha cinza; telas escuras (3D) com `DarkBarRenderer`.
- Icones = glifos Segoe Fluent/MDL2 desenhados (`UiKit.Icon`), sem arquivos de imagem; conferir o glifo antes (desenhar
  candidatos num PNG).
- Toda acao tem Ctrl+Z/Ctrl+Y (regra do usuario). Sair com mudanca nao salva: Salvar / Descartar / Cancelar.
- Conferir por captura sem abrir na tela do usuario: --snapshot, --gm-shot, --adm-shot, --wq-shot, --prefs-shot.

## 5. Protocolo de atualizacao (OBRIGATORIO ao descobrir algo)
- REGRA DO USUARIO: todo CODIGO novo e toda INFORMACAO nova sao registrados na mesma sessao, no lugar certo deste mapa,
  seguindo a mesma estrutura (secao §, linha telegrafica, tag de confianca), para que a proxima consulta seja um pulo e
  nao uma busca:
  | O que surgiu | Onde registrar |
  |---|---|
  | arquivo novo no MyAceEditor | ENGINE-KB §E6 (1 linha: arquivo -> o que faz) |
  | feature nova/mudada no MyAceEditor | ENGINE-KB §E7 (+ §E2 se for capacidade nova) |
  | fato do jogo (formato, campo, regra, daemon, mapa) | PW-KB, secao certa (§0 diz qual) |
  | fato de GM | PW-KB §S31 |
  | onde algo fica no C++ do jogo (arquivo:funcao) | SOURCE-MAP.md §M0 (1 linha: assunto -> arquivo:simbolo) |
  | regra que derruba o gs / item que nao funciona | docs/REGRAS-DO-JOGO.md + checagem em ItemRules/QuestRules |
  | fonte nova em D:/Perfect World/source | rodar mkmap.py (e acrescentar a area em AREAS se for pasta nova) |
  | etapa iniciada/concluida | docs/AFAZERES.md |
  Depois de mudar esta skill ou o SOURCE-MAP, copiar para `~/.claude/skills/pw-engine/` (as duas copias iguais).
- REGRA DO USUARIO: TODA informacao nova sobre QUALQUER estrutura do PW (formato, campo, struct, protocolo, comando, regra do gs/cliente, config, arquivo) entra na KB na mesma sessao, mesmo pequena. Na duvida: Grep na KB; se nao achar, grave.
- Fato do JOGO -> PW-KB (secao certa). Fato da ENGINE/codigo -> ENGINE-KB. Achou onde algo fica no codigo C++ do jogo -> acrescentar 1 linha no `§M0` do SOURCE-MAP.md (assunto -> arquivo:simbolo).
- Fato de GM -> SEMPRE em PW-KB §S31. Antes de mexer no painel do GM, leia §S31 + docs/GM-PAINEL.md.
- Toda linha com tag conf ([C][A][B][F][M][W][I][?], e [T]=testado no nosso codigo).
- Contradicao resolvida -> §Z e corrige a linha original. Duvida nova -> §Q.
- Feature pronta -> ENGINE-KB §E8 -> §E7; arquivo novo -> §E6; mudou camada/fluxo -> docs/ARQUITETURA.md.
- Telegrafico, sem dumps; preferir referencia cruzada (PW-KB§Sx, SOURCE-MAP§Mx).
- Ao fim de uma tarefa que gerou conhecimento novo, atualizar a KB na mesma sessao e avisar o usuario em 1 linha.
