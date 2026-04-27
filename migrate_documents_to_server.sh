#!/bin/bash
# ════════════════════════════════════════════════════════════════════
# Migriert Dokumente von lokalem Mac auf den Server
# - DB-Rows werden via Kategorie-/Typ-Namen gemappt (IDs unterschiedlich)
# - Files werden physisch via scp übertragen
#
# Usage: ./migrate_documents_to_server.sh [employee_id]
#        Ohne Argument → alle Mitarbeiter
# ════════════════════════════════════════════════════════════════════

set -euo pipefail

LOCAL_PSQL="/Library/PostgreSQL/18/bin/psql"
LOCAL_PG_USER=postgres
LOCAL_PG_PASS='201058'
LOCAL_PG_DB=hr_system
LOCAL_DOCS_DIR="/Users/Walter/projects/hr-system/data/documents"

# Lokales Passwort als Env-Var setzen — sonst fragt psql bei jedem Aufruf erneut
export PGPASSWORD="$LOCAL_PG_PASS"

SERVER_USER=ubuntu
SERVER_HOST=83.228.209.119
SERVER_DOCS_DIR="/var/data/hr-system/documents"
SERVER_PG_USER=hrapp
SERVER_PG_DB=hrsystem
SERVER_PG_PASS='HrSystemneuVonWalter2026!'

EMPLOYEE_FILTER=""
if [ -n "${1:-}" ]; then
    EMPLOYEE_FILTER="WHERE d.employee_id = $1"
    echo "── Migration für Mitarbeiter-ID $1 ──"
else
    echo "── Migration für ALLE Mitarbeiter ──"
fi

# Aufräumen alter Reste aus Fehlversuchen
rm -f /tmp/migrate_docs.*.sql /tmp/docs_migration.*.tar.gz /tmp/migrate_files.*.txt /tmp/docs_filelist.*.txt 2>/dev/null

# Eindeutige temporäre Dateinamen mit Zeitstempel + PID (macOS-tauglich)
TS=$(date +%Y%m%d_%H%M%S)_$$
SQL_FILE="/tmp/migrate_docs_${TS}.sql"
TAR_FILE="/tmp/docs_migration_${TS}.tar.gz"
FILE_LIST="/tmp/migrate_files_${TS}.txt"
TAR_LIST="/tmp/docs_filelist_${TS}.txt"

# ── Schritt 1: Generiere SQL für Server ─────────────────────────────────
echo ""
echo "1/4  SQL aus lokaler DB generieren…"

"$LOCAL_PSQL" -h localhost -U "$LOCAL_PG_USER" -d "$LOCAL_PG_DB" -t -A <<EOF > "$SQL_FILE"
SELECT 'INSERT INTO employee_dokument (employee_id, dokument_typ_id, branch_code, filename_original, filename_storage, mime_type, groesse_bytes, bemerkung, gueltig_von, gueltig_bis, hochgeladen_von, hochgeladen_am) ' ||
       'SELECT ' ||
       d.employee_id || ', ' ||
       '(SELECT t.id FROM dokument_typ t JOIN dokument_kategorie k ON k.id=t.kategorie_id WHERE k.name = ' || quote_literal(k.name) || ' AND t.name = ' || quote_literal(t.name) || ' LIMIT 1), ' ||
       quote_nullable(d.branch_code) || ', ' ||
       quote_literal(d.filename_original) || ', ' ||
       quote_literal(d.filename_storage) || ', ' ||
       quote_literal(d.mime_type) || ', ' ||
       d.groesse_bytes || ', ' ||
       quote_nullable(d.bemerkung) || ', ' ||
       quote_nullable(d.gueltig_von::text) || '::date, ' ||
       quote_nullable(d.gueltig_bis::text) || '::date, ' ||
       'NULL, ' ||
       quote_literal(d.hochgeladen_am::text) || '::timestamptz ' ||
       'WHERE NOT EXISTS (SELECT 1 FROM employee_dokument ed WHERE ed.employee_id = ' || d.employee_id || ' AND ed.filename_original = ' || quote_literal(d.filename_original) || ');'
FROM employee_dokument d
JOIN dokument_typ t       ON t.id = d.dokument_typ_id
JOIN dokument_kategorie k ON k.id = t.kategorie_id
$EMPLOYEE_FILTER
ORDER BY d.id;
EOF

ROW_COUNT=$(grep -c '^INSERT' "$SQL_FILE" || true)
echo "     → $ROW_COUNT INSERT-Statements generiert ($SQL_FILE)"

if [ "$ROW_COUNT" -eq 0 ]; then
    echo "❌ Keine Dokumente gefunden. Abbruch."
    rm -f "$SQL_FILE"
    exit 0
fi

# ── Schritt 2: Files via tar+scp übertragen ─────────────────────────────
echo ""
echo "2/4  Physische Dateien übertragen…"

# Welche Files brauchen wir? (storage_filename pro DB-Row)
"$LOCAL_PSQL" -h localhost -U "$LOCAL_PG_USER" -d "$LOCAL_PG_DB" -t -A -F'|' <<EOF > "$FILE_LIST"
SELECT branch_code, employee_id, filename_storage
FROM employee_dokument d
$EMPLOYEE_FILTER;
EOF

# Tar-Archiv mit relativen Pfaden bauen
> "$TAR_LIST"
while IFS='|' read -r branch emp fname; do
    [ -z "$fname" ] && continue
    rel="$branch/$emp/$fname"
    if [ -f "$LOCAL_DOCS_DIR/$rel" ]; then
        echo "$rel" >> "$TAR_LIST"
    else
        echo "  ⚠ Lokale Datei fehlt: $rel"
    fi
done < "$FILE_LIST"

FILE_COUNT=$(wc -l < "$TAR_LIST" | tr -d ' ')
echo "     → $FILE_COUNT Dateien werden gepackt"

(cd "$LOCAL_DOCS_DIR" && tar -czf "$TAR_FILE" -T "$TAR_LIST")
echo "     → Tar-Größe: $(du -h "$TAR_FILE" | cut -f1)"

# Übertragen
scp -q "$SQL_FILE" "$SERVER_USER@$SERVER_HOST:/tmp/migrate_docs.sql"
scp -q "$TAR_FILE" "$SERVER_USER@$SERVER_HOST:/tmp/docs_migration.tar.gz"
echo "     → Hochgeladen"

# ── Schritt 3: Auf dem Server entpacken + SQL einspielen ────────────────
echo ""
echo "3/4  Auf dem Server entpacken + SQL einspielen…"
ssh "$SERVER_USER@$SERVER_HOST" 'bash -s' <<'REMOTE'
set -euo pipefail
sudo mkdir -p /var/data/hr-system/documents
sudo tar -xzf /tmp/docs_migration.tar.gz -C /var/data/hr-system/documents
sudo chown -R www-data:www-data /var/data/hr-system

PGPASSWORD='HrSystemneuVonWalter2026!' psql -h localhost -U hrapp -d hrsystem -f /tmp/migrate_docs.sql

# Aufräumen
rm -f /tmp/migrate_docs.sql /tmp/docs_migration.tar.gz
REMOTE

# ── Schritt 4: Lokale Temp-Dateien löschen ──────────────────────────────
rm -f "$SQL_FILE" "$TAR_FILE" "$FILE_LIST" "$TAR_LIST"

echo ""
echo "✅ Migration abgeschlossen."
echo ""
echo "Verifizieren auf dem Server:"
echo "  ssh $SERVER_USER@$SERVER_HOST \"PGPASSWORD='$SERVER_PG_PASS' psql -h localhost -U $SERVER_PG_USER -d $SERVER_PG_DB -c 'SELECT COUNT(*) FROM employee_dokument;'\""
