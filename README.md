# MyAceEditor — editor de dados de jogo (WinForms nativo)

Editor desktop **nativo em C# / WinForms** (.NET 10), inspirado no Angelica Core Editor /
sEledit. Sem servidor web, sem navegador — uma janela nativa com árvore + grade.

## Modos

- **(padrão)** editor desktop: `MyAceEditor.exe`
- **`--mcp`** servidor MCP por stdio, para um agente de IA
- **`--inspect <elements.data>`** diagnóstico do parser do Perfect World (gera inspect-report.txt)

## Arquitetura

```
EditorForm (WinForms)  ──►  ModuleStore / ElementsReader  ──►  .data / elements.data
```

- **UI** (`UI/EditorForm.cs`): MenuStrip + TreeView (listas→itens) + DataGridView (campos).
- **Modelo** (`Model/DataModel.cs`): Módulo→Lista→Item→Campo (tipado).
- **Store** (`Store/ModuleStore.cs`): módulo carregado, load/save JSON, change set.
- **Perfect World** (`PerfectWorld/`): parser binário do `elements.data` recuperado por
  engenharia reversa do cliente (ver `FORMAT.md`). 1ª metade (itens/equip/NPCs) já mapeada.
- **Tools MCP** (`Tools/*.cs`): as mesmas operações expostas a um agente de IA (modo `--mcp`).

## Rodar

```bash
dotnet run
```

Abre o editor. Use **Arquivo → Abrir elements.data (Perfect World)…** para carregar um
`elements.data` real (seleção por diálogo nativo).

## Executável único

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```

Gera `dist\MyAceEditor.exe`.

## Próximos passos

1. Mapear os CAMPOS dos registros grandes (nome/stats/ícone dos itens e equipamentos).
2. Fechar o parse da 2ª metade do elements.data (região de tasks) até o EOF.
3. Implementar a ESCRITA binária (gravar o elements.data de volta).
