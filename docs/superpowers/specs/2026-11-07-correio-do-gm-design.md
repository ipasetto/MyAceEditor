# Correio do GM com qualquer item — desenho

Data: 07/11/2026 · Versão do jogo: só 1.5.5 · Status: aguardando revisão do usuário

## 1. O que o usuário pediu
- Painel do GM: mandar um item (e/ou moedas) pelo correio para um jogador.
- O item é escolhido na **JAV** (`UI/ItemSearchWindow.cs`, a janela de escolher item que já usamos no Mundo 3D, na loja
  e nas quests).
- **Qualquer item** tem que poder ir (opção B), sem quebrar o jogo nem o servidor.
- Regra nova do usuário (07/11): **tratamento de erro em tudo** (seção 7).

Suposições (corrigir na revisão se estiverem erradas):
- O envio exige o servidor neste PC, como o aviso geral e os eventos do painel (o GM contratado sem servidor não usa).
- O item sai "como um drop normal do jogo": o que o gs sorteia num drop (durabilidade, atributos aleatórios), nós
  sorteamos igual. Escolher atributos à mão fica fora (seção 9).

## 2. Como o correio chega ao jogo (fontes do 1.5.3, conferidas em 07/11)
- `SysSendMail` (tipo 4214) entra no **gdeliveryd, porta 29100** (estado `GDeliverServer` do `rpcalls.xml`).
  O gdeliveryd monta o correio (`syssendmail.hpp`: remetente "GM", tipo web) e manda `DBSendMail` ao gamedbd.
  A resposta `SysSendMail_Re` (4215) traz `retcode` e o `tid` que mandamos.
- Campos: `tid` u32, `sysid` i32, `sys_type` u8 (iweb usa 3), `receiver` i32 (ID do personagem), `title` Octets,
  `context` Octets (máx. 400 bytes), `attach_obj` GRoleInventory, `attach_money` u32.
- `GRoleInventory`: id u32, pos i32 (-1), count i32, max_count i32, data Octets (**os dados internos da peça**),
  proctype i32, expire_date i32, guid1 i32, guid2 i32, mask i32.
- `SysSendMail3` (8068) e `SysAuctionPrepareItem` (4363) não servem: o primeiro só é aceito do lado do gauthd e
  também vai sem dados internos; o segundo corre numa conexão interna que o gs abre com o gdeliveryd.

## 3. Por que precisamos gerar os dados internos
- Quando o jogador tira o item do correio, o gs monta a peça com `MakeItemEntry(GDB::itemdata)`
  (`cgame/gs/item.cpp:88`): pilha, preço e proteção vêm do elements; item de tempo sem validade é recusado; depois
  `LoadBody` lê os dados internos com o `Load` da classe do item (`cgame/gs/item/*.h`).
- Quase todo tipo lê os dados (`ar >> _ess`): arma, armadura, ornamento, poção, pergaminho, livro, runa, montaria,
  roupa, pedra, dado de quest... Com dados vazios a leitura falha e o item é recusado. Só materiais e poucos outros
  aceitam vazio.
- O gs gera esses dados em `generate_item_temp.h` (`cgame/gs/template/`, ~60 geradores, um por tipo), chamado por
  `generate_item_for_drop` com a marca `IMT_CREATE` (é o que `generate_item_for_delivery`, em `netmsg.cpp:932`, usa).
  Os tipos "estáticos" (poção, material, pergaminho...) têm dados fixos montados ao carregar
  (`itemdataman::generate_item_for_sell` + `duplicate_static_item`).

## 4. Abordagens consideradas
1. **Gerar os dados no editor, copiando as regras do gs, tipo a tipo (escolhida).** Funciona para qualquer item, sem
   mexer no servidor. Custo: portar os geradores com cuidado e conferir cada tipo.
2. Pedir ao servidor para gerar: não existe porta aberta para isso (seção 2).
3. Copiar os dados de um item igual que já está com algum personagem (lendo o gamedbd): só funciona se alguém já
   tiver o item; fica como ferramenta de conferência, não como caminho principal.

## 5. Peças (cada uma com uma função só)
| Peça | Arquivo | O que faz |
|---|---|---|
| Gerador | `PerfectWorld/ItemData/ItemDataGenerator.cs` + um arquivo por família (`Equip.cs`, `Consumables.cs`, `Special.cs`) | id do item -> `MailItem` (dados internos, pilha, proteção, máscara, validade, guid) seguindo o gerador do gs daquele tipo; tipo sem gerador conferido = recusa com motivo |
| Leitor de conferência | `PerfectWorld/ItemData/ItemDataReader.cs` | lê os dados como o `Load` do gs lê; usado para provar que o que geramos é aceito (mesmo tamanho, nada sobrando, valores dentro das faixas do elements) |
| Regras | `PerfectWorld/ItemRules.cs` (`CheckMailItem`) | antes de enviar: o ID existe no elements **do servidor**; tipo com gerador conferido; quantidade entre 1 e a pilha; item de tempo ganha validade; moedas dentro do limite; texto até 400 bytes |
| Envio | `PerfectWorld/GmMail.cs` | monta o `SysSendMail` com o `Gnet` que já existe, manda à 29100, espera o `SysSendMail_Re` com o mesmo `tid` (tempo limite) e traduz o `retcode` |
| Tela | `UI/GmMailWindow.cs` + botão na aba Jogadores do `UI/GmPanel.cs` | passos para leigos, JAV, resumo, envio, aviso de sucesso |

## 6. A tela (padrão para leigos e UiKit)
Passos numerados que abrem aos poucos, com visto:
1. **Quem recebe**: escolher da lista de quem está online (já lida do gdeliveryd) ou digitar o nome; mostra o
   personagem achado (nome + ID). Personagem fora do ar também recebe.
2. **O que mandar**: botão "Escolher item" abre a JAV (Favoritos e Recentes); quantidade limitada à pilha do item;
   moedas opcionais. Tipo ainda sem gerador conferido aparece apagado na JAV, com a dica do motivo.
3. **Mensagem**: título e texto (contador de bytes até 400).
4. **Resumo** em frases ("Fulano vai receber 10 × Poção de Vida e 5.000 moedas pelo correio, com o título ...") e
   o botão azul **Enviar**. Depois: janela de sucesso ou o erro em palavras simples.
- Ctrl+Z/Ctrl+Y desfazem o que foi preenchido nos passos. O envio em si não tem volta (o correio já foi): por isso o
  resumo e a confirmação vêm antes, e o histórico do painel guarda o que foi mandado.
- Textos via `Loc.T` em pt-BR, en, ru e zh-CN; espaço livre embaixo; Esc fecha (pergunta se há algo preenchido).

## 7. Tratamento de erro (regra do usuário, vale para tudo)
- **Antes de agir**: `CheckMailItem` + servidor no ar + personagem achado. Qualquer falha impede o envio e diz o que
  fazer.
- **Gerador**: tipo desconhecido, item fora do elements, campo fora da faixa = exceção com motivo; o leitor de
  conferência roda sobre o que foi gerado antes de todo envio; se não fechar, não envia.
- **Rede**: conexão, envio e espera da resposta em `try/catch` com tempo limite; sem resposta = "não deu para
  confirmar o envio" (não diz que foi), com orientação para conferir a caixa do personagem antes de mandar de novo
  (evita item duplicado).
- **Resposta do servidor**: `retcode` diferente de 0 vira mensagem simples (tabela dos códigos `ERR_*` conhecidos;
  desconhecido mostra o número).
- **Log**: detalhe técnico (pacote, retcode, exceção) no log do painel; para o usuário, frase curta.
- Nada fica meio feito: o envio é um pacote só, e o editor não grava arquivos nesse fluxo.

## 8. Etapas e conferência
Cada etapa libera na JAV só os tipos já conferidos.
1. **Correio + tipos estáticos e simples**: envio completo, tela, regras; geradores de material, poção, pergaminhos,
   livro de habilidade, runas, itens de quest, fogos, cartões e demais "estáticos".
2. **Equipamentos**: arma, armadura, ornamento, flecha, aljava (atributos fixos e sorteio como no drop, defesa mágica,
   durabilidade, buracos).
3. **Especiais**: montaria, roupa, pedra, ovo e comida de mascote, gênio e equipamento de gênio, cartas, dados,
   casamento e o resto da lista do `generate_item`.

Conferência em cada tipo:
- **Teste do leitor**: para todos os IDs daquele tipo no elements do 1.5.5, gerar e ler de volta sem sobra.
- **Teste com o jogo (oráculo)**: o GM cria o mesmo item no próprio personagem (`d 10800 id 1`); o editor lê o
  inventário pelo gamedbd (29400) e compara a estrutura com a nossa (tamanho e campos fixos iguais; sorteados dentro
  da faixa). Comando de linha: `--mail-oracle`.
- **No jogo** (usuário): mandar um item de cada família para um personagem de teste e tirar do correio.

## 9. Fora do escopo
Escolher atributos/refino à mão; mandar vários itens num correio só (o protocolo leva um); correio em massa; GM sem
servidor neste PC; outras versões.

## 10. Registro
PW-KB §S31 (correio do GM e o porquê dos dados internos), §S10/§S17 (formato dos dados internos por tipo, à medida
que cada gerador for conferido); ENGINE-KB §E6/§E7; corrigir a linha da PW-KB que diz que o `SysSendMail` vai ao
gamedbd (vai ao gdeliveryd 29100); anotar que "JAV" = `ItemSearchWindow`.
