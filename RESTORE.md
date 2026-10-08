# HR-System: Backup & Restore

## Backup-Speicherort
```
/var/backups/hr-system/db-YYYY-MM-DD_HH-MM.dump.gpg     ← PostgreSQL-Dump (custom format)
/var/backups/hr-system/docs-YYYY-MM-DD_HH-MM.tar.gz.gpg ← Documents-Tarball
/var/backups/hr-system/vor-deploy/db-vor-deploy-prod-<Zeit>-<Commit>.dump.gpg ← Sicherung unmittelbar vor jedem Deploy
```

Tägliches automatisches Backup um 03:00 via Cron (`crontab -l` als root),
Skript `/usr/local/bin/hr-system-backup.sh`, Log `/var/log/hr-system-backup.log`.
Am Ende jedes Laufs werden alle `*.gpg` (inkl. `vor-deploy/`) nach
**Infomaniak Swiss Backup** kopiert (siehe «Ausser Haus» unten).
Die Zeile «Backup OK» im Log erscheint nur, wenn auch dieser Upload geklappt hat.

Testinstanz: eigenes Skript `/usr/local/bin/backup-hr-test.sh` (03:30), Ordner
`/var/backups/hr-system-test/`, eigene Passphrase `/etc/hr-system/backup-test.passphrase`,
nur lokal (Kunstdaten).

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
# Walter-Vorgabe 13.06.2026: Passwort NICHT mehr im Code. Aus /etc/hr-system/env
# laden (gleicher Speicher-Ort wie für den App-Service).
set -a; source /etc/hr-system/env; set +a
PGPASSWORD="$DB_PASSWORD" pg_restore \
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

### D) Deploy rückgängig machen — Programm UND Datenbank

Für den Fall, dass ein Deploy Daten beschädigt hat (z.B. eine falsche
Start-Migration). **Alles, was seit dem Deploy erfasst wurde, geht verloren** —
nur nach Rücksprache mit Walter.

`./deploy.sh` sichert vor jedem Update die Datenbank (nach dem Stopp, also exakt
der Stand vor den Start-Migrationen) und schiebt das bisherige Programm nach
`/var/www/hr-system.vorher`. Welche Sicherung zu welchem Deploy gehört, steht in
`/var/log/onecrew-deploys.log` (`sicherung_prod=…`).

```bash
DUMP=$(sudo ls -t /var/backups/hr-system/vor-deploy/db-vor-deploy-prod-*.dump.gpg | head -1)
echo "$DUMP"   # prüfen: richtiger Deploy?

sudo systemctl stop hr-system

# Datenbank: wie Szenario A, Schritte 1, 3, 4 mit $DUMP
sudo gpg --batch --passphrase-file /etc/hr-system/backup.passphrase \
    --decrypt "$DUMP" > /tmp/restore.dump
sudo -u postgres psql -c "DROP DATABASE IF EXISTS hrsystem;"
sudo -u postgres psql -c "CREATE DATABASE hrsystem OWNER hrapp;"
set -a; source /etc/hr-system/env; set +a
PGPASSWORD="$DB_PASSWORD" pg_restore -h localhost -U hrapp -d hrsystem \
    --no-owner --no-acl /tmp/restore.dump
rm /tmp/restore.dump

# Programm: weiter mit Szenario E ab «Programm tauschen»
```

### E) Deploy rückgängig machen — nur Programm

Der Normalfall bei einem fehlerhaften Update: die Start-Migrationen fügen nur
Spalten/Tabellen hinzu, das alte Programm läuft darauf weiter (es überspringt den
Startblock, weil die Datenbank einen höheren Schema-Stand meldet). Keine Daten gehen verloren.

```bash
sudo systemctl stop hr-system
# Programm tauschen
sudo rm -rf /var/www/hr-system.kaputt
sudo mv /var/www/hr-system /var/www/hr-system.kaputt
sudo mv /var/www/hr-system.vorher /var/www/hr-system
sudo chown -R www-data:www-data /var/www/hr-system
sudo systemctl start hr-system
```

Danach den Fehler im Code beheben und normal mit `./deploy.sh` ausrollen.
Achtung: Ein weiterer Deploy überschreibt `.vorher` — es gibt nur eine Generation.
Testinstanz analog: `hr-system-test`, Datenbank `hr_system_test` (Besitzer `hr_test`),
Sicherungen in `/var/backups/hr-system-test/vor-deploy/`, Passphrase `backup-test.passphrase`.

### F) Backup aus Swiss Backup zurückholen (Server verloren)

```bash
# auf dem neuen Server, rclone mit dem Swiss-Backup-Zugang eingerichtet
rclone lsl swissbackup:default/onecrew-nachtbackup | sort -k2,3 | tail
rclone copy swissbackup:default/onecrew-nachtbackup/db-YYYY-MM-DD_03-00.dump.gpg /var/backups/hr-system/
# danach Szenario A bzw. B — Passphrase aus dem Passwort-Manager
```

### G) Gelöschtes oder verändertes Dokument zurückholen (bis 1 Jahr)

Der Dokumenten-Spiegel (siehe unten) legt jede Nacht alles, was seit dem Vortag
gelöscht, gedreht, in PDF umgewandelt oder sonst überschrieben wurde, nach
`papierkorb/<Lauf>` — mit dem gleichen Pfad wie im Dokumentenordner.
Den Speicherpfad liefert die Datenbank (`employee_dokument.filename_storage`).

```bash
R="sudo rclone --config /root/.config/rclone/rclone.conf"
$R lsf swissbackup-doku:papierkorb                         # Läufe (Datum)
$R lsf -R swissbackup-doku:papierkorb | grep 807369baa77d  # Datei suchen
$R copy swissbackup-doku:papierkorb/2026-10-09_03-00/058/68/807369baa77d419191e688f3c64ff08a.PDF /tmp/zurueck/
# Ganzer Ordner wie heute: swissbackup-doku:aktuell (z.B. nach Server-Verlust)
$R copy swissbackup-doku:aktuell /var/data/hr-system/documents --transfers 8
sudo chown -R www-data:www-data /var/data/hr-system/documents
```

## Dokumenten-Spiegel (Walter 08.10.2026)
- Ziel `swissbackup-doku:` = rclone-**crypt** über `swissbackup:default/onecrew-dokumente`
  (Inhalt UND Dateinamen verschlüsselt; Infomaniak sieht nur Zeichensalat).
- Passwort: `/etc/hr-system/backup-docs.passphrase` (nur root) + Passwort-Manager
  «HR-System Dokumenten-Spiegel». **Ohne dieses Passwort ist der Spiegel wertlos** —
  es ist ein ANDERES als die Backup-Passphrase.
- Nächtlich im Backup-Skript: `rclone sync … aktuell --backup-dir papierkorb/<Lauf>`,
  `--max-delete 1000` (mehr Löschungen ⇒ Abbruch + «MIT FEHLERN» im Log, z.B. bei
  einem versehentlich geleerten Ordner). Papierkorb-Läufe älter als 365 Tage werden
  nach Ordnerdatum entfernt.
- Skript-Quelle: `server/hr-system-backup.sh` im Repo. Installieren:
  `scp server/hr-system-backup.sh ubuntu@83.228.209.119:/tmp/ && ssh ubuntu@83.228.209.119 'sudo install -m 700 -o root -g root /tmp/hr-system-backup.sh /usr/local/bin/'`

Neuer Server — Zugang wieder einrichten (nach `swissbackup` selbst):
```bash
sudo rclone --config /root/.config/rclone/rclone.conf config create swissbackup-doku crypt \
    remote=swissbackup:default/onecrew-dokumente filename_encryption=standard \
    directory_name_encryption=true password='<Passwort aus dem Passwort-Manager>'
```

## Rotation
- Datenbank-Sicherungen (auch `vor-deploy/`): 14 Tage, lokal und in Swiss Backup.
- Dokument-Tarballs: 3 Tage, lokal und in Swiss Backup (je ~6.5 GB) — laufen parallel
  zum Spiegel weiter, bis eine Wiederherstellung aus dem Spiegel geprüft ist.
- Dokumenten-Spiegel: aktueller Stand immer; Papierkorb 365 Tage.
- `vor-deploy/`: zusätzlich nur die letzten 10 pro System.

## Ausser Haus (Off-Site) — eingerichtet
Das nächtliche Skript kopiert mit `rclone copy` (nie `sync`) nach
`swissbackup:default/onecrew-nachtbackup` (Infomaniak Swiss Backup, Schweiz).
Zugang: `/root/.config/rclone/rclone.conf`. Verschlüsselt mit derselben Passphrase
wie lokal — Infomaniak sieht nur `.gpg`-Dateien.
Geprüft 08.10.2026: letzte 10 Läufe «Swiss Backup OK», Dateien vom Tag liegen dort.

Kontrolle:
```bash
sudo grep -E "Backup (OK|MIT FEHLERN)|✗" /var/log/hr-system-backup.log | tail
sudo rclone --config /root/.config/rclone/rclone.conf lsl swissbackup:default/onecrew-nachtbackup | sort -k2,3 | tail -4
```
