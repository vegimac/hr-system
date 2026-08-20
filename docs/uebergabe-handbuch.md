# OneCrew — Übergabe- & Notfall-Handbuch

**Zweck (Walter-Vorgabe 19.08.2026):** Dieses Dokument befähigt die Nachfolger
(Sohn 1 = Übernehmer, Sohn 2 = Backup), OneCrew vollständig zu betreiben,
weiterzuentwickeln und im Notfall wiederherzustellen — auch ohne Walter.
Es wird im Repo gepflegt (versioniert) und bei jeder relevanten Änderung
nachgeführt. **Geheimnisse (Passwörter, Keys) stehen NIE hier** — hier steht
nur, WO sie liegen. Ablageort aller Secrets: Walters Passwort-Manager
(☐ Name/Zugang für die Söhne regeln, z.B. Familien-Vault oder versiegelter
Umschlag mit Master-Zugang).

> ☐ = muss Walter ergänzen/regeln · Stand: 19.08.2026

---

## 1. Was ist OneCrew (in 5 Sätzen)

Schweizer HR- und Lohnsystem der Schaub Restaurants GmbH (McDonald's-Franchise,
6 Filialen). Produktiv sind heute Stammdaten, Dokumente, Postfach/MA-App,
Dienstpläne; der **Lohn läuft im Testmodus, scharf ab 1.1.2027**. Technik:
ASP.NET Core 8 + PostgreSQL + statisches HTML/JS-Frontend (kein Build-Step).
Stempelzeiten/Verträge/Absenzen kommen per API aus easy@work. Fachliche
Referenz der Lohnrechnung: `docs/lohn-formeln.md` (+ PDF im selben Ordner).

## 2. Systemlandkarte

| Baustein | Wo | Details |
|---|---|---|
| Quellcode | GitHub `vegimac/hr-system` (privat) | Branch **main** = Wahrheit. ☐ Söhne als Collaborators/Owner eintragen |
| Arbeitskopie | Walters Mac `/Users/Walter/projects/hr-system` | inkl. `CLAUDE.md` = Projektgedächtnis für Claude |
| Server | Infomaniak VPS, Ubuntu, IP **83.228.209.119**, User `ubuntu` | App unter `/var/www/hr-system`, systemd-Service `hr-system`, nginx davor |
| Domains | **onecrew.ch** (MA/produktiv), test.hr-srgmbh.ch (Admin/Test) | ☐ Registrar + DNS-Verwaltung dokumentieren (vermutlich Infomaniak) |
| Datenbank | PostgreSQL lokal auf dem VPS, DB `hr_system` | Zugriff via TablePlus (SSH-Tunnel) |
| Dokumente/Uploads | Server-Filesystem (`Documents.StoragePath` / Mailbox-Storage) | im täglichen Backup enthalten |
| Backups | `/var/backups/hr-system/` täglich 03:00 (Cron, root) | GPG-verschlüsselt! Details + Restore: **RESTORE.md** im Repo |

## 3. Zugänge & Secrets — Inventar

Alle Werte liegen im Passwort-Manager (☐ Vault-Name: ____________).
Diese Liste ist die Checkliste, dass nichts vergessen geht:

| # | Zugang | Wofür | Ablage |
|---|---|---|---|
| 1 | GitHub-Konto `vegimac` (+ 2FA-Recovery!) | Quellcode | ☐ |
| 2 | SSH-Key für `ubuntu@83.228.209.119` | Server/Deploy | Key auf Walters Mac (`~/.ssh/`), ☐ Kopie für Söhne |
| 3 | Infomaniak-Kundenkonto | VPS, Rechnungen, DNS, Domains | ☐ |
| 4 | Server-ENV `/etc/hr-system/env` | `DB_PASSWORD`, `JWT_SECRET`, `EASYATWORK_CLIENT_ID/SECRET` | liegt auf dem Server (root) |
| 5 | Backup-Passphrase | GPG-Entschlüsselung der Backups — **ohne sie sind Backups wertlos** | `/etc/hr-system/backup.passphrase` + Passwort-Manager «HR-System Backup Passphrase» |
| 6 | PostgreSQL `postgres`-User | TablePlus/DB-Arbeiten | via ENV/Passwort-Manager |
| 7 | easy@work API (Client-ID/Secret) + Support-Kontakt | Stempelzeiten/MA-Sync | ENV + ☐ Support-Mailadresse notieren |
| 8 | SMTP-Konto (Absender OneCrew-Mails) | Lohnzettel-Versand, App-Links | Admin-UI → SMTP (DB `smtp_setting`), Passwort ☐ |
| 9 | eCall (SMS-Konto) | Kandidaten-/Willkommens-SMS | ☐ Konto + Login |
| 10 | OneCrew-Admin-Benutzer | System-Administration | ☐ je eigener Admin-Login pro Sohn (KEINE geteilten Logins) |
| 11 | Anthropic/Claude-Konto (Cowork) | Weiterentwicklung mit Claude | ☐ Konto/Abo-Inhaber, Rechnung |
| 12 | Swissdec (Beratervertrag, Kollab-Plattform) | Zertifizierungsweg | ☐ sobald Zugänge da |
| 13 | Apple-/Google-Konten? | keine App-Store-Präsenz (PWA) — n/a | — |

## 4. Betrieb — das tägliche Runbook

- **Automatisch:** 05:00 easy@work-Auto-Sync (MA/Verträge/Stempelzeiten, alle
  Filialen) · 03:00 Backup. Beides läuft ohne Zutun.
- **Monatlich (Lohn, ab 2027):** Akonto-Lauf Mitte Monat → Definitivlauf →
  DTA an Bank → Lohnbelege ins MA-Postfach. Ablauf-Doku: CLAUDE.md
  «Bearbeitungs-Status-Map»; Zahlen-Referenz: `docs/lohn-formeln.md`.
- **Bei Störung:** ① `sudo systemctl status hr-system` / `restart` ·
  ② Logs: `sudo journalctl -u hr-system -n 200` · ③ 502 nach Deploy = meist
  Startup-Crash → Log lesen (bekannte Klassiker in CLAUDE.md «Stolperfallen») ·
  ④ Notfall-Restore: **RESTORE.md** Schritt für Schritt.
- **Backup-Probe:** ☐ halbjährlich einen Restore auf Test üben (Termin!).

## 5. Deploy — wie Code live geht

Auf Walters Mac (oder jedem Mac mit Repo + SSH-Key + .NET 8 SDK):

```bash
cd /Users/Walter/projects/hr-system && git pull origin main && ./deploy.sh
```

`deploy.sh` macht: dotnet publish → tar → scp auf den VPS → Service-Restart.
Danach im Browser Hard-Reload (Cmd+Shift+R). Fürs Frontend gilt: bei jeder
JS/CSS-Änderung wird der Cache-Buster (`?v=…`) hochgezählt — macht Claude
automatisch.

## 6. Weiterentwicklung — Zusammenarbeit mit Claude

So arbeitet Walter heute, und so können es die Söhne übernehmen:

1. **Werkzeug:** Claude (Anthropic) im Cowork-Modus, verbunden mit dem
   Projektordner `/Users/Walter/projects/hr-system`. ☐ Konto/Abo klären.
2. **Das Gedächtnis ist `CLAUDE.md`** im Repo: alle Konventionen, Fach-
   Entscheide («Walter-Vorgaben»), Stolperfallen. Claude liest sie bei jedem
   Start. **Neue Grundsatz-Entscheide immer dort verankern lassen** («merke
   dir …») — dann weiss auch der nächste Chat Bescheid.
3. **Arbeitsritual:** Aufgabe in Alltagssprache stellen (Screenshot hilft),
   Claude baut und liefert am Ende einen Copy-Paste-Terminalblock
   (build/test/commit/push/deploy). Diesen ausführen, Ergebnis prüfen,
   Rückmeldung geben. Fachentscheide trifft der Mensch, Claude schlägt vor.
4. **Leitplanken:** nur EIN aktiver Branch (main) · Lohn-Formeln bleiben im
   Code (`docs/lohn-formeln.md` ist die Referenz) · SQL-Migrationen laufen
   via TablePlus · niemals «Mirus» in der Oberfläche zeigen · «Swissdec»
   werblich erst nach Zertifikat verwenden (Markenrecht, Beratervertrag!).
5. **Ohne Claude geht es auch:** Standard-.NET-Projekt — jeder C#-Entwickler
   findet sich mit CLAUDE.md + `docs/` zurecht.

## 7. Fachwissen-Landkarte (wo steht was)

| Thema | Dokument |
|---|---|
| Alle Lohn-Formeln | `docs/lohn-formeln.md` + `docs/OneCrew-Lohn-Formelwerk.pdf` |
| Konventionen, Entscheide, Stolperfallen | `CLAUDE.md` |
| Backup/Restore | `RESTORE.md` |
| Lohnschema/Vertragsmodelle-Konzept | `docs/lohnschema-vertragsmodelle.docx` |
| Absenz-Matrix-Konzept | `docs/absenz-matrix-konzept.md` |
| UI-Designsprache | `docs/liquid-glass-ui-konzept.md` |
| SQL-Historie | `migrations-archive/` |
| Tests (Rechenregeln festgenagelt) | `Tests/` — Lauf: `dotnet test Tests/hr-system.Tests.csproj` |

## 8. Externe Ansprechpartner

| Wer | Wofür | Kontakt |
|---|---|---|
| Infomaniak Support | VPS/Domains | ☐ |
| easy@work Support | API/Sync-Fragen | ☐ (bestätigte Kontakte in CLAUDE.md-Historie) |
| GastroSocial | BVG/AHV fachlich | ☐ |
| Swissdec (Coach/Experte gem. Beratervertrag) | Zertifizierung | ☐ nach Vertragsabschluss |
| Treuhand/Revision | Fibu/Abschluss | ☐ |

## 9. Notfall-Szenario «Walter fällt aus» — die Kurzanleitung

1. Ruhe bewahren: der Betrieb läuft automatisch weiter (Sync, Backups, App).
2. Passwort-Manager-Zugang holen (☐ geregelter Ort), damit Punkt 3–5 gehen.
3. Prüfen, dass Backups laufen (`ls /var/backups/hr-system/` — Datum heute?).
4. Für Änderungen/Fehler: Kapitel 5 (Deploy) + 6 (Claude) — der nächste
   Lohnlauf gelingt mit dem Runbook in Kapitel 4.
5. Rechnungen im Blick: Infomaniak (Server/Domains), Anthropic, eCall —
   ☐ Zahlungsmittel so hinterlegen, dass nichts wegen Karten-Ablauf stirbt.

---
*Pflege-Regel: Wer einen Zugang/Dienst hinzufügt oder ändert, führt dieses
Handbuch im selben Commit nach.*
