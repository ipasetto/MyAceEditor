# Formato elements.data (engine Angelica / Perfect World)

Engenharia reversa do formato binário editado pelo Angelica Core Editor.
Confirmado contra `elements.data` v`0x3000003C` (Perfect World 1.4.4), lendo o
parser do próprio cliente (`elementclient.exe`, função `FUN_0071a310`) no Ghidra.

## Cabeçalho (8 bytes)

| offset | tipo   | campo     | valor no arquivo testado |
|--------|--------|-----------|--------------------------|
| 0      | uint32 | version   | `0x3000003C`             |
| 4      | uint32 | signature | `0x4F614E5B` ("[NaO")    |

## Estrutura geral

O corpo é uma sequência de **blocos**. O caso comum é:

```
uint32 count
registro[count]      // registro de tamanho fixo por bloco
```

O tamanho de cada registro é uma constante no código do cliente (o `fread(buf, SIZE, 1)`),
então a ordem e os tamanhos são exatos. Cada registro começa, na maioria dos blocos, por
`int id` seguido de `wchar name[...]` (UTF-16). Validado: o walk lê corretamente 20 blocos
com nomes reais (Espada, Lâmina, Armadura Pesada, Poção de Cura, …).

### Duas fases (descoberto por RE)

O parser `FUN_0071a310` lê o arquivo em duas fases:

1. **Fase inline** (~58 blocos): os blocos são lidos direto na função, um após o outro,
   no padrão `count + registros`. Os tamanhos de registro recuperados vão de 0x44 (68) a
   0x8e8 (2280) bytes. (Tabela completa gerada a partir do dump do Ghidra.)

2. **Fase de sub-leitores** (~45 blocos): após a fase inline, o parser chama uma série de
   funções `FUN_0071xxxx(_File)` (p.ex. `FUN_0071fad0`, `FUN_00720df0`, `FUN_0071d5b0`…),
   cada uma lendo seu próprio bloco. Essas structs são mapeadas decompilando cada sub-leitor.

### Seções de tamanho variável

Há pelo menos duas seções que **não** são `count + registros` fixos, e sim um blob variável:

```
uint32 a
uint32 len
byte   data[len]     // tamanho dado pelo campo anterior
uint32 b             // (presente na 1ª ocorrência)
```

Uma ocorre logo após o bloco de 0x270 (≈bloco 20); outra perto do fim do arquivo. O leitor
precisa tratar esses pontos explicitamente (não dá para inferir só pelo tamanho).

### Primitivos

- `int32` / `uint32` / `float` → 4 bytes
- `wchar name[N]` → string UTF-16 de N chars (N*2 bytes), cortada no NUL

## Blocos confirmados (amostra da fase inline)

| idx | rec bytes | exemplo (id "nome") |
|-----|-----------|---------------------|
| 0   | 84 (0x54) | 216 "A_A11" (propriedades/efeitos) |
| 1   | 68 (0x44) | 1 "Espada" (categorias de equipamento) |
| 2   | 356 (0x164) | 2 "Lâmina" |
| 4   | 68        | 125 "Corpo" |
| 5   | 72 (0x48) | 127 "Armadura Pesada" |
| 7   | 68        | 171 "Amuleto" |
| 10  | 68        | 1794 "Recupera HP" |
| 11  | 68        | 1795 "Poção de Cura" |

(blocos maiores como 1420/1128/1168 bytes são itens/equipamentos completos; o nome não fica
no offset 4 — layout a mapear campo a campo)

## Próximos passos

1. Gerar a tabela COMPLETA de tamanhos (fase inline + sub-leitores) a partir do dump do Ghidra
   e tratar as 2 seções variáveis — objetivo: o walk chegar exatamente ao fim do arquivo.
2. Mapear os campos (nomes/tipos) dos registros grandes (itens, equipamentos, NPCs).
3. Implementar a ESCRITA (gravar de volta) preservando a ordem exata dos blocos.
4. Ligar ao modelo Módulo→Lista→Item→Campo da UI.
