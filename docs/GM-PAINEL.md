# Painel do GM — o combinado (entrevista de 29/10/2026)

## O que é
- Programa SEPARADO do editor (ex.: MyAceGM.exe), que só precisa do cliente do jogo. A aba Servidor do MyAceEditor
  tem um botão azul (no estilo do "Administrar") que abre o painel. Mais tarde o mesmo programa vai para um GM
  contratado, que NÃO tem acesso aos arquivos do servidor.
- Age no jogo de verdade (o personagem logado no cliente aberto), não no Mundo 3D.
- Nada de GM ligado ao editor: dar item, teleportar, testar quest… tudo só pelo painel.

## Como os comandos chegam
- Pelo CONSOLE do jogo (o servidor só aceita teleporte, invisível, item, monstro… vindos do cliente do próprio GM).
- O painel abre o elementclient.exe com `console:1` na linha de comando: o console fica ligado sem gravar nada nos
  arquivos do jogo.
- Funções de GM (aviso, banir, calar, expulsar, online) também pelo jogo, com a permissão de GM da conta — assim
  funcionam para o GM contratado, que não tem o servidor.

## Segurança
- Só conta GM. O gs do servidor de teste já recusa os comandos de debug de quem não é GM (PW-KB §S12).

## Funções (os 4 grupos)
- Meu personagem: teleporte, invisível, invencível, nível +1 / máximo com renascimentos, moedas, EXP, cultivo,
  skills sem recarga, item pelo ID, limpar inventário, espaços do inventário.
- Jogadores: online (mapa/posição), ir até, puxar, expulsar, calar, banir/desbanir, item/moedas por correio, dados.
- Mundo: aviso geral, EXP/drop/moedas em dobro, gerar monstro/NPC, spawner/evento, matar monstros do mapa,
  mapas/instâncias, reiniciar com contagem.
- Testar conteúdo: dar/completar/zerar quest pelo ID, ir até o NPC/área da quest, criar item, invocar monstro,
  hora do dia.

## Dentro do painel
- Item por nome ou ID (lista do cliente: nome, ícone, ID) com quantidade.
- Teleporte até NPC/monstro buscando pelo nome.
- Mapa clicável (o mesmo da tecla M): clicar leva até o lugar.

## Feito (29/10, 1ª versão — testar no jogo)
- MyAceEditor.exe --gm [pasta element] (ou o exe copiado como MyAceGM.exe); botão azul "🎮 Painel do GM…" na aba Servidor.
- Abas: Meu personagem | Itens (busca nome/ID, ícone, quantidade) | NPCs e monstros (ir até, invocar) | Jogadores |
  Mundo | Mapa (mapa pintado do mundo; clique = d_goto x z; mostra a coordenada do jogo).
- Envio: GameConsole — traz o jogo à frente, Shift+VK_OEM_3 abre o console, digita a linha (SendInput Unicode), Enter,
  fecha o console e volta ao painel. Se o console já estiver aberto quando o painel age, o atalho o FECHA e a linha
  vai para o chat: deixe o console fechado.
- Comandos usados: d 205, d 10837 1/0, d 8903 73125, d 10852 1, d 2000, d 10822, d 10850 n, d 1988, d 10889 n, d 1989 n,
  d 2016 n, d 10853, d_goto x z, d 8888 tag x z, d_showpos, d 10800 id qtd, d 2001 id, d 8900 id,
  d 10802 id qtd tempo 0, d 10857 id qtd tempo, d 10855 10855, d_settimeofday h,mm; gm_showid, gm_online_num,
  gm_moveto_player id, gm_callin_player id, gm_kickout_role id s "motivo", gm_shutup_role id s "motivo",
  gm_forbid_role 100|102 id s "motivo", gm_broadcast "texto", gm_restart_sev s.
- Ainda não: dar/completar/zerar quest (não achei comando do console para isso), correio, EXP/drop em dobro, lista
  de quem está online com nomes (gm_list_user não faz nada no cliente 1.5.3).

## Teste do usuário 29/10 e correções
- Aspa antes do comando: a tecla do console (aspas no ABNT2) abre o console no "apertar" e o caractere entra nele;
  agora um Backspace o apaga. Fecha com Esc (não pela tecla, que deixava a aspa e às vezes jogava o texto no chat).
  O foco fica no jogo. Várias linhas vão numa abertura só (ex.: subir N níveis = N × d 2000).
- gs 1.5.5 (disassembly do DebugCommandHandler): NÃO tem d 10993 (nível máximo) nem d 205 nesse handler; tem
  2000, 1988, 1989, 2016, 8903 (só com 73125), 10822 (renascer), 10850 (fama), 10889 (EXP, 1 int), 10800, 2001,
  8900, 10802, 10857, 10853, 10855, 10837, 8888, 10852. Fonte: source_server_153 cgame/gs/playercmd.cpp.
- gm_broadcast do console não chegou ao servidor (glinkd/gdeliveryd sem registro); com o servidor neste PC o aviso e
  a lista de quem está online vão pelo gdeliveryd (como o Administrar). gm_online_num só abre a janela do Ctrl+G.
- Mapa: arrastar com o esquerdo, ir com o direito. Itens: lista virtual com todos (antes parava em 500 = só armas).

## Em aberto (conferir antes de prometer)
- Números dos comandos `d <n>` por versão (lista triviapw 2017 não diz a versão).
- Como escrever no console do jogo aberto (janela do elementclient) — teste é do usuário (não abrimos o cliente).
- Quais funções de "Jogadores"/"Mundo" o console aceita e quais só o servidor faz (correio, EXP em dobro, mapas).

## Pedido de 30/10 (chi e eventos) — testar no jogo
- Meu personagem → "Chi (barras de fúria)": "Barras liberadas" 1..3 manda d 1991 n×100 (máximo de chi); "Encher o chi"
  manda 3 × d 1992 (+100 cada). Os dois existem no gs 1.5.5.
- Mundo → "Eventos": lista do Ctrl+G do jogo (GM_ACTIVITY_CONFIG do elements.data). Ligar = liga os gatilhos da
  "openlist"; Desligar = desliga os da "closelist". Também liga/desliga um gatilho do npcgen pelo ID.
  Vai pelo gdeliveryd (GMControlGame "active_npc_generator id"), para cada mapa rodando: precisa do servidor neste PC.
  Sem servidor (GM contratado): Ctrl+G dentro do jogo (o console de texto não tem comando para eventos).
- Tudo sobre GM fica também na PW-KB §S31.
