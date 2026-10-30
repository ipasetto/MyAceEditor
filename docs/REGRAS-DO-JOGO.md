# Regras do jogo que o MyAceEditor tem de respeitar

> **Para qualquer pessoa ou IA que mexa neste código:** estas regras são obrigatórias. Elas vêm do código do
> servidor (`source_server_153`), do servidor da versão 1.5.5 desmontado e de testes no jogo. Quebrar uma delas faz
> o servidor (gs) cair ao iniciar, fazer um item não existir ou uma quest parar de funcionar. Toda função nova (no
> editor, no Mundo 3D, no Painel do GM, em ferramentas) que cria, clona, copia, exclui ou muda o ID de um registro
> tem de passar por estas checagens antes de gravar. Regra nova descoberta entra aqui **e** no código
> (`PerfectWorld/ItemRules.cs` ou `PerfectWorld/QuestRules.cs`), com a fonte.

## 1. Como as checagens funcionam

- Ao abrir o jogo, o editor guarda os problemas que **já vêm** nos arquivos originais (o jogo oficial tem alguns).
- Ao salvar o `elements.data` ou o `tasks.data`, só problemas **novos** bloqueiam, com mensagem dizendo o que
  corrigir. Nunca desligue a checagem para "conseguir salvar".
- Código: `ItemRules.Check(module, cashLimit)` (elements.data) e `QuestRules.Check(tasks, module)` (tasks.data);
  ligados em `EditorForm.SaveElementsCore` e `EditorForm.SaveTasksCore`.
- Linha de comando para conferir (sem abrir a janela): `--item-rules <elements.data> [teste]`,
  `--quest-rules <elements.data> <tasks.data> [teste]`, `--next-id <elements.data> <bloco>`,
  `--item-diff <elements.data> <id> <id>`. O modo `teste` estraga um registro por regra (só na memória) e confere
  que todas acusam.
- Toda regra tem de valer em **todas as versões** (testadas: 144, 155, 1.5.5 e 180). Regra que depende da versão
  decide pelos dados do arquivo original, não por um número fixo.

## 2. elements.data (ItemRules)

| Regra | O que acontece se quebrar | Fonte |
|---|---|---|
| ID único no espaço "essence" (itens, NPCs, monstros, serviços dividem os números) | o gs acha só um dos dois: item que não cai, NPC que some | `generate_item` → `get_data_type(id)` |
| Item de loja (`proc_type` com 0x8) com ID até **65534** | o gs 1.5.x cai ao iniciar (`world_manager::SetCashItem`, tabela de 65535, ASSERT). O 180 aceita mais: só vale se o arquivo original não tiver item de loja acima disso | `worldmanager.cpp`; gs 1.5.5 `cmp $0xfffe` |
| Armadura, ornamento, moda e carta: `id_sub_type` existe na lista `*_SUB_TYPE` | o gs não cria o item | `generate_armor/decoration/fashion/poker` |
| Arma de subtipo 300, 293, 76 ou 291 (sem animação) | o gs nunca cria a arma | `generate_weapon`; gs 1.5.5 |
| Monstro com `id_type` existente em `MONSTER_TYPE` | o gs cai ao iniciar | `npc_stubs_manager::LoadTemplate` |
| `MONSTER_ADDON.param1` de 0 a 9 nos atributos que o tipo do monstro usa | o gs cai ao iniciar | idem |
| NPC com serviço de teleporte: o serviço tem pelo menos 1 destino | o gs cai ao iniciar | idem |
| NPC com serviço de redistribuir atributos: nenhum valor negativo | o gs cai ao iniciar | idem |
| Pedra (`STONE_ESSENCE.level`) de 1 a 20 | o gs cai quando a pedra é usada | `player.cpp` |
| Configs buscadas por ID fixo continuam com o mesmo ID: 696, 701, 801, 854, 1022–1027, 1038, 1064, 1290, 1424 (renascer), 1425, 1444, 1637, 1735, 1740, 2013 | a função cai ou para (renascer faz ASSERT sem a 1424) | `get_data_ptr(<ID>, ID_SPACE_CONFIG)` |

**Não são regras** (o gs corrige sozinho ao carregar, `elementdataman::NormalizeRandom`): chances que não somam 1
em atributos do tipo de monstro, ódio, habilidades, quantidade de drop, drops (32) e minas; `drop_times` fica entre
0 e 10. Receitas sem par só geram aviso no log.

**Novo ID** (`IdRefactor.NextFreeId`): o primeiro depois do maior do próprio bloco que ninguém do espaço usa; itens
até 65534. Nunca use "maior ID + 1" da lista sozinha.

## 3. tasks.data (QuestRules)

| Regra | O que acontece se quebrar | Fonte |
|---|---|---|
| ID de quest único | o gs ignora a segunda ("Dup Task Found") | `ATaskTemplMan::AddOneTaskTempl` |
| Item, monstro e NPC que a quest usa existem no elements.data (recompensa, pedido, matar, invocar, entregar) | não derruba, mas a quest não entrega, não conta ou não aparece | campos de `IdRefactor.QuestFieldPoints` |
| Versão do tasks.data igual à do gs | o gs não carrega nenhuma quest | `LoadTasksFromPack` |

Criar quest: seguir `docs/QUESTS-3D.md` e `QuestBuilder` (NPC que entrega e recebe com a quest no serviço, IDs dos
filhos livres, até 29 caracteres no nome da etapa, regiões dentro do limite da versão).

## 4. Outros arquivos

- **gshop / gshopsev:** item sem preço (1ª opção de venda abaixo de 0,01) derruba o gs (`GShopFile.ServerProblems`).
  Cliente e servidor com o **mesmo timestamp**; o timestamp só muda quando a loja muda de fato.
- **configs.pck (cor do nome e descrição):** só a linha do item muda; o arquivo tem de voltar idêntico ao ser relido
  antes de gravar (`ItemTexts`). `.pck` v2 e v3 (`PckWriter`).
- **aipolicy.data:** versão de gatilhos que o gs da versão lê (o envio converte quando o gs recusa).

## 5. Regras de trabalho do projeto

- Editar dado do jogo = editar o **par cliente + servidor** (elements, tasks, loja, IA). Salvar não envia; o
  "Salvar e enviar ao servidor" envia só o que mudou (comparado pelo conteúdo).
- Formato só ganha edição depois de **round-trip byte-idêntico**; sempre backup + escrita atômica.
- Tudo funciona em **todas as versões**; mudou uma, confira as outras.
- Todo texto da tela por `Loc.T`, com tradução em `lang/en.json`, `lang/ru.json` e `lang/zh-CN.json`.
- Repositório público: nunca gravar senhas, chaves de rede (isec/osec), dados pessoais ou bytes de patch.
- Código fechado ou descompilado (SpeedTree, editores de terceiros, Angelica) é só estudo: nunca copiar.
- Bases de conhecimento: `docs/kb/PW-KB.md` (jogo) e `docs/kb/ENGINE-KB.md` (este código).
