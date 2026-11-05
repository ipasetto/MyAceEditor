#!/bin/bash
# Controle do servidor local (instalado por provision.sh em /usr/local/bin/pwctl).
#   pwctl start [mapas...]   sobe tudo na ordem do pacote (PW-KB §S5); mapas = gs01 (mundo) + instâncias pedidas
#   pwctl stop               para tudo (gamedbd e uniquenamed com SIGUSR1 primeiro, para gravar o banco)
#   pwctl status             1 linha por serviço: "<nome> on|off"
#   pwctl account <login> <senha> [gm]   cria a conta (senha = base64(md5(login+senha))) e, com "gm", dá GM
#   pwctl log <serviço>      últimas linhas do log
#   pwctl defaults | defaults-set <tag>...   mapas que sobem com o "start" sem mapas (fora de /home: troca de pacote não apaga)
P=/home; L=/home/logs; DEF=/etc/pwctl-default-maps
mkdir -p $L

svc() {   # nome processo pasta comando...
    local name=$1 proc=$2 dir=$3; shift 3
    if pgrep -f "$proc" >/dev/null; then echo "##JA $name"; return; fi
    (cd "$P/$dir" && setsid nohup "$@" > "$L/$name.log" 2>&1 < /dev/null &)   # setsid: sobrevive ao fim do comando do wsl
    # espera o processo existir: se o comando do wsl acaba antes do setsid, o WSL mata o filho e ele nunca sobe
    # (gs-start sozinho voltava na hora e a instância não subia; o "start" escapava pelo sleep entre os mapas)
    for i in $(seq 1 20); do pgrep -f "$proc" >/dev/null && { sleep 1; echo "##SUBIU $name"; return; }; sleep 0.5; done
    echo "##ERRO $name não subiu (veja $L/$name.log)"
}

# mapas padrão além do gs01: a lista gravada; sem lista, o is61 (tag 161) se o pacote tiver, onde nascem os personagens
# novos no 1.5.x; sem ele a tela de personagens abre e entrar no mundo falha (gdeliveryd: PlayerLogin ... gs_id -1)
defmaps() { if [ -f $DEF ]; then cat $DEF; else grep -q '^\[World_is61\]' "$P/gamed/gs.conf" && echo is61; fi; true; }

# trava de velocidade do gs (property_policy::UpdateSpeed, playertemplate.h): bônus fixo de movimento > 3 m/s vira 3 e
# > 5 vira 0. O bloco (andar e correr) é achado pela sequência de instruções; desligar = trocar os 2 primeiros bytes
# por um salto para o fim do bloco (0x7c bytes); ligar = voltar os 2 bytes. gs de outra versão: não acha e não mexe. 
SPEEDCAP_REST='\xec\xd9\x05.{4}\xd9\xc9\xda\xe9\xdf\xe0\xf6\xc4\x45\x74\x02\xeb\x28\xd9\x45\xec\xd9\x05.{4}\xd9\xc9\xda\xe9\xdf\xe0\xf6\xc4\x45\x74\x02\xeb\x0a\xb8....\x89\x45\xec\xeb\x08\xb8..\x40\x40\x89\x45\xec\xd9\x45\xe4\xd9\x05.{4}\xd9\xc9\xda\xe9\xdf\xe0\xf6\xc4\x45\x74\x02\xeb\x28\xd9\x45\xe4\xd9\x05.{4}\xd9\xc9\xda\xe9\xdf\xe0\xf6\xc4\x45\x74\x02\xeb\x0a\xb8....\x89\x45\xe4\xeb\x08\xb8..\x40\x40\x89\x45\xe4'
speedcap_find() {   # imprime "<offset> on|off" (um só achado) ou nada; perl lê o binário inteiro (grep para em 0x0a)
    SC="$SPEEDCAP_REST" perl -0777 -ne 'my $r = $ENV{SC}; my (@on, @off);
        while (/\xd9\x45$r/sg) { push @on, pos() - length($&) }
        while (/\xeb\x7a$r/sg) { push @off, pos() - length($&) }
        if (@on == 1 && !@off) { print "$on[0] on\n" } elsif (@off == 1 && !@on) { print "$off[0] off\n" }' "$P/gamed/gs"
}

wait_port() { for i in $(seq 1 ${2:-30}); do (echo > /dev/tcp/127.0.0.1/$1) 2>/dev/null && return 0; sleep 1; done; return 1; }

case "$1" in
start)
    shift
    service mariadb start >/dev/null 2>&1 || service mysql start >/dev/null 2>&1
    svc logservice "logservice logservice.conf" logservice ./logservice logservice.conf; sleep 2
    svc uniquenamed "uniquenamed gamesys.conf" uniquenamed ./uniquenamed gamesys.conf; sleep 2
    # authd do pacote instalado: o do 1.5.x (/home/authd) quando existe; senão o do 1.4.4 (/home/mauthd/build)
    AU=$P/mauthd/build; [ -x $P/auth/build/authd.sh ] && AU=$P/auth/build; [ -x $P/authd/authd.sh ] && AU=$P/authd
    if ! pgrep -f "authd [^ ]*table.xml" >/dev/null; then (cd $AU && ./authd.sh start >/dev/null 2>&1); echo "##SUBIU authd"; else echo "##JA authd"; fi
    wait_port 29200 30 || echo "##AVISO authd não abriu a porta 29200"
    svc gamedbd "gamedbd gamesys.conf" gamedbd ./gamedbd gamesys.conf; sleep 3
    # gacd executa código do anti-cheat montado em memória de dados; sem READ_IMPLIES_EXEC o kernel atual derruba
    # (segfault "error 15" com ip no heap) quando ele roda essa rotina — parecia cair ao dar itens
    svc gacd "gacd gamesys.conf" gacd setarch i386 -X ./gacd gamesys.conf; sleep 2
    svc gfactiond "gfactiond gamesys.conf" gfactiond ./gfactiond gamesys.conf; sleep 2
    svc gdeliveryd "gdeliveryd gamesys.conf" gdeliveryd ./gdeliveryd gamesys.conf; sleep 3
    svc glinkd "glinkd gamesys.conf 1" glinkd ./glinkd gamesys.conf 1; sleep 3
    # sem mapas pedidos: o mundo (gs01) + os padrões escolhidos no Administrar (defmaps)
    if [ -n "$*" ]; then maps="$*"; else maps="gs01 $(defmaps)"; fi
    seen=" "
    for m in $maps; do
        case "$seen" in *" $m "*) continue;; esac; seen="$seen$m "
        # mapa que o gs.conf deste pacote não tem (lista salva com outra versão) fica de fora
        grep -q "^\[\(World\|Instance\)_$m\]" "$P/gamed/gs.conf" || { echo "##SEM $m"; continue; }
        svc "gs-$m" "gs $m\$" gamed ./gs $m; sleep 5
    done
    # plugins de evento (docs/PLUGINS.md): sobem junto com o servidor quando estão instalados
    [ -x $P/gamed/myace/myaceplugins ] && svc myaceplugins "^./myaceplugins" gamed/myace ./myaceplugins
    echo "##OK iniciado"
    ;;
stop)
    for p in gamedbd uniquenamed; do pkill -USR1 -x $p 2>/dev/null; done; sleep 3
    for p in myaceplugins glinkd gs gdeliveryd gfactiond gacd gamedbd uniquenamed logservice; do pkill -9 -x $p 2>/dev/null; done
    for d in $P/authd $P/mauthd/build $P/auth/build; do [ -x $d/authd.sh ] && (cd $d && ./authd.sh stop >/dev/null 2>&1); done
    pkill -9 -f "authd [^ ]*table.xml" 2>/dev/null
    echo "##OK parado"
    ;;
status)
    for p in logservice uniquenamed gamedbd gacd gfactiond gdeliveryd glinkd gs; do
        if pgrep -x $p >/dev/null; then echo "$p on"; else echo "$p off"; fi
    done
    if pgrep -f "authd [^ ]*table.xml" >/dev/null; then echo "authd on"; else echo "authd off"; fi
    if mysqladmin ping >/dev/null 2>&1; then echo "mariadb on"; else echo "mariadb off"; fi
    if (echo > /dev/tcp/127.0.0.1/29000) 2>/dev/null; then echo "porta29000 on"; else echo "porta29000 off"; fi
    ;;
account)
    # o cliente põe o login em minúsculas antes de calcular a senha (EC_LoginSwitch.cpp, MakeLower) -> grava igual
    login=$(printf '%s' "$2" | tr 'A-Z' 'a-z'); pass="$3"
    [ -n "$login" ] && [ -n "$pass" ] || { echo "##ERRO login e senha"; exit 1; }
    case "$login$pass" in *[\'\"\\\;\ ]*) echo "##ERRO use só letras e números"; exit 1;; esac
    service mariadb start >/dev/null 2>&1 || service mysql start >/dev/null 2>&1
    hash=$(printf '%s' "$login$pass" | openssl dgst -md5 -binary | base64)
    if [ "$(mysql -N pw -e "SELECT COUNT(*) FROM users WHERE name='$login'")" != "0" ]; then
        mysql pw -e "CALL changePasswd('$login', '$hash')" && echo "##OK senha trocada"
    else
        mysql pw -e "CALL adduser('$login', '$hash', '0', '0', '0', '0', '', '0', '0', '0', '0', '0', '0', '0', '', '', '$hash')" || { echo "##ERRO adduser"; exit 1; }
        echo "##OK conta criada"
    fi
    # conta antiga com maiúsculas (a comparação do banco ignora caixa): nome fica em minúsculas
    mysql pw -e "UPDATE users SET name='$login' WHERE name='$login'"
    if [ "$4" = "gm" ]; then
        uid=$(mysql -N pw -e "SELECT id FROM users WHERE name='$login'")
        # addGM grava os direitos na tabela auth; repetir daria chave duplicada
        if [ "$(mysql -N pw -e "SELECT COUNT(*) FROM auth WHERE userid=$uid")" = "0" ]; then
            mysql pw -e "CALL addGM($uid, 1)" && echo "##OK GM" || { echo "##ERRO addGM"; exit 1; }
        else
            echo "##OK GM (já era)"
        fi
    fi
    exit 0
    ;;
log)
    tail -n ${3:-60} "$L/$2.log" 2>/dev/null
    ;;
# ---------- painel de administração (só números nos argumentos; textos não passam por aqui) ----------
accounts)
    # id, login, criada, GM (nº de direitos), online (zona), último login, banida até (unix), cash pendente
    mysql -B -N pw -e "SELECT u.ID, u.name, u.creatime,
        (SELECT COUNT(*) FROM auth a WHERE a.userid=u.ID),
        IFNULL((SELECT p.zoneid FROM point p WHERE p.uid=u.ID AND p.zoneid IS NOT NULL LIMIT 1), ''),
        IFNULL((SELECT MAX(p.lastlogin) FROM point p WHERE p.uid=u.ID), ''),
        IFNULL((SELECT MAX(UNIX_TIMESTAMP(f.ctime)+f.forbid_time) FROM forbid f WHERE f.userid=u.ID AND f.type=100), ''),
        IFNULL((SELECT SUM(c.cash) FROM usecashnow c WHERE c.userid=u.ID), 0)
        FROM users u ORDER BY u.ID"
    ;;
gm)       # pwctl gm <uid> on|off
    uid=$(( $2 + 0 ))
    if [ "$3" = "on" ]; then
        [ "$(mysql -N pw -e "SELECT COUNT(*) FROM auth WHERE userid=$uid")" = "0" ] && mysql pw -e "CALL addGM($uid, 1)"
        echo "##OK GM ligado"
    else
        mysql pw -e "DELETE FROM auth WHERE userid=$uid" && echo "##OK GM desligado"
    fi
    ;;
gold)     # pwctl gold <uid> <gold>: o gdeliveryd entrega na conta (1 gold = 100 de cash)
    uid=$(( $2 + 0 )); cash=$(( $3 * 100 ))
    [ $cash -gt 0 ] || { echo "##ERRO quantidade"; exit 1; }
    mysql pw -e "CALL usecash($uid, 1, 0, 1, 0, $cash, 1, @e)" && echo "##OK gold enviado"
    ;;
ban)      # pwctl ban <uid> <segundos>: proíbe o login (tipo 100, o mesmo que o GMKickoutUser grava)
    uid=$(( $2 + 0 )); secs=$(( $3 + 0 ))
    mysql pw -e "CALL addForbid($uid, 100, $secs, 'MyAceEditor', 0)" && echo "##OK banida"
    ;;
unban)
    uid=$(( $2 + 0 ))
    mysql pw -e "DELETE FROM forbid WHERE userid=$uid" && echo "##OK liberada"
    ;;
deluser)  # pwctl deluser <uid>: apaga a conta do banco de contas (os personagens o painel apaga antes, no gamedbd)
    uid=$(( $2 + 0 ))
    [ $uid -gt 0 ] || { echo "##ERRO conta"; exit 1; }
    mysql pw -e "DELETE FROM auth WHERE userid=$uid; DELETE FROM forbid WHERE userid=$uid; DELETE FROM point WHERE uid=$uid;
                 DELETE FROM usecashnow WHERE userid=$uid; DELETE FROM users WHERE ID=$uid" && echo "##OK conta excluída"
    ;;
maps)     # mapas em execução: "<tag> <RAM em MB> <pronto 0|1>" + memória do Linux
    for pid in $(pgrep -x gs); do
        tag=$(tr '\0' ' ' < /proc/$pid/cmdline | awk '{print $2}')
        # pronto = o gs terminou de carregar o mapa e se anunciou ao glinkd/gdeliveryd
        ok=0; grep -aq announceproviderid "$L/gs-$tag.log" 2>/dev/null && ok=1
        echo "$tag $(( $(awk '/VmRSS/{print $2}' /proc/$pid/status) / 1024 )) $ok"
    done
    free -m | awk '/^Mem:/{print "##RAM", $2, $7}'
    ;;
plugins-start) # programa dos plugins de evento (setsid nohup + espera, como os daemons)
    svc myaceplugins "^./myaceplugins" gamed/myace ./myaceplugins
    ;;
plugins-stop)
    pkill -9 -x myaceplugins 2>/dev/null; echo "##OK parado"
    ;;
gs-start) # pwctl gs-start <tag>
    tag=$(printf '%s' "$2" | tr -cd 'a-z0-9_')
    [ -n "$tag" ] || exit 1
    svc "gs-$tag" "gs $tag\$" gamed ./gs $tag
    ;;
gs-stop)
    tag=$(printf '%s' "$2" | tr -cd 'a-z0-9_')
    [ -n "$tag" ] && pkill -9 -f "^./gs $tag\$" && echo "##OK parado $tag"
    ;;
defaults) # mapas que sobem junto com o servidor (além do gs01): 1 tag por linha
    defmaps
    ;;
defaults-set) # pwctl defaults-set <tag>...: grava a lista (gs01 sempre sobe e não entra nela)
    shift
    for t in "$@"; do t=$(printf '%s' "$t" | tr -cd 'a-z0-9_'); [ -n "$t" ] && [ "$t" != gs01 ] && echo "$t"; done > $DEF.tmp && mv $DEF.tmp $DEF
    echo "##OK padrões gravados"
    ;;
speedcap) # estado da trava de velocidade no gs: "on" (original), "off" (removida) ou "none" (gs sem o trecho conhecido)
    r=$(speedcap_find); echo "${r#* }" | grep -qx "on\|off" && echo "${r#* }" || echo none
    ;;
speedcap-set) # pwctl speedcap-set on|off: para os mapas, troca 2 bytes do gs (backup do original uma vez) e sobe de novo
    want=$2; [ "$want" = on ] || [ "$want" = off ] || { echo "##ERRO use on|off"; exit 1; }
    r=$(speedcap_find); [ -n "$r" ] || { echo "##ERRO trecho da trava não achado neste gs"; exit 1; }
    off=${r% *}; cur=${r#* }
    [ "$cur" = "$want" ] && { echo "##OK já está $want"; exit 0; }
    [ -f "$P/gamed/gs.original-trava" ] || cp -p "$P/gamed/gs" "$P/gamed/gs.original-trava"
    running=$(for pid in $(pgrep -x gs); do tr '\0' ' ' < /proc/$pid/cmdline | awk '{print $2}'; done)
    pkill -9 -x gs 2>/dev/null; sleep 1   # executável em uso não pode ser gravado (Text file busy)
    if [ "$want" = off ]; then printf '\xeb\x7a'; else printf '\xd9\x45'; fi | dd of="$P/gamed/gs" bs=1 seek="$off" conv=notrunc status=none
    r2=$(speedcap_find); [ "${r2#* }" = "$want" ] && echo "##OK trava $want" || echo "##ERRO a troca não conferiu"
    for m in $running; do svc "gs-$m" "gs $m\$" gamed ./gs $m; sleep 5; done
    ;;
*)
    echo "uso: pwctl start|stop|status|account|log|accounts|gm|gold|ban|unban|maps|gs-start|gs-stop|defaults|defaults-set|speedcap|speedcap-set"
    ;;
esac
