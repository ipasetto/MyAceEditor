# Compatibilidade por ferramenta e versão

Cada linha é uma ferramenta do MyAceEditor e cada coluna uma versão do Perfect World que existe no acervo
(PW-KB §S33). Só entra **OK** o que foi testado de verdade (teste de linha de comando ou uso no jogo); o resto fica
**?** até alguém testar. Regra do projeto: mexeu numa versão, reconfere as outras e atualiza esta tabela.

**Legenda:** ✅ OK (regrava idêntico / funciona) · 👁 só leitura · ❌ não funciona (motivo nas notas) · ? não testado · — não existe nessa versão

**Versões (arquivos usados nos testes):**
- **1.3.6** = servidor `PW_Server/Perfect_World_Server_1.3.6` (elements v10, tasks v55, aipolicy versão 0)
- **1.4.4** = cliente `PW 144 BR` (elements v61) e servidor `PWServer_1.4.4v60` (v60)
- **1.5.2** = só o código-fonte do servidor (`source_server_152`); nenhum dado para testar
- **1.5.5** = clientes `PW 155 BR` e `PWI 1.5.5` (elements v156) e os servidores 1.5.5 (o do WSL é o `pw_server_162-155`)
- **1.7.2** = cliente Maori (só a loja foi testada)
- **1.7.6** = servidor `pw_server_1.7.6_2025` e cliente 1.7.6 Build 4311 RU (elements v386, tasks v191)
- **1.8** = cliente `PW 180 BR` (elements v531, tasks v198)

## Arquivos de dados

| Ferramenta | 1.3.6 | 1.4.4 | 1.5.2 | 1.5.5 | 1.7.2 | 1.7.6 | 1.8 | Notas |
|---|---|---|---|---|---|---|---|---|
| Elements (abrir) | ✅ | ✅ | — | ✅ | ? | ✅ | ✅ | --roundtrip / --load-bench |
| Elements (salvar) | ✅ | ✅ | — | ✅ | ? | 👁 | ? | 1.7.6: comprimido MPPC, regravar comprimido ainda não validado ([1]) |
| Quests / tasks.data | ❌ | ✅ | — | ✅ | ? | ✅ | ✅ | v93/v129/v128/v191/v198 regravam idênticos; v55 trava a leitura ([2]) |
| GShop (loja) | ? | ✅ | — | ✅ | ❌ | ❌ | ? | layout V1 (1.4.x) e V2 (1.5.x); 1.7.x precisa do V3 ([3]) |
| AI Policy | ✅ | ✅ | — | ✅ | ? | ✅ | ✅ | aipolicy versão 0 (1.3.6) e operações 103-105 (1.7.6) |
| NPCGen | ✅ | ? | — | ✅ | ? | ✅ | ? | npcgen v10 e v11 regravam idênticos |
| Efeitos do mapa (.ecwld) | ? | ✅ | — | ✅ | ? | ? | ✅ | 332/332 mapas idênticos (144, 155, PWI, 180) |

## Mundo 3D

| Ferramenta | 1.3.6 | 1.4.4 | 1.5.2 | 1.5.5 | 1.7.2 | 1.7.6 | 1.8 | Notas |
|---|---|---|---|---|---|---|---|---|
| Abrir o mapa e ver | ? | ✅ | — | ✅ | ? | ? | ✅ | testado em 144, 155, PWI e 180 (30/10) |
| Monstros e NPCs (pôr, mover, área) | ? | ✅ | — | ✅ | ? | ? | ✅ | grava no npcgen |
| Quests no mundo | ? | ? | — | ✅ | ? | ? | ? | |
| Demais ferramentas do mundo | ? | ? | — | ? | ? | ? | ? | conferir uma por uma |

## Interfaces

| Ferramenta | 1.3.6 | 1.4.4 | 1.5.2 | 1.5.5 | 1.7.2 | 1.7.6 | 1.8 | Notas |
|---|---|---|---|---|---|---|---|---|
| Editar janelas | ? | ✅ | — | ✅ | ? | ? | ✅ | |
| Criar interface (Vincular) | ? | ✅ | — | ✅ | ? | ? | ✅ | --uid-check: interface nova e clássica nos 4 clientes; falta testar no jogo |
| Importar pasta / Trocar com outro cliente | ? | ✅ | — | ✅ | ? | ? | ✅ | |
| Telas de carregamento | ? | ? | — | ✅ | ? | ? | ? | |
| Tradutor de textos (configs/interfaces) | ? | ✅ | — | ✅ | ? | ? | ? | 144 → 155 e PWI ← 155 BR |
| Ícones (atlas do surfaces.pck) | ? | ? | — | ✅ | ? | ? | ? | ícone novo testado no 155 BR; falta ver no jogo |
| Extrator PCK | ? | ✅ | — | ✅ | ? | ? | ✅ | .pck v2 e v3 |

## Servidor e jogo ao vivo

Servidores de teste no WSL, um por versão (02/11): 1.4.4 (`D:\PWServer144v60`) e 1.5.5 (`D:\PWServer155`, a reinstalar). As outras versões ficam **?** até haver servidor delas.

| Ferramenta | 1.3.6 | 1.4.4 | 1.5.2 | 1.5.5 | 1.7.2 | 1.7.6 | 1.8 | Notas |
|---|---|---|---|---|---|---|---|---|
| Servidor (instalar, ligar, mapas, enviar arquivos) | ? | ✅ | — | ✅ | ? | ? | ? | 1.4.4: instalado e no ar 02/11 (gs carrega, envio com o cliente certo). 1.5.5: reinstalar na distro própria |
| Painel do GM | ? | ? | — | ✅ | ? | ? | ? | |
| Plugins (Aparição Rara) | ? | ? | — | ✅ | ? | ? | ? | GMControlGame e PublicChat iguais no fonte 1.5.2 e 1.5.3 |
| Anúncio de morte / combate (IA) | ? | ? | ? | ? | ? | ? | ? | corrigido em 02/11, falta ver no jogo |
| Verificação (regras do jogo) | ? | ✅ | — | ✅ | ? | ? | ✅ | 1.4.4: rodou contra o servidor 144 (02/11); linha da página de anúncios conferida em 144 BR, PWI 155 e 180 BR (02/11) |
| Login no servidor local (authd) | ? | ✅ | — | ✅ | ? | ? | ? | 1.4.4: authd com {?=call} do jar ("usuário não existe"); corrigido 02/11 (classpath na ordem do pacote), login e criação de personagem OK 02/11; entrar no mundo cai no cliente ([6]) |

## Preferências

| Ferramenta | 1.3.6 | 1.4.4 | 1.5.2 | 1.5.5 | 1.7.2 | 1.7.6 | 1.8 | Notas |
|---|---|---|---|---|---|---|---|---|
| Cada opção que mexe em arquivo do jogo | ? | ? | — | ? | ? | ? | ? | listar as opções e testar uma por uma |

## Pendências

1. **Elements comprimido (1.7.x):** abre, mas o editor não deixa salvar até a regravação comprimida ser validada.
2. **Quests antigas (v55, 1.3.6):** a leitura não termina; falta um limite com aviso, para não prender a tela, e o layout das versões abaixo de 93.
3. **Loja 1.7.x:** item com tamanho novo (cerca de 1448 B no cliente e 232 B no servidor). Para descobrir o layout: par cliente/servidor do 1.7.6 e o `playermall.cpp` do fonte 1.7.4.
4. **Tudo que está "?":** testar e preencher. Origem dos dados: PW-KB §S33 e §Q, ENGINE-KB §E7 e `docs/AFAZERES.md`.
5. **Login 1.4.4 (authd):** o `application.jar` do mauthd chama os procedimentos com `{?=call}` (o conector manda `SELECT`, o MariaDB recusa como FUNCTION); o pacote traz `build/application/procedure/handler.class` com `{call}`, que só vale com `.` antes do jar (como no `authd.sh` original). Falta o usuário logar no 144.
6. **Entrar no mundo 1.4.4 (cliente PW 144 BR):** login e criação de personagem OK (02/11); o cliente caía ao entrar no mundo por causa da página de anúncios da internet ("ParsePage thread"), que o MyAce só corrigia na instalação e no cliente aberto naquela hora. Corrigido 02/11 (todo envio corrige o cliente usado); falta o usuário entrar.
7. **Instalar servidor (leitura dos pacotes 05/11, PW-KB §S34):** 1.3.6 sem home/ e sem libtask.so; 1.5.5v156 com login em home/auth/build, fora do que o instalador trata; 1.7.6 com 64 bits, gauthd e licenseservice. Células seguem **?** até instalar de verdade.
