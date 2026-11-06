# Ferramentas do Mundo 3D pensadas para o leigo

Pedido do usuário em 06/11: o design das ferramentas tem de partir do usuário mais leigo. O exemplo foi o teste 30: "preciso de um
NPC para criar um ponto e de um ponto para pôr um NPC?". Sucesso: alguém que nunca abriu `world_targets.txt` cria um teleporte
que funciona no jogo sem precisar saber o que são ponto, serviço ou `combined_services`.

## Regras que valem para toda ferramenta nova

1. **A ferramenta fala do que o jogador vê, não do arquivo.** "Teleporte", "quem atende aqui", "para onde ele leva". Nada de `id_to_discover` na tela.
2. **Uma ideia, um lugar.** Se duas coisas só funcionam juntas, são um passo só (ponto + NPC).
3. **Passos numerados que abrem conforme o anterior fica pronto**, com ✓ no que terminou. O passo seguinte não aparece antes da hora.
4. **Nada entra nos arquivos antes do botão de salvar**, que fica sempre à vista, com um resumo em frases do que vai acontecer.
5. **Criar e editar são a mesma tela.** Clicar em algo que já existe abre os mesmos passos preenchidos.
6. **Escolher no mundo e escolher na lista**, sempre os dois.
7. **O editor conserta o que depende de outra coisa.** O bit de serviço, o par de arquivos, a quest do Atlas: o usuário não vê.

## Teleporte

Um teleporte é um lugar do mapa de viagem mais o NPC que atende ali.

| Passo | O que o usuário faz | O que o editor grava |
|---|---|---|
| 1. Onde o jogador chega | nome e clique no chão | ponto no `.txt` e no `.sev`, território pelo `precinct.clt` |
| 2. Quem atende aqui | cria um NPC ou usa um que existe (clique no mundo ou lista) | `id_to_discover` e o bit `0x08` de `combined_services` |
| 3. Para onde esse NPC leva | janela de destinos, com taxa e nível | `NPC_TRANSMIT_SERVICE` |
| 4. Quem mais leva até aqui | opcional, clique no mundo ou lista, taxa e nível; caixa do Atlas | destino nos serviços dos outros NPCs; subquest na quest 6940 |
| Como vai ficar | lê o resumo e clica em Salvar teleporte | tudo acima; depois, Salvar e enviar ao servidor |

Serviço usado por vários NPCs: o editor pergunta se muda para todos ou faz uma cópia só para aquele.
Excluir tira o ponto do par e de tudo que aponta para ele.

## Por que o ponto não era descoberto (print 9)

`npcgenerator.cpp` e `CECNPCServer::GetWayPointID` só olham `id_to_discover` se `combined_services & 0x08`. A ferramenta anterior gravava
só o ponto. O campo de serviço 8 da tabela de máscaras aparece como "(não disponível)", mas é este.

## Atlas Geográfico (12758)

É um `TASKDICE` que dá a quest 6940, com dois grupos de subquests de espera zero. Cada subquest tem `m_ulNewRelayStation` igual a um
ponto: ler o Atlas ativa todos. A opção "o Atlas também descobre" acrescenta uma subquest copiada de uma que já existe.

## Editar NPC (teste 8)

Criar ganhou "Ou mudar um NPC que já existe" e "Corrigir" o último criado. A edição abre os mesmos passos. Grava por cima, em um passo
do Ctrl+Z. Receita do jogo abre preenchida; mudar cria outra receita, e a original fica para quem usa.
