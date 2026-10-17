# MyAceEditor

**Engine de edição do Perfect World (motor Angelica)**: aplicação desktop nativa em C#/WinForms
(.NET 10) para ler e editar os dados do jogo, cliente e servidor, numa única janela.

![Editor de elementos](docs/img/editor.png)

## Recursos

- Abre a pasta do Perfect World e **recarrega sozinho** na próxima vez.
- Lê o `elements.data` (cerca de 40 mil itens em 95 blocos) a partir de definições `.cfg` por versão.
- **Ícones reais do jogo**, lidos direto do `surfaces.pck`, sem extrair nada.
- Lista no estilo dos editores clássicos (Rody's Angelica Editor): seletor de bloco, filtro por
  tipo/subtipo, busca por nome ou ID (no bloco ou em todos), ordenação por coluna.
- Nomes nas **cores de grade** do jogo, tooltip no estilo do jogo, campos agrupados e rotulados em português.
- Novo item com código seguinte automático, clonar, excluir, favoritos.
- Modo **MCP**: as mesmas operações disponíveis para agentes de IA.

## Documentação

| Documento | Para quê |
|---|---|
| [docs/ARQUITETURA.md](docs/ARQUITETURA.md) | Como a engine e o Perfect World funcionam: diagramas, camadas, fluxo, formatos |
| [docs/kb/PW-KB.md](docs/kb/PW-KB.md) | Base de conhecimento do **jogo**: cliente, servidor, protocolo, formatos binários, versões |
| [docs/kb/ENGINE-KB.md](docs/kb/ENGINE-KB.md) | Base de conhecimento da **engine**: o que está implementado, roadmap, dúvidas em aberto |
| [.claude/skills/pw-engine](.claude/skills/pw-engine/SKILL.md) | Skill do Claude Code que ensina o agente a usar e manter as KBs |

## Rodar

Requer o SDK do .NET 10 e Windows.

```bash
dotnet run
```

Depois, **Arquivo → Abrir pasta do Perfect World…** e escolha a pasta que contém `element\`.

## Gerar o executável

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```

Gera `dist\MyAceEditor.exe`, um arquivo único que roda sem instalar nada.

## Estado

| Área | Estado |
|---|---|
| `elements.data`: leitura | ✅ ~98,6% (falta a cauda de Guerra Territorial) |
| `elements.data`: gravação | ✅ byte-idêntica, com backup automático |
| `.pck`/`.pkx` nativo + ícones | ✅ |
| Vínculos (drops com nome/%, quem usa o item) | ✅ |
| Quests (`tasks.data` v93): campos, textos, diálogos, subquests | ✅ (edição de prêmios 🔜) |
| Descrição e cor oficial dos itens (`configs.pck`) | ✅ |
| GShop, AI Policy, NPCGen | 🔜 formatos documentados na PW-KB |
| Verificação do par cliente × servidor | ✅ |
| Deploy de servidor | 🔜 |

Versão de referência testada: cliente/servidor Perfect World 1.4.4 (`elements.data` v60).

## Créditos

O conhecimento dos formatos vem do estudo de ferramentas e documentação da comunidade
(sELedit, sTASKedit, sGSHOPedit, AIPolicyEditor, sNPCedit, sMAPtool, uPCK, UIEdit, fóruns
mmorpg-devs.ru, RaGEZONE e PWDevGroup), listadas em [PW-KB §R](docs/kb/PW-KB.md).
Perfect World é marca da Perfect World Co., Ltd. Este projeto não é afiliado a ela.
