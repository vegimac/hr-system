#!/bin/bash
# HR-System Backup — lokal + Swiss Backup (rclone copy, nie sync)
# Dokumente zusätzlich Datei für Datei gespiegelt, mit Papierkorb (Walter 08.10.2026).
# Installiert auf dem Server als /usr/local/bin/hr-system-backup.sh (root, 700),
# Cron 03:00. Quelle ist diese Datei im Repo (server/), nicht die Kopie auf dem Server.
set -uo pipefail

DATE=$(date +%Y-%m-%d_%H-%M)
BACKUP_DIR=/var/backups/hr-system
DOCS_DIR=/var/data/hr-system/documents
PASSPHRASE_FILE=/etc/hr-system/backup.passphrase
KEEP_DB_DAYS=14
KEEP_DOCS_DAYS=3
RCLONE_CONF=/root/.config/rclone/rclone.conf
RCLONE_REMOTE=swissbackup:default/onecrew-nachtbackup
# crypt-Remote über swissbackup:default/onecrew-dokumente, Passwort
# /etc/hr-system/backup-docs.passphrase (Einrichtung: RESTORE.md «Dokumente-Spiegel»)
DOCS_REMOTE=swissbackup-doku:aktuell
DOCS_PAPIERKORB=swissbackup-doku:papierkorb
KEEP_PAPIERKORB_DAYS=365

set -a
# shellcheck disable=SC1091
source /etc/hr-system/env
set +a
DB_NAME="${DB_NAME:-hrsystem}"
DB_USER="${DB_USER:-hrapp}"
DB_HOST="${DB_HOST:-localhost}"
DB_PASS="${DB_PASSWORD:-}"

mkdir -p "$BACKUP_DIR"
echo "── $(date '+%Y-%m-%d %H:%M:%S') Backup START ──"
ok=0

if [ -z "$DB_PASS" ]; then
  echo "  ✗ DB_PASSWORD fehlt in /etc/hr-system/env"; ok=1
else
  echo "  PostgreSQL-Dump…"
  if PGPASSWORD="$DB_PASS" pg_dump -h "$DB_HOST" -U "$DB_USER" -F c "$DB_NAME" \
    | gpg --batch --yes --passphrase-file "$PASSPHRASE_FILE" \
          --symmetric --cipher-algo AES256 \
          --output "$BACKUP_DIR/db-$DATE.dump.gpg"
  then
    echo "  → db-$DATE.dump.gpg ($(du -h "$BACKUP_DIR/db-$DATE.dump.gpg" | cut -f1))"
  else
    echo "  ✗ DB-Dump fehlgeschlagen"; ok=1
  fi
fi

if [ -d "$DOCS_DIR" ]; then
  avail_kb=$(df -Pk "$BACKUP_DIR" | awk 'NR==2{print $4}')
  if [ "${avail_kb:-0}" -lt 8500000 ]; then
    echo "  ⚠ Zu wenig Platz für Docs-Backup — übersprungen"; ok=1
  else
    echo "  Documents-Tarball…"
    if tar -czf - -C "$(dirname "$DOCS_DIR")" "$(basename "$DOCS_DIR")" \
      | gpg --batch --yes --passphrase-file "$PASSPHRASE_FILE" \
            --symmetric --cipher-algo AES256 \
            --output "$BACKUP_DIR/docs-$DATE.tar.gz.gpg"
    then
      echo "  → docs-$DATE.tar.gz.gpg ($(du -h "$BACKUP_DIR/docs-$DATE.tar.gz.gpg" | cut -f1))"
    else
      echo "  ✗ Docs-Backup fehlgeschlagen"
      rm -f "$BACKUP_DIR/docs-$DATE.tar.gz.gpg"
      ok=1
    fi
  fi
else
  echo "  ⚠ Documents-Verzeichnis fehlt: $DOCS_DIR"
fi

echo "  Rotation (DB >${KEEP_DB_DAYS}d, Docs >${KEEP_DOCS_DAYS}d)…"
find "$BACKUP_DIR" -name "db-*.dump.gpg"     -mtime +"$KEEP_DB_DAYS"   -delete
find "$BACKUP_DIR" -name "docs-*.tar.gz.gpg" -mtime +"$KEEP_DOCS_DAYS" -delete
find "$BACKUP_DIR" -name "hrsystem-*.sql.gz" -delete 2>/dev/null || true

if [ -f "$RCLONE_CONF" ] && command -v rclone >/dev/null; then
  echo "  Swiss Backup (rclone copy → $RCLONE_REMOTE)…"
  if rclone --config "$RCLONE_CONF" copy "$BACKUP_DIR" "$RCLONE_REMOTE" \
        --include "*.gpg" --retries 3 --low-level-retries 10
  then
    echo "  → Swiss Backup OK"
    rclone --config "$RCLONE_CONF" delete "$RCLONE_REMOTE" \
      --include "db-*.dump.gpg" --min-age "${KEEP_DB_DAYS}d" 2>/dev/null || true
    rclone --config "$RCLONE_CONF" delete "$RCLONE_REMOTE" \
      --include "docs-*.tar.gz.gpg" --min-age "${KEEP_DOCS_DAYS}d" 2>/dev/null || true
  else
    echo "  ✗ Swiss Backup Upload fehlgeschlagen"; ok=1
  fi

  # Gelöschte/überschriebene Dokumente wandern nach papierkorb/<Lauf> statt zu
  # verschwinden. Aufräumen nach Ordnerdatum, NICHT per --min-age: verschobene
  # Dateien behalten ihr altes Änderungsdatum und würden sofort gelöscht.
  if rclone --config "$RCLONE_CONF" listremotes | grep -qx "swissbackup-doku:"; then
    if [ -d "$DOCS_DIR" ] && [ -n "$(ls -A "$DOCS_DIR" 2>/dev/null)" ]; then
      echo "  Dokumente spiegeln (→ $DOCS_REMOTE, Papierkorb ${KEEP_PAPIERKORB_DAYS} Tage)…"
      if rclone --config "$RCLONE_CONF" sync "$DOCS_DIR" "$DOCS_REMOTE" \
            --backup-dir "$DOCS_PAPIERKORB/$DATE" --max-delete 1000 \
            --fast-list --checkers 16 --retries 3 --low-level-retries 10
      then
        echo "  → Dokumente-Spiegel OK"
        grenze=$(date -d "-${KEEP_PAPIERKORB_DAYS} days" +%Y-%m-%d)
        rclone --config "$RCLONE_CONF" lsf --dirs-only "$DOCS_PAPIERKORB" 2>/dev/null \
          | sed 's#/$##' | while read -r lauf; do
              if [[ "$lauf" < "$grenze" ]]; then
                rclone --config "$RCLONE_CONF" purge "$DOCS_PAPIERKORB/$lauf" \
                  && echo "    Papierkorb $lauf entfernt"
              fi
            done
      else
        echo "  ✗ Dokumente-Spiegel fehlgeschlagen"; ok=1
      fi
    else
      echo "  ⚠ Dokumente-Ordner leer oder fehlt — Spiegel übersprungen"; ok=1
    fi
  else
    echo "  ⚠ Remote swissbackup-doku fehlt — Dokumente-Spiegel übersprungen"; ok=1
  fi
else
  echo "  ⚠ rclone/Config fehlt — Offsite übersprungen"; ok=1
fi

echo "  Aktuell lokal:"
ls -lh "$BACKUP_DIR"/*.gpg 2>/dev/null | awk '{print "    " $9, "(" $5 ")"}' || true
df -h / | awk 'NR==2{print "  Disk:", $3, "von", $2, "(" $5 ")"}'

if [ "$ok" -eq 0 ]; then
  echo "── $(date '+%Y-%m-%d %H:%M:%S') Backup OK ──"
  exit 0
fi
echo "── $(date '+%Y-%m-%d %H:%M:%S') Backup MIT FEHLERN ──"
exit 1
