# Roadmap

Tarefas futuras do MyAceEditor, organizadas em marcos. A meta do **Marco 1** é a paridade com os
editores clássicos da comunidade (Rody's Angelica Editor / sELedit); os marcos seguintes transformam
o editor numa **engine** completa do Perfect World.

Legenda: ✅ pronto · 🚧 em andamento · ⬜ a fazer · 🟢 bom para começar · 🔴 difícil
Cada tarefa tem uma **[issue no GitHub](https://github.com/ipasetto/MyAceEditor/issues)** (#1–#32) com contexto, por onde começar e critério de pronto — comente lá antes de começar. Boas para começar: [good first issue](https://github.com/ipasetto/MyAceEditor/labels/good%20first%20issue). Detalhe função por função: [PARIDADE-RODY.md](PARIDADE-RODY.md).

---

## Já pronto

- ✅ Leitura do `elements.data` (~98,6%) guiada por `.cfg` e **gravação byte-idêntica** com backup
- ✅ Leitura nativa de `.pck`/`.pkx` e ícones reais do jogo
- ✅ Tela de itens no estilo Rody: blocos agrupados, filtro por tipo, sub-abas, cor oficial, tooltip com drops
- ✅ Vínculos e "dropado por" com links; editores de duplo clique (classes, addons, itens)
- ✅ Descrição dos atributos (addons) em português, como no jogo
- ✅ Seleção múltipla e ações em lote
- ✅ Quests (`tasks.data` v93): campos, textos, diálogos e subquests, com gravação idêntica
- ✅ Verificação do par cliente × servidor

---

## Marco 1 — Paridade com os editores clássicos

### Edição de itens
- ⬜ Desfazer / refazer + histórico de ações
- ⬜ Copiar e colar itens entre listas e entre arquivos (com dependências e ID único)
- ⬜ Pesquisa avançada (todas as listas, vários critérios) e substituir em massa
- ⬜ Verificar erros na lista e no arquivo inteiro (IDs quebrados, probabilidades que não somam 100%)
- ⬜ 🔴 Trocar o ID de um item em todos os arquivos (elements, tasks, gshop, npcgen)
- ⬜ 🟢 Mover itens (cima/baixo/topo/fim) e ordenar os selecionados
- ⬜ 🟢 Mostrar campos sim/não como caixa de seleção
- ⬜ 🟢 Editores de máscara: proteção de item (`proc_type`) e imunidades de monstro
- ⬜ 🟢 Melhorar os rótulos em português dos campos (`PerfectWorld/FieldLabels.cs`)
- ⬜ Nomes de habilidades (`skillstr.txt`) e de quests nos vínculos
- ⬜ Ícones de monstros e NPCs
- ⬜ Importar e exportar itens; salvar "versão cliente"; perfis e arquivos recentes

### Textos do jogo
- ⬜ 🔴 Gravar `.pck` (necessário para editar descrições, cores, interface)
- ⬜ Editar a descrição (`item_ext_desc.txt`) e a cor (`item_color.txt`) dos itens

### Quests
- ⬜ Editar prêmios, itens pedidos e monstros a matar (hoje são preservados, mas não editáveis)
- ⬜ Criar, clonar e excluir quests
- ⬜ Suporte a outras versões do `tasks.data` além da v93

### Outros arquivos
- ⬜ Editor de **GShop** (cliente e servidor juntos; formato em PW-KB §S19)
- ⬜ Editor de **NPCGen** / spawns (PW-KB §S22)
- ⬜ Editor de **AI Policy** (PW-KB §S18)
- ⬜ 🔴 Fechar a leitura dos ~1,4% finais do `elements.data` (Guerra Territorial)

---

## Marco 2 — Ferramentas da comunidade

- ⬜ **Auto-tradutor** de quests, itens e textos do cliente
- ⬜ Unificar (`join`) `elements.data`, `tasks.data` e `item_ext_desc.txt` de versões diferentes
- ⬜ Converter `elements.data` entre versões (`.rules`)
- ⬜ 🟢 Exportar listas (NPCs para NPCGen, monstros para AI Policy, tabela, ícones em imagem)

---

## Marco 3 — Engine

- ⬜ Corrigir o par automaticamente (exportar loja, elements e tasks para os dois lados)
- ⬜ Enviar arquivos para o servidor e reiniciar (SSH)
- ⬜ 🔴 Raças iniciais (constante no `elementclient.exe` + `createrace.xml` + máscara do servidor)
- ⬜ 🔴 Regras gravadas nos executáveis do servidor (jogadores na Guerra Territorial, clãs)
- ⬜ 🔴 Abrir janelas do próprio jogo dentro da engine (`interfaces.pck`, PW-KB §S20)
- ⬜ 🔴 Editor de mapas (altura, navegação, água, rotas — PW-KB §S22)
- ⬜ 🔴 Edição de habilidades

---

## Como as tarefas são escolhidas

1. O que destrava outras tarefas vem primeiro (ex.: gravar `.pck` antes de editar descrições).
2. Paridade com os editores clássicos antes de recursos novos.
3. Todo formato novo começa pela leitura com prova de round-trip; a edição vem depois.
