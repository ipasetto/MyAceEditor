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

## Em aberto (conferir antes de prometer)
- Números dos comandos `d <n>` por versão (lista triviapw 2017 não diz a versão).
- Como escrever no console do jogo aberto (janela do elementclient) — teste é do usuário (não abrimos o cliente).
- Quais funções de "Jogadores"/"Mundo" o console aceita e quais só o servidor faz (correio, EXP em dobro, mapas).
