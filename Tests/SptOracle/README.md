# Juiz das árvores

Programa de 32 bits que chama a speedtreert.dll do cliente 1.5.5 e grava o que o jogo gera, para comparar com SptGrower.

Medida atual (06/11): 84 de 96 árvores idênticas; galhos 100%, folhas 99,9%, frondes 98%.

1. Compile com `dotnet build -c Release` e copie a speedtreert.dll do cliente para a pasta de saída (a DLL não vai no repositório).
2. Rode com o dotnet de 32 bits: `spk.dll --dump <pasta com .spt> <pasta real>`.
3. No MyAce: `--spt-obj <pasta com .spt> <pasta nosso>`.
4. `python compare.py <pasta real> <pasta nosso> 0.03 10` compara vértice a vértice e mostra o que ainda difere.
