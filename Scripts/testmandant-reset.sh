#!/bin/bash
# ════════════════════════════════════════════════════════════════════
# Testinstanz auf «leeren Mandanten» zurücksetzen (Walter-Entscheid 07.09.2026)
#
# Ziel: test.onecrew.ch wird KOMPLETT geleert und als leere Firma neu
# aufgebaut — nur unsere Konfiguration (Lohnstammdaten, Kontoplan,
# Kataloge, Regelwerke, Texte) kommt aus Produktiv, KEIN Mitarbeiter,
# KEINE Filiale, KEINE Integration. Danach wird der Swissdec-Testmandant
# «Muster AG» (SWISSCEC/Testmandant) Schritt für Schritt eingeladen.
#
# Läuft auf dem VPS (ssh ubuntu@83.228.209.119), nicht auf dem Mac:
#     scp Scripts/testmandant-reset.sh Scripts/testmandant-konfig-tabellen.txt ubuntu@83.228.209.119:~/
#     ssh ubuntu@83.228.209.119 'bash ~/testmandant-reset.sh'
#
# Ablauf:
#   1. Sicherheitsabfrage (MUSTER tippen) + Backup der bisherigen Test-DB
#   2. hr-system-test stoppen, hr_system_test DROP + CREATE (Owner hr_test)
#   3. Schema-Bootstrap: Struktur (NUR Struktur) aus hrsystem
#   4. hr-system-test starten → Seeds + Erst-Admin (walter.schaub@gmail.com,
#      ADMIN_INIT_PASSWORD aus /etc/hr-system/test.env), dann wieder stoppen
#   5. Konfigurationstabellen (Liste testmandant-konfig-tabellen.txt) aus
#      hrsystem als Daten-Dump ziehen und in hr_system_test laden
#      (Zieltabellen vorher geleert, damit Seeds nicht mit Prod-IDs kollidieren)
#   6. Sicherheitsschalter: Versand komplett auf Umleitung, Integrations-
#      Tabellen leer, app_setting von Prod-spezifischen Schlüsseln befreit
#   7. Hauptsitz «Muster AG» + Filiale «Hauptsitz Luzern» (#LU) anlegen,
#      damit das Dashboard reagiert (App startet mit 0 Filialen nicht sauber)
#   8. Dokumenten-Storage der Testinstanz leeren, Service starten, Health-Check
#
# Eiserne Regeln (Runbook) bleiben gewahrt: kein Personenbezug wandert nach
# Test, kein DB-User mit Zugriff auf beide DBs (Dump via postgres-Hausmeister
# in eine Datei, Load als hr_test), kein gemeinsames Dokumentenverzeichnis.
# ════════════════════════════════════════════════════════════════════
set -euo pipefail

PROD_DB="hrsystem"
TEST_DB="hr_system_test"
TEST_USER="hr_test"
TEST_UNIT="hr-system-test"
TEST_ENV="/etc/hr-system/test.env"
TEST_PORT="5100"
TEST_STORAGE="/var/data/hr-system-test/documents"
TABELLEN_DATEI="${1:-$HOME/testmandant-konfig-tabellen.txt}"
WORK="$(mktemp -d /tmp/testmandant.XXXXXX)"
trap 'rm -rf "$WORK"' EXIT

echo "══════════════════════════════════════════════════════════════"
echo " Testinstanz → leerer Mandant (Muster AG)"
echo "══════════════════════════════════════════════════════════════"
[ -f "$TABELLEN_DATEI" ] || { echo "✗ Tabellenliste fehlt: $TABELLEN_DATEI"; exit 1; }
sudo test -f "$TEST_ENV" || { echo "✗ $TEST_ENV fehlt"; exit 1; }
systemctl cat "$TEST_UNIT" >/dev/null 2>&1 || { echo "✗ Unit $TEST_UNIT fehlt"; exit 1; }

# Verbindungsdaten der Testinstanz (Passwort NUR aus der Env-Datei, nie im Skript).
TEST_CONN=$(sudo grep -E '^ConnectionStrings__DefaultConnection=' "$TEST_ENV" | cut -d= -f2-)
TEST_PW=$(echo "$TEST_CONN" | tr ';' '\n' | grep -i '^Password=' | cut -d= -f2-)
[ -n "$TEST_PW" ] || { echo "✗ Kein Passwort in $TEST_ENV gefunden"; exit 1; }
TEST_URL="postgresql://${TEST_USER}:${TEST_PW}@127.0.0.1/${TEST_DB}"

mapfile -t TABELLEN < <(grep -vE '^\s*(#|$)' "$TABELLEN_DATEI" | sed -E 's/[[:space:]]+//g')
echo "Konfigurationstabellen laut Liste: ${#TABELLEN[@]}"
[ "${#TABELLEN[@]}" -ge 30 ] || { echo "✗ Tabellenliste unvollständig (${#TABELLEN[@]}) — Abbruch."; exit 1; }
# Vorprüfung: nur Tabellen, die auf Produktiv wirklich existieren (Lesezugriff).
VORHANDEN=$(sudo -u postgres psql -tA -d "$PROD_DB" -c "SELECT table_name FROM information_schema.tables WHERE table_schema='public'")
GEFILTERT=()
for t in "${TABELLEN[@]}"; do
    if grep -qx "$t" <<<"$VORHANDEN"; then GEFILTERT+=("$t"); else echo "   ⚠ übersprungen (existiert auf Produktiv nicht): $t"; fi
done
TABELLEN=("${GEFILTERT[@]}")
echo "Konfigurationstabellen aus Produktiv: ${#TABELLEN[@]}"

echo ""
echo "⚠  Die Test-DB $TEST_DB wird GELÖSCHT und leer neu aufgebaut."
echo "   Alle bisherigen Kunstdaten (999001–999005, E2/E3-Übungen) gehen verloren."
read -r -p "   Zum Bestätigen MUSTER tippen: " ANTWORT
[ "$ANTWORT" = "MUSTER" ] || { echo "Abgebrochen."; exit 1; }

# ── 1. Backup der bisherigen Test-DB (Feuerwehr-Kopie) ───────────────
STAMP=$(date '+%Y-%m-%d_%H-%M')
BACKUP_DIR="/var/backups/hr-system-test"
sudo mkdir -p "$BACKUP_DIR"
echo "── 1/8 Backup der alten Test-DB → $BACKUP_DIR/db-vor-reset-$STAMP.dump"
sudo -u postgres pg_dump -Fc "$TEST_DB" -f "/tmp/db-vor-reset-$STAMP.dump" || echo "   (alte DB nicht dumpbar — weiter)"
[ -f "/tmp/db-vor-reset-$STAMP.dump" ] && sudo mv "/tmp/db-vor-reset-$STAMP.dump" "$BACKUP_DIR/"

# ── 2. Service stoppen, DB neu ───────────────────────────────────────
echo "── 2/8 $TEST_UNIT stoppen, $TEST_DB neu anlegen"
sudo systemctl stop "$TEST_UNIT"
sudo -u postgres psql -v ON_ERROR_STOP=1 -q <<SQL
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${TEST_DB}' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS ${TEST_DB};
CREATE DATABASE ${TEST_DB} OWNER ${TEST_USER};
REVOKE CONNECT ON DATABASE ${TEST_DB} FROM PUBLIC;
GRANT  CONNECT ON DATABASE ${TEST_DB} TO ${TEST_USER};
SQL

# ── 3. Schema-Bootstrap (nur Struktur) ───────────────────────────────
echo "── 3/8 Schema (NUR Struktur) aus $PROD_DB übernehmen"
sudo -u postgres pg_dump --schema-only -Fc "$PROD_DB" > "$WORK/schema.dump"
pg_restore --no-owner --no-acl -d "$TEST_URL" "$WORK/schema.dump"
rm -f "$WORK/schema.dump"

# ── 4. Erststart: Seeds + Erst-Admin ─────────────────────────────────
echo "── 4/8 Erststart (Seeds, Erst-Admin) — warte bis gesund (max. 300 s)"
sudo systemctl start "$TEST_UNIT"
OK=0
for i in $(seq 1 100); do
    if curl -s -m 3 "http://127.0.0.1:${TEST_PORT}/api/instance-info" | grep -q '"label":"[^"]'; then OK=1; break; fi
    sleep 3
done
[ "$OK" = "1" ] || { echo "✗ Testinstanz nach Erststart nicht gesund: sudo journalctl -u $TEST_UNIT -n 80"; exit 1; }
sleep 5   # Seeds fertig schreiben lassen
sudo systemctl stop "$TEST_UNIT"

# ── 5. Konfiguration aus Produktiv laden ─────────────────────────────
echo "── 5/8 Konfigurationstabellen aus $PROD_DB → $TEST_DB"
DUMP_ARGS=()
for t in "${TABELLEN[@]}"; do DUMP_ARGS+=(--table="public.$t"); done
# Daten-Dump (plain SQL, mit setval für die Sequenzen) über den Hausmeister.
sudo -u postgres pg_dump --data-only --no-owner --no-acl \
    "${DUMP_ARGS[@]}" "$PROD_DB" > "$WORK/konfig.sql"
# Zieltabellen leeren (Seeds weg, sonst PK-Kollisionen), dann laden.
# Läuft als postgres (Hausmeister): nur ein Superuser darf die FK-Prüfung
# fürs Laden abschalten (session_replication_role) — hr_test darf das nicht.
{
  echo "SET session_replication_role = replica;"   # FK-Prüfung während des Ladens aus
  for t in "${TABELLEN[@]}"; do echo "TRUNCATE TABLE public.$t CASCADE;"; done
  cat "$WORK/konfig.sql"
  echo "SET session_replication_role = DEFAULT;"
} | sudo -u postgres psql -v ON_ERROR_STOP=1 -q -o /dev/null -d "$TEST_DB" > "$WORK/load.log" 2>&1 || { grep -v "^NOTICE:  truncate cascades" "$WORK/load.log"; echo "✗ Laden der Konfiguration fehlgeschlagen"; exit 1; }
grep -v "^NOTICE:  truncate cascades" "$WORK/load.log" || true
rm -f "$WORK/konfig.sql"
echo "   geladen:"
for t in "${TABELLEN[@]}"; do
    n=$(psql -tA "$TEST_URL" -c "SELECT count(*) FROM public.$t")
    printf "   %-32s %6s Zeilen\n" "$t" "$n"
done

# ── 6. Sicherheitsschalter ───────────────────────────────────────────
echo "── 6/8 Sicherheitsschalter setzen"
psql -v ON_ERROR_STOP=1 -q "$TEST_URL" <<'SQL'
-- Versand: JEDE Kategorie auf Umleitung (kein Mail/SMS scharf).
UPDATE versand_kategorie SET mail_scharf = false, sms_scharf = false, updated_at = now();
-- Integrationen bleiben leer (Seeds könnten Platzhalter angelegt haben).
TRUNCATE TABLE smtp_setting, ecall_setting, dvelop_setting,
               easyatwork_branch_mapping, easyatwork_employee_alias,
               easyatwork_sync_log, easyatwork_sync_state, easyatwork_ma_sync_log CASCADE;
-- app_setting: Prod-spezifische Schlüssel (Mirus/easy/SSO/URLs/Tokens) entfernen.
DELETE FROM app_setting
 WHERE lower(key) ~ '(mirus|easy|eid|sso|oauth|token|secret|password|url|smtp|sms|ecall|dvelop|swissdec_endpoint)';
-- Nur der Admin (Walter) bleibt; alles andere weg (Seeds/Tests).
DELETE FROM user_branch_access;
DELETE FROM app_user WHERE email <> 'walter.schaub@gmail.com';
UPDATE app_user SET is_hr_team = true WHERE email = 'walter.schaub@gmail.com';
SQL
echo "   verbleibende app_setting-Schlüssel:"
psql -tA "$TEST_URL" -c "SELECT '   · ' || key FROM app_setting ORDER BY key"

# ── 7. Hauptsitz + erste Filiale «Muster AG» ─────────────────────────
echo "── 7/8 Hauptsitz Muster AG + Filiale #LU anlegen"
psql -v ON_ERROR_STOP=1 -q "$TEST_URL" <<'SQL'
-- Swissdec-BUR-Nummer hat 9 Zeichen (A92978109) — Spalte war varchar(8).
-- (Im Code ebenfalls auf 20 erweitert, Program.cs; hier sofort für die Test-DB.)
ALTER TABLE company_profile ALTER COLUMN bur_nr TYPE varchar(20);
INSERT INTO hauptsitz (name, uid, strasse, plz, ort, kanton_code, is_active, created_at, updated_at)
SELECT 'Muster AG', 'CHE-999.999.996', 'Bahnhofstrasse 1', '6003', 'Luzern', 'LU', true, now(), now()
 WHERE NOT EXISTS (SELECT 1 FROM hauptsitz WHERE uid = 'CHE-999.999.996');
INSERT INTO company_profile (company_name, restaurant_code, hauptsitz_id, street, house_number, zip_code, city,
                             country, kanton_code, bur_nr, uid_bfs, email, phone, is_active)
SELECT 'Muster AG · Hauptsitz Luzern', 'LU', h.id, 'Bahnhofstrasse', '1', '6003', 'Luzern',
       'CH', 'LU', 'A92978109', 'CHE-999.999.996', 'MusterAG@xxxxx.ch', '041 218 65 32', true
  FROM hauptsitz h WHERE h.uid = 'CHE-999.999.996'
   AND NOT EXISTS (SELECT 1 FROM company_profile WHERE restaurant_code = 'LU');
-- (Admin sieht alle Filialen ohne user_branch_access-Eintrag.)
SQL

# ── 8. Storage leeren, starten, prüfen ───────────────────────────────
echo "── 8/8 Dokumenten-Storage leeren, Service starten"
if [ -d "$TEST_STORAGE" ]; then
    sudo find "$TEST_STORAGE" -mindepth 1 -delete
    sudo chown -R www-data:www-data "$TEST_STORAGE"
fi
sudo systemctl start "$TEST_UNIT"
OK=0
for i in $(seq 1 60); do
    BODY=$(curl -s -m 3 "http://127.0.0.1:${TEST_PORT}/api/instance-info" || true)
    if echo "$BODY" | grep -q '"label":"[^"]'; then OK=1; break; fi
    sleep 3
done
if [ "$OK" = "1" ]; then
    if echo "$BODY" | grep -q '"schemaOk":false'; then
        echo "✗ Schema-Prüfung meldet Abweichung: sudo journalctl -u $TEST_UNIT -n 100 | grep SCHEMA"; exit 1
    fi
    # Der Erst-Admin wurde beim Start neu angelegt (Kataloge-TRUNCATE hatte
    # app_user mitgeleert) → jetzt HR-Team setzen.
    psql -q "$TEST_URL" -c "UPDATE app_user SET is_hr_team = true WHERE email = 'walter.schaub@gmail.com';"
    echo "   Benutzer: $(psql -tA "$TEST_URL" -c "SELECT count(*) FROM app_user") (erwartet 1)"
    echo ""
    echo "✓ Testinstanz läuft als leerer Mandant «Muster AG»."
    echo "  Login: walter.schaub@gmail.com / ADMIN_INIT_PASSWORD aus $TEST_ENV"
    echo "  Nächster Schritt: Filialen BE/VD/TI/AG/ZG + Versicherer aus SWISSCEC/Testmandant/company_export.csv"
else
    echo "✗ Testinstanz nicht gesund: sudo journalctl -u $TEST_UNIT -n 80"; exit 1
fi
