#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: hr-system-backup.sh [--rotate-only] [--dry-run]

Environment:
  BACKUP_DIR      Target directory (default: /var/backups/hr-system)
  RETENTION_DAYS  Delete backups older than this many days (default: 30)
  DB_HOST         PostgreSQL host (default: localhost)
  DB_PORT         PostgreSQL port (default: 5432)
  DB_USER         PostgreSQL user (default: hrapp)
  DB_NAME         PostgreSQL database (default: hrsystem)
  DOCS_DIR        Document storage directory (default: /var/data/hr-system/documents)
  INCLUDE_DOCS    Set to 1 to also create a documents tarball (default: 0)

Database authentication is intentionally not stored in this script. Use
/root/.pgpass, the postgres peer user, or PGPASSWORD in the cron environment.
EOF
}

BACKUP_DIR="${BACKUP_DIR:-/var/backups/hr-system}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-hrapp}"
DB_NAME="${DB_NAME:-hrsystem}"
DOCS_DIR="${DOCS_DIR:-/var/data/hr-system/documents}"
INCLUDE_DOCS="${INCLUDE_DOCS:-0}"

ROTATE_ONLY=0
DRY_RUN=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --rotate-only)
            ROTATE_ONLY=1
            ;;
        --dry-run)
            DRY_RUN=1
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
    shift
done

if ! [[ "$RETENTION_DAYS" =~ ^[0-9]+$ ]]; then
    echo "RETENTION_DAYS must be a non-negative integer." >&2
    exit 2
fi

mkdir -p "$BACKUP_DIR"
umask 0027

rotate_backups() {
    echo "Rotating backups older than ${RETENTION_DAYS} days in ${BACKUP_DIR}"

    if [ "$DRY_RUN" -eq 1 ]; then
        find "$BACKUP_DIR" -maxdepth 1 -type f \
            \( -name 'hrsystem-*.sql.gz' -o -name 'hrsystem-docs-*.tar.gz' \) \
            -mtime +"$RETENTION_DAYS" -print
    else
        find "$BACKUP_DIR" -maxdepth 1 -type f \
            \( -name 'hrsystem-*.sql.gz' -o -name 'hrsystem-docs-*.tar.gz' \) \
            -mtime +"$RETENTION_DAYS" -print -delete
    fi
}

if [ "$ROTATE_ONLY" -eq 1 ]; then
    rotate_backups
    exit 0
fi

timestamp="$(date +%Y%m%d-%H%M%S)"
db_backup="${BACKUP_DIR}/hrsystem-${timestamp}.sql.gz"
tmp_db="${db_backup}.tmp"
tmp_docs=""

cleanup() {
    rm -f "$tmp_db"
    if [ -n "$tmp_docs" ]; then
        rm -f "$tmp_docs"
    fi
}
trap cleanup EXIT

echo "Creating database backup ${db_backup}"
pg_dump \
    -h "$DB_HOST" \
    -p "$DB_PORT" \
    -U "$DB_USER" \
    -d "$DB_NAME" \
    --no-owner \
    --no-acl \
    | gzip -9 > "$tmp_db"

gzip -t "$tmp_db"
mv "$tmp_db" "$db_backup"
chmod 0640 "$db_backup"
echo "Database backup written: ${db_backup}"

if [ "$INCLUDE_DOCS" = "1" ]; then
    if [ ! -d "$DOCS_DIR" ]; then
        echo "Document directory not found: ${DOCS_DIR}" >&2
        exit 1
    fi

    docs_backup="${BACKUP_DIR}/hrsystem-docs-${timestamp}.tar.gz"
    tmp_docs="${docs_backup}.tmp"

    echo "Creating document backup ${docs_backup}"
    tar -czf "$tmp_docs" -C "$(dirname "$DOCS_DIR")" "$(basename "$DOCS_DIR")"
    mv "$tmp_docs" "$docs_backup"
    chmod 0640 "$docs_backup"
    echo "Document backup written: ${docs_backup}"
fi

rotate_backups
