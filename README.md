# MyAceEditor

[![Licença: GPL v3](https://img.shields.io/badge/licen%C3%A7a-GPLv3-blue.svg)](LICENSE)
[![Contribuições bem-vindas](https://img.shields.io/badge/contribui%C3%A7%C3%B5es-bem--vindas-brightgreen.svg)](CONTRIBUTING.md)

**Engine de edição do Perfect World (motor Angelica)**: aplicação desktop nativa em C#/WinForms
(.NET 10) para ler e editar os dados do jogo, cliente e servidor, numa única janela.
Projeto **aberto e comunitário**: a meta é tornar o Perfect World 100% editável com uma ferramenta livre.

> **English:** open-source (GPL-3.0) native editor/engine for Perfect World game data. Docs are in
> Portuguese, but contributions in English are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

![Editor de elementos](docs/img/editor.png)

## Recursos

- Abre a pasta do Perfect World e **recarrega sozinho** na próxima vez.
- Lê e **grava** o `elements.data` (cerca de 40 mil itens em 95 blocos), com prova de gravação idêntica e backup automático.
- **Ícones reais do jogo**, lidos direto do `surfaces.pck`, sem extrair nada.
- Tela no estilo dos editores clássicos (Rody's Angelica Editor): blocos agrupados por categoria, filtro
  por tipo, busca, sub-abas por grupo de campos, nome na cor oficial do jogo, tooltip com drops.
- **Duplo clique** abre editores próprios: classes (caixinhas), atributos com a descrição em português,
  seletor de itens/monstros/receitas com ícone.
- **Vínculos**: o que cada item usa e quem o usa, "dropado por" com chance, tudo com links.
- Seleção múltipla e ações em lote (clonar, excluir, alterar um campo em vários itens).
- **Quests** (`tasks.data` v93): campos, textos, diálogos e subquests.
- **Verificação do par cliente × servidor**: aponta o que derruba o login com "versão errada".
- Modo **MCP**: as mesmas operações disponíveis para agentes de IA.

## Rodar

Requer Windows e o SDK do .NET 10.

```bash
dotnet run
```

Depois, **Arquivo → Abrir pasta do Perfect World…** e escolha a pasta que contém `element\`.

## Gerar o executável

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true -o dist
```

Gera `dist\MyAceEditor.exe`, um arquivo único que roda sem instalar nada. Copie também a pasta `lang\` para ao lado do `.exe` (ela traz os idiomas da interface; veja [CONTRIBUTING.md](CONTRIBUTING.md) para traduzir).

## Estado

| Área | Estado |
|---|---|
| `elements.data`: leitura | ✅ ~98,6% (falta a cauda de Guerra Territorial) |
| `elements.data`: gravação | ✅ byte-idêntica, com backup automático |
| `.pck`/`.pkx` nativo + ícones | ✅ leitura (gravação 🔜) |
| Editores de campo, vínculos, "dropado por", seleção múltipla | ✅ |
| Quests (`tasks.data` v93) | ✅ (edição de prêmios 🔜) |
| Descrição e cor oficial dos itens (`configs.pck`) | ✅ leitura |
| Verificação do par cliente × servidor | ✅ |
| GShop, AI Policy, NPCGen | 🔜 formatos já documentados |

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
Perfect World é marca da Perfect World Co., Ltd. Este projeto não é afiliado a ela e não distribui
arquivos do jogo.
