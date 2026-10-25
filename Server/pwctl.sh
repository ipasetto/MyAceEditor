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
    if ! pgrep -f "authd /etc/table.xml" >/dev/null; then (cd $P/mauthd/build && ./authd.sh start >/dev/null 2>&1); echo "##SUBIU authd"; else echo "##JA authd"; fi
    wait_port 29200 30 || echo "##AVISO authd não abriu a porta 29200"
    svc gamedbd "gamedbd gamesys.conf" gamedbd ./gamedbd gamesys.conf; sleep 3
    svc gacd "gacd gamesys.conf" gacd ./gacd gamesys.conf; sleep 2
    svc gfactiond "gfactiond gamesys.conf" gfactiond ./gfactiond gamesys.conf; sleep 2
    svc gdeliveryd "gdeliveryd gamesys.conf" gdeliveryd ./gdeliveryd gamesys.conf; sleep 3
    svc glinkd "glinkd gamesys.conf 1" glinkd ./glinkd gamesys.conf 1; sleep 3
    maps="${*:-gs01}"
    for m in $maps; do svc "gs-$m" "gs $m\$" gamed ./gs $m; sleep 5; done
    echo "##OK iniciado"
    ;;
stop)
    for p in gamedbd uniquenamed; do pkill -USR1 -x $p 2>/dev/null; done; sleep 3
    for p in glinkd gs gdeliveryd gfactiond gacd gamedbd uniquenamed logservice; do pkill -9 -x $p 2>/dev/null; done
    (cd $P/mauthd/build && ./authd.sh stop >/dev/null 2>&1)
    pkill -9 -f "authd /etc/table.xml" 2>/dev/null
    echo "##OK parado"
    ;;
status)
    for p in logservice uniquenamed gamedbd gacd gfactiond gdeliveryd glinkd gs; do
        if pgrep -x $p >/dev/null; then echo "$p on"; else echo "$p off"; fi
    done
    if pgrep -f "authd /etc/table.xml" >/dev/null; then echo "authd on"; else echo "authd off"; fi
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
    tail -n 60 "$L/$2.log" 2>/dev/null
    ;;
*)
    echo "uso: pwctl start|stop|status|account|log"
    ;;
esac
