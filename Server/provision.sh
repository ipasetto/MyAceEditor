#!/bin/bash
# Servidor local do MyAceEditor (PW-KB §S5 §S30): instala um pacote de servidor PW ("server files" com home/ etc/ lib/
# + pasta SQL) dentro da distro WSL "PWServer". Pode rodar de novo: refaz só o que faltar e mantém o banco existente.
# Uso: provision.sh <pasta server files> <pasta SQL> <senha do banco>
# Cada etapa imprime "##PASSO <nome>" (o programa mostra o andamento); erro = linha "##ERRO <texto>" e saída 1.
set -u
SRC="$1"; SQL="$2"; DBPASS="$3"
export DEBIAN_FRONTEND=noninteractive
step() { echo "##PASSO $*"; }
fail() { echo "##ERRO $*"; exit 1; }
[ -d "$SRC/home" ] || fail "não achei $SRC/home"
# reinstalação: para o servidor antes de copiar por cima dos programas (senão "Text file busy")
[ -x /usr/local/bin/pwctl ] && /usr/local/bin/pwctl stop >/dev/null 2>&1

step "Rede e nomes do servidor"
# o WSL regera /etc/hosts a cada início; os serviços do PW se acham por apelidos (dbserver, link1, game1...)
cat > /etc/wsl.conf <<'EOF'
[network]
generateHosts = false
[boot]
systemd = false
EOF
if [ -f "$SRC/etc/hosts" ]; then tr -d '\r' < "$SRC/etc/hosts" > /etc/hosts; fi
grep -q "^127.0.0.1[[:space:]]*localhost" /etc/hosts || sed -i '1i 127.0.0.1 localhost' /etc/hosts
grep -q "$(hostname)" /etc/hosts || echo "127.0.0.1 $(hostname)" >> /etc/hosts

step "Pacotes (bibliotecas de 32 bits, banco de dados, Java)"
dpkg --add-architecture i386
apt-get update -q || fail "apt-get update"
apt-get install -y -q libc6:i386 libgcc-s1:i386 libxml2:i386 zlib1g:i386 \
    mariadb-server mariadb-client openjdk-8-jre-headless dos2unix procps net-tools psmisc openssl || fail "instalação de pacotes"

step "Arquivos do servidor"
mkdir -p /home
cp -a "$SRC/home/." /home/ || fail "cópia de home"
for f in authd.conf table.xml gmopgen.xml GMserver.conf iweb.conf; do [ -f "$SRC/etc/$f" ] && cp "$SRC/etc/$f" /etc/; done
[ -d "$SRC/etc/ssl" ] && cp -a "$SRC/etc/ssl" /etc/pw-ssl 2>/dev/null
# bibliotecas antigas que vêm no pacote (libstdc++.so.5, libpcre.so.0, libtask.so)
# menos a libxml2: a antiga de alguns pacotes (1.5.5) por cima da do Ubuntu derruba o gacd ao ler o gacd.xml (SIGSEGV)
if [ -d "$SRC/lib" ]; then
    for f in "$SRC/lib/"*; do case "$(basename "$f")" in libxml2*) ;; *) cp -a "$f" /usr/lib/i386-linux-gnu/ ;; esac; done
fi
# reinstalação por cima de uma instalação que já trocou a libxml2: volta a do sistema
apt-get install --reinstall -y -q libxml2:i386 >/dev/null 2>&1
ldconfig
mkdir -p /home/logs
chmod -R a+rwX /home
find /home -maxdepth 2 -type f ! -name "*.conf" ! -name "*.txt" ! -name "*.xml" -exec chmod a+x {} +
find /home -maxdepth 3 \( -name "*.sh" -o -name "*.conf" \) -type f -exec dos2unix -q {} + 2>/dev/null
dos2unix -q /etc/table.xml /etc/authd.conf 2>/dev/null

step "Endereços (tudo em 127.0.0.1; o glinkd escuta para o Windows)"
sed -i -E 's/^(address[[:space:]]*=[[:space:]]*)192\.168\.[0-9.]+/\10.0.0.0/' /home/glinkd/gamesys.conf
grep -rl -E "192\.168\.[0-9]+\.[0-9]+" /home --include=*.conf 2>/dev/null | while read -r f; do sed -i -E 's/192\.168\.[0-9]+\.[0-9]+/127.0.0.1/g' "$f"; done

step "Login (authd em Java)"
cat > /home/mauthd/build/authd.sh <<'EOF'
#!/bin/sh
# authd do pacote, com o Java do sistema (o Java 6 do pacote perde links ao vir do Windows)
cd /home/mauthd/build
AULIB=/home/mauthd/lib
cp=$AULIB/mysql-connector-java-5.1.10-bin.jar:.:$AULIB/jio.jar:$AULIB/application.jar:$AULIB/commons-collections-3.1.jar:$AULIB/commons-dbcp-1.2.1.jar:$AULIB/commons-pool-1.2.jar:$AULIB/commons-logging-1.0.4.jar:$AULIB/log4j-1.2.9.jar:.
case $1 in
  start) setsid nohup java -Dcom.wanmei.au.HitEncoding=GBK -cp $cp authd /etc/table.xml Enthrallment > /home/logs/authd.log 2>&1 < /dev/null & echo $! > service.pid ;;
  stop) [ -f service.pid ] && kill -9 "$(cat service.pid)"; rm -f service.pid ;;
esac
EOF
chmod +x /home/mauthd/build/authd.sh
# conexão do authd com o banco: usuário próprio "pw" com a senha gerada pelo programa
sed -i -E "s/username=\"[^\"]*\"/username=\"pw\"/; s/password=\"[^\"]*\"/password=\"$DBPASS\"/" /etc/table.xml

step "Banco de dados"
service mariadb start >/dev/null 2>&1 || service mysql start >/dev/null 2>&1 || fail "não consegui iniciar o MariaDB"
for i in $(seq 1 20); do mysqladmin ping >/dev/null 2>&1 && break; sleep 1; done
if ! mysql -e "USE pw" >/dev/null 2>&1; then
    tr -d '\r' < "$SQL/PW_tabels.sql" | mysql || fail "tabelas (PW_tabels.sql)"
    tr -d '\r' < "$SQL/PW_procedures.sql" | mysql pw || fail "procedimentos (PW_procedures.sql)"
fi
mysql -e "CREATE USER IF NOT EXISTS 'pw'@'localhost' IDENTIFIED BY '$DBPASS'; ALTER USER 'pw'@'localhost' IDENTIFIED BY '$DBPASS';
          CREATE USER IF NOT EXISTS 'pw'@'127.0.0.1' IDENTIFIED BY '$DBPASS'; ALTER USER 'pw'@'127.0.0.1' IDENTIFIED BY '$DBPASS';
          GRANT ALL ON pw.* TO 'pw'@'localhost'; GRANT ALL ON pw.* TO 'pw'@'127.0.0.1';
          GRANT SELECT ON mysql.proc TO 'pw'@'localhost'; GRANT SELECT ON mysql.proc TO 'pw'@'127.0.0.1'; FLUSH PRIVILEGES;" || fail "usuário do banco"
# o conector JDBC do authd (5.1.10, 2009) lê mysql.proc para chamar os procedimentos de login

step "Controle do servidor"
tr -d '\r' < "$(dirname "$0")/pwctl.sh" | sed '1s/^\xEF\xBB\xBF//' > /usr/local/bin/pwctl && chmod +x /usr/local/bin/pwctl
echo "##OK instalado"
