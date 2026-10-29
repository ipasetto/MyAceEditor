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

# nome de personagem: o gdeliveryd valida convertendo para name_charset; GB2312 recusa á, ç, ã, õ… (cliente em
# português/inglês). UTF-8 aceita qualquer letra; o nome continua gravado em UTF-16.
sed -i -E 's/^(name_charset[[:space:]]*=[[:space:]]*)GB2312/\1UTF-8/' /home/gdeliveryd/gamesys.conf

step "Login (authd em Java)"
# o authd do próprio pacote: /home/authd (1.5.x: java direto + table.xml ao lado) ou /home/mauthd/build (1.4.4).
# Um script authd.sh igual para os dois, com o Java do sistema (o Java 6 do pacote perde links ao vir do Windows).
# O 1.5.x tem outro application.jar (entrega de cash etc.): usar o do 1.4.4 com servidor 1.5.x deixa cash pendente.
make_authd() {   # pasta, table.xml
    local dir=$1 tbl=$2 lib
    lib=$(cd "$dir" && { [ -d lib ] && echo "$dir/lib" || echo "$dir/../lib"; })
    cat > "$dir/authd.sh" <<EOF
#!/bin/sh
cd $dir
cp=\$(ls $lib/*.jar | paste -sd: -):.
case \$1 in
  start) setsid nohup java -Dcom.wanmei.au.HitEncoding=GBK -cp \$cp authd $tbl Enthrallment > /home/logs/authd.log 2>&1 < /dev/null & echo \$! > service.pid ;;
  stop) [ -f service.pid ] && kill -9 "\$(cat service.pid)"; rm -f service.pid ;;
esac
EOF
    chmod +x "$dir/authd.sh"
    # conexão com o banco: usuário próprio "pw" com a senha gerada pelo programa
    sed -i -E "s/username=\"[^\"]*\"/username=\"pw\"/; s/password=\"[^\"]*\"/password=\"$DBPASS\"/" "$tbl"
}
if [ -f /home/authd/authd.class ]; then dos2unix -q /home/authd/table.xml 2>/dev/null; make_authd /home/authd /home/authd/table.xml; fi
if [ -d /home/mauthd/build ]; then make_authd /home/mauthd/build /etc/table.xml; fi

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
