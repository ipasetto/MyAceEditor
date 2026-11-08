# Roadmap

O que já está pronto no MyAceEditor e o que ainda falta. O foco atual é a versão **1.5.5** do Perfect World.
Cada tarefa aberta tem uma **[issue no GitHub](https://github.com/ipasetto/MyAceEditor/issues)** ou um item em
[AFAZERES.md](AFAZERES.md); comente antes de começar.

Legenda: ✅ pronto · ⬜ a fazer · 🔴 difícil

---

## Já pronto

- ✅ `elements.data`: leitura completa, até o último byte, guiada por `.cfg` e **gravação byte-idêntica** com backup
- ✅ Tela de itens no estilo Rody: blocos, filtro por tipo, sub-abas, cor oficial, tooltip com drops, vínculos
- ✅ Editores de duplo clique (classes, addons, itens, drops com barras), lote, pesquisa avançada, desfazer/refazer
- ✅ Trocar o ID de um item em todos os arquivos; descrição e cor dos itens; converter entre versões
- ✅ Tradução e auto-tradutor (itens, quests, falas, interface)
- ✅ Quests (`tasks.data`) completas, e criação de quests pelo Mundo 3D
- ✅ GShop, NPCGen, AI Policy (com anúncios ao morrer / ao entrar em combate), `extra_drops.sev`
- ✅ `.pck` / `.pkx`: leitura e gravação
- ✅ Interfaces: editor, criar interface, importar/exportar, trocar com outro cliente, telas de carregamento
- ✅ Mundo 3D: terreno, construções, árvores, grama, água, céu; pontos, caminhos, eventos, zonas, territórios,
  teleportes, NPCs e monstros, cenário
- ✅ Verificação do par cliente × servidor, servidor local (WSL), painel de administração, plugins e painel do GM

---

## Falta

- ⬜ 🔴 **Editor de mapas no Mundo 3D**: terreno, textura do chão, grama, árvores, construções e objetos, e gerar
  de novo a colisão e a passagem do servidor (`map.bht`, `rmap`, `dhmap`). Roteiro em
  [MUNDO-3D-EDITOR.md](MUNDO-3D-EDITOR.md)

---

## Como as tarefas são escolhidas

1. O que destrava outras tarefas vem primeiro.
2. Todo formato novo começa pela leitura com prova de round-trip; a edição vem depois.
3. Mexeu no cliente, mexe no servidor junto (o par).
