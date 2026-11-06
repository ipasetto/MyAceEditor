# Editor de zonas no Mundo 3D

Pedido do usuário em 06/11: editor completo das zonas cujo nome aparece quando o jogador passa por elas. Tem de ter tratamento de erro,
layout intuitivo para qualquer pessoa, zoom no mapa (também no mapa de guerra territorial), salvar, editar, apagar e criar do zero.

Sucesso: alguém que nunca ouviu falar de `precinct` cria, muda e apaga uma zona, e o jogo mostra o nome, toca a música e faz renascer no
lugar certo. Nenhuma ação deixa cliente e servidor diferentes sem avisar.

Valem as regras de [teleportes para leigos](2026-11-06-teleportes-leigos-design.md). Desenho da tela: [zonas-layout.svg](2026-11-06-zonas-layout.svg).

## O que o estudo achou

Fontes lidas: `el_precinct.cpp`, `city_region.cpp`, `playercmd.cpp`, `pk_protected_filter.cpp`, `EC_World.cpp`, `EC_GameUIMan.cpp`, `DlgMiniMap.cpp`, `DlgFriendList.cpp`
e o exportador oficial `AIGenExportor.cpp`. Medido em 71 mapas do 155 BR contra o servidor 162-155, só com leitura.

### Como o jogo usa uma zona

| Dado | Quem lê | Efeito no jogo |
|---|---|---|
| Nome | cliente | texto em cima do minimapa, aviso ao entrar, lista de amigos. Ciano quando a zona é protegida |
| ID | só cliente | o nome e a música só trocam quando o ID muda. ID 0 nunca mostra nome |
| Música, laço | cliente | toca em ordem. Laço: 0 não repete, 1 repete tudo, 2 repete sem a primeira. O intervalo é ignorado pelo cliente |
| Som de dia e de noite | cliente | som de fundo. O cliente põe `music\` na frente do som; a música já vem com `Music\` no texto |
| Marcas | cliente | lista de lugares no minimapa enquanto o jogador está na zona |
| Forma, prioridade, instância de origem | os dois | decide em que zona o jogador está |
| Cidade e instância de destino | servidor | para onde vai quem morre ou volta à cidade |
| Território | os dois | dono do território, NPCs ligados ao território, serviços do NPC |
| Proteção PK | os dois | jogador até o nível 29 sem nome rosa ou vermelho não pode ser atacado |

### Regras que mudam o desenho

1. **O menor número de prioridade ganha.** `CELPrecinctSet::IsPointIn` usa `p->GetPriority() <= atual`. No empate ganha a que vem depois no arquivo.
   `PrecinctFile.At` faz o contrário, com `>`: em 3.071 de 40.000 pontos sorteados no mapa do mundo ele acha a zona errada. Isso afeta a música do Mundo 3D
   e o território do teleporte novo. A correção entra neste trabalho.
2. **Só valem as zonas da instância atual.** `IsPointIn` pula quem tem origem diferente. Os mapas a32, a40, a64, a70 e a71 guardam zonas de outra origem. a72 e b01 têm
   várias origens na mesma pasta. A ferramenta mostra as da instância aberta e mantém as outras intactas. Quando o arquivo tem mais de uma origem, uma caixa **Instância** no topo da lista troca qual delas aparece.
3. **Os carimbos têm de ser iguais.** O servidor manda o carimbo do `.sev` e o cliente compara com o do `.clt` (`CheckOutInst`). Diferente, o jogador vê um erro de dados
   da instância ao entrar. Mudou o `.sev`, grava carimbo novo nos dois. Mudou só nome, música ou marcas, não toca no `.sev` nem no carimbo, e o servidor nem precisa reiniciar.
4. **O arquivo do servidor é `config/<mapa>/precinct.sev`**, pelo `base_path` do `gs.conf`. No servidor 162-155 não há cópia solta em `config/`.
5. **A mesma ID em várias áreas forma uma zona com partes.** O mapa do mundo tem 67 grupos assim, e as partes nem sempre são iguais: 52 têm o mesmo nome, 39 a mesma cidade, 4 a mesma música.
6. **O mapa do mundo não tem buraco.** 40.000 pontos sorteados, nenhum fora de zona. Em lugar sem zona, a música para, o jogador renasce onde morreu e voltar à cidade falha.
7. **ID não é único entre mapas** (628 repetem em outro mapa), mas o cliente só procura no mapa atual. Basta ser único dentro do mapa.
8. **Área que cruza a si mesma:** o exportador oficial recusa, mas 94 zonas originais têm isso. A regra vale só para zona que o usuário mexeu.
9. **Os arquivos reais incluem as áreas de ID 0 no `.sev`**, embora o exportador das fontes as pule. A lista do `.clt` e a do `.sev` têm a mesma ordem, com ou sem ID 0.
10. **Cidade (0, 0, 0)** aparece em 871 zonas de instância e em nenhuma do mundo. Fica como está. Zona nova precisa de cidade.

### Prova do formato

| Medição (71 mapas, 2.308 áreas) | Resultado |
|---|---|
| `.clt` regravado a partir dos dados lidos | 71 de 71 byte a byte |
| `.sev` regravado a partir dos dados lidos | 71 de 71 byte a byte |
| Coordenada do texto igual à do binário | 68.856 de 68.856 |
| Versões | `.clt` sempre 7; o `.sev` do servidor v156 tem mapas na versão 6 |
| Pares diferentes de fábrica | a23 (carimbo e 4 cidades), a40 (carimbo, 5 áreas no cliente e 6 no servidor) |

Como o texto regrava idêntico, o editor grava o arquivo inteiro no formato oficial. Não precisa de remendo por trecho. O cliente lê certos números na mesma linha, então a quebra de linha faz parte do formato.

## Onde fica a ferramenta

Três caminhos foram avaliados:

- **Só no 3D:** a mediana de uma zona tem 445 m e a maior passa de 1 km, e não se vê a vizinhança. Fazer um polígono pelo chão é difícil para o leigo.
- **Janela separada, como Territórios:** simples, mas tira a pessoa do Mundo 3D e não mostra o chão.
- **Mapa dentro do Mundo 3D (escolhido):** o ladrilho Zonas cobre a cena com o mapa do jogo, com zoom, e o painel da ferramenta toma o lugar do menu, como pede o padrão do Mundo 3D.
  O botão **Ver no chão** mostra a zona aberta desenhada sobre o terreno e **Voltar ao mapa** retorna.

Isso muda um ponto da conversa anterior: editar os cantos acontece no mapa, e o 3D serve para conferir. Clicar no chão em Ver no chão também escolhe a zona.

## A tela

O mapa ocupa o centro. O painel da direita tem dois estados.

**Lista:** seta de voltar, cartão de estado do par, **Nova zona** em azul, busca e lista com nome em destaque e segunda linha cinza (partes, território, proteção, "não mostra nome ao jogador").

**Zona aberta:** os passos abaixo, sempre com **Como vai ficar**, **Salvar zona** e **Excluir esta zona** à vista.

Na barra de baixo do mapa, **Aqui o jogador vê** acompanha o mouse: zona, renascer, território e proteção, pela mesma regra do jogo. Clicar escolhe a zona que o jogador veria ali. Clicar de novo no mesmo ponto passa para a zona de baixo.

Cores: zona aberta em azul, protegida em ciano como no jogo, as demais com contorno claro e fundo quase transparente. O nome só é desenhado quando cabe dentro da zona naquele zoom.

## Os passos

| Passo | O que a pessoa faz | O que o editor resolve sozinho |
|---|---|---|
| 1. Nome | digita o que o jogador lê, com prévia igual ao minimapa | ID novo; zona de ID 0 que ganha nome recebe ID para o nome aparecer |
| 2. Área | arrasta cantos, duplo clique na borda põe canto, botão direito tira; Desenhar de novo; Mais uma parte | altura dos cantos, prioridade e ordem no arquivo |
| 3. Música e som | escolhe na lista, ouve antes, ordena; Repetir: sempre, nunca, sem a primeira; som de dia e de noite | prefixo `music\`; intervalo fica como está |
| 4. Regras do lugar | renascer em (lista das cidades do mapa ou clique no mapa); território (só no mundo, pelos nomes do `domain.data`); Novatos protegidos de PK | instância de destino e altura do ponto |
| 5. Lugares no minimapa | opcional: nome e clique no mapa | altura do ponto |
| Como vai ficar | lê as frases e clica em Salvar zona | escolhe se muda um arquivo ou os dois |

Quando a área encosta em outra, o passo 2 mostra **Fica por cima** ou **Fica por baixo** de cada vizinha. A palavra prioridade não aparece na tela: por cima usa prioridade 0 e vai para o fim do arquivo; por baixo usa o maior número das vizinhas mais 1.

Zona com partes: pílulas Parte 1, Parte 2... Os passos 1, 3 e 4 têm a caixa **Vale para as N partes**, já marcada quando as partes têm valor igual e desmarcada quando diferem. A área é sempre de uma parte só.

## Criar, editar e apagar

**Criar do zero.** Nova zona abre o passo 1. No passo 2 cada clique põe um canto; clicar no primeiro canto ou apertar Enter fecha. O editor preenche música, cidade e território com os da zona que estava embaixo, e a pessoa só muda o que quiser. A zona nova usa ID livre do mapa, origem igual à instância aberta, prioridade 0, e entra no fim do arquivo, então fica por cima do que havia ali.

**Editar.** Clicar numa zona abre os mesmos passos preenchidos, todos com ✓.

**Apagar.** Excluir mostra o que muda para o jogador. Zona com partes pergunta se é só esta parte ou a zona inteira. Se uma parte do mapa ficar sem zona, o aviso fica vermelho, a área aparece hachurada, e o texto diz que ali a música para e quem morre renasce no lugar. A pessoa pode cancelar ou confirmar.

## Zoom

Uma peça nova, `UI/MapCanvas.cs`, cuida do zoom nas duas ferramentas:

- roda do mouse aproxima mantendo o ponto sob o cursor;
- botões **+**, **−** e **Tudo** sempre à vista;
- arrastar um lugar vazio move o mapa; clique sem arrastar seleciona;
- de encaixar o mapa inteiro até 16 vezes, com escala em metros no canto;
- linhas e cantos mantêm o tamanho na tela, e o alvo de clique é medido em pixels da tela;
- abrir uma zona na lista centraliza e enquadra essa zona.

O mapa de fundo é o mesmo da tecla M (`WorldView.WorldMap`): pintado no mundo, pergaminho ou minimapa nas instâncias. O mapa do mundo tem 4 m por pixel, então a imagem fica macia no zoom alto, mas as linhas continuam nítidas. Para precisão, existe Ver no chão.

`DomainEditorWindow` passa a usar o mesmo `MapCanvas`, com os mesmos gestos, e a conversão de `View`, `ToScreen` e `ToMap` sai de lá. A janela da tecla M fica como está.

## Salvar

1. Confere as regras da zona alterada.
2. Monta os bytes em memória, lê de volta e compara com os dados. Diferença cancela antes de tocar no disco.
3. Só se algo do servidor mudou: carimbo novo, diferente do antigo, nos dois arquivos.
4. Confere se o arquivo mudou no disco desde a abertura.
5. Guarda cópia com `BackupStore` dos arquivos que vão mudar.
6. Grava com escrita atômica. Se o segundo arquivo falhar, volta o primeiro.
7. Relê do disco e compara.
8. Atualiza música e território do Mundo 3D.

Mensagem final conforme o que mudou:

- só cliente: "Salvo. Feche e abra o jogo para ver."
- os dois: "Salvo no cliente e no servidor. Reinicie o mapa no servidor e o jogo."

O editor não reinicia o servidor. Quem joga em outra máquina precisa receber o `precinct.clt` novo.

Um `.sev` na versão 6 sobe para a 7 ao gravar, pois a proteção PK não existe na 6 e o mapa do mundo prova que o servidor lê a 7.

## Erros

**Ao abrir**

| Situação | O que a pessoa vê | O que o editor faz |
|---|---|---|
| `.clt` não existe | "Este mapa ainda não tem zonas" e Nova zona | cria o par ao salvar |
| `.clt` quebrado ou versão diferente de 7 | motivo em palavras simples | abre só para ver, ou não abre se não leu; nunca grava por cima |
| Regravar não reproduz os dados lidos | "Este arquivo tem um formato que não conheço" | só leitura |
| Sem pasta do servidor | cartão amarelo: "Só o cliente será salvo" | libera nome, música, marcas; forma e regras perguntam antes |
| `.sev` não existe no mapa | igual ao anterior, citando o mapa | não cria sozinho |
| `.sev` quebrado | cartão vermelho | só cliente |
| Carimbos diferentes (a23, a40) | cartão amarelo: "O jogador vê um erro de dados neste mapa" e botão Igualar | ao salvar, grava o mesmo carimbo |
| Listas diferentes (a40) | cartão vermelho com a diferença | permite só nome, música e marcas até a pessoa escolher Usar o cliente como certo, que regrava o `.sev` depois de mostrar o que some |

**Ao editar**

| Situação | Tipo | Texto |
|---|---|---|
| Nome vazio | barra | "Dê um nome para a zona." |
| Aspas ou quebra de linha no nome | o campo não aceita | até 48 letras; a maior hoje tem 47 |
| Menos de 3 cantos | barra | "Uma zona precisa de pelo menos 3 cantos." |
| Área cruzando a si mesma, em zona mexida | barra, com o canto em vermelho | "A borda cruza a si mesma: arraste o canto marcado." |
| Canto fora do mapa | o canto para na borda | sem mensagem |
| Renascer vazio em zona nova ou em lugar sem terreno | barra | "Escolha onde o jogador renasce." |
| Território que não existe mais | barra | "Este território foi excluído: escolha outro." |
| Música que o cliente não tem | pergunta | linha vermelha: "Este arquivo não existe no cliente. Sem ele a zona fica muda." |
| Parte do mapa sem zona | pergunta | ver Apagar |
| NPC de território passa a outro território | pergunta | lista os NPCs afetados |

Barra significa que Salvar zona fica cinza, com o motivo escrito acima dele. Zona que a pessoa não mexeu nunca barra o salvar.

**Ao salvar**

| Situação | O que acontece |
|---|---|
| Arquivo aberto em outro programa | "Feche o programa que está usando o arquivo" e Tentar de novo |
| Sem permissão ou disco cheio | motivo real; nada muda |
| Arquivo mudou fora do editor | Recarregar ou Gravar por cima |
| Falha no segundo arquivo | o primeiro volta; "Nada foi salvo" |
| Falha ao voltar o primeiro | mostra onde está a cópia de segurança |
| Sair com mudança | Salvar, Descartar ou Cancelar, também ao trocar de zona, de mapa ou fechar |

Falhas vão para o `AppLog`.

## Ctrl+Z

Antes de salvar, cada mudança é um passo: nome, canto, música, regra, parte. Depois de salvar, Ctrl+Z volta o arquivo anterior e regrava o par com um carimbo novo, igual aos teleportes. Excluir e criar também voltam. Ctrl+Y refaz.

## Código

| Arquivo | Papel |
|---|---|
| `PerfectWorld/PrecinctFile.cs` | passa a ler todos os campos e a escrever (`Parse`, `Render`). `At` corrigido, com instância |
| `PerfectWorld/PrecinctServer.cs` | novo: lê e escreve o `.sev` |
| `PerfectWorld/ZonePair.cs` | novo: abre o par, compara, salva, cópia, volta, retrato para o Ctrl+Z. Sem interface |
| `PerfectWorld/ZoneRules.cs` | novo: regras da seção Erros, testáveis sem tela |
| `UI/MapCanvas.cs` | novo: mapa com zoom e arraste. Não conhece zonas nem territórios |
| `UI/WorldView.ZoneTool.cs` | novo: painel, passos e desenho das zonas no mapa |
| `UI/ZoneMusicWindow.cs` | novo: escolher e ouvir músicas |
| `UI/DomainEditorWindow.cs` | usa `MapCanvas` |
| `UI/WorldView.Tools.cs` | ladrilho Zonas e `Tool.Zones` |
| `UI/WorldMusic.cs`, `UI/WorldView.TeleportTool.cs` | usam `At` corrigido |
| `lang/*.json` | textos em en, ru e zh-CN |

## Testes

`--zone-test <maps> <config> <saída>`, sem abrir janela:

- regravar `.clt` e `.sev` de todos os mapas, byte a byte;
- criar, renomear, mover e apagar em memória, e conferir que as outras zonas não mudam;
- `At` igual à regra do jogo nos pontos sorteados;
- carimbo muda só quando o `.sev` muda;
- cada regra de `ZoneRules` barra o caso certo e deixa passar as 94 zonas antigas;
- falha forçada no segundo arquivo, em pasta temporária, devolve o primeiro.

`--zone-shot` gera capturas da lista, da zona aberta e de zoom alto. `--domain-test` continua passando, e uma captura mostra o zoom dos territórios.

Ficam para o jogo, na pasta de testes do usuário: renomear e ver o aviso, entrar sem erro de dados, morrer e renascer, proteção de novato, música, zona nova, zona apagada.

## Fora deste trabalho

- `region.clt` e `.sev`: santuário e caixas de transporte são outra coisa.
- Polígonos do `domain.data`: são pixels do mapa de guerra e não seguem a forma da zona.
- Campo de território do teleporte: as fontes lidas não usam esse valor, então salvar zona não mexe em `world_targets`.
- Roteiros de ajuda que comparam o nome da zona (`GetCurrentPrecinctName`): renomear pode desligar uma dica. Fica registrado, sem checagem.
- Mover zonas entre mapas e editar cantos no 3D.
