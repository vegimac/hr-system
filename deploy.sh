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
scp "$TARBALL" "$SERVER_USER@$SERVER_IP:~/"

echo "── 4/4 Server-Deploy ──"
ssh "$SERVER_USER@$SERVER_IP" "bash -s" "$MODE" "$COMMIT" <<'REMOTE'
set -e
MODE="$1"
COMMIT="$2"
TEST_RESULT="-"
PROD_RESULT="-"

log_deploy() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') commit=$COMMIT modus=$MODE test=$TEST_RESULT prod=$PROD_RESULT" \
        | sudo tee -a /var/log/onecrew-deploys.log > /dev/null
}

# ── Testinstanz (Kanarienvogel) ──────────────────────────────────────
if [ "$MODE" = "both" ] || [ "$MODE" = "test" ]; then
    if systemctl list-unit-files 2>/dev/null | grep -q '^hr-system-test\.service'; then
        echo "── Testinstanz deployen ──"
        sudo systemctl stop hr-system-test
        sudo rm -rf /var/www/hr-system-test/*
        sudo tar -xzf ~/hr-system-publish.tar.gz -C /var/www/hr-system-test 2>/dev/null
        sudo chown -R www-data:www-data /var/www/hr-system-test
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
            TEST_RESULT="ok"
            echo "    ✓ Testinstanz gesund (HTTP 200 + Label)"
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
    echo "── Produktiv deployen ──"
    sudo systemctl stop hr-system
    sudo rm -rf /var/www/hr-system/*
    sudo tar -xzf ~/hr-system-publish.tar.gz -C /var/www/hr-system 2>/dev/null
    sudo chown -R www-data:www-data /var/www/hr-system
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
REMOTE

echo ""
if [ "$MODE" = "test" ]; then
    echo "✅ Test-Deployment erfolgreich. App unter https://test.onecrew.ch"
else
    echo "✅ Deployment erfolgreich. App unter https://onecrew.ch"
fi
