# Pincel de grama e flores — desenho

Aprovado pelo usuário em 08/11/2026. Etapa 5 da ferramenta Objetos do mundo do Mundo 3D.

## O que o jogo guarda (PW-KB, 08/11)
- Tipos: lista no `.ecwld` (cabeçalho +40 offset, +48 quantidade): `ECWDFILEGRASSTYPE` 24 B (mistura de alfa, alfa de
  recorte, tamanho, variação, maciez, distância de vista) + `.mox` do `grasses.pck` (flores também são tipos de grama).
- Áreas: `ECBSDFILEGRASS7` (36 B) no `.ecbsd` (tipo, centro, célula, colunas, linhas, sub-terreno, entrada no bsdata),
  referência de 8 B (ID, offset) no bloco do `.ecwld`; mapa de bits (1 bit = 1 tufo) compactado (zlib) em
  `bsdata\<sub+1>.dat`. Sub-terreno = pedaço do mapa − 1 (conferido nas 2.430 áreas do world).
- `bsdata\N.dat`: "MOXB", versão 2, i32 compactado (cabeçalho de 12 B); entradas (u32 tamanho compactado, u32 original,
  dados); no fim, tabela de N offsets + i32 N. Há entradas que não são de grama: o editor não mexe nelas.
- Servidor: nada (grama não tem colisão).

## Desenho
1. Pílula **Grama**: JAV dos tipos (este mapa, world, outros mapas), prévia do tufo, estrela e recentes.
2. **Pincel**: círculo no chão seguindo o mouse; arrastar pinta. Painel: tamanho (1–30 m), densidade (%), Borracha
   (apaga grama de qualquer tipo), Concluir. Cada arrastada = um passo de Ctrl+Z; Esc sai (abandono pergunta).
3. Onde o tipo já tem área: liga os bits dela. Onde não tem: área nova por bloco do cenário (células de 0,5 m).
4. Tipo de outro mapa entra na lista de tipos do mapa no Salvar.
5. Salvar junto dos Objetos do mundo (tudo ou nada, barra azul, backup da ferramenta): bits novos acrescentados no fim
   do `bsdata` (entradas velhas intactas; a tabela aponta para as novas), registro de área novo no fim do `.ecbsd`,
   referência no bloco, tipos novos no `.ecwld`.

## Testes
`--objects-test`: sub-terreno = pedaço, pintar/desfazer/refazer, área nova com tipo de outro mapa salva numa cópia
temporária e relida (tipo, áreas novas e mudadas), `.ecwld` regrava igual.
