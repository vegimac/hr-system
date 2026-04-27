# HR-System: Backup & Restore

## Backup-Speicherort
```
/var/backups/hr-system/db-YYYY-MM-DD_HH-MM.dump.gpg     ← PostgreSQL-Dump (custom format)
/var/backups/hr-system/docs-YYYY-MM-DD_HH-MM.tar.gz.gpg ← Documents-Tarball
```

Tägliches automatisches Backup um 03:00 via Cron (`crontab -l` als root).

Manueller Sofort-Lauf:
```bash
sudo /usr/local/bin/hr-system-backup.sh
```

## Passphrase

Liegt verschlüsselt in `/etc/hr-system/backup.passphrase` (nur root lesbar).
Auch im Walter's Passwort-Manager unter "HR-System Backup Passphrase".

**OHNE Passphrase ist das Backup wertlos.**

## Restore-Szenarien

### A) Datenbank wiederherstellen (komplett)

```bash
DUMP=/var/backups/hr-system/db-2026-04-26_16-41.dump.gpg

# 1. Entschlüsseln
sudo gpg --batch --passphrase-file /etc/hr-system/backup.passphrase \
    --decrypt "$DUMP" > /tmp/restore.dump

# 2. App stoppen (Verbindungen lösen)
sudo systemctl stop hr-system

# 3. DB leeren und neu erstellen
sudo -u postgres psql -c "DROP DATABASE IF EXISTS hrsystem;"
sudo -u postgres psql -c "CREATE DATABASE hrsystem OWNER hrapp;"

# 4. Restore
PGPASSWORD='HrSystemneuVonWalter2026!' pg_restore \
    -h localhost -U hrapp -d hrsystem \
    --no-owner --no-acl --verbose /tmp/restore.dump

# 5. App starten
sudo systemctl start hr-system

# 6. Aufräumen
rm /tmp/restore.dump
```

### B) Documents wiederherstellen

```bash
ARCHIVE=/var/backups/hr-system/docs-2026-04-26_16-41.tar.gz.gpg

# Bestehende Documents wegsichern (just in case)
sudo mv /var/data/hr-system/documents /var/data/hr-system/documents.old

# Entschlüsseln und entpacken
sudo gpg --batch --passphrase-file /etc/hr-system/backup.passphrase \
    --decrypt "$ARCHIVE" \
    | sudo tar -xzf - -C /var/data/hr-system

# Berechtigungen setzen
sudo chown -R www-data:www-data /var/data/hr-system/documents
```

### C) Einzelne Datei aus Backup zurückholen

```bash
ARCHIVE=/var/backups/hr-system/docs-2026-04-26_16-41.tar.gz.gpg
WANTED="documents/058/68/807369baa77d419191e688f3c64ff08a.PDF"

sudo gpg --batch --passphrase-file /etc/hr-system/backup.passphrase \
    --decrypt "$ARCHIVE" \
    | sudo tar -xzf - -C /tmp "$WANTED"

# Datei liegt nun in /tmp/$WANTED
```

## Rotation
Backups älter als 30 Tage werden automatisch gelöscht beim nächsten Backup-Lauf.

## Off-Site (TODO für Produktion)
Aktuell nur lokal auf dem Server. Bei Server-Verlust = Backups weg.
Für Produktion: täglicher Sync zu Infomaniak Swiss Backup oder S3.
