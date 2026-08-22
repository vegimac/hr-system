# Testinstanz-Runbook — test.onecrew.ch (Bauplan v1.2 FINAL)

Stand: 22.08.2026 · Go von Walter + ChatGPT + Cursor · Ausführung: Walter per SSH, in einer ruhigen Stunde, niemand im System, Prod-Backup unmittelbar davor.

**Grundregeln (verbindlich):**
- Claude greift NICHT auf den Server zu — Walter tippt jedes Kommando selbst.
- Nie echte Daten in die Testinstanz. Nie Secrets teilen. Nie ein DB-User für zwei DBs. Nie ein gemeinsames Dokumentenverzeichnis.
- Jeder Block endet mit einem ✅-Prüfpunkt. Stimmt der Prüfpunkt nicht → STOPP, nichts weiter tippen, Claude fragen.
- Produktiv (`hr-system`, DB `hrsystem`, `/var/www/hr-system`) wird in diesem Runbook NIE verändert — nur gelesen. Einzige Ausnahme: die zwei GRANT/REVOKE-Zeilen in B2 (ändern nichts am laufenden Betrieb, nur explizite Rechte).

---

## Vorbereitung: 4 neue Geheimnisse würfeln

Auf dem Mac (Terminal) vier Werte erzeugen und in einen Passwortmanager/Zettel notieren — sie werden unten eingesetzt. NIE ins Repo, NIE in den Chat.

```bash
echo "NEU-1 (hr_test DB-Passwort):  $(openssl rand -base64 24 | tr -d '/+=')"
echo "NEU-2 (JWT_SECRET Test):      $(openssl rand -base64 48 | tr -d '/+=' | cut -c1-48)"
echo "NEU-3 (Admin-Initpasswort):   $(openssl rand -base64 18 | tr -d '/+=')"
echo "NEU-4 (Basic-Auth Türsteher): $(openssl rand -base64 15 | tr -d '/+=')"
```

| Platzhalter | Zweck |
|---|---|
| `<NEU-1>` | Passwort des neuen DB-Users `hr_test` |
| `<NEU-2>` | JWT-Secret der Testinstanz (eigener Schlüsselbund!) |
| `<NEU-3>` | Initial-Passwort des Test-Admins (ADMIN_INIT_PASSWORD) |
| `<NEU-4>` | Basic-Auth-Passwort für den nginx-Türsteher (Ergänzung E2) |

**Verifiziert am 22.08.2026 (Trockenübung):** Die Produktiv-DB heisst **`hrsystem`** (NICHT hr_system), ihr Besitzer/App-User ist **`hrapp`** (Nicht-Superuser — sehr gut, echter eigener Schlüssel). Die Restore-Probe des Prod-Backups war erfolgreich (491 employees, Probe-DB danach gelöscht).

---

## B0 · Nur lesen + Rückfallnetz (STOPP-Punkte!)

```bash
ssh ubuntu@83.228.209.119
```

**B0.1 — Manuelles Prod-Backup:** einfach das bestehende Backup-Skript von Hand laufen lassen (macht DB + Dokumente + Swiss Backup):

```bash
sudo /usr/local/bin/hr-system-backup.sh
ls -lh /var/backups/hr-system/ | tail -4
```

✅ Zwei frische Dateien mit heutigem Datum/Uhrzeit. (Erledigt 22.08.2026, inkl. Restore-Probe in Wegwerf-DB.)

**B0.2 — Prod-Unit lesen (nichts ändern):**

```bash
sudo systemctl cat hr-system
```

Notieren: `WorkingDirectory`, `ExecStart`, `EnvironmentFile` (erwartet `/etc/hr-system/env`), `User`.

**B0.3 — Prod-DB-Verbindung verifizieren (Name + User):**

```bash
sudo grep -iE "connection|database|username" /etc/hr-system/env
```

✅ Erwartet: Datenbank **`hrsystem`**, User **`hrapp`** (verifiziert 22.08.2026 via `psql \l`). Das env-File übersteuert die appsettings.json — massgeblich ist, was HIER steht. Passwörter nicht in den Chat kopieren.

**B0.4 — Prod-StoragePath verifizieren (WICHTIGSTER STOPP):**

```bash
sudo grep -RinE "StoragePath" /etc/hr-system/env /var/www/hr-system/appsettings.json 2>/dev/null
```

✅ Der Pfad muss AUSSERHALB von `/var/www/` liegen (erwartet `/var/data/hr-system/documents`).
🛑 **Liegt er unter `/var/www/` → SOFORT STOPP.** Dann würde jeder Deploy die echten Dokumente löschen — zuerst mit Claude den Prod-Storage umziehen, erst danach weiter.

**B0.5 — Prod-Port notieren:**

```bash
sudo grep -E "ASPNETCORE_URLS|URLS" /etc/hr-system/env
```

Nur notieren (deploy.sh liest ihn selbst zur Laufzeit).

---

## B1 · DNS

Im Infomaniak-Manager (Domain onecrew.ch): neuen **A-Record** anlegen:

- Name: `test` · Typ: `A` · Ziel: `83.228.209.119` · TTL: 300

Prüfen (kann ein paar Minuten dauern):

```bash
dig +short test.onecrew.ch
```

✅ Antwort ist `83.228.209.119`.

---

## B2 · Datenbank — exakte Reihenfolge (TablePlus oder psql, als postgres)

Reiner SQL-Block, Zeile für Zeile (TablePlus: als Superuser verbunden; `<NEU-1>` einsetzen):

Die Produktiv-DB heisst **`hrsystem`** mit App-User **`hrapp`** (verifiziert 22.08.2026 — NICHT hr_system/postgres!). `hrapp` ist KEIN Superuser, darum ist das GRANT vor dem REVOKE hier zwingend — ohne würde die REVOKE-Zeile Produktiv aussperren:

```sql
CREATE ROLE hr_test LOGIN PASSWORD '<NEU-1>';
CREATE DATABASE hr_system_test OWNER hr_test;

-- GRANT VOR REVOKE — ZWINGEND (hrapp ist Nicht-Superuser; ohne dieses
-- GRANT würde das REVOKE unten die laufende Produktiv-App aussperren):
GRANT CONNECT ON DATABASE hrsystem TO hrapp;

REVOKE CONNECT ON DATABASE hrsystem       FROM PUBLIC;
REVOKE CONNECT ON DATABASE hr_system_test FROM PUBLIC;
GRANT  CONNECT ON DATABASE hr_system_test TO hr_test;
```

Direkt danach prüfen, dass Produktiv noch lebt: Browser onecrew.ch neu laden, Login geht. 🛑 Falls nicht: sofort `GRANT CONNECT ON DATABASE hrsystem TO hrapp;` ausführen.

**Sofortiger Negativ-Beweis** (per SSH — DAS ist der Kern der ganzen Isolation):

```bash
psql "postgresql://hr_test:<NEU-1>@127.0.0.1/hrsystem" -c "SELECT 1"
```

✅ MUSS SCHEITERN mit «permission denied for database hrsystem». Klappt der Zugriff → 🛑 STOPP, Claude fragen.

```bash
psql "postgresql://hr_test:<NEU-1>@127.0.0.1/hr_system_test" -c "SELECT 1"
```

✅ Muss klappen (Antwort `1`).

**Bonus-Beweis in Gegenrichtung** (ohne Passwort, rein lesend — weil hrapp ein echter Nicht-Superuser ist, können wir das schon HEUTE prüfen, nicht erst vor Instanz 3):

```bash
sudo -u postgres psql -c "SELECT has_database_privilege('hrapp','hr_system_test','CONNECT');"
```

✅ Antwort `f` (false) — Produktiv-User kommt nicht in die Test-DB.

> **Ergänzung E1 (nur Doku, nichts tun):** Der Superuser `postgres` erreicht `hr_system_test` trotz REVOKE weiterhin — Superuser lassen sich nicht per REVOKE aussperren. Die PASS-Kriterien sind «hr_test ↛ hrsystem» und (Bonus, dank Nicht-Superuser hrapp schon heute prüfbar) «hrapp ↛ hr_system_test». Die ursprüngliche E1-Auflage «vor Instanz 3 eigenen Nicht-Superuser für Produktiv anlegen» ist damit faktisch bereits erfüllt — `hrapp` IST dieser User. Offen bleibt nur der Hausmeister-Hinweis: `postgres` selbst kann jede Tür öffnen (unvermeidbar, nur Walter nutzt ihn).

---

## B3 · Verzeichnisse

```bash
sudo mkdir -p /var/www/hr-system-test
sudo mkdir -p /var/data/hr-system-test/documents
sudo chown -R www-data:www-data /var/data/hr-system-test
sudo chmod -R 750 /var/data/hr-system-test
sudo mkdir -p /var/backups/hr-system-test
```

✅ `ls -ld /var/data/hr-system-test/documents` zeigt `www-data www-data`. Der Storage liegt fest AUSSERHALB des Wipe-Pfads `/var/www/hr-system-test`.

---

## B4 · Env-Datei /etc/hr-system/test.env

```bash
sudo nano /etc/hr-system/test.env
```

Inhalt (Platzhalter ersetzen; alle Schlüssel gegen den Code verifiziert am 22.08.2026):

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5100

# Voller Connection-String — OHNE ${DB_PASSWORD}-Platzhalter, darum braucht
# die Testinstanz bewusst KEIN DB_PASSWORD (das ist das Prod-Secret):
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=hr_system_test;Username=hr_test;Password=<NEU-1>

JWT_SECRET=<NEU-2>
ADMIN_INIT_PASSWORD=<NEU-3>
Documents__StoragePath=/var/data/hr-system-test/documents

# Passkeys: nur vorbereitet, auf Test NICHT einrichten
WEBAUTHN_RPID=test.onecrew.ch
WEBAUTHN_ORIGINS=https://test.onecrew.ch

INSTANCE_LABEL=TESTUMGEBUNG — KUNSTDATEN
Smtp__SiteUrl=https://test.onecrew.ch/
```

**Bewusst NICHT gesetzt:** `DB_PASSWORD`, `EASYATWORK_BASE_URL/CLIENT_ID/CLIENT_SECRET`, `Smtp__Host` (Mail bleibt tot — SMTP-Konfig liegt in der DB-Tabelle `smtp_setting`, die auf Test leer ist und leer BLEIBT: im Test-Admin-UI nie SMTP konfigurieren).

```bash
sudo chmod 600 /etc/hr-system/test.env
sudo chown root:root /etc/hr-system/test.env
```

✅ `sudo ls -l /etc/hr-system/test.env` zeigt `-rw------- root root`.

---

## B5 · systemd-Unit (nur enable, NICHT starten!)

```bash
sudo nano /etc/systemd/system/hr-system-test.service
```

Inhalt (mit der Prod-Unit aus B0.2 abgleichen — gleiche Struktur, nur Pfade/Env anders):

```ini
[Unit]
Description=OneCrew HR-System — TESTINSTANZ (Kunstdaten)
After=network.target postgresql.service

[Service]
WorkingDirectory=/var/www/hr-system-test
ExecStart=/usr/bin/dotnet /var/www/hr-system-test/hr-system.dll
EnvironmentFile=/etc/hr-system/test.env
User=www-data
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable hr-system-test
```

🛑 **NICHT `systemctl start`!** Das Verzeichnis ist noch leer — der erste `./deploy.sh test` (unten) füllt es und startet. Der Erststart seedet die DB und dauert Minuten; der 300-Sekunden-Check im Deploy wartet genau darauf.

✅ `systemctl is-enabled hr-system-test` → `enabled`.

---

## B6 · nginx + Zertifikat + Türsteher (Ergänzung E2)

**B6.1 — Neuer Serverblock (erst OHNE Türsteher, damit certbot sauber läuft):**

```bash
sudo nano /etc/nginx/sites-available/test.onecrew.ch
```

```nginx
server {
    listen 80;
    server_name test.onecrew.ch;

    client_max_body_size 50m;

    location /.well-known/acme-challenge/ {
        auth_basic off;
        root /var/www/html;
    }

    location / {
        proxy_pass http://127.0.0.1:5100;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/test.onecrew.ch /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

✅ `nginx -t` meldet `syntax is ok` + `test is successful`.

**B6.2 — Zertifikat:**

```bash
sudo certbot --nginx -d test.onecrew.ch
```

✅ certbot meldet Erfolg. Danach Kontrollblick, dass der PROD-Block unangetastet ist:

```bash
grep -A2 "server_name" /etc/nginx/sites-enabled/* | grep -B1 "onecrew.ch"
```

✅ `onecrew.ch` (Prod) und `test.onecrew.ch` sind getrennte Blöcke.

**B6.3 — Türsteher (Basic Auth) nachrüsten:**

```bash
sudo apt install -y apache2-utils
sudo htpasswd -c -B /etc/nginx/.htpasswd-test walter
# → Passwort <NEU-4> eingeben
sudo chmod 640 /etc/nginx/.htpasswd-test
sudo chown root:www-data /etc/nginx/.htpasswd-test
```

Dann im Block `location / { … }` von `test.onecrew.ch` (certbot hat die Datei erweitert) ZWEI Zeilen ergänzen:

```nginx
    location / {
        auth_basic "OneCrew Testumgebung";
        auth_basic_user_file /etc/nginx/.htpasswd-test;
        proxy_pass http://127.0.0.1:5100;
        ...
    }
```

```bash
sudo nginx -t && sudo systemctl reload nginx
```

✅ Browser: `https://test.onecrew.ch` fragt zuerst nach walter/`<NEU-4>` (danach kommt vorerst 502 — die App läuft ja noch nicht, das ist richtig so).
Hinweis: die Deploy-Checks laufen auf `127.0.0.1:5100` an nginx vorbei — der Türsteher stört den Kanarienvogel nicht. Für Swissdec später gezielt öffnen.

---

## Erst-Deploy + Ersteinrichtung

Auf dem **Mac**:

```bash
cd /Users/Walter/projects/hr-system && ./deploy.sh test
```

Der Erststart legt alle Tabellen an und seedet — der Check wartet bis 300 s. ✅ Meldung «✓ Testinstanz gesund (HTTP 200 + Label)».

Browser: `https://test.onecrew.ch` → Türsteher (walter/`<NEU-4>`) → gelber Banner **«⚠ TESTUMGEBUNG — KUNSTDATEN»** → Login `admin` / `<NEU-3>`.

Danach der volle Staffel-Lauf einmal komplett:

```bash
cd /Users/Walter/projects/hr-system && ./deploy.sh
```

✅ Erst Test (Check mit Label), dann Prod (Check nur HTTP 200), am Ende Logzeile.

---

## B7 · Backup Testinstanz (eigener Tresor, eigene Passphrase)

```bash
openssl rand -base64 32 | sudo tee /etc/hr-system/backup-test.passphrase > /dev/null
sudo chmod 600 /etc/hr-system/backup-test.passphrase

sudo nano /usr/local/bin/backup-hr-test.sh
```

```bash
#!/bin/bash
set -e
STAMP=$(date +%Y%m%d-%H%M)
DIR=/var/backups/hr-system-test
sudo -u postgres pg_dump -Fc hr_system_test -f "$DIR/db-$STAMP.dump"
tar -czf "$DIR/storage-$STAMP.tar.gz" -C /var/data/hr-system-test .
gpg --batch --yes --passphrase-file /etc/hr-system/backup-test.passphrase \
    -c "$DIR/db-$STAMP.dump"
gpg --batch --yes --passphrase-file /etc/hr-system/backup-test.passphrase \
    -c "$DIR/storage-$STAMP.tar.gz"
rm "$DIR/db-$STAMP.dump" "$DIR/storage-$STAMP.tar.gz"
find "$DIR" -name "*.gpg" -mtime +14 -delete
```

```bash
sudo chmod 700 /usr/local/bin/backup-hr-test.sh
sudo /usr/local/bin/backup-hr-test.sh          # Probelauf
ls -lh /var/backups/hr-system-test/
sudo crontab -e
# neue Zeile:  30 3 * * * /usr/local/bin/backup-hr-test.sh
```

✅ Zwei `.gpg`-Dateien liegen im Backup-Ordner. (Prod-Backup 03:00 bleibt unberührt; Test läuft 03:30.)

---

## B8 · Restore-Test (Feuerwehrübung — nur die TEST-DB!)

Vorher die Prod-Referenz notieren:

```bash
sudo -u postgres psql -d hrsystem -c "SELECT count(*) FROM employee"
```

Dann (JEDEN DB-Namen laut lesen, bevor Enter gedrückt wird!):

```bash
sudo systemctl stop hr-system-test
LATEST=$(ls -t /var/backups/hr-system-test/db-*.dump.gpg | head -1)
gpg --batch --passphrase-file /etc/hr-system/backup-test.passphrase \
    -o /tmp/restore-test.dump -d "$LATEST"

sudo -u postgres dropdb hr_system_test        # ← "hr_system_test" — LAUT LESEN
sudo -u postgres createdb -O hr_test hr_system_test
```

**Connect-Rechte NEU setzen — eine frische DB holt sich das PUBLIC-Connect zurück (Cursor-Auflage v1.2):**

```sql
REVOKE CONNECT ON DATABASE hr_system_test FROM PUBLIC;
GRANT  CONNECT ON DATABASE hr_system_test TO hr_test;
```

```bash
sudo -u postgres pg_restore -d hr_system_test /tmp/restore-test.dump
rm /tmp/restore-test.dump
sudo systemctl start hr-system-test
```

✅ Test läuft wieder (Banner + Login) UND die Prod-Zeilenzahl von oben ist unverändert:

```bash
sudo -u postgres psql -d hrsystem -c "SELECT count(*) FROM employee"
```

---

## C · Abnahme — alle 12 Punkte müssen PASS sein

| # | Prüfung | Wie | PASS |
|---|---|---|---|
| 1 | hr_test kommt nicht in hrsystem | `psql "postgresql://hr_test:<NEU-1>@127.0.0.1/hrsystem" -c "SELECT 1"` scheitert | ☐ |
| 2 | Prod-Login/MA-Liste unverändert | Browser onecrew.ch: Login + Mitarbeiterliste normal | ☐ |
| 3 | Prod-Token auf Test ungültig | Auf onecrew.ch in der Browser-Konsole `localStorage.hrToken` kopieren; `curl -H "Authorization: Bearer <TOKEN>" https://test.onecrew.ch/api/auth/me -u walter:<NEU-4>` → 401 | ☐ |
| 4 | Test-MA erscheint nicht in Prod | Auf Test einen Kunst-MA anlegen («Testina Muster»), auf Prod suchen → nicht vorhanden | ☐ |
| 5 | Test-Upload landet im Test-Storage | Auf Test ein PDF hochladen; `sudo ls -R /var/data/hr-system-test/documents` zeigt es; Prod-Storage unverändert | ☐ |
| 6 | Mail auf Test tot | `psql "postgresql://hr_test:<NEU-1>@127.0.0.1/hr_system_test" -c "SELECT count(*) FROM smtp_setting"` → 0 | ☐ |
| 7 | easy@work «nicht konfiguriert» | Test-UI → easy@work-Modul: Meldung statt Fehler-Schleife; `sudo journalctl -u hr-system-test -n 50` ohne Crash-Loop | ☐ |
| 8 | Banner nur auf Test | test.onecrew.ch: gelber Banner · onecrew.ch: keiner | ☐ |
| 9 | ./deploy.sh aktualisiert beide + Logzeile | `sudo tail -3 /var/log/onecrew-deploys.log` | ☐ |
| 10 | Gebrochenes Test-Env bricht VOR Prod ab | `sudo nano /etc/hr-system/test.env` → JWT_SECRET-Zeile auskommentieren → `./deploy.sh` bricht mit Kanarienvogel-Meldung ab, onecrew.ch läuft weiter → Zeile wiederherstellen, `./deploy.sh test` | ☐ |
| 11 | Restore-Test lässt Prod unberührt | B8 durchgeführt, Prod-Count identisch | ☐ |
| 12 | Beide StoragePaths explizit, unterschiedlich, ausserhalb /var/www/ | `sudo grep StoragePath /etc/hr-system/env /etc/hr-system/test.env` | ☐ |

Erst wenn alle 12 auf PASS stehen, ist das Muster «eine Instanz pro Kunde» bewiesen.

---

## E · Die drei dokumentierten Ergänzungen (Schlusskontrolle 22.08.2026)

- **E1 postgres-Superuser-Ausnahme:** siehe Kasten in B2. Produktiv nutzt bereits den Nicht-Superuser `hrapp` (verifiziert 22.08.2026) — die E1-Pflicht ist damit faktisch erfüllt; der Bonus-Beweis «hrapp ↛ hr_system_test» läuft in B2 gleich mit. Nur der Superuser `postgres` bleibt als unvermeidbarer Hausmeister-Schlüssel dokumentiert.
- **E2 Türsteher:** umgesetzt in B6.3 (Basic Auth, ACME-Pfad frei). Für Swissdec-Prüfungen bei Bedarf gezielt öffnen (Zeilen auskommentieren + reload), danach wieder schliessen.
- **E3 Atomic Deploy (nur notiert, nicht bauen):** spätere Verbesserung — `releases/`-Verzeichnisse + `current`-Symlink statt stop→wipe→unpack. Der Storage liegt ohnehin ausserhalb `/var/www` und ist vom Wipe nie betroffen.

---

## Notfall / Rückbau

Die Testinstanz lässt sich jederzeit spurlos abschalten, ohne dass Produktiv etwas merkt:

```bash
sudo systemctl stop hr-system-test && sudo systemctl disable hr-system-test
sudo rm /etc/nginx/sites-enabled/test.onecrew.ch && sudo nginx -t && sudo systemctl reload nginx
```

(DB `hr_system_test`, Storage und Backups können liegen bleiben oder später gelöscht werden — sie berühren Produktiv nicht.) `./deploy.sh` erkennt die fehlende/deaktivierte Unit NICHT automatisch — bei dauerhaftem Rückbau auch die Unit-Datei löschen (`sudo rm /etc/systemd/system/hr-system-test.service && sudo systemctl daemon-reload`), dann überspringt der Deploy den Test-Teil wieder.

---

## Versionslog

- **v1.2 FINAL (22.08.2026):** zwei getrennte Gesundheits-Checks (Test: 200+Label · Prod: nur 200, Port aus Env, Fallback is-active), 300-s-Timeout, GRANT vor REVOKE immer, B8-Rechte-Neusetzung nach createdb, Ergänzungen E1–E3. Go: Walter + ChatGPT + Cursor.
- v1.1: getrennte Verzeichnisse statt Ein-Verzeichnis, gestaffelter Deploy, Storage-Regel /var/data.
- v1.0: Grundkonzept Instanz-pro-Kunde, Etappen 1–3.
