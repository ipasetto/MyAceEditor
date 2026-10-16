# COMPILADO — MyAceEditor / PW Engine

> Documento-resumo de leitura rápida (para IA e humano). Fonte da verdade do estado atual.
> Projeto isolado em `C:\Godot\MyAceEditor` (NÃO confundir com o jogo em `C:\Godot\sapo`).

---

## 1. Visão (o que isto é)

Uma **engine do Perfect World**, não só um editor de `.data`. Objetivo final: tornar o
jogo **100% editável** a partir de uma única aplicação desktop nativa.

Capacidades-alvo (roadmap de longo prazo):
- Editar **todos** os dados do cliente/servidor (itens, NPCs, skills, tasks, gshop…).
- **Editar skills** e, no limite, mexer no motor/comportamento do jogo.
- **Gerar arquivos de servidor** e **subir automaticamente** (deploy).
- **Corrigir conflitos**, **verificar erros de versão** e **erros de servidor**.
- Abrir **interfaces do próprio jogo** dentro da engine (ex.: janela do GShop idêntica
  à do jogo, inventário do player, personagens etc.) para editar visualmente.

## 2. Stack técnica

- **WinForms nativo**, C#, **.NET 10** (`WinExe`, `net10.0-windows`, `AllowUnsafeBlocks`).
- Sem web / sem navegador (decisão do usuário: 100% nativo).
- Pacotes: `ModelContextProtocol 0.4.0-preview.1`, `Microsoft.Extensions.Hosting 10.0.0`.
- Modos de execução (`Program.cs`):
  - padrão → `EditorForm` (UI desktop).
  - `--mcp` → servidor MCP stdio (expõe as operações a um agente IA).
  - `--inspect <file>` / `--testcfg <cfg> <data>` → diagnóstico do parser.

## 3. Formato `elements.data` (RESOLVIDO ~98,6%)

- Header: `uint32 version` + `uint32 signature 0x4F614E5B`. Versão `0x3000003C` = v60.
- Blocos: `uint32 count` + registros fixos. Hierarquia via `id_major_type` / `id_sub_type`
  ligando aos blocos `*_MAJOR_TYPE` / `*_SUB_TYPE`.
- 2 seções "blob especial" (u32 + u32 len + bytes) + seção variável `TALK_PROC`.
- **Chave da solução:** arquivos `.cfg` da comunidade (sTASKedit/Rody) definem os campos
  por versão → evitou engenharia reversa pesada. Arquivo que casa: **`PW_1.4.4_v60.cfg`** (150 blocos).
- Tipos: int32/float=4, int16=2, byte=1, `wstring:N`=N bytes (N/2 chars UTF-16), `string:N`=N.
- **Restante ~1,4%:** cauda de Territory War (a partir de `NPC_WAR_TOWERBUILD_SERVICE`) — não crítico.

## 4. Funcionalidades do editor (prontas)

- Abrir pasta do PW / `elements.data`; **auto-carrega** a última pasta ao reabrir.
- Árvore por **categoria → tipo → subtipo** com nomes traduzidos (PT).
- Lista estilo jogo: **código + ícone + descrição**, **cores de grade** do PW (★/☆/»…).
- **Tooltip** estilo jogo no hover; painel de itens **preto** (como a janela do jogo),
  demais painéis em **branco aço**; tamanhos de painel fixos.
- Clique direito: **clonar / novo / deletar / favoritar**; categoria **Favoritos**.
- Botão **novo item** com **código subsequente automático**.
- **Busca global** por nome ou código; **ordenar** por código/nome.
- Campos agrupados e rotulados (Básico, Requisitos, Ataque, Defesa, Durabilidade,
  Economia, Atributos/Addons, Modelo/Arquivos, Outros); addons vazios ocultados.
- Ícones: **do jogo** (não gerados). Loader TGA próprio (tipo 2 e RLE 10, 24/32bpp).

## 5. Mapa de arquivos

| Caminho | Função |
|---|---|
| `MyAceEditor.csproj` | projeto .NET 10 WinForms |
| `Program.cs` | entrypoint + modos (ui / --mcp / --inspect / --testcfg) |
| `UI/EditorForm.cs` | **UI principal** (árvore, grade, busca, menus, ícones, tooltip) |
| `PerfectWorld/CfgElementsReader.cs` | parser do `elements.data` guiado por `.cfg` |
| `PerfectWorld/ElementsCfg.cs` | parser dos arquivos `.cfg` |
| `PerfectWorld/FieldLabels.cs` | rótulos PT + agrupamento de campos |
| `PerfectWorld/IconLoader.cs` | carregador de ícones (TGA/PNG/BMP; DDS pendente) |
| `PerfectWorld/cfg/PW_1.4.4_v60.cfg` | **definição que casa** (150 blocos) |
| `PerfectWorld/server_ref/` | refs do binário gs (DWARF): ordem de blocos, structs |
| `Model/DataModel.cs` | Módulo→Lista→Item→Campo |
| `Store/ModuleStore.cs` | carga/salva/estado atual |

## 6. Extração de .pck (ícones/UI do jogo)

- `.pck`: assinatura `0x4DCA23EF` (offset 0) + fileSize (offset 4); rodapé `0xF00DBEEF`;
  `.pkx` = índice separado para `.pck` grande.
- **Atual (leve):** lançar GUI do `sPCK.exe` + apontar pasta de ícones manualmente.
- **Nativo (ADIADO):** leitura automática do `.pck` → ícones 100% automáticos. Iniciar só
  quando o usuário disser **"pode fazer o pck nativo"**. Refs: forum mmorpg-devs.ru
  (threads "pkx и с чем их едят", "Файловая структура игрового клиента"). Possível add de DDS.

## 7. Git

- Repo privado **MyAceEditor** (user `IgorPasetto` / `iPasetto`). ~20 commits locais.
- Pendente: publicar via GitHub Desktop (remote `origin` foi removido p/ permitir Publish limpo).

## 8. Pendências / próximos passos

1. **PCK nativo** (gatilho do usuário) → ícones automáticos.
2. **Escrita binária** — gravar `elements.data` de volta (hoje só leitura).
3. Fechar ~1,4% restante (cauda Territory War).
4. Abas de editor: **GShop, Tasks, AI Policy, Extrator PCK**.
5. Deploy de servidor (gerar + subir + checar conflitos/versão/erros).
6. Abrir interfaces do jogo dentro da engine (GShop, inventário, personagens).

## 9. Restrições permanentes (do usuário)

- **Recurso/token limitado:** não fazer nada pesado (RE profunda, grandes fetches, loops
  longos) **sem autorização explícita**.
- Projeto 100% nativo (sem web).
- Ícones sempre os **do jogo**.
