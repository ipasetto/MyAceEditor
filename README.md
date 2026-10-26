# MyAceEditor

<p align="center">
  <a href="README.md"><img src="https://img.shields.io/badge/Portugu%C3%AAs-selecionado-2ea44f?style=for-the-badge" alt="Português"></a>
  <a href="README.en.md"><img src="https://img.shields.io/badge/English-read-555?style=for-the-badge" alt="English"></a>
</p>

[![Licença: GPL v3](https://img.shields.io/badge/licen%C3%A7a-GPLv3-blue.svg)](LICENSE)
[![Contribuições bem-vindas](https://img.shields.io/badge/contribui%C3%A7%C3%B5es-bem--vindas-brightgreen.svg)](CONTRIBUTING.md)

**Engine de edição do Perfect World (motor Angelica)**: aplicação desktop nativa em C#/WinForms
(.NET 10) para ler e editar os dados do jogo, cliente e servidor, numa única janela.
Projeto **aberto e comunitário**: a meta é tornar o Perfect World 100% editável com uma ferramenta livre.

**[⬇ Baixar a versão de teste v0.2.0-beta (Windows)](https://github.com/ipasetto/MyAceEditor/releases/tag/v0.2.0-beta)** · [como instalar, passo a passo](#como-instalar-passo-a-passo)

![Editor de elementos](docs/img/editor.png)

## Novidades da v0.2.0-beta

**Atualizado em 26/10/2026.**

- **Servidor de teste no seu PC**: a aba Servidor instala e controla um servidor do Perfect World dentro do Windows (WSL); **Salvar e enviar ao servidor** leva tudo o que você editou e reinicia.
- **Painel de administração** (como o iweb, mas numa janela): contas (criar, senha, GM, **dar CASH por ID**, banir), personagens, quem está no jogo (expulsar, calar), EXP/drop/dinheiro em dobro, aviso para todos e cada mapa ou instância.
- **NPCGen**: onde nascem monstros, NPCs e coletáveis, com quantidade, renascimento, raio e monstro editáveis, e um assistente de **monstro/NPC novo** a partir de um modelo.
- **Mundo 3D**: o mapa do jogo em 3D (terreno, construções, água e os modelos dos monstros e NPCs) para ver e mover os pontos de nascimento.
- **Verificação mais completa**: a versão que o `elementclient.exe` e o `gs` exigem, o que está no servidor local e as **janelas do `interfaces.pck` que derrubam o jogo**, com correção automática.
- **Gravação de `.pck`**: o editor de interfaces salva direto no `interfaces.pck`, com backup, sem precisar do sPCK.
- **Traduzir a partir de outro cliente** (Ferramentas): textos, quests, falas de NPC e pacotes de outro cliente, só o que cabe na versão do seu.
- **Salvar tudo** (Ctrl+S) e salvar automático; a loja não deixa salvar item sem preço (o servidor não subia).

## Recursos

### Elements (`elements.data`)
- Abre a pasta do Perfect World e **recarrega sozinho** na próxima vez; lê e **grava** o `elements.data` com prova de gravação idêntica e backup automático.
- **Ícones reais do jogo**, lidos direto do `surfaces.pck`; miniaturas 3D para monstros e NPCs sem ícone.
- Tela no estilo dos editores clássicos: blocos por categoria, filtro por tipo, busca, sub-abas por grupo, nome na cor oficial do jogo.
- **Tooltip como no jogo**: atributos, defesas por elemento, imunidades, drops com ícone e chance, e quem usa o item.
- **Vínculos**: o que cada item usa e quem o usa; **tipos e subtipos** listam os subtipos e os itens deles (duplo clique filtra a lista).
- Códigos explicados na própria grade (tipo de combate, agressivo, terra/água/ar, segundos por golpe).
- **Desfazer/refazer** (Ctrl+Z/Ctrl+Y) com histórico de ações.
- **Alterar em lote**: definir, somar, multiplicar, sequência, aleatório e substituir texto, com prévia antes/depois.
- **Procurar erros** (Ctrl+E): IDs que não existem, probabilidades fora de 0–100% ou que não somam 100%, IDs repetidos; corrige as probabilidades com um clique.
- **Salvar em outra versão**: converte o `elements.data` para cima ou para baixo (qualquer versão com `.cfg`), casando blocos e campos pelo nome.
- **Traduzir com outro elements.data**: copia nomes e textos pelo ID de um arquivo já traduzido, escolhendo os blocos e só o que falta traduzir.

### Loja de cash (GShop)
- **A mesma tela do jogo**: desenhada com o `qshop.xml` do `interfaces.pck` e as texturas do `surfaces.pck`, com as fontes do próprio cliente.
- Duplo clique no nome, no preço ou numa categoria edita ali mesmo; painel ao lado para item, quantidade, figura, descrição colorida, 4 opções de venda e presente.
- Salva o `gshop.data` (cliente) e o `gshopsev.data` (servidor) juntos, com o mesmo timestamp; desfazer/refazer.

### Quests (`tasks.data` v93)
- Campos, textos, diálogos e subquests; **itens exigidos/dados, monstros a matar e prêmios** (ouro, exp, SP, reputação, itens) editáveis.
- **Clonar** quest (IDs novos, textos recodificados) e **excluir** quest; nome da quest vira link a partir dos NPCs.

### AI Policy (`aipolicy.data`)
- Árvore de políticas e triggers com explicação em português, adicionar/excluir operações, **clonar política ou trigger**, busca e tradução.

### NPCGen e Mundo 3D
- Aba **NPCGen**: todos os pontos de monstro, NPC e coleta de cada mapa (`npcgen.data` lido e gravado byte a byte); quantidade, renascimento, raio, gatilho e quem nasce ali.
- **Mundo 3D**: terreno com as texturas do jogo, construções, água, luz e os modelos dos monstros e NPCs; ir para um nome ou coordenada, arrastar pontos, criar monstro/NPC novo ali, mapa-múndi (M).

### Interface do jogo e pacotes
- **Extrator de PCK nativo** (sem sPCK): prévia de imagens e textos, extrai para `<pacote>.files` como o sPCK.
- **Editor de interfaces** dentro do extrator: as janelas do `interfaces.pck` desenhadas como no jogo; clicar, arrastar, redimensionar e mudar o texto; **grava direto no `.pck`** com backup.

### Servidor de teste
- Aba **Servidor**: instala um servidor 1.4.4 no WSL (o Linux do Windows), cria a conta, liga e desliga; os arquivos originais do pacote não são alterados.
- **Salvar e enviar ao servidor**: grava tudo e leva elements, quests, loja, IA e a configuração do servidor, refazendo o lado do servidor a partir do cliente.
- **Painel Administrar**: contas, CASH por ID (de conta ou de personagem), personagens lidos do banco do jogo, quem está no jogo, atributos do mundo, aviso geral, mapas e instâncias com a memória de cada um.

### Cliente × servidor
- **Verificação do par**: aponta o que derruba o login com "versão errada" e **corrige** refazendo o lado do servidor a partir do cliente, com backup.
- Confere a **versão compilada** no `elementclient.exe` e no `gs` (os dois recusam outro elements), o **servidor local** e as **janelas que o jogo procura** no `interfaces.pck`.
- **Traduzir a partir de outro cliente**: o cliente alvo recebe só os campos que ele tem, mesmo que a origem seja de versão maior.
- Modo **MCP**: as mesmas operações disponíveis para agentes de IA.
- Interface em português, inglês e chinês simplificado (`lang/*.json`).

### Galeria

| | |
|---|---|
| ![Mundo 3D](docs/img/world3d.jpg) | ![NPCGen](docs/img/npcgen.png) |
| **Mundo 3D: monstros e NPCs no mapa do jogo** | **NPCGen: onde cada um nasce** |
| ![Servidor de teste](docs/img/server.png) | ![Verificação](docs/img/check.png) |
| **Servidor de teste no PC** | **Verificação cliente × servidor** |
| ![Painel: mundo](docs/img/admin-world.png) | ![Editor de interfaces](docs/img/interface.png) |
| **Painel: EXP em dobro, travas e aviso geral** | **Janelas do jogo editáveis, gravadas no .pck** |
| ![Loja como no jogo](docs/img/gshop.png) | ![Quests](docs/img/quests.png) |
| **Loja de cash com a tela do jogo** | **Quests** |
| ![Tipos e subtipos](docs/img/types.png) | ![Traduzir com outro elements.data](docs/img/translate.png) |
| **Tipos de arma: subtipos e itens** | **Traduzir com outro arquivo** |
| ![Procurar erros](docs/img/find-errors.png) | ![Tooltip de monstro](docs/img/tooltip-monster.png) |
| **Procurar erros** | **Tooltip de monstro** |

## Como instalar (passo a passo)

Não precisa instalar nada além do próprio programa. Funciona no **Windows 10 ou 11 (64 bits)**.

1. Abra a página **[Releases](https://github.com/ipasetto/MyAceEditor/releases)** e, na versão mais recente, clique no arquivo **`MyAceEditor-...-win-x64.zip`** para baixar (cerca de 70 MB).
2. Na pasta Downloads, clique com o **botão direito** no `.zip` → **Extrair tudo…** → **Extrair**.
   Não abra o programa de dentro do `.zip`: ele precisa das pastas `lang`, `PerfectWorld` e `Server` que vêm junto.
3. Entre na pasta extraída `MyAceEditor` e dê dois cliques em **`MyAceEditor.exe`**.
4. Se aparecer **"O Windows protegeu o computador"**: clique em **Mais informações** → **Executar assim mesmo**.
   Isso acontece porque o programa ainda não tem assinatura digital paga; o código é aberto e está todo aqui no GitHub.
5. No programa: **Arquivo → Abrir pasta do Perfect World…** e escolha a pasta do seu jogo (a que tem a pasta `element` dentro).
   Na próxima vez ele abre sozinho a mesma pasta.

**Servidor de teste (opcional)**: na aba **Servidor**, escolha a pasta `server files` de um pacote 1.4.4 e clique em **Instalar / completar**.
Se o WSL ainda não estiver no Windows, o programa avisa: rode `wsl --install --no-distribution` num terminal como administrador e reinicie o PC uma vez. Depois é só **Iniciar** e entrar no jogo pelo servidor "Local".

**Dicas**
- O programa faz **cópia de segurança** antes de gravar, mas trabalhe numa **cópia da pasta do jogo** até se acostumar.
- Para trocar o idioma: menu **Idioma / Language**.
- Para atualizar: baixe o `.zip` novo e extraia por cima (ou numa pasta nova). Suas preferências ficam guardadas no Windows.
- Achou um erro? Abra uma [Issue](https://github.com/ipasetto/MyAceEditor/issues) com um print da tela.

> As versões **beta** são de teste: muita coisa já funciona, mas ainda pode haver erros.

## Rodar a partir do código (desenvolvedores)

Requer Windows e o SDK do .NET 10.

```bash
dotnet run
```

Depois, **Arquivo → Abrir pasta do Perfect World…** e escolha a pasta que contém `element\`.

## Gerar o executável

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true -o dist
```

Gera em `dist\` o `MyAceEditor.exe` (arquivo único que roda sem instalar nada) junto com as pastas `lang\`, `PerfectWorld\` e `Server\` e o `glfw3.dll` do Mundo 3D. Distribua a pasta inteira (veja [CONTRIBUTING.md](CONTRIBUTING.md) para traduzir a interface).

## Estado

| Área | Estado |
|---|---|
| `elements.data`: leitura / gravação | ✅ ~98,6% / byte-idêntica com backup |
| Converter `elements.data` entre versões | ✅ (formato comum; v191+ 🔜) |
| `.pck`/`.pkx` nativo + ícones + extrator | ✅ leitura; gravação ✅ em `.pck` v2 (v3 e `.pkx` 🔜) |
| Editores de campo, vínculos, lote, desfazer, procurar erros, tradutor | ✅ |
| Quests (`tasks.data` v93) | ✅ campos, textos, listas e prêmios; clonar/excluir (mudar quantidade de entradas 🔜) |
| GShop (cliente + servidor) | ✅ com a tela do jogo |
| AI Policy | ✅ leitura, edição de operações, clonar, busca |
| Editor de interfaces (`interfaces.pck`) | ✅ grava direto no pacote |
| Verificação e correção do par cliente × servidor | ✅ inclui versão dos programas e janelas do `interfaces.pck` |
| NPCGen (onde monstros, NPCs e minas nascem) | ✅ editar pontos, quantidade, renascimento e monstro; Mudar ID atualiza |
| Mundo 3D | ✅ terreno, construções, água, modelos; mover e criar pontos |
| Servidor de teste local (WSL) + painel de administração | ✅ 1.4.4; ações do painel ainda em teste dentro do jogo |
| Iniciar instâncias pelo painel, editar personagem, enviar item por correio | 🔜 |

Versão de referência testada: cliente/servidor Perfect World 1.4.4 (`elements.data` v60, `tasks.data` v93).
O que vem pela frente está no **[roadmap](docs/ROADMAP.md)**.

## Contribuir

Contribuições são muito bem-vindas! Leia o **[guia de contribuição](CONTRIBUTING.md)** e escolha uma
tarefa no **[roadmap](docs/ROADMAP.md)** — as marcadas com 🟢 são boas para começar.

## Documentação

| Documento | Para quê |
|---|---|
| [docs/ARQUITETURA.md](docs/ARQUITETURA.md) | Como a engine e o Perfect World funcionam: diagramas, camadas, fluxo, formatos |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Tarefas futuras organizadas em marcos |
| [docs/PARIDADE-RODY.md](docs/PARIDADE-RODY.md) | Checklist função por função em relação aos editores clássicos |
| [docs/kb/PW-KB.md](docs/kb/PW-KB.md) | Base de conhecimento do **jogo**: cliente, servidor, protocolo, formatos binários, versões |
| [docs/kb/ENGINE-KB.md](docs/kb/ENGINE-KB.md) | Base de conhecimento da **engine**: o que está implementado e como |
| [.claude/skills/pw-engine](.claude/skills/pw-engine/SKILL.md) | Skill do Claude Code que ensina o agente a usar e manter as KBs |

## Licença

[GNU General Public License v3.0](LICENSE). Você pode usar, estudar, modificar e distribuir o
MyAceEditor; versões modificadas que forem distribuídas também precisam ter o código aberto sob a GPL-3.0.

## Créditos

O conhecimento dos formatos vem do estudo de ferramentas e documentação da comunidade
(sELedit, sTASKedit, sGSHOPedit, AIPolicyEditor, sNPCedit, sMAPtool, uPCK, UIEdit, Rody's Angelica Editor,
fóruns mmorpg-devs.ru, RaGEZONE e PWDevGroup), listadas em [PW-KB §R](docs/kb/PW-KB.md).
O código deste repositório é original.

**Contribuidores:** LAOCHEN (RaGEZONE) — leitor multi-versão do elements.data (170 .cfg, v6–v623),
decodificador MPPC, janela de progresso/cancelamento, testes de regressão e tradução para chinês
simplificado (`lang/zh-CN.json`, documentação em [docs/zh-CN](docs/zh-CN)). A versão completa em chinês
está no branch `contrib/laochen-zh-CN`.

Perfect World é marca da Perfect World Co., Ltd. Este projeto não é afiliado a ela e não distribui
arquivos do jogo.
