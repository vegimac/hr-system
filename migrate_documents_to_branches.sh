#!/bin/bash
# ════════════════════════════════════════════════════════════════════
# Migriert Alt-Dokumente in die Filial-Ordner-Struktur
#
# Usage: ./migrate_documents_to_branches.sh
#
# 1. Spielt backfill_documents_branch.sql ein (setzt branch_code in DB)
# 2. Verschiebt Dateien physisch von:
#       data/documents/{emp_id}/{file}
#    nach:
#       data/documents/{branch_code}/{emp_id}/{file}
# ════════════════════════════════════════════════════════════════════

set -e

PSQL="/Library/PostgreSQL/18/bin/psql"
PGUSER=postgres
PGDB=hr_system
PROJECT_DIR="/Users/Walter/projects/hr-system"
STORAGE="$PROJECT_DIR/data/documents"

if [ ! -d "$STORAGE" ]; then
    echo "❌ Storage-Verzeichnis nicht gefunden: $STORAGE"
    exit 1
fi

echo "── Schritt 1: branch_code in DB aktualisieren ──"
"$PSQL" -h localhost -U "$PGUSER" -d "$PGDB" -f "$PROJECT_DIR/backfill_documents_branch.sql"

echo ""
echo "── Schritt 2: Dateien verschieben ──"
moved=0
skipped=0

# Hole alle Dokumente mit branch_code aus DB
"$PSQL" -h localhost -U "$PGUSER" -d "$PGDB" -t -A -F '|' \
    -c "SELECT employee_id, branch_code, filename_storage FROM employee_dokument WHERE branch_code IS NOT NULL ORDER BY id" \
| while IFS='|' read emp_id branch_code filename; do
    legacy="$STORAGE/$emp_id/$filename"
    target_dir="$STORAGE/$branch_code/$emp_id"
    target="$target_dir/$filename"

    if [ -f "$legacy" ]; then
        if [ -f "$target" ]; then
            echo "  ⚠ Schon im Ziel, überspringe: $filename"
            skipped=$((skipped+1))
        else
            mkdir -p "$target_dir"
            mv "$legacy" "$target"
            echo "  ✅ $filename  →  $branch_code/$emp_id/"
            moved=$((moved+1))
        fi
    fi
done

echo ""
echo "── Schritt 3: Leere Legacy-Ordner aufräumen ──"
# Jeder Ordner direkt in $STORAGE, dessen Name eine reine Zahl ist (= alter MA-ID-Ordner)
# und der jetzt leer ist: löschen
for dir in "$STORAGE"/*/; do
    name=$(basename "$dir")
    if [[ "$name" =~ ^[0-9]+$ ]]; then
        # Nur löschen wenn leer (kein Inhalt mehr)
        if [ -z "$(ls -A "$dir" 2>/dev/null)" ]; then
            rmdir "$dir"
            echo "  🗑  Leeres Legacy-Verzeichnis entfernt: $name/"
        fi
    fi
done

echo ""
echo "✅ Migration abgeschlossen."
