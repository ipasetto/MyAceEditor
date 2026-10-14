# Formato elements.data (engine Angelica / Perfect World)

Notas de engenharia reversa do formato binário editado pelo Angelica Core Editor.
Confirmado empiricamente contra `elements.data` v`0x3000003C` (Perfect World 1.4.4).

## Cabeçalho (8 bytes)

| offset | tipo   | campo     | valor no arquivo testado |
|--------|--------|-----------|--------------------------|
| 0      | uint32 | version   | `0x3000003C`             |
| 4      | uint32 | signature | `0x4F614E5B` ("[NaO")    |

`signature` é constante e serve para validar o arquivo.

## Corpo: sequência de blocos

Depois do cabeçalho vem uma sequência fixa de **blocos**. Cada bloco é:

```
uint32 count
registro[count]      // layout fixo por tipo de bloco
```

Os blocos aparecem numa ordem fixa, definida pela versão. Cada tipo de registro tem
um layout próprio (campos de tamanho fixo: int32, float, string UTF-16 de N chars…).

### Primitivos

- `int32` / `uint32` / `float` → 4 bytes
- `wstr[N]` → string UTF-16 de N caracteres (N*2 bytes), cortada no primeiro NUL

### Blocos mapeados até agora

**Bloco 0 — `property_list`** (2580 registros × 84 bytes) — propriedades/efeitos.
Ex.: `A_A11`, `A_A12`, … até `"Aumenta o HP Máximo em 1%."`

| campo | tipo    | bytes |
|-------|---------|-------|
| id    | int32   | 4     |
| name  | wstr[32]| 64    |
| f1    | int32   | 4     |
| f2    | int32   | 4     |
| f3    | int32   | 4     |
| f4    | int32   | 4     |

**Bloco 1 — `equip_category`** (9 registros) — categorias de equipamento (ex.: "Espada").
Layout **parcial**: id + nome confirmados; demais campos a mapear.

## Próximos passos

1. Mapear o layout completo do bloco 1 e seguir pelos blocos seguintes
   (equipamentos, armas, armaduras, itens, NPCs, monstros…). São ~60 blocos.
2. Usar Ghidra/REA no binário do servidor (ou no editor) para extrair as structs
   exatas de cada bloco, em vez de inferir byte a byte.
3. Implementar a ESCRITA (gravar de volta no formato binário) mantendo a ordem dos blocos.
4. Ligar os blocos ao modelo Módulo→Lista→Item→Campo da UI para edição visual.
