# MyAceEditor — editor de dados de jogo com IA

Versão própria, original, inspirada na arquitetura do **Angelica Core Editor**.
Um único executável **.NET 10** que roda em dois modos:

- **App local (padrão)** — serve a interface web (`wwwroot/`) + uma **API REST** em `http://localhost:5174`.
  A UI carrega o arquivo `.data` real do disco e grava as mudanças de verdade.
- **Servidor MCP** (`--mcp`) — expõe as ferramentas para um agente de IA (Claude, etc.), igual ao Angelica Core.

## Arquitetura

```
┌─────────────────────────┐   HTTP/REST   ┌───────────────────────┐
│  UI (wwwroot/index.html)│ ◄───────────► │  ASP.NET + ModuleStore │ ──► arquivo .data
└─────────────────────────┘               └───────────┬───────────┘
                                                       │  (mesmo ModuleStore)
                                              modo --mcp │
                                                       ▼
                                                  IA via MCP
```

- **Modelo de dados** (`Model/DataModel.cs`): `Módulo → Lista → Item → Campo` (tipado). Genérico — um `Item`
  é um saco de campos, então as mesmas ferramentas editam NPCs, Tasks, Triggers, etc.
- **Change set** (`Changes/ChangeSet.cs`): toda escrita é proposta (preview) e só é aplicada em lote.
- **Store** (`Store/ModuleStore.cs`): módulo carregado + change set; lê/grava `.data` (JSON).
- **API** (`Web/EditorApi.cs`): `GET /api/module`, `POST /api/load`, `POST /api/apply`, `POST /api/save`.
- **Tools MCP** (`Tools/*.cs`): as mesmas operações expostas ao agente de IA.

## Rodar como app local

```bash
dotnet run
```

Abre em **http://localhost:5174** (carrega `sample.data` automaticamente). A barra de conexão no topo mostra
**"servidor"** quando a UI está falando com o backend, e **"demo"** quando aberta sem servidor.

## Rodar como servidor MCP (para a IA)

```bash
dotnet run -- --mcp
```

Registrar no Claude Code:

```bash
claude mcp add myaceeditor -- dotnet "C:\Godot\MyAceEditor\bin\Debug\net10.0\MyAceEditor.dll" --mcp
```

## Gerar um executável local (.exe único)

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```

Gera `dist\MyAceEditor.exe` — roda sem precisar do .NET instalado. Abre o editor no navegador em
localhost:5174. (Depois dá para empacotar numa janela desktop com WebView2, virando um app nativo.)

## Formato de arquivo

O módulo é salvo como **`.data`** (JSON por dentro). A extensão é só convenção — dá para trocar à vontade.
`sample.data` é o exemplo que abre por padrão.

## Próximos passos

1. **Abrir/Salvar com diálogo** de arquivo no app local (escolher qualquer `.data`).
2. **Mais sistemas** (AI policy, NpcGen, Scripts) como novas listas + tools.
3. **Integridade de referências** ao renomear/remover ids.
4. **Janela desktop** (WebView2) e **export para Godot** (`.tres`/`.gd`).
