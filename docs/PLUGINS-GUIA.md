# Como usar e criar plugins de evento

Plugins são eventos automáticos que rodam **ao lado do servidor**, sem mexer no `gs`. Um programa pequeno
(`myaceplugins`, Linux) fica ligado junto com o servidor, decide quando cada evento acontece e fala com o servidor pelo
`gdeliveryd` — o mesmo caminho que o iweb e o painel do GM usam. Por isso funciona igual em todas as versões do jogo.

O desenho completo está em [PLUGINS.md](PLUGINS.md).

## 1. Usar um plugin (sem programar)

A área **Plugins** do editor tem:

- **Plugins instalados** (botão na barra de cima) — abre a janela com os plugins que o editor tem. Cada plugin tem um
  nome próprio fixo (ex.: *Aparição Rara*) e o botão **Pôr em execução**.
- **Em execução no servidor** (lista à esquerda) — as cópias que você pôs para rodar. Pode haver várias cópias do mesmo
  plugin, cada uma com nome, descrição e regras próprias. O **▶** na frente de cada linha chama o evento na hora.
- À direita, o cartão da cópia escolhida: o que ela está fazendo agora e as regras dela para editar.

Passos para a *Aparição Rara* (um monstro que aparece por sorteio):

1. Em **NPCGen** ou no **Mundo 3D**, clique no ponto do monstro.
2. Em **Plugins**, clique em **Plugins instalados** → **Pôr em execução** na *Aparição Rara* e depois em **Usar o ponto escolhido**. O editor
   cria um evento (controlador) desligado só para aquele ponto e faz o monstro nascer uma vez por evento.
3. Na barra de cima do editor: **Salvar e enviar ao servidor** e reinicie o mapa (o npcgen mudou).
4. Ajuste o sorteio (a cada X minutos, chance %, quanto tempo fica) e os avisos para todos.
5. Na área Plugins: **Salvar e enviar** e **Ligar programa**.

O cartão de cada cópia mostra o que ela está fazendo (próximo sorteio, ativo até...), o resultado da última ação
(faixa verde = deu certo, vermelha = não deu, com o motivo) e quando dá para chamar de novo. O painel do GM também
tem **Chamar agora** e **Encerrar**.

## 2. Compartilhar (arquivo .json)

**Exportar plugin** grava a cópia escolhida num `.json`; **Importar plugin** traz um `.json` para a lista. Formato:

```json
{
  "version": 1,
  "plugins": [
    {
      "id": "rare1", "type": "rare", "name": "Besouro da Sorte", "description": "Aparece no fim da tarde",
      "enabled": true, "worldTag": 1, "controller": 127224,
      "everyMin": 30, "chancePct": 20, "durationMin": 5, "cooldownSec": 75,
      "startChannel": 9, "startText": "Um monstro raro apareceu!",
      "endChannel": 9, "endText": "O monstro raro foi embora.",
      "point": "world · Besouro da Sorte (61648)"
    }
  ]
}
```

| Campo | O que é |
|---|---|
| `type` | qual plugin do catálogo (`rare` = Aparição Rara) |
| `worldTag` | número do mapa no `gs.conf` (1 = mundo) |
| `controller` | ID do evento (controlador) no `npcgen.data` — o que o servidor liga e desliga |
| `everyMin`, `chancePct` | de quanto em quanto tempo sorteia e a chance de cada sorteio |
| `durationMin` | quanto tempo o evento fica ligado; no fim, os monstros que sobraram somem |
| `cooldownSec` | espera mínima entre um evento e o próximo (o renascer do ponto + 15 s) |
| `startChannel`/`endChannel` | canal do aviso: 9 anúncio, 8 sistema, 1 mundo, 12 super mundo |

Ao importar num servidor diferente, refaça o passo **Usar o ponto escolhido** (o ID do controlador é de cada mapa).

## 3. Criar um tipo de plugin novo (para programadores)

Um tipo de plugin tem quatro partes, todas em C#:

1. **Regras** — `PerfectWorld/PluginRules.cs` (`PluginRule`): os campos que o tipo precisa. Use campos novos com
   valor padrão: arquivos antigos continuam abrindo.
2. **Comportamento** — `Plugins/Runner.cs`: o laço `Tick()` roda a cada segundo. Para um tipo novo, trate
   `r.Type == "<seu tipo>"` e decida quando agir. As peças prontas são:
   - `Controller(r, on)` — liga/desliga um controlador do npcgen (`GMControlGame` 380:
     `active_npc_generator` / `cancel_npc_generator`);
   - `Say(canal, texto)` — aviso para todos (`PublicChat` 79);
   - `Result(r, s, ok, msg)` — grava o resultado que as telas mostram;
   - o estado (`plugins-state.json`): `<id>` (texto do agora), `<id>#ready`, `<id>#active`, `<id>#last`.
3. **Catálogo** — `UI/PluginsView.cs` (`Catalog`): nome próprio fixo, ícone e descrição do tipo, e os campos do
   formulário dele.
4. **Textos** — todo texto da tela passa por `Loc.T` com tradução em `lang/en.json`, `ru.json` e `zh-CN.json`.

Compilar o programa para Linux:

```
dotnet publish Plugins/MyAcePlugins.csproj -c Release -r linux-x64 -o Plugins/out
```

O editor leva o `Plugins/out/myaceplugins` junto e instala no servidor ao **Ligar programa**
(`pwctl plugins-start`).

Regras para um plugin novo:

- **Nada dentro do `gs`.** Só o que o `gdeliveryd` aceita de fora (veja `docs/kb/PW-KB.md` §S31). Se precisar de
  algo novo do servidor, procure primeiro no fonte e registre na KB.
- **Funcionar em todas as versões** que o editor abre; o que for de uma versão só precisa de um aviso na tela.
- **Seguro ao reiniciar.** O programa começa com todos os controladores dos plugins desligados.
- Plugins seus não precisam ficar neste repositório: um `.json` exportado já basta para usar em outro servidor.
