# Como contribuir com o MyAceEditor

> **English summary:** MyAceEditor is a native C#/WinForms (.NET 10) editor/engine for Perfect World
> (Angelica engine) game data, licensed under **GPL-3.0**. Contributions are welcome. Docs and code
> comments are in Portuguese (pt-BR), but issues and PRs in English are fine. Before coding, read
> [docs/ARQUITETURA.md](docs/ARQUITETURA.md) and pick a task from [docs/ROADMAP.md](docs/ROADMAP.md).
> Key rules: prove byte-identical round-trip before enabling edits of a format, never commit game files
> or secrets, never copy code from closed/decompiled tools — write your own, and follow the game rules in
> [docs/REGRAS-DO-JOGO.md](docs/REGRAS-DO-JOGO.md) (also for AI assistants).

Obrigado pelo interesse! Este projeto quer tornar o Perfect World **100% editável** com uma
ferramenta aberta, feita pela comunidade e para a comunidade.

## 1. Antes de começar

1. Leia [docs/ARQUITETURA.md](docs/ARQUITETURA.md): visão geral, camadas e fluxo.
2. Leia [docs/REGRAS-DO-JOGO.md](docs/REGRAS-DO-JOGO.md): regras do servidor que nenhuma mudança pode quebrar (valem também para quem usa IA).
3. Escolha uma tarefa em [docs/ROADMAP.md](docs/ROADMAP.md). As marcadas com 🟢 são boas para começar.
4. Abra uma *issue* dizendo no que vai trabalhar, para ninguém fazer a mesma coisa em paralelo.

## 2. Ambiente

- Windows 10/11 e o **SDK do .NET 10**.
- Um cliente de Perfect World para testar (pasta com `element\`). Os arquivos do jogo **não** vêm
  no repositório: use os seus.
- Compilar e rodar:

```bash
dotnet run
```

### Modos de diagnóstico (testam sem abrir a janela)

| Modo | Para quê |
|---|---|
| `--roundtrip <elements.data>` | Prova que ler e gravar devolve o arquivo idêntico |
| `--tasks <tasks.data>` | Mesma prova para as quests |
| `--icons <elements.data>` | Cobertura de ícones lidos do `surfaces.pck` |
| `--addons <elements.data>` | Descrição dos atributos |
| `--check <element> <home do servidor>` | Compara cliente e servidor |
| `--snapshot <saida.png> [aba] [bloco id]` | Print da janela, sem abri-la na tela |

Rode o modo do formato que você mexeu antes de abrir o PR, e cole o resultado no PR.

## 3. Regras de engenharia (inegociáveis)

1. **Round-trip antes de editar.** Um formato só ganha edição depois de provar que ler e gravar sem
   mudanças devolve o arquivo **byte a byte idêntico**.
2. **Nunca destruir dado do usuário.** Gravação sempre com backup (`.bak` com data/hora) e escrita
   atômica (arquivo temporário + troca).
3. **Cliente e servidor andam juntos.** `elements`, `tasks`, `gshop`/`gshopsev` e `aipolicy` existem
   nos dois lados; editar só um quebra o login (ver aba Verificação).
4. **Formatos não conhecem a UI, e a UI não conhece bytes.** Leitores/gravadores ficam em
   `PerfectWorld/`; a interface fica em `UI/`.
5. **Nativo e leve.** Nada de web, Electron ou dependências pesadas. Ícones e textos vêm do próprio jogo.

## 4. Base de conhecimento (KB)

Todo conhecimento novo sobre o jogo ou sobre a engine vai para as KBs, no mesmo PR:

| Arquivo | O que registrar |
|---|---|
| [docs/kb/PW-KB.md](docs/kb/PW-KB.md) | Fatos do **jogo**: formatos, protocolo, servidor, versões |
| [docs/kb/ENGINE-KB.md](docs/kb/ENGINE-KB.md) | Fatos da **engine**: o que foi implementado e como |
| [docs/PARIDADE-RODY.md](docs/PARIDADE-RODY.md) | Marque `[x]` quando uma função de paridade ficar pronta |

Formato: frases curtas, uma ideia por linha, com **etiqueta de confiança** em cada fato:
`[C]` código-fonte lido · `[A]` arquivo real · `[B]` binário · `[T]` testado no nosso código ·
`[I]` inferência · `[?]` dúvida. Contradição resolvida vai para a seção `§Z`; dúvida nova, para `§Q`.

## 4.1 Traduzir a interface (sem programar)

Os textos da interface ficam em arquivos JSON na pasta `lang/`:

- `lang/en.json`: inglês, já traduzido. Use como modelo.
- Para outro idioma, copie `en.json` para `lang/<código>.json` (ex.: `lang/es.json`, `lang/zh.json`) e traduza **só os valores** (o texto à direita dos `:`). Não mude as chaves (o texto à esquerda).
- Mantenha os trechos especiais: `&` (atalho do menu), `
` (quebra de linha), `{0}` e `{1}` (valores preenchidos pelo programa).
- Chave que faltar no seu arquivo aparece em português, então dá para traduzir aos poucos.
- Para testar, escolha o idioma no menu **Idioma / Language** e abra o programa. Quando o idioma aparecer no menu, envie um Pull Request com o arquivo `lang/<código>.json`.

Ao rodar a versão compilada, a pasta `lang\` precisa estar ao lado do `MyAceEditor.exe`.

## 5. Código

- C# moderno, no estilo do código ao redor (nomes, densidade de comentários, organização).
- Comentários e textos da interface em **português**.
- Sem segredos: nada de senhas, chaves de rede (isec/osec) ou dados pessoais, nem em código nem nas KBs.

## 6. Questões legais (importante)

- **Não copie código de ferramentas fechadas ou descompiladas** (editores pagos, código recuperado
  de binários protegidos). Use-as só como referência de *comportamento* e escreva a sua implementação.
  Formatos de arquivo são fatos técnicos e podem ser documentados livremente.
- **Não envie arquivos do jogo** (`*.data`, `*.pck`, imagens, textos extraídos em massa). Eles
  pertencem à Perfect World Co. O `.gitignore` já bloqueia os principais.
- Ao contribuir, você concorda em licenciar sua contribuição sob a **GPL-3.0**, a mesma licença do projeto.

## 7. Commits e pull requests

- Mensagens de commit em português, no imperativo ou descritivas (ex.: "Editor de GShop: leitura do
  gshop.data"), com um parágrafo curto explicando o porquê quando não for óbvio.
- Um PR por assunto. Descreva o que mudou, como testou (saída do modo de diagnóstico) e o que falta.
- Atualize a KB e o [ROADMAP](docs/ROADMAP.md) quando concluir uma tarefa.

## 8. Reportando problemas

Abra uma *issue* com: versão do cliente/servidor (ex.: 1.4.4, `elements.data` v60), o que você fez,
o que esperava, o que aconteceu, e a mensagem de erro ou print. **Não anexe** arquivos do jogo.
