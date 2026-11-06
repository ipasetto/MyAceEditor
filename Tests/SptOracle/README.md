# Juiz das árvores

Programa de 32 bits que chama a speedtreert.dll do cliente 1.5.5 e grava o que o jogo gera, para comparar com SptGrower.

1. Compile com `dotnet build -c Release` e copie a speedtreert.dll do cliente para a pasta de saída (a DLL não vai no repositório).
2. Rode com o dotnet de 32 bits: `spk.dll <pasta com .spt> real_all.txt`.
3. No MyAce: `--spt-all <pasta com .spt> ours_all.txt`.
4. `python compare.py 10 <pasta dos dois arquivos>` mostra o que ainda difere.
