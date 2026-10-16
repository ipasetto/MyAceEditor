# MyAceEditor — engine de edição do Perfect World (WinForms nativo)

Aplicação desktop **nativa em C# / WinForms** (.NET 10). O objetivo não é só editar
`.data`: é ser uma **engine do Perfect World**, tornando o jogo **100% editável** a
partir de uma única janela (dados, skills, servidor e, no futuro, as próprias telas do jogo).

> Resumo completo e atualizado do projeto: **[COMPILADO.md](COMPILADO.md)**.
> Projeto isolado — nada aqui se mistura com o jogo em `C:\Godot\sapo`.

## Modos

- **(padrão)** editor desktop: `MyAceEditor.exe`
- **`--mcp`** servidor MCP por stdio (as mesmas operações para um agente de IA)
- **`--inspect <elements.data>`** diagnóstico do parser (gera `inspect-report.txt`)
- **`--testcfg <cfg> <data>`** valida um `.cfg` contra um `elements.data`

## Arquitetura

```
EditorForm (WinForms)  ──►  ModuleStore / CfgElementsReader  ──►  elements.data (.cfg)
```

- **UI** (`UI/EditorForm.cs`): árvore categoria→tipo→subtipo + lista estilo jogo
  (código + ícone + descrição, cores de grade) + grade de campos agrupados.
- **Modelo** (`Model/DataModel.cs`): Módulo→Lista→Item→Campo (tipado).
- **Perfect World** (`PerfectWorld/`): parser do `elements.data` guiado por `.cfg` da
  comunidade (`PW_1.4.4_v60.cfg`, 150 blocos). ~98,6% do arquivo já mapeado.
- **MCP** (`--mcp`): expõe as operações a um agente de IA.

## Rodar

```bash
dotnet run
```

Em **Arquivo → Abrir pasta do PW / elements.data…**. A última pasta é **recarregada
automaticamente** na próxima abertura.

## Executável único

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```

Gera `dist\MyAceEditor.exe`.

## Roadmap

Curto prazo: PCK nativo (ícones automáticos), **escrita** do `elements.data`, fechar a
cauda restante (~1,4%), abas GShop/Tasks/AI Policy.
Longo prazo (engine): edição de skills, geração e deploy de servidor com checagem de
conflitos/versão/erros, e abertura das interfaces do jogo dentro da engine.

Detalhes, formato binário e pendências em **[COMPILADO.md](COMPILADO.md)**.
