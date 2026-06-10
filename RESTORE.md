# HR-System: Backup & Restore

## Backup-Speicherort

Aktueller Produktions-Stand:

```bash
/var/backups/hr-system/hrsystem-YYYYMMDD-HHMMSS.sql.gz
```

Optional kann das Backup-Script zusätzlich die Dokumente sichern:

```bash
/var/backups/hr-system/hrsystem-docs-YYYYMMDD-HHMMSS.tar.gz
```

Die Datenbank-Backups sind normale PostgreSQL-SQL-Dumps, gzip-komprimiert.
Die DB-Authentifizierung steht absichtlich nicht im Script. Verwende
`/root/.pgpass`, den `postgres`-Peer-User oder `PGPASSWORD` in der
Cron-Umgebung.

## Script installieren

```bash
sudo install -m 0750 Scripts/hr-system-backup.sh /usr/local/bin/hr-system-backup.sh
```

Manueller Sofort-Lauf:

```bash
sudo /usr/local/bin/hr-system-backup.sh
```

Mit Dokumenten-Backup:

```bash
sudo INCLUDE_DOCS=1 /usr/local/bin/hr-system-backup.sh
```

Täglicher Cron um 03:00 als root (`sudo crontab -e`):

```cron
0 3 * * * /usr/local/bin/hr-system-backup.sh >> /var/log/hr-system-backup.log 2>&1
```

## Rotation / Sofort-Aufräumen

Das Script löscht nach einem erfolgreichen Lauf automatisch alte Dateien:

- `hrsystem-*.sql.gz`
- `hrsystem-docs-*.tar.gz`

Default: älter als 30 Tage.

Gefahrlos prüfen, was gelöscht würde:

```bash
sudo /usr/local/bin/hr-system-backup.sh --rotate-only --dry-run
```

Danach wirklich löschen:

```bash
sudo /usr/local/bin/hr-system-backup.sh --rotate-only
```

Für eine andere Aufbewahrung:

```bash
sudo RETENTION_DAYS=45 /usr/local/bin/hr-system-backup.sh --rotate-only --dry-run
sudo RETENTION_DAYS=45 /usr/local/bin/hr-system-backup.sh --rotate-only
```

## Restore-Szenarien

### A) Datenbank wiederherstellen (komplett)

```bash
DUMP=/var/backups/hr-system/hrsystem-20260610-030002.sql.gz

# 1. App stoppen (Verbindungen lösen)
sudo systemctl stop hr-system

# 2. DB leeren und neu erstellen
sudo -u postgres psql -c "DROP DATABASE IF EXISTS hrsystem;"
sudo -u postgres psql -c "CREATE DATABASE hrsystem OWNER hrapp;"

# 3. Restore
gunzip -c "$DUMP" | psql -h localhost -U hrapp -d hrsystem

# 4. App starten
sudo systemctl start hr-system
```

### B) Documents wiederherstellen

```bash
ARCHIVE=/var/backups/hr-system/hrsystem-docs-20260610-030002.tar.gz

# Bestehende Documents wegsichern (just in case)
sudo mv /var/data/hr-system/documents /var/data/hr-system/documents.old

# Entpacken
sudo tar -xzf "$ARCHIVE" -C /var/data/hr-system

# Berechtigungen setzen
sudo chown -R www-data:www-data /var/data/hr-system/documents
```

### C) Einzelne Datei aus Documents-Backup zurückholen

```bash
ARCHIVE=/var/backups/hr-system/hrsystem-docs-20260610-030002.tar.gz
WANTED="documents/058/68/807369baa77d419191e688f3c64ff08a.PDF"

sudo tar -xzf "$ARCHIVE" -C /tmp "$WANTED"

# Datei liegt nun in /tmp/$WANTED
```

## Off-Site (TODO für Produktion)

Aktuell nur lokal auf dem Server. Bei Server-Verlust = Backups weg.
Für Produktion: täglicher Sync zu Infomaniak Swiss Backup oder S3.
