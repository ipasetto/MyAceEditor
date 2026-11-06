# Estudo: pontos de teleporte novos no Mundo 3D (teste 30)

Implementado em 06/11: ferramenta **Teleportes** do Mundo 3D e **Criar > Teleportador**. Falta o teste no jogo. Lista completa de quem leva aonde no 1.5.5: [TELEPORTE-155.txt](TELEPORTE-155.txt)
(`--teleport-report <pasta element> <saída>`).

## Como funciona hoje

| Peça | Onde | O que guarda |
|---|---|---|
| Ponto | cliente `maps\world\world_targets.txt` (UTF-16) | ID, nome, tag do mapa, x, y, z, região. É o que aparece no mapa de viagem |
| Ponto | servidor `config/world_targets.sev` | i32 quantidade, depois 24 B por ponto: ID, tag do mapa, x, y, z, região |
| Serviço | `NPC_TRANSMIT_SERVICE` | `num_targets` e, por destino, `idTarget`, `fee` (taxa) e `required_level` |
| NPC que leva | `NPC_ESSENCE.id_transmit_service` | qual serviço ele usa |
| NPC que descobre | `NPC_ESSENCE.id_to_discover` | ponto que o jogador ativa ao falar com ele |
| Jogador | `_waypoint_list`, IDs de 16 bits | pontos que ele já ativou |

No cliente 155 BR: 95 NPCs teleportadores, 427 ligações, 92 pontos, taxa máxima 10.000.

## Regras (fonte: `npcgenerator.cpp` e `serviceprovider.cpp`)

- O jogador só viaja para ponto que ele já ativou: `IsWaypointActived(target & 0xFFFF)`. **Ponto novo precisa de um NPC com
  `id_to_discover`**, ou de um jeito de ativar, senão aparece a opção e a viagem é recusada.
- ID do ponto cabe em 16 bits. O ID tem de existir nos dois arquivos.
- `idTarget` que não existe no `.sev`: o gs avisa no log e o destino vira (0, 0, 0) no mapa 0.
- `num_targets` igual a 0 derruba o gs (`ASSERT`). Já está em `ItemRules`.
- Serviço com ID errado no NPC: o gs pula o NPC.
- O `.txt` do cliente e o `.sev` do servidor têm de ser iguais: é um par (PW-KB §S7).

## Como seria a ferramenta

Novo ladrilho **Teleportes** no Mundo 3D, no padrão das ferramentas:

1. **Ver:** cada ponto vira um marcador com o nome. Clicar num NPC teleportador desenha linhas até os destinos, com taxa e nível.
2. **Novo ponto:** clicar no chão, dar o nome. O editor escolhe o ID livre e grava o par `.txt` e `.sev`.
3. **Quem descobre:** escolher um NPC perto do ponto (preenche `id_to_discover`) ou criar um pela ferramenta Criar.
4. **Quem leva:** marcar os NPCs que vão levar até lá, com taxa e nível. Serviço usado por mais de um NPC pergunta se muda para
   todos ou cria uma cópia só para este, como nos caminhos.
5. **Conferir antes de salvar:** ponto sem quem descobre, destino que não existe nos dois arquivos, ID acima de 16 bits.

## O que o estudo de precinct e domínio resolveu

- O mapa de viagem usa só o x e o z do ponto, pelo `GetPixelsByPos`: ponto novo aparece sem imagem nova.
- O bloco aceita 32 destinos; a ferramenta para em 32.
- O domínio do ponto novo vem da área do `precinct.clt` de maior prioridade naquele x e z; fora de área fica -1. Confere com 64 dos 65 pontos do mapa 1.
- O gs lê `config/world_targets.sev` (gs.conf: `Root` + `GlobalData`), não o de `config/world/`.
- ID útil até 32767: o jogador guarda o ponto em 16 bits com sinal.

## O que falta

- Teste no jogo: criar um ponto, ativar falando com o NPC e viajar.
