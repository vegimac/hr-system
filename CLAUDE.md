# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## ⚠ ACHTUNG TIME — ZUERST LESEN (Walter, ABSOLUT, wiederkehrend)

**Bevor du irgendein C#-/DB-Feld mit Datum/Zeit anfasst oder neu anlegst:**

| | Pflicht |
|---|---|
| Schreiben | immer `DateTime.Now` — **nie** `DateTime.UtcNow` |
| PostgreSQL | immer `timestamp without time zone` — **nie** `timestamptz` / `TIMESTAMPTZ` |
| EF-Mapping | `.HasColumnType("timestamp without time zone")` auf jedem `DateTime`-Property |
| Model-Default | `= DateTime.Now` |

Sonst: HTTP 500 beim Speichern (Npgsql lehnt `Kind=Local` auf `timestamptz` ab) — oft nur leeres «Fehler:» im UI. Schon mehrfach passiert (Zusatzadresse, Absenz, Absenz-Typ-Nachrechnung). Checklist auch in `.cursor/rules/achtung-timestamp.mdc`.

**Seit 01.09.2026 prüft der Schema-Wächter das automatisch.** Beim Start vergleicht er bei JEDEM `DateTime`-Property den EF-Typ gegen `information_schema.columns` und schreibt jede Abweichung als Warnung ins Log (`SCHEMA-PRUEFUNG   Zeitzone:  tabelle.spalte ← Model.Property (Datenbank: … / EF: …)`); `/api/instance-info` liefert die Anzahl als `schemaZeitzonen`. Bewusst **nicht** blockierend — es gibt Altbestand, und ob eine Abweichung knallt, hängt davon ab, ob jemand Lokalzeit hineinschreibt. Nach einem Deploy also ins Log schauen: steht dort eine Zeitzonen-Warnung zu einer NEUEN Tabelle, fehlt `.HasColumnType(...)`. (Anlass: `mail_bounce.empfangen_am` — die Spalte existierte, nur der Typ passte nicht, also fiel es der reinen Spaltenprüfung nicht auf und knallte erst beim ersten Rückläufer.)

---

## 📚 Pflichtlektüre nach Arbeitsgebiet (ABSOLUT — verbatim ausgelagerte CLAUDE.md-Teile)

Diese Dateien SIND Teil der CLAUDE.md (am 29.08.2026 unverändert ausgelagert, um den Kontext zu verschlanken). Vor Arbeit im jeweiligen Gebiet die Datei ZUERST lesen — nichts darin ist «nur Doku», alle Vorgaben gelten unverändert:

| Arbeitsgebiet | ZUERST lesen |
|---|---|
| Lohn-Logik, Verträge, QST, Absenzen, Zulagen, Darlehen, Lohnausweis, Importer | `docs/claude/fachlogik.md` |
| Lohnlauf-Workflow, Akonto, Perioden, DTA, Edit-Sperren | `docs/claude/lohn-workflow.md` |
| Fibu-Journal, Kontoplan, Buchungen | `docs/claude/fibu-stand.md` |
| Swissdec / ELM | `docs/claude/swissdec-stand.md` |
| Testinstanz test.onecrew.ch | `docs/claude/testinstanz-stand.md` |
| Verfügbarkeit, i18n, Reports, externe Endpoints | `docs/claude/module-stand.md` |
| Architektur (Schichten, Lohn-Berechnung, Frontend-Eigenheiten) | `docs/claude/architektur.md` |
| Rollen, Sicherheit, SV-Höchstlöhne, BVG-Grenzen, Concurrency, Audit | `docs/claude/sicherheit-und-lohnregeln.md` |
| Stolperfallen / wiederkehrende Bugs (Start-Seeds, Schema-Stand, Stichtag …) | `docs/claude/stolperfallen.md` |
| Oberfläche: Liquid Glass, Masken, Selects, Dialoge, Downloads, Buttons, Datumsfelder | `docs/claude/konventionen-ui.md` |
| Code: C#-Strings, Raw-SQL, Datum/Zeit, Uploads, Virenscanner, Dokumente, Land/Nationalität | `docs/claude/konventionen-code.md` |
| Fachregeln: Ablage, Personaldaten, Ferien/Kündigung, Dienstalter, Mails, QST-Konkubinat, Unterschriften, Schulungen … | `docs/claude/konventionen-fach.md` |

## Was das ist

Schweizer HR/Lohnabrechnungs-System für Schaub Restaurants GmbH (McDonald's-Franchise mit 6 Filialen). Live unter `test.hr-srgmbh.ch`. ASP.NET Core 8 + EF Core + PostgreSQL + Single-Page-HTML/JS-Frontend (kein Build-Step).

Geschäftsdomäne: Schweizer L-GAV Gastronomie, Quellensteuer (Kreisschreiben 45), Mirus-Lohnsystem-Migration, easy@work-CSV-Import. Vertragsmodelle: **FLEX** (Stundenlohn, hiess bis 08.07.2026 «UTP» — echter Rename in Code+DB, Legacy-Alias «UTP» wird in Mappern weiter akzeptiert; ACHTUNG: absenz_typ.code='UTP' ist ein ANDERER Namensraum und blieb unangetastet), **MTP** (garantierte Stunden), **FIX** (Festpensum), **FIX-M** (Management-Festlohn).

## Bauen, deployen, lokal entwickeln

```bash
# Lokale Entwicklung
dotnet run                  # läuft gegen lokale PostgreSQL aus appsettings.json

# Build-Check
dotnet build

# Deploy auf Produktions-Server (Infomaniak VPS, Ubuntu)
./deploy.sh                 # publish + tar + scp + systemctl restart

# Frontend ist statisch — nach jedem Frontend-Edit muss deployt werden,
# damit Änderungen live sichtbar sind. KEIN Hot-Reload, KEIN Build-Step.
```

DB-Backup/Restore: siehe `RESTORE.md`. Backups laufen täglich um 03:00 auf dem Server (Prod) bzw. 03:30 (Testinstanz, eigene Passphrase `/etc/hr-system/backup-test.passphrase`). Prod wird danach per `rclone copy` nach **Infomaniak Swiss Backup** kopiert (ausser Haus, geprüft 08.10.2026; «Backup OK» im Log schliesst den Upload ein). Die Dokumente werden zusätzlich Datei für Datei verschlüsselt gespiegelt (`swissbackup-doku:`, eigenes Passwort `/etc/hr-system/backup-docs.passphrase`); Gelöschtes/Überschriebenes landet 365 Tage im Papierkorb (`RESTORE.md` G). Das Backup-Skript liegt im Repo unter `server/hr-system-backup.sh` — dort ändern, dann auf den Server installieren. **Weg zurück beim Deploy (Walter 08.10.2026):** `deploy.sh` sichert pro System nach dem Stopp die Datenbank verschlüsselt nach `…/vor-deploy/` (mit Probe-Entschlüsselung; scheitert sie, startet das alte Programm wieder und der Deploy bricht ab) und schiebt das alte Programm nach `/var/www/<app>.vorher` statt es zu löschen. Zurückrollen: `RESTORE.md` Szenario E (nur Programm) bzw. D (Programm + Datenbank). Diese Sicherung nie entfernen, um den Deploy schneller zu machen.

### Testinstanz test.onecrew.ch → PFLICHT: `docs/claude/testinstanz-stand.md` lesen

Vor JEDER Arbeit an/mit der Testinstanz die Datei lesen (verbatim ausgelagert 29.08.2026). Eiserne Kurzregeln: **NIE echte Daten in die Testinstanz** (kein pg_dump prod→test); keine geteilten Secrets/DB-User/Verzeichnisse; `./deploy.sh` ist gestaffelt (erst Test, dann Prod); Test-Login walter.schaub@gmail.com (kein «admin»).

### Swissdec ELMv6.0.1 → PFLICHT: `docs/claude/swissdec-stand.md` lesen

Vor JEDER Swissdec-/ELM-Arbeit die Datei lesen (Stand E1–E3 + Hauptsitz-Verwaltung, verbatim ausgelagert 29.08.2026). Eiserne Kurzregeln: Zertifizierungsbasis = **ELMv6.0.1** (Schema-Patch auf ELM 6.0, Mail Fachstelle ELM 30.10.2026; bis zum November-Release bleibt das XML bei `schemaVersion` 0.0, danach blockt der Distributor 6.0.0); Übungs-XML NUR aus test.onecrew.ch-Kunstdaten via Refapps-Transmitter (kein Transmitter-Zertifikat vor Zertifizierung); Ping NUR manuell; Konzept `docs/swissdec-elm6-konzept.md`.

### Deploy-Befehl IMMER mitliefern (Walter-Vorgabe 01.08.2026, ABSOLUT)

Walter deployt **nicht selbst** und muss den Befehl **nie** nachfragen müssen.
Nach **jeder** Änderung (Backend und/oder Frontend), sobald commit+push fertig sind,
steht am Ende der Antwort **immer** der komplette Copy-Paste-Block — ohne Ausnahme,
ohne «falls nötig», ohne abgekürztes `./deploy.sh`:

```bash
cd /Users/Walter/projects/hr-system && git fetch origin && git checkout <AKTUELLER-BRANCH> && git pull origin <AKTUELLER-BRANCH> && ./deploy.sh
```

Danach kurz: Hard-Reload (Cmd+Shift+R). Branch-Name = **nur** der aktive Feature-Branch unten (nie blind `main`). Gleiches gilt in PR-Beschreibungen.

### IMMER BEIDE Systeme — Server UND Datenbanken (Walter-Vorgabe 01.09.2026, ABSOLUT)

«Bitte immer alles für beide Server.» Es gibt **nie** einen Deploy, der nur eines
der beiden Systeme trifft:

- **Deploy:** immer `./deploy.sh` **ohne Argument** (= Modus `both`, gestaffelt:
  erst Test, dann Prod). `./deploy.sh test` bzw. `./deploy.sh prod` nur, wenn
  Walter im selben Satz ausdrücklich genau eines der beiden verlangt.
- **SQL-Migrationen:** jedes Statement gehört in **beide** Datenbanken —
  `hr_system_test` UND `hrsystem`. Der Befehl, den Walter bekommt, enthält
  immer beide Läufe; nie «und in der Testdatenbank dann auch noch».
- **Dateien neben dem Code** (z.B. `wwwroot/img/...`): landen über
  `dotnet publish` automatisch auf beiden — nichts von Hand auf den Server
  kopieren.
- Merksatz: Wenn in einer Antwort nur ein System vorkommt, fehlt die Hälfte.


### NUR EIN aktiver Feature-Branch (Walter-Vorgabe 01.08.2026, verschärft 02.08.2026, ABSOLUT)

Walter arbeitet **immer nur mit einem Branch** — sonst liegen Fixes verstreut und er
weiss nicht, welchen Befehl er deployen soll.

- **Aktiver Branch** (alles Neue hier rein): `main` (Stand 17.08.2026 — der frühere Sammelbranch `cursor/stundenkontrolle-monatsblatt-3bcf` ist gemergt und gelöscht; gearbeitet wird direkt auf `main`, bis Walter explizit einen neuen Feature-Branch verlangt)
- **VERBOTEN:** neuer `cursor/…-3bcf` von `main` abzweigen «für das nächste Thema», solange der aktive Branch nicht in `main` ist. Cloud-Agent-Default «feature branch from main» gilt hier **nicht**.
- Folge-Arbeit = weiter auf diesem Branch committen + pushen + denselben Deploy-Befehl.
- Alte offene PRs/Branches nur noch mergen **in diesen einen Branch**, nicht parallel weiterbauen.
- **Lehre 02.08.2026:** Absenzen-Import-Korrektur wurde fälschlich von `main` neu gebrancht → Timestamp-Fix fehlte → HTTP 500 (wirkte wie alter UTP-Bug). Fixes liegen oft schon auf dem aktiven Branch, nicht auf `main`.
- Neuer Branch erst wenn: aktiver Branch in `main` gemergt **oder** Walter explizit «neuer Branch».

## Architektur-Big-Picture

## Code-Landkarte → PFLICHT: `docs/code-landkarte.md`

Bevor du etwas Neues baust: in der Landkarte nachsehen, wo es hingehört, und mit `grep` prüfen,
ob es das schon gibt (Lehre ELM: fünf parallele Oberflächen statt einer). Neue Funktionen in die
passende bestehende Datei bzw. eine neue kleine Datei im Modul-Ordner — **keine neue Riesendatei**
(Richtwert: eine Datei ≤ ~1'500 Zeilen). Gleiche Regel in `.cursor/rules/code-landkarte.mdc`.

## Eiserne Kurzregeln (immer — Details in den Pflicht-Dateien oben)

**Datum/Zeit/Datenbank**
- `DateTime.Now`, nie `UtcNow`; Spalten `timestamp without time zone`; `.HasColumnType(...)` auf jedem `DateTime` (oben, ACHTUNG TIME).
- Kein `DateOnly.FromDateTime`/`TimeOnly.FromDateTime` in EF-SQL-Projektionen — roh laden, im Speicher umwandeln.
- easy@work-Zeiten sind UTC → immer `EawDateUtil.ParseSwissDate`; Anzeige überall `dd.MM.yyyy`.
- Start-SQL in `Program.cs`: neues SQL ⇒ `SchemaStand` +1 im selben Commit; `ALTER/CREATE` VOR `SchemaCheckService.Pruefe`; Seeds echt idempotent; nie User-editierbare Felder überschreiben.
- Nie `{` `}` in `ExecuteSqlRaw` (oder `{{ }}`); SQL für Walter als reiner Block für TablePlus, immer für **beide** Datenbanken.
- Stichtag für Lohn-/Compliance-Checks = älteste offene Lohnperiode, nie `DateTime.Now`.

**C#**
- In C#-Strings nie ein ASCII-`"` als Anführungszeichen — Guillemets `«…»`.
- Rollen mit `User.IsInRole(...)`, Audit-Actor aus dem Token, Lohnbeträge nur serverseitig (`/api/payroll/confirm`).
- Neuer Endpoint ohne Attribut = HR-only (gut); MA-Endpunkte mit `{id}` ⇒ `[MaEintrag(typeof(…))]`.
- Uploads: nur PDF/Bilder (`DokumentUploadRegel.Pruefe`), Virenscanner-Filter greift; Uploads ohne Formular ⇒ `VirenScanner.PruefeAsync`.

**Oberfläche**
- Liquid Glass für alles Neue; Eingabemasken nach ov-Standard (`class="modal"`); keine grellen Default-Farben.
- Farben nur über die Variablen `--oc-*` aus `css/farben.css` (hell + dunkel an einer Stelle), nie feste Farben; was im Dunkel-Modus hell bleiben muss ⇒ Klasse `hell-bleiben`; `css/dark.css` wird erzeugt (`Scripts/dark-css-erzeugen.py`), nicht von Hand ändern.
- Ja/Nein immer `liquidConfirm`, nie `confirm()`; Downloads nur über `saveBlobAsk`/`saveUrlAsk`, PDF/Bild zuerst `previewFileModal`.
- Natives `<select>` schreiben (liquid-select macht den Look), nie `.no-liquid` auf sichtbaren Selects; Datumsfelder einfach `type="date"`.
- Keine Aktionsbuttons oben rechts (Zone `#langSwitcher`, bis y≈60 px); Titel + Buttons bleiben beim Scrollen fix (`fixhead`); Zurück nur über `seite-zurueck`.
- Tabellenzeilen: nur das ⋮-Menü (`.dok-menu-btn`); MA-Listen nach Vorname sortiert, Anzeige «Vorname Nachname».
- DOM-IDs mit Modul-Präfix, vorher auf Doppelte prüfen; CSS-/JS-Änderung ⇒ Cache-Buster in `index.html` hochzählen.
- Filial-Auswahl folgt immer dem Sidebar-Selektor `fixedCompanyProfileId`.

**Fach**
- Zertifizierung = Endstand: nie etwas «nur für Swissdec» bauen; Lohnzeilen zählen nach den Häkchen ihrer Lohnposition.
- Vier Vertragsmodelle FLEX/MTP/FIX/FIX-M; alte Lohnpositions-Codes nie wiederverwenden; kein Ferien-Vorbezug.
- Unterschrift auf Formularen = eingeloggter User (Ausnahme QST-Anmeldung wählbar); Arbeitgeber links, MA rechts; Telefon = Filiale.
- Dienstalter über `Dienstalter.Massgebend`, Mindestlohn-Alter über `MindestlohnAlter`, Personaldaten über `PersonaldatenPruefung` — nie eigene Ableitungen.

**Arbeitsweise**
- Kein Code löschen ohne `grep` über Frontend + Backend + SQL; Felder in Masken nie eigenmächtig entfernen.
- Vor jedem Refactor suchen, ob es das schon gibt (Stolperfalle 1).
- Nach jeder Änderung: commit + push auf `main` und den kompletten Deploy-Befehl liefern (oben).

## Geschäftslogik-Kernkonzepte → PFLICHT: `docs/claude/fachlogik.md` lesen

Vor JEDER Arbeit an Lohn-Logik, Verträgen, QST, Absenzen, Zulagen, Darlehen, Lohnausweis oder Importern die Datei lesen (ABSOLUT — verbatim ausgelagert 29.08.2026; enthält u.a. ELM-Code-Standard, Auszahlungs-/Saldo-Logik je Modell, Ferien-Tagessätze, Mindestlohn-Regeln, QST-Komplex K1–K4, BVG-Zusatz, Importer-Klassifizierung). Eiserne Kurzregeln, die IMMER gelten: 4 Modelle **FLEX/MTP/FIX/FIX-M**; alte Lohnpositions-Codes NIE wiederverwenden (ELM-Raster); kein Ferien-Vorbezug (Pott-Cap, bewusste Mirus-Abweichung — nicht «fixen»); Stichtag für Lohn-Checks = älteste offene Lohnperiode, nie DateTime.Now; rückwirkende QST-Änderungen NUR über Korrektur-Posten.

## Verfügbarkeit aus easy@work → PFLICHT: `docs/claude/module-stand.md` lesen

Vor Arbeit an Verfügbarkeiten/easy@work-Sync die Datei lesen (verbatim ausgelagert 29.08.2026). Kurzregel: Pflege in easy@work, OneCrew read-only; UTC→Zürich IMMER via `EawDateUtil`.

## i18n → in `docs/claude/module-stand.md`

Kurzregeln: `data-i18n`-Attribute + `i18n.t()`; PDFs/E-Mails/Domänen-Codes NIE übersetzen; Default de. Details in der Datei.

## Lohnlauf-Workflows (Status-Map + Edit-Sperre) → PFLICHT: `docs/claude/lohn-workflow.md` lesen

Vor JEDER Arbeit am Lohnlauf/Akonto/Perioden/DTA die Datei lesen (ABSOLUT — verbatim ausgelagert 29.08.2026; Status-Map = single source of truth, GF-Sperre-Regeln, Snapshot-Frische, DTA-3-Schritt, Zahldatum-Locks). Eiserne Kurzregeln: Status-Schema ist bindend (UI-Abweichung = Bug); **kein DTA mit Beträgen ≤ 0**; kein Rollen-Bypass beim LohnEditLock; Stempelzeiten sind read-only (nur easy@work-API); Snapshots nie teilweise patchen; Wiedereröffnung nur jüngste Periode.

## Externe Endpoints / Reports / Saldo-Listen → in `docs/claude/module-stand.md`

PLZ-Lookup, Compliance-Check, Saldo-Listen (409 `PERIODE_NICHT_ABGESCHLOSSEN`-Riegel), Altersstruktur-Report — Details in der Datei.

## Fibu-Journal / Kontoplan / Abacus → PFLICHT: `docs/claude/fibu-stand.md` lesen

Vor JEDER Fibu-/Kontoplan-/Journal-Arbeit die Datei lesen (verbatim ausgelagert 29.08.2026). Eiserne Kurzregeln: Konto **1920** = Durchlaufkonto (muss 0 aufgehen); Verlinkung NUR über Codes (`categoryCode`/`code`), nie über Texte; FamZ/zulagenExtra liegen AUSSERHALB des Journal-Netto; «Codes nachtragen» ist amount-preserving, «Snapshots neu berechnen» nicht.
