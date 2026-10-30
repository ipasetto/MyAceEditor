#!/bin/bash
# Controle do servidor local (instalado por provision.sh em /usr/local/bin/pwctl).
#   pwctl start [mapas...]   sobe tudo na ordem do pacote (PW-KB §S5); mapas = gs01 (mundo) + instâncias pedidas
#   pwctl stop               para tudo (gamedbd e uniquenamed com SIGUSR1 primeiro, para gravar o banco)
#   pwctl status             1 linha por serviço: "<nome> on|off"
#   pwctl account <login> <senha> [gm]   cria a conta (senha = base64(md5(login+senha))) e, com "gm", dá GM
#   pwctl log <serviço>      últimas linhas do log
P=/home; L=/home/logs
mkdir -p $L

svc() {   # nome processo pasta comando...
    local name=$1 proc=$2 dir=$3; shift 3
    if pgrep -f "$proc" >/dev/null; then echo "##JA $name"; return; fi
    (cd "$P/$dir" && setsid nohup "$@" > "$L/$name.log" 2>&1 < /dev/null &)   # setsid: sobrevive ao fim do comando do wsl
    echo "##SUBIU $name"
}

wait_port() { for i in $(seq 1 ${2:-30}); do (echo > /dev/tcp/127.0.0.1/$1) 2>/dev/null && return 0; sleep 1; done; return 1; }

case "$1" in
start)
    shift
    service mariadb start >/dev/null 2>&1 || service mysql start >/dev/null 2>&1
    svc logservice "logservice logservice.conf" logservice ./logservice logservice.conf; sleep 2
    svc uniquenamed "uniquenamed gamesys.conf" uniquenamed ./uniquenamed gamesys.conf; sleep 2
    # authd do pacote instalado: o do 1.5.x (/home/authd) quando existe; senão o do 1.4.4 (/home/mauthd/build)
    AU=$P/mauthd/build; [ -x $P/authd/authd.sh ] && AU=$P/authd
    if ! pgrep -f "authd [^ ]*table.xml" >/dev/null; then (cd $AU && ./authd.sh start >/dev/null 2>&1); echo "##SUBIU authd"; else echo "##JA authd"; fi
    wait_port 29200 30 || echo "##AVISO authd não abriu a porta 29200"
    svc gamedbd "gamedbd gamesys.conf" gamedbd ./gamedbd gamesys.conf; sleep 3
    # gacd executa código do anti-cheat montado em memória de dados; sem READ_IMPLIES_EXEC o kernel atual derruba
    # (segfault "error 15" com ip no heap) quando ele roda essa rotina — parecia cair ao dar itens
    svc gacd "gacd gamesys.conf" gacd setarch i386 -X ./gacd gamesys.conf; sleep 2
    svc gfactiond "gfactiond gamesys.conf" gfactiond ./gfactiond gamesys.conf; sleep 2
    svc gdeliveryd "gdeliveryd gamesys.conf" gdeliveryd ./gdeliveryd gamesys.conf; sleep 3
    svc glinkd "glinkd gamesys.conf 1" glinkd ./glinkd gamesys.conf 1; sleep 3
    # sem mapas pedidos: o mundo (gs01) e, se o pacote tiver, o is61 (tag 161), onde nascem os personagens novos no
    # 1.5.x; sem ele a tela de personagens abre e entrar no mundo falha (gdeliveryd: PlayerLogin ... gs_id -1)
    if [ -n "$*" ]; then maps="$*"; else maps="gs01"; grep -q '^\[World_is61\]' "$P/gamed/gs.conf" && maps="$maps is61"; fi
    for m in $maps; do svc "gs-$m" "gs $m\$" gamed ./gs $m; sleep 5; done
    echo "##OK iniciado"
    ;;
stop)
    for p in gamedbd uniquenamed; do pkill -USR1 -x $p 2>/dev/null; done; sleep 3
    for p in glinkd gs gdeliveryd gfactiond gacd gamedbd uniquenamed logservice; do pkill -9 -x $p 2>/dev/null; done
    for d in $P/authd $P/mauthd/build; do [ -x $d/authd.sh ] && (cd $d && ./authd.sh stop >/dev/null 2>&1); done
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
maps)     # mapas em execução: "<tag> <RAM em MB>" + memória do Linux
    for pid in $(pgrep -x gs); do
        tag=$(tr '\0' ' ' < /proc/$pid/cmdline | awk '{print $2}')
        echo "$tag $(( $(awk '/VmRSS/{print $2}' /proc/$pid/status) / 1024 ))"
    done
    free -m | awk '/^Mem:/{print "##RAM", $2, $7}'
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
*)
    echo "uso: pwctl start|stop|status|account|log|accounts|gm|gold|ban|unban|maps|gs-start|gs-stop"
    ;;
esac
