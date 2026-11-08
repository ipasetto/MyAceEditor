# Luzes da cena e nome automático das construções — desenho

Aprovado pelo usuário em 08/11/2026. Ferramenta Objetos do mundo do Mundo 3D.

## Luzes (pílula Luzes)
- Arquivo: `maps/<mapa>/scenelights.dat` (CECSceneLights::Load): u32 0x80000001, i32 N, N × 112 B = posição 3f,
  alcance, atenuação 3f, cor 4f (rgb + força; a26 usa 2,0), ambiente 4f, caixa A3DAABB 48 B (sempre posição ± alcance,
  conferido nas 1.687 luzes de 34 mapas), flags (bit "só à noite"). Só cliente.
- No jogo iluminam personagens, NPCs e monstros perto; chão e construções já vêm com a luz assada (fica para a luz do
  terreno).
- Ferramenta: lâmpadas no mundo (cor da luz) com o alcance da escolhida; escolher, mover pelas setas, cartão com
  posição, alcance, cor, ambiente e "só à noite"; Pôr luz (uma por clique, prévia no mouse), Ctrl+C/V, Delete, Ctrl+Z
  por ação; grava no Salvar cenário (tudo ou nada, barra azul, backup da ferramenta). Mapa sem arquivo (world) ganha um.

## Nome automático das construções
- Nome = palpite pela forma (fina e alta = torre/poste; comprida e baixa = muro/cerca; larga e chata =
  piso/plataforma; pequena = objeto pequeno; grande = construção grande; senão construção) + tamanho + mapa onde mais
  aparece. Ex.: "Muro 12×3 m · Cidade do Dragão".
- Trocar o nome no cartão ("Dar nome"). Nomes do usuário num arquivo único do programa
  (`%AppData%\MyAceEditor\building-names.json`), ligados à forma (mesma assinatura do catálogo): outro cliente com a
  mesma construção mostra o mesmo nome. Exportar/importar a lista.
- Aparece no cartão, nas linhas do catálogo e na busca da JAV.
