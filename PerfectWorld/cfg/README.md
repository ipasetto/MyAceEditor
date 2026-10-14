# Definições do elements.data (.cfg)

Breakthrough: os editores da comunidade (sTASKedit / Rody's Angelica Editor, DataTranslator.exe)
usam arquivos `.cfg` que definem CADA campo de CADA bloco do elements.data, POR VERSÃO.

Formato do .cfg:
```
<nº de blocos>
<n>
NNN - NOME_DO_BLOCO
<flag>
campo1;campo2;...        (nomes)
tipo1;tipo2;...          (tipos: int32, float, wstring:N, string:N, byte...)
```
Tamanhos: int32/float=4, int16=2, byte=1, wstring:N = N bytes, string:N = N bytes.
Blocos especiais: TALK_PROC é "RAW / byte:AUTO" (árvores de diálogo de NPC, tamanho variável).

A versão do arquivo do usuário é 0x3000003C = v60 → **PW_1.4.4_v60.cfg** é o match.

Validado (cfg_walk_poc.py): parse byte-perfeito de ~99% do arquivo real (blocos 0..99,
incluindo a 2ª metade: NPCs, serviços, faces, receitas, pets, fashion) + TALK_PROC (3,1 MB).
Resta afinar os últimos blocos de Territory War no fim.

Campos do TALK_PROC (el_fields.txt): Name, hello_msg, desc, condition.
