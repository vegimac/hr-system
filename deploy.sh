#!/bin/bash
# ════════════════════════════════════════════════════════════════════
# Deploy-Skript für Schaub HR-System (OneCrew)
# Mac → Server (Infomaniak VPS) — gestaffelt: zuerst Test, dann Prod
#
# Usage:  ./deploy.sh          → Testinstanz, dann Produktiv (Standard)
#         ./deploy.sh test     → nur Testinstanz
#         ./deploy.sh prod     → nur Produktiv
#
# Kanarienvogel-Prinzip (Bauplan v1.2, 22.08.2026): schlägt der
# Test-Deploy oder sein Gesundheits-Check fehl, bricht das Skript ab,
# BEVOR Produktiv angefasst wird. Existiert die Test-Unit (noch) nicht,
# wird der Test-Teil übersprungen — heutiges Verhalten bleibt.
#
# Gesundheits-Checks (getrennt, bewusst unterschiedlich):
#   Test: HTTP 200 UND Label nicht leer auf 127.0.0.1:5100/api/instance-info
#         (Timeout 300 s — der Erststart seedet die DB, das dauert Minuten)
#   Prod: NUR HTTP 200 — das Prod-Label ist absichtlich leer!
#         Port zur Laufzeit aus der Prod-Unit/Env gelesen; nicht lesbar →
#         Fallback systemctl is-active.
#
# Weg zurück (Walter 08.10.2026): nach dem Stop und VOR dem Entpacken wird
# die Datenbank verschlüsselt gesichert (…/vor-deploy/, mit Probe-Entschlüsselung)
# und das bisherige Programm nach /var/www/<app>.vorher verschoben.
# Scheitert die Sicherung, startet das alte Programm wieder und der Deploy
# bricht ab — es gibt keinen Deploy ohne Sicherung. Zurückrollen: RESTORE.md D/E.
# ════════════════════════════════════════════════════════════════════

set -e  # Bei jedem Fehler abbrechen

MODE="${1:-both}"
case "$MODE" in both|test|prod) ;; *) echo "Usage: ./deploy.sh [test|prod]"; exit 1;; esac

PROJECT_DIR="/Users/Walter/projects/hr-system"
SERVER_USER="ubuntu"
SERVER_IP="83.228.209.119"
TARBALL="$HOME/hr-system-publish.tar.gz"

cd "$PROJECT_DIR"
COMMIT=$(git rev-parse --short HEAD 2>/dev/null || echo "unbekannt")

echo "── 1/4 dotnet publish (commit $COMMIT, Modus: $MODE) ──"
# Vorher publish-Ordner löschen, sonst nestet sich dotnet rekursiv hinein
# (Warning NETSDK1194 + 'path too long' bei der .sln + -o-Kombination).
# Explizit .csproj angeben, NICHT die .sln.
rm -rf ./publish
dotnet publish hr-system.csproj -c Release -r linux-x64 --self-contained false -o ./publish

echo "── 2/4 Tar packen ──"
tar -czf "$TARBALL" -C ./publish .
SIZE=$(du -h "$TARBALL" | cut -f1)
echo "    $TARBALL ($SIZE)"

echo "── 3/4 Hochladen ──"
# rsync mit Wiederaufnahme (Walter 10.09.2026): ein Hänger bei 160 MB bricht
# nicht mehr alles ab, sondern setzt beim nächsten Versuch fort. Bis zu 5 Versuche.
if command -v rsync >/dev/null 2>&1; then
    n=0
    until rsync --partial --inplace --progress -e "ssh -o ServerAliveInterval=15 -o ServerAliveCountMax=4" "$TARBALL" "$SERVER_USER@$SERVER_IP:~/"; do
        n=$((n+1)); [ "$n" -ge 5 ] && { echo "Upload nach 5 Versuchen abgebrochen."; exit 1; }
        echo "Upload unterbrochen – Versuch $((n+1))/5 in 5 s …"; sleep 5
    done
else
    scp "$TARBALL" "$SERVER_USER@$SERVER_IP:~/"
fi

echo "── 4/4 Server-Deploy ──"
ssh "$SERVER_USER@$SERVER_IP" "bash -s" "$MODE" "$COMMIT" <<'REMOTE'
set -e
MODE="$1"
COMMIT="$2"
TEST_RESULT="-"
PROD_RESULT="-"
SICHERUNG="-"
SICHERUNG_TEST="-"
SICHERUNG_PROD="-"
STAMP=$(date +%Y-%m-%d_%H-%M-%S)

log_deploy() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') commit=$COMMIT modus=$MODE test=$TEST_RESULT prod=$PROD_RESULT sicherung_test=$SICHERUNG_TEST sicherung_prod=$SICHERUNG_PROD" \
        | sudo tee -a /var/log/onecrew-deploys.log > /dev/null
}

# Datenbank verschlüsselt sichern, solange der Dienst steht (= exakt der Stand
# vor den Start-Migrationen). Danach Probe: entschlüsseln + Inhaltsverzeichnis
# lesen — erst dann gilt die Sicherung. pg_restore --list hört nach dem
# Inhaltsverzeichnis auf; cat leert den Rest, sonst bricht gpg mit «Broken pipe»
# ab, bevor es die Prüfsumme am Dateiende kontrolliert hat. Dateiname beginnt mit «db-», damit die
# nächtliche Rotation (14 Tage) und bei Prod der Swiss-Backup-Upload greifen.
# $1 Datenbank · $2 Passphrase-Datei · $3 Ordner · $4 Kürzel
sicherung_vor_deploy() {
    local db="$1" pass="$2" ordner="$3" kurz="$4"
    local ziel="$ordner/db-vor-deploy-$kurz-$STAMP-$COMMIT.dump.gpg"
    SICHERUNG="-"
    if ! sudo test -f "$pass"; then
        echo "    ✗ Passphrase-Datei fehlt: $pass"
        return 1
    fi
    sudo mkdir -p "$ordner"
    sudo chmod 700 "$ordner"
    if ! sudo bash -c "set -o pipefail; cd /tmp; sudo -u postgres pg_dump -F c '$db' | gpg --batch --yes --passphrase-file '$pass' --symmetric --cipher-algo AES256 --output '$ziel'"; then
        sudo rm -f "$ziel"
        echo "    ✗ Datenbank-Sicherung fehlgeschlagen ($db)"
        return 1
    fi
    if ! sudo bash -c "set -o pipefail; gpg --batch --quiet --passphrase-file '$pass' --decrypt '$ziel' | { pg_restore --list > /dev/null && cat > /dev/null; }"; then
        sudo rm -f "$ziel"
        echo "    ✗ Probe-Entschlüsselung der Sicherung fehlgeschlagen ($db)"
        return 1
    fi
    echo "    ✓ Sicherung: $ziel ($(sudo du -h "$ziel" | cut -f1))"
    # Je System die letzten 10 Sicherungen behalten.
    sudo find "$ordner" -maxdepth 1 -name "db-vor-deploy-$kurz-*.dump.gpg" -printf '%T@ %p\n' \
        | sort -rn | tail -n +11 | cut -d' ' -f2- | xargs -r sudo rm -f
    SICHERUNG=$(basename "$ziel")
}

# Bisheriges Programm nach <app>.vorher schieben statt löschen (eine Generation).
release_beiseite() {
    local app="$1"
    sudo rm -rf "$app.vorher"
    sudo mkdir -p "$app.vorher"
    sudo find "$app" -mindepth 1 -maxdepth 1 ! -name '.*' -exec mv -t "$app.vorher" {} +
}

# Sanduhr-Seite VOR dem Service-Stop nach /var/www/html legen (überlebt
# rm -rf der App-Verzeichnisse). nginx zeigt sie bei 502/503, solange
# Kestrel noch startet (Walter 20.09.2026).
install_laden_seite() {
    sudo mkdir -p /var/www/html /etc/nginx/snippets
    TMP=$(mktemp -d)
    tar -xzf ~/hr-system-publish.tar.gz -C "$TMP" wwwroot/server-laden.html 2>/dev/null || true
    if [ -f "$TMP/wwwroot/server-laden.html" ]; then
        sudo cp "$TMP/wwwroot/server-laden.html" /var/www/html/onecrew-server-laden.html
        sudo chmod a+r /var/www/html/onecrew-server-laden.html
    fi
    rm -rf "$TMP"
    [ -f /var/www/html/onecrew-server-laden.html ] || return 0

    sudo tee /etc/nginx/snippets/onecrew-server-laden.conf > /dev/null <<'NGX'
error_page 502 503 504 /__onecrew-laden;
location = /__onecrew-laden {
    internal;
    auth_basic off;
    alias /var/www/html/onecrew-server-laden.html;
    default_type text/html;
    charset utf-8;
    add_header Cache-Control "no-store" always;
}
NGX

    # Sicherungskopien NIE in sites-enabled ablegen: nginx liest dort jede
    # Datei, eine Kopie waere ein doppelter server-Block (Ausfall 30.09.2026).
    BAK=/etc/nginx/backup-deploy
    sudo mkdir -p "$BAK"
    sudo find /etc/nginx/sites-enabled -maxdepth 1 -name '*.bak-*' -exec mv -t "$BAK" {} + 2>/dev/null || true

    GEAENDERT=""
    for f in /etc/nginx/sites-enabled/*; do
        [ -f "$f" ] || continue
        grep -q 'proxy_pass http://127.0.0.1:5' "$f" || continue
        grep -q 'onecrew-server-laden.conf' "$f" && continue
        sudo cp "$f" "$BAK/$(basename "$f").bak-laden"
        sudo sed -i 's#^[[:space:]]*server {#&\n    include /etc/nginx/snippets/onecrew-server-laden.conf;#' "$f"
        GEAENDERT="$GEAENDERT $f"
        echo "    nginx-Include in $f"
    done
    if sudo nginx -t >/dev/null 2>&1; then
        sudo systemctl reload nginx
        echo "    nginx: Sanduhr-Seite bei 502 aktiv"
        for f in $GEAENDERT; do sudo rm -f "$BAK/$(basename "$f").bak-laden"; done
    else
        echo "    nginx: Konfig-Check fehlgeschlagen — Include zurückgenommen"
        for f in $GEAENDERT; do
            [ -f "$BAK/$(basename "$f").bak-laden" ] && sudo mv "$BAK/$(basename "$f").bak-laden" "$f"
        done
    fi
}
install_laden_seite

# ── Testinstanz (Kanarienvogel) ──────────────────────────────────────
if [ "$MODE" = "both" ] || [ "$MODE" = "test" ]; then
    if systemctl list-unit-files 2>/dev/null | grep -q '^hr-system-test\.service'; then
        echo "── Testinstanz deployen ──"
        sudo systemctl stop hr-system-test
        if ! sicherung_vor_deploy hr_system_test /etc/hr-system/backup-test.passphrase \
                /var/backups/hr-system-test/vor-deploy test; then
            sudo systemctl start hr-system-test
            TEST_RESULT="FEHLER-SICHERUNG"
            log_deploy
            echo ""
            echo "✗ FEHLER: Keine Sicherung der Test-Datenbank — Deploy abgebrochen."
            echo "  Testinstanz läuft wieder mit dem alten Programm, Produktiv unberührt."
            exit 1
        fi
        SICHERUNG_TEST="$SICHERUNG"
        release_beiseite /var/www/hr-system-test
        sudo tar -xzf ~/hr-system-publish.tar.gz -C /var/www/hr-system-test 2>/dev/null
        sudo chown -R www-data:www-data /var/www/hr-system-test
        # Mac tar behält oft 600 — nginx/Diagnose brauchen world-readable wwwroot
        sudo chmod -R a+rX /var/www/hr-system-test/wwwroot
        sudo systemctl start hr-system-test

        # Check: HTTP 200 UND Label nicht leer (Prod hätte ein leeres Label —
        # so beweist der Check auch, dass die richtige Instanz antwortet).
        # 100 Versuche à 3 s = Timeout 300 s (Erststart-Seed dauert Minuten).
        echo "    warte auf Testinstanz (max. 300 s) …"
        TEST_OK=0
        for i in $(seq 1 100); do
            RESPONSE=$(curl -s -m 3 -w '\n%{http_code}' http://127.0.0.1:5100/api/instance-info 2>/dev/null || true)
            HTTP_CODE=$(echo "$RESPONSE" | tail -n 1)
            BODY=$(echo "$RESPONSE" | sed '$d')
            if [ "$HTTP_CODE" = "200" ] && echo "$BODY" | grep -q '"label":"[^"]'; then
                TEST_OK=1
                break
            fi
            sleep 3
        done

        if [ "$TEST_OK" = "1" ]; then
            # Schema-Pruefung (Walter 31.08.2026): stimmt das EF-Modell nicht
            # mit der Datenbank ueberein, scheitert spaeter JEDE Abfrage auf
            # die betroffene Tabelle — bis hin zur Anmeldung. Hier abbrechen,
            # bevor Produktiv angefasst wird.
            if echo "$BODY" | grep -q '"schemaOk":false'; then
                TEST_RESULT="FEHLER-SCHEMA"
                log_deploy
                echo ""
                echo "✗ FEHLER: Modell und Datenbank passen nicht zusammen."
                echo "  Produktiv wird NICHT angefasst (Kanarienvogel)."
                echo "  Welche Spalten fehlen:"
                sudo journalctl -u hr-system-test -n 200 --no-pager \
                    | grep "SCHEMA-PRUEFUNG" | tail -n 30
                echo ""
                echo "  Zu tun: ALTER TABLE ... ADD COLUMN IF NOT EXISTS in Program.cs"
                echo "          UND HasColumnName im AppDbContext ergänzen."
                exit 1
            fi
            TEST_RESULT="ok"
            echo "    ✓ Testinstanz gesund (HTTP 200 + Label + Schema)"
        else
            TEST_RESULT="FEHLER"
            log_deploy
            echo ""
            echo "✗ FEHLER: Testinstanz nach 300 s nicht gesund."
            echo "  Produktiv wird NICHT angefasst (Kanarienvogel)."
            echo "  Diagnose:  sudo journalctl -u hr-system-test -n 50"
            exit 1
        fi
    else
        TEST_RESULT="keine-unit"
        echo "── Testinstanz: keine Unit hr-system-test vorhanden — übersprungen ──"
    fi
fi

# ── Produktiv ────────────────────────────────────────────────────────
if [ "$MODE" = "both" ] || [ "$MODE" = "prod" ]; then
    # Neustart-Seite (Walter 21.09.2026): waehrend der ~5 s Kestrel-Neustart zeigt
    # nginx statt «502 Bad Gateway» eine Seite «OneCrew wird aktualisiert…» mit
    # Auto-Reload alle 3 s — wie auf der Testinstanz (snippets/onecrew-test-start.conf
    # seit 09.09.2026). Idempotent: Snippet + HTML werden jedes Mal geschrieben, das
    # include nur einmal eingehaengt. Scheitert nginx -t, wird die Site-Config
    # zurueckgesetzt und der Deploy laeuft ohne Startseite weiter.
    SITE=/etc/nginx/sites-enabled/hr-system
    if [ -f "$SITE" ]; then
        sudo tee /etc/nginx/snippets/onecrew-prod-start.conf > /dev/null <<'NGX'
# OneCrew Prod: Kestrel-Neustart darf nicht haengen, 502/503/504 -> Startseite (Walter 21.09.2026)
proxy_connect_timeout 2s;
proxy_next_upstream off;
error_page 502 503 504 = /onecrew-startet.html;
NGX
        sudo tee /var/www/html/onecrew-startet-prod.html > /dev/null <<'HTML'
<!doctype html><html lang="de"><head><meta charset="utf-8"><meta http-equiv="refresh" content="3"><title>OneCrew startet…</title>
<style>body{font-family:-apple-system,Helvetica,sans-serif;background:#f6f3ee;color:#3f3f3f;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}div{text-align:center}h1{font-size:22px;margin:0 0 8px}p{color:#8b8b8b;margin:0}</style></head>
<body><div><h1>OneCrew wird gerade aktualisiert…</h1><p>Der Server startet neu. Die Seite lädt in wenigen Sekunden automatisch.</p></div></body></html>
HTML
        if ! grep -q 'onecrew-prod-start.conf' "$SITE"; then
            sudo mkdir -p /etc/nginx/backup-deploy
            sudo cp "$SITE" /etc/nginx/backup-deploy/hr-system.bak-startseite
            awk '{print} /client_max_body_size 100M;/ && !done {print "    include snippets/onecrew-prod-start.conf;"; print "    location = /onecrew-startet.html { root /var/www/html; internal; try_files /onecrew-startet-prod.html =404; }"; done=1}' "$SITE" | sudo tee "$SITE.neu" > /dev/null
            sudo mv "$SITE.neu" "$SITE"
            if sudo nginx -t >/dev/null 2>&1; then
                sudo systemctl reload nginx
                echo "    ✓ Neustart-Seite in nginx (Prod) eingehaengt"
            else
                sudo cp /etc/nginx/backup-deploy/hr-system.bak-startseite "$SITE"
                echo "    ! nginx -t fehlgeschlagen — Neustart-Seite nicht eingehaengt, Config zurueckgesetzt"
            fi
        fi
    fi

    echo "── Produktiv deployen ──"
    sudo systemctl stop hr-system
    if ! sicherung_vor_deploy hrsystem /etc/hr-system/backup.passphrase \
            /var/backups/hr-system/vor-deploy prod; then
        sudo systemctl start hr-system
        PROD_RESULT="FEHLER-SICHERUNG"
        log_deploy
        echo ""
        echo "✗ FEHLER: Keine Sicherung der Produktiv-Datenbank — Deploy abgebrochen."
        echo "  Produktiv läuft wieder mit dem alten Programm."
        exit 1
    fi
    SICHERUNG_PROD="$SICHERUNG"
    release_beiseite /var/www/hr-system
    sudo tar -xzf ~/hr-system-publish.tar.gz -C /var/www/hr-system 2>/dev/null
    sudo chown -R www-data:www-data /var/www/hr-system
    # Mac tar behält oft 600 — nginx/Diagnose brauchen world-readable wwwroot
    sudo chmod -R a+rX /var/www/hr-system/wwwroot
    sudo systemctl start hr-system

    # Prod-Port zur Laufzeit aus Unit/Env lesen (NICHT hart verdrahten).
    # Prod bindet laut Unit an localhost:5000 (verifiziert B0, 22.08.2026) —
    # das Muster akzeptiert localhost UND 127.0.0.1.
    UNITDUMP=$(sudo systemctl cat hr-system 2>/dev/null || true)
    PROD_PORT=$(echo "$UNITDUMP" | grep -oE '(127\.0\.0\.1|localhost):[0-9]+' | head -n 1 | awk -F: '{print $NF}')
    if [ -z "$PROD_PORT" ]; then
        ENVFILE=$(echo "$UNITDUMP" | grep -E '^EnvironmentFile=' | head -n 1 | cut -d= -f2- | sed 's/^-//')
        if [ -n "$ENVFILE" ] && sudo test -f "$ENVFILE"; then
            PROD_PORT=$(sudo grep -oE '(127\.0\.0\.1|localhost):[0-9]+' "$ENVFILE" 2>/dev/null | head -n 1 | awk -F: '{print $NF}')
        fi
    fi

    if [ -n "$PROD_PORT" ]; then
        # Check: NUR HTTP 200. KEIN Label-Check — das Prod-Label ist
        # absichtlich leer; ein Warten auf ein Label würde jeden
        # Produktiv-Deploy per Timeout töten (Cursor-Fund, v1.2).
        echo "    warte auf Produktiv (Port $PROD_PORT, max. 300 s) …"
        PROD_OK=0
        for i in $(seq 1 100); do
            HTTP_CODE=$(curl -s -m 3 -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PROD_PORT/api/instance-info" 2>/dev/null || true)
            if [ "$HTTP_CODE" = "200" ]; then
                PROD_OK=1
                break
            fi
            sleep 3
        done
        if [ "$PROD_OK" = "1" ]; then
            PROD_RESULT="ok"
            echo "    ✓ Produktiv gesund (HTTP 200)"
            # Schema auch auf Produktiv pruefen. Hier nur WARNEN statt
            # abbrechen: die Instanz laeuft bereits, ein Abbruch wuerde nichts
            # zurueckrollen — aber uebersehen darf man es nicht.
            PROD_INFO=$(curl -s -m 3 "http://127.0.0.1:$PROD_PORT/api/instance-info" 2>/dev/null || true)
            if echo "$PROD_INFO" | grep -q '"schemaOk":false'; then
                PROD_RESULT="ok-SCHEMA-WARNUNG"
                echo ""
                echo "⚠ ACHTUNG: Auf Produktiv passen Modell und Datenbank nicht zusammen!"
                sudo journalctl -u hr-system -n 200 --no-pager \
                    | grep "SCHEMA-PRUEFUNG" | tail -n 30
                echo ""
            fi
        else
            PROD_RESULT="FEHLER"
            log_deploy
            echo ""
            echo "✗ FEHLER: Produktiv antwortet nach 300 s nicht."
            echo "  Diagnose:  sudo journalctl -u hr-system -n 50"
            exit 1
        fi
    else
        # Fallback: Port nicht ermittelbar → wie bisher is-active prüfen.
        echo "    (Prod-Port nicht ermittelbar — Fallback is-active)"
        sleep 3
        sudo systemctl is-active hr-system
        PROD_RESULT="ok-isactive"
    fi

    ls -la /var/www/hr-system/hr-system.dll | awk '{print $6,$7,$8,$9}'
fi

log_deploy
echo "── Status ──"
echo "commit=$COMMIT test=$TEST_RESULT prod=$PROD_RESULT"
echo "Sicherung vor Deploy: test=$SICHERUNG_TEST prod=$SICHERUNG_PROD"
echo "Vorheriges Programm:  /var/www/hr-system.vorher bzw. /var/www/hr-system-test.vorher"
REMOTE

echo ""
if [ "$MODE" = "test" ]; then
    echo "✅ Test-Deployment erfolgreich. App unter https://test.onecrew.ch"
else
    echo "✅ Deployment erfolgreich. App unter https://onecrew.ch"
fi
