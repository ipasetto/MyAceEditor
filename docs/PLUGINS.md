# Plugins de evento (desenho, entrevista com o usuário em 01/11/2026)

Eventos automáticos no servidor, sem mexer no gs: um programa pequeno ao lado do servidor liga e desliga os
controladores do npcgen e avisa os jogadores. Funciona igual em todas as versões (mesmo protocolo do gdeliveryd).

## Como funciona
- **O que nasce e onde**: preparado antes no npcgen, em pontos de evento ligados a um controlador desligado
  (marcados no Mundo 3D a partir do plugin). Mudar o npcgen pede reiniciar o mapa; ligar/desligar é ao vivo.
- **Ponto de plugin**: nasce UMA vez por evento (renascer automático desligado no ponto); a espera entre eventos = Renascer do ponto + 15 s (cooldownSec), vale para o sorteio e para o "Chamar agora".
- **Ligar/desligar**: `GMControlGame` (380) no gdeliveryd :29100 com `active_npc_generator <id>` /
  `cancel_npc_generator <id>`, para cada gs (worldtag) do mapa. Desligar tira os monstros vivos do evento (o usuário
  escolheu: no fim, os que sobraram somem).
- **Avisos do plugin**: `PublicChat` (79) no gdeliveryd, como o iweb (corrigido 01/11: o `ChatBroadCast` 120 é ignorado pelo gdeliveryd). Antes estava escrito ChatBroadCast (120): canal (u8: 0 normal, 1 mundo, 3 clã, 8 sistema,
  9 anúncio, 11 instância, 12 super mundo, 14 país), emoção (u8), srcroleid (i32; 0 = sem nome), msg (Octets
  UTF-16LE), data (Octets). O gdeliveryd repassa a todos os jogadores (WorldChat). Texto genérico, escrito pelo usuário
  no plugin: ao começar e ao terminar o evento. Os anúncios da IA (AiAnnounce: morre / entra em combate) continuam valendo.
- **Programa**: C#, o mesmo código do editor (protocolo GNET do painel GM), publicado para Linux (WSL) sem instalar
  nada; roda sempre, com o editor fechado. Lê as regras dos plugins (arquivo enviado pelo "Salvar e enviar") e grava
  estado + histórico que a tela lê.

## Primeira versão (modelos prontos + regras, sem programar)
- **Monstro raro (chance)**: a cada X minutos sorteia N%; se cair, liga o controlador por Y minutos, avisa; no fim
  desliga e avisa.
- **Chefe errante**: horário sorteado dentro de uma janela (ex.: 20h-23h), lugar sorteado entre vários pontos
  (um controlador por lugar), dura X minutos.
- **Mercador viajante / colheita especial / rodízio**: NPC ou pontos de coleta ligados por um período em lugar sorteado;
  rodízio = um evento diferente por dia da semana.

## Tela "Plugins" (barra lateral, acima do Extrator PCK; padrão UiKit)
- Lista de plugins (nome + 2ª linha com o próximo disparo); criar a partir de um modelo; editar as regras.
- Estado ao vivo (cartão): programa rodando ou não, próximo evento, o que está ativo agora.
- Disparar agora / pausar (sem apagar).
- Histórico: hora, plugin, sorteio, ligou/desligou, aviso enviado.
- "Marcar pontos no Mundo 3D": abre o Mundo 3D para pôr os pontos do evento (cria o controlador desligado).

## A decidir na hora de construir
- Formato do arquivo de regras (JSON) e onde fica no servidor (ao lado do gamed/config).
- Como o programa sobe junto com o servidor (script de start do WSL) e como a tela fala com ele (arquivos de estado).
