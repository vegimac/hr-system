# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Was das ist

Schweizer HR/Lohnabrechnungs-System für Schaub Restaurants GmbH (McDonald's-Franchise mit 6 Filialen). Live unter `test.hr-srgmbh.ch`. ASP.NET Core 8 + EF Core + PostgreSQL + Single-Page-HTML/JS-Frontend (kein Build-Step).

Geschäftsdomäne: Schweizer L-GAV Gastronomie, Quellensteuer (Kreisschreiben 45), Mirus-Lohnsystem-Migration, easy@work-CSV-Import. Vertragsmodelle: **UTP** (Stundenlohn), **MTP** (garantierte Stunden), **FIX** (Festpensum), **FIX-M** (Management-Festlohn).

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

DB-Backup/Restore: siehe `RESTORE.md`. Backups laufen täglich um 03:00 auf dem Server.

## Architektur-Big-Picture

### Schichten

```
wwwroot/index.html (12k Zeilen)  ←── Single-Page-App, alle Module außer Mitarbeiter-Liste
wwwroot/employees.js (4k Zeilen) ←── MA-Liste, MA-Detail, Tabs, Adressen, Bankkonten
wwwroot/import.html (2k Zeilen)  ←── CSV-Import (eigene Page, eigener Login-Flow)

           ↓ JWT Bearer Token
Controllers/*.cs (47 Stück)       ←── REST-Endpoints, gruppiert pro Domäne
   ↓
Services/*.cs                     ←── Komplexe Logik (PDF-Generierung, Tarife, Karenz)
   ↓ EF Core
Data/AppDbContext.cs              ←── Postgres-Mapping aller ~50 Entities
Models/*.cs (48 Stück)
```

### Frontend-Eigenheiten (wichtig!)

- `wwwroot/index.html` ist die Haupt-App. **Alle wichtigen Module** sind hier (Verträge, Lohn, Quellensteuer, Posteingang, Filialen, Lohnpositionen, Periode-Config, etc.). Sehr lange Datei — bei Änderungen mit `grep`/`Read` gezielt suchen, nie blind editieren.
- `wwwroot/employees.js` enthält den **Mitarbeiter-Tab** (linke Liste + rechtes Detail mit Sub-Tabs Personal / Familie / **Bank** / Quellensteuer / Stempelzeiten / Absenzen / KTG/UVG / Dokumente). Der Sub-Tab „Bank" (Walter-Vorgabe 14.05.2026) hält nur noch die Bankverbindungs-Liste — aus dem Personal-Tab ausgelagert, damit die Seite nicht so lang ist. Reihenfolge der Tabs steht zusätzlich in `_empTabsOrder` (für Pfeil-Navigation) und muss mit der Tab-Bar in `renderEmployeeDetail` synchron bleiben. Lade-Logik pro Tab in `switchEmpTab`: `bank` → `loadBankAccountsTab`. Phantom-MA (`isPayrollExcluded`) zeigen im Bank-Tab nur den „MA ohne Lohn"-Hinweis. **Postfach-Passwort-Reset:** sitzt als Button im Detail-Header (`#empHeaderActions`, neben „Bearbeiten") und ruft direkt `postfachResetPassword(empId)` — kein eigener Tab/Block mehr. `startEmpEdit()` ersetzt den Inhalt von `#empHeaderActions` durch Speichern/Abbrechen. Backend `ResetPasswordAsync` setzt nebst dem Passwort auch `FailedLoginCount=0` + `LockedUntil=null` — der Reset hebt also eine Login-Sperre gleich mit auf, ein separater Unlock-Button ist nicht nötig. Die Funktionen `loadPostfachAccountBlock` / `renderPostfachAccountBlock` / `postfachUnlock` bleiben als Code erhalten, werden aber nicht mehr aus dem UI aufgerufen.
- `wwwroot/import.html` ist eine **separate Page** für CSV-Import aus easy@work. Wird via `openImportTool()` mit Token+BranchId als URL-Parameter aufgerufen.
- **Doppelte DOM-IDs sind ein wiederkehrender Bug** — `getElementById` liefert das erste Element. Bei Element-Erstellung daher prüfen ob ID bereits existiert (z.B. ehemals `importResult` doppelt vorhanden für CSV-Import und Stempelzeiten-Import).

### Authorization

- JWT Bearer (Secret in `appsettings.json` oder `Jwt:Secret`-EnvVar). Frontend speichert Token in `localStorage.hrToken`.
- Rollen: `admin` (alle Filialen), `superuser` (HR-Verantwortliche), `user` (normaler Benutzer mit Filial-Zugang via `user_branch_access`).
- Für sensible Endpoints `[Authorize(Roles = "admin,superuser")]` verwenden — Muster ist etabliert.

## Geschäftslogik-Kernkonzepte

### Vertragsmodelle und Mindestlohn

- 4 Modelle: **UTP, MTP, FIX, FIX-M**.

#### Auszahlungs- und Saldo-Logik je Modell (Walter-Vorgabe, gilt absolut)

| Modell | Ferien | Feiertag | 13. ML |
|---|---|---|---|
| **UTP** | Saldo (CHF, „Ferien-Geld") — NICHT monatlich ausbezahlt | **Monatlich ausbezahlt** | **Monatlich ausbezahlt** |
| **MTP** | Saldo (Tage) — Auszahlung NUR nach Vorgaben (`PayrollPeriodeConfig.thirteenthMonthPayoutMonths` etc.) | **Monatlich ausbezahlt** | Saldo, Auszahlung NUR nach Vorgaben |
| **FIX** / **FIX-M** | Saldo (Tage) — KEINE Auszahlung, nur akkumulieren | Saldo (Tage) — KEINE Auszahlung, nur akkumulieren | Saldo (CHF), Auszahlung NUR nach Vorgaben. 13. ML zusätzlich oben in der Lohnpositionen-Liste anzeigen (Akkumulation transparent). |

**Sozialleistungs-Abzug:** wird ERST bei der tatsächlichen Auszahlung von Ferien oder 13. ML angewendet — NICHT beim monatlichen Akkumulieren in den Saldo. Daher beim Austritt eines UTP-MA (Ferien-Geld-Auszahlung) und beim Auszahlungsmonat eines MTP/FIX-M (13. ML) jeweils AHV/ALV/NBU/KTG/LGAV auf den Auszahlungsbetrag rechnen.

**MTP-Ferien-Auszahlung bei Bezug (Walter-Vorgabe, 09.05.2026):** Bei einem MTP-MA werden im Bezugsmonat von Ferientagen die garantierten Stunden gekürzt (Sollstunden- und Festlohn-Reduktion ist korrekt — so funktioniert das MTP-Modell). Die Ferien-Auszahlung erfolgt anteilsmässig **aus dem Pott**, der den **aktuellen Monat einschliesst**:

```
Pott CHF   = Vormonats-Ferien-Geld + Ferienentschädigung diesen Monat
Pott Tage  = Vormonats-Tage-Saldo + Ferien-Tage-Accrual diesen Monat
Tagessatz  = Pott CHF / Pott Tage
Auszahlung = Tagessatz × bezogene Tage diesen Monat
```

Beispiel: Saldo 800 + Akkumulation 200 = 1000 CHF / (8 + 2) = 10 Tage → 100 CHF/Tag, bei 6 bezogenen Tagen → 600 CHF Auszahlung. Cap = Pott CHF (kein Vorbezug). Ferien-Geld-Saldo neu = Pott − Auszahlung. Logik in `PayrollController.cs` im MTP-Block (`mtpFerienAuszahlungBetrag`).

**Nacht-Saldo (Stunden):** für ALLE Modelle inkl. UTP im Lohnzettel anzeigen.
- **Lohn-Anzeige in der Vertrags-Card** (siehe `renderVtDetail` in `index.html`):
  - **FIX/FIX-M:** Hauptfeld zeigt 100%-Monatslohn (`monthlySalaryFte`) damit man den Mindestlohn auf 100%-Basis sieht. Daneben „Lohn ({Pensum}%)" als read-only Info mit dem effektiven Wert (`monthlySalary`).
  - **MTP:** Hauptfeld zeigt Stundenlohn (`hourlyRate`). Daneben „Garantiert / Monat" als Info, berechnet als **`guaranteedHoursPerWeek × hourlyRate × 52 / 12`**. Diese Formel ist Walter-Standard für die Plausibilitätsprüfung des MTP-Mindesteinkommens.
  - **UTP:** Stundenlohn pur, keine Info-Zeile.
- DB-Spalte `employment_model_code` in `minimum_wage_rule_new` und `social_insurance_rate` nutzt **identische Codes** (`UTP/MTP/FIX/FIX-M`). Frühere `PARTTIME/FULLTIME` wurden migriert. `MapEmploymentModel`-Funktionen in `ComplianceController` und `ContractsController` mappen Legacy-Werte trotzdem für Rückwärtskompatibilität.
- **Manager-Funktionen** (REST_MANAGER, ASST_1, ASST_2, SHIFT_LEADER_1_6, SHIFT_LEADER_7_PLUS) ⇒ **immer FIX-M**, egal was im CSV-`Contract type` steht. Konstante `FIX_M_ROLES` in `import.html`.

### easy@work-Import: finale Vertrags- und MA-Klassifizierung

**Group memberships ist die Primärquelle** für Hierarchie und Vertragsmodell (95% gefüllt, vs. Funktion-Spalte teilweise inkonsistent):

| Group membership | JobGroup | Vertragsmodell | Hinweis |
|---|---|---|---|
| `Store Manager` | REST_MANAGER | FIX-M | Restaurant-Leiter |
| `Shift Manager+` | SHIFT_LEADER_7_PLUS | FIX-M | > 6 Mt. Manager-Erfahrung, höherer Mindestlohn |
| `Shift Manager-` | SHIFT_LEADER_1_6 | FIX-M | 1–6 Mt. Manager-Erfahrung, tieferer Mindestlohn |
| `Supervisor` | — | **kein Vertrag** | MA mit `IsPayrollExcluded=true` — Phantom-MA für easy@work-Zugang ohne Lohn (z.B. Nihat Erdikli, in jeder Filiale präsent). System führt ihn, aber NIE im Lohn-Tab. |
| `Employee` (oder leer) | CREW | UTP / MTP / FIX | abhängig von `Pay frequency` × `Contract type` (siehe unten) |

**Vertragstyp-Bestimmung für CREW** (Group membership = Employee):

| Pay frequency | Contract type | Modell | Stunden-Bedeutung |
|---|---|---|---|
| `month` | beliebig | FIX | „Anzahl" = Pensum (h/Woche) |
| `hour` | `MTP/TPM` | MTP | „Anzahl" = garantierte Std/Woche |
| `hour` | `Flex` oder leer | UTP | Default 17 Std/Woche |

**easy@work-Defaults bei leer:**
- Anrede leer → aus Geschlecht (`male`=Herr, `female`=Frau)
- Contract type leer → `Flex` → UTP
- Anzahl leer → `17 Stunden/Woche`
- Pay frequency leer → aus Contract type (Fix → month, sonst → hour)
- Group membership leer → `Employee` → CREW
- Qualification CCNT leer → `5 Sans qualification` → EduLevel `Ia`

**Eintrittsdatum aus easy@work-Import (Walter-Vorgabe 13.05.2026):** `Von` ist das **Eintrittsdatum ins Unternehmen** → wird direkt als `Employee.EntryDate` übernommen. Ist `Von` leer, wird fix `01.01.2024` gesetzt. KEIN Rückgriff mehr auf `Datum der Betriebszugehörigkeit` / `Eintrittsdatum` (waren zu dünn gefüllt). Für `Employment.ContractStartDate` (= echter Lohn-Beginn pro Vertrag) wird `Pay rate from` genutzt — fällt auf `Von` zurück wenn leer. Der easy@work-Import läuft über `PUT /api/employees/{id}` (EmployeesController) — dieser Pfad überschreibt `EntryDate` bei JEDEM Re-Import (kein Leer-Guard, anders als die Stammdaten-/Archiv-Importer). Telefon wird beim Import über `formatPhone()` in `import.html` auf `+41 79 333 44 55` normalisiert (akzeptiert 9-/10-/11-/12-stellig, mit/ohne `+`, `0041`, `0`-Vorwahl).

**Was easy@work NICHT zuverlässig liefert** (andere Quellen oder manuell):
- AHV-Nummer (0%) → Mirus-Lohnabrechnung XLS
- Bewilligung (13% in EXPIRATN_DT) → Mirus-Bewilligungsliste-XLSX-Importer
- Familienstand (17%) → manuell pflegen
- Education Level (0%) → via CCNT-Default Ia, sonst manuell
- Position OFS (0%) → für LSE später, manuell

### Lohnausweis (ESTV Form 11 dfe)

- Jahres-Lohnausweis pro MA + Jahr. Template in `Assets/Forms/Lohnausweis_Form11_DFE.pdf` (ESTV-Form 01.21 trilingual de/fr/it, 44 AcroForm-Felder). `LohnausweisPdfService` füllt das AcroForm via iText (analog `QstAnmeldungPdfService`).
- Backend: `LohnausweisController` (`/api/lohnausweis/{empId}/{year}/preview` + `…/pdf`). Preview aggregiert die `PayrollSnapshot`s des Jahres → Brutto, Netto, QST aus den denormalisierten Snapshot-Spalten; SV-/BVG-Abzüge aus `SlipJson` (Code-Pattern 600.11/12/13 für AHV/ALV/NBU, 600.21 für BVG, mit Label-Substring-Fallback).
- Filial-Defaults für die Boxen am `CompanyProfile`: `LohnausweisBoxFFreierTransport` (Werks-Bus, bei McD = false), `LohnausweisBoxGKantineGratis` (Crew-Meal gratis, bei Schaub = false weil 50%-Anteil), `LohnausweisPos21VerpflegungMonat` (Geldwert Verpflegung pro Monat; standardmäßig 0).
- Frontend: `js/hr-lohnausweis.js` + Page `lohnausweis` mit MA-Picker + Jahr-Input. Vorschau-Modal zeigt alle Werte editierbar (Walter kann Spesen-Pauschalen ergänzen, Bemerkungen schreiben) — danach POST mit eventuell editiertem DTO an Backend für PDF.
- AG-Unterschrift: aus `currentUser.signature_png` (analog QST-Anmeldung) — eingebettet im Bestätigungs-Block unten rechts.
- **Skalierung Verpflegungs-Geldwert:** Backend rechnet `LohnausweisPos21VerpflegungMonat × AnzahlSnapshots` (= Anzahl Lohnabrechnungen des Jahres). Für Teiljahres-MA stimmt das automatisch (es gibt nur Snapshots für die angestellten Monate).
- Phase-2-Punkte (offen): Spesen-Reglement-Hinweise (Ziffer 14), Auto-Versand an MA-Postfach am Stichtag, Bulk-Generierung für alle MA eines Jahres, Ablage als Dokument beim MA.

### Quellensteuer (QST)

- Kantonale Tarife geladen in `QuellensteuerTarifService` (Singleton, Files in `Assets/qst-tarife/`).
- ESTV-Kreisschreiben 45: Variant A = nur 1 Arbeitgeber → kein Hochrechnen. Variant B = mehrere → Hochrechnung auf B1/B2/B3.
- Mindestbetrag pro Kanton (z.B. LU 13 CHF) — `GetMindestbetrag` in `QuellensteuerTarifService`. Mindestbetrag-Check in `PayrollController` muss VOR dem `qstBetrag <= 0`-Return greifen.
- Anmeldeformulare pro Kanton: `QstAnmeldungPdfService` mit Mappern für SO/AG/ZH/BE (jeder Kanton hat andere AcroForm-Feldnamen + andere Ja/Nein-Konventionen).
- **Wohnkanton** für QST kommt aus `employee.canton_code` (Hauptadresse direkt am Employee). Zusatzadressen in `employee_address` sind NICHT QST-relevant.

### Lohnperioden

- **Akonto-Lohn-Modell (Walter-Vorgabe 15./16.05.2026)**: die Lohnperiode ist **immer der Kalendermonat** (1.–31.). `payroll_periode_config`-Periodenregel-Tabelle + UI sind weg — `PayrollController`/`PayrollPeriodeController`/`AbsencesController` nehmen fix `startDay=1`. Konkrete Perioden weiterhin in `payroll_periode`.
- Tabelle `payroll_periode_config` und FK `payroll_periode.config_id` bleiben für historische Daten erhalten, werden aber nicht mehr beschrieben/gelesen — kein UI, kein Periodenregel-Modal, kein Lohnperioden-Banner. Die Backend-Endpoints `/api/payroll-perioden/config*` sind toter Code-Pfad.
- **Lohnverwaltung-Modus persistent + Default Akonto (Walter 16.05.2026):** der Akonto/Definitiv-Schalter speichert die Wahl in `localStorage.hrLohnMode`, Default `akonto` (Akonto-Lauf ist Mitte Monat und der häufigere Lauf). `_akWfMode` wird in `akonto-workflow.js` aus localStorage gelesen und bei jedem `setLohnMode()`-Aufruf zurückgeschrieben.
- **Definitiv-Lock (Walter 16.05.2026):** Definitivlauf-Bestätigen ist gesperrt sobald der Akonto-Lauf der gleichen Periode + Filiale begonnen wurde aber noch nicht `AUSBEZAHLT` ist. Zulässige Status für Definitiv: `OFFEN` (Akonto bewusst übersprungen — Legacy/keine Akonto-Termine) und `AUSBEZAHLT`. Zwischenstati `IN_BEARBEITUNG_GF` / `BEI_HR` / `HR_FREIGEGEBEN` blockieren. Frontend: `_checkDefinitivLock()` in `akonto-workflow.js` zeigt `#lohnDefinitivLockBanner` + versteckt `#lohnTopActions`. Backend-Guard in `PayrollController.ConfirmPayroll` (vor dem Snapshot-Check) gibt 409 zurück. Wird bei Mode-Switch, Periode-Wechsel, Filial-Wechsel und Aktualisieren neu evaluiert.
- **`akonto_zahlung.status` CHECK-Constraint:** muss alle vier Werte erlauben — `('BERECHNET', 'FREIGEGEBEN_GF', 'AUSBEZAHLT', 'STORNIERT')`. Die ursprüngliche Phase-1-Migration listete nur drei und sorgte für HTTP 500 beim Freigeben. Fix in `migrations-archive/fix_akonto_zahlung_status_check.sql`, in der Phase-2-Migration ist der `DROP CONSTRAINT IF EXISTS … ADD CONSTRAINT …`-Block jetzt mit drin.
- **Akonto-6-Regel-Werk (Walter-Vorgabe 16.05.2026, Etappe 5):**
  1. Kein Akonto wenn Vertragsende ≤ Periodenende.
  2. Kein Akonto bei Krankheit / Unfall / Mutterschaft AM Stichtag (Stichtag-Overlap, kurze Absenzen davor blocken nicht).
  3. FIX: `AkontoProzentFix × Definitiv-Auszahlung`, abgerundet auf CHF 10.
  4. FIX-M: wie Regel 3.
  5. MTP: `AkontoProzentHourly × (gestempelte Stunden bis Stichtag × HourlyRate + Ferien-Pott − SV-Abzüge)`, abgerundet auf CHF 10.
  6. UTP: wie Regel 5.
  Ferien-Pott (Regel 5/6): nur Ferien-Bezüge mit `DateTo ≤ Stichtag` zählen. Pott CHF = Vormonats-Feriengeld + Akkumulation diesen Monat. Pott Tage = Vormonats-Tage-Saldo + Tage-Accrual. Tagessatz × bezogene Tage = Auszahlung, gedeckelt auf Pott CHF. Über den Stichtag hinausragende Ferien werden komplett ignoriert und im Definitivlauf nachverrechnet. Code in `Services/FerienAuszahlungService.cs`.
  - FIX/FIX-M-Berechnung in `AkontoLaufService` ist nur grobe Brutto-Schätzung (Monatslohn). Exakte Korrektur via `POST /api/akonto/workflow/sync-fix-from-slip/{id}` — Frontend ruft das beim Slip-Load auf, Backend setzt `NettoAkonto = AkontoProzentFix × auszahlungsbetrag / 100 / 10 * 10`. Sync-Endpoint ist auf FIX/FIX-M restringiert (für UTP/MTP wäre Loopback unnötig — die lokale Berechnung ist exakt).
  - Konfiguration pro Filiale: `CompanyProfile.AkontoProzentFix` (Default 80), `CompanyProfile.AkontoProzentHourly` (Default 100). UI im Filial-Einstellungen-Tab → Akonto-Block. PATCH-Endpoint `/api/companyprofiles/{id}/akonto-prozent` mit beiden optionalen Feldern.
  - Migration: `migrations-archive/add_akonto_prozent_hourly.sql`.

### Importer-Klassifizierung (Backend ↔ Frontend gespiegelt)

Backend `EmployeeImportController.ResolveClassification()` und Frontend
`resolveJobGroupCode()` / `mapContractType()` in `import.html` verwenden die
SELBE Logik — bei Anpassung BEIDE Seiten gleichzeitig pflegen, sonst läuft
Auto-Import auseinander:

1. Group memberships = Primärquelle für JobGroup + Modell
2. Funktion = Fallback nur für ASST_1, ASST_2 (kein eigener Group-Eintrag) und HOST_CT/SWING
3. **Aktiv vs. Personaldossier:** Bis-Datum `< heute` ⇒ inaktiv (Karteileiche). Stammdaten werden importiert, aber KEIN Vertrag, KEIN Snapshot, KEIN Lohn-Check. Bis leer ODER `>= heute` ⇒ aktiv (auch befristete laufende Verträge sind aktiv). Aggregiert pro Personalnummer: hat der MA mind. eine offene Zeile, ist er gesamthaft aktiv.
4. Phantom-MA-Trigger: Group memberships enthält „supervisor" → `IsPayrollExcluded=true`, kein Vertrag, kein Snapshot
5. Anrede leer → aus Geschlecht (`NormalizeSalutation` / `buildPersonPayload`)
6. CREW-Modell: Pay-frequency leer → aus Contract type (Fix → month, sonst → hour); dann month → FIX, hour+MTP → MTP, hour+Flex/leer → UTP
7. UTP-Default Wochenstunden = 17 (`ApplyWeeklyHoursDefault`)
8. **Eintrittsdatum:** `Von` → `Employee.EntryDate`, leer → `01.01.2024` (Walter-Vorgabe 13.05.2026). `Pay rate from` → `Employment.ContractStartDate` (Lohn-Beginn pro Vertragsperiode, mit Fallback `Von`). easy@work-Import (`import.html`) PUTet auf `/api/employees/{id}` → EntryDate + PhoneMobile + Adresse werden bei JEDEM Re-Import überschrieben.

**Frontend-Buckets:** Auto-Import in `import.html` teilt die Zeilen in fünf Töpfe — `auto` (Vertrag erfassen), `phantom` (Supervisor → MA ohne Lohn), `inactive` (Personaldossier ohne Vertrag), `addr` (bestehende MA → nur Stammdaten/Kontakt aktualisieren), `manual` (kein Mapping). Jeder Topf hat seinen eigenen POST-Pfad: Auto via `/api/employments`, Phantom + Inaktiv + Addr via `/api/employees` (POST oder PUT), Manual bleibt im UI für Walter. **Ein-Button-Prinzip (Walter-Vorgabe 14.05.2026):** der grüne Button „Alle erfassbaren automatisch importieren" (`runAutoImportAll()`) verarbeitet ALLE fünf Töpfe in einem Klick — inkl. `addr` (PUT `/api/employees/{id}` für jeden bestehenden MA, überschreibt Adresse/Telefon/Eintrittsdatum bei jedem Lauf). Die früheren Zusatz-Buttons `btnRunAll` (nur Adressdaten) und `btnRunAllNew` (nur neue MA) sind dauerhaft ausgeblendet, die Funktionen `runAll()` / `runAllNew()` bleiben als Code erhalten. easy@work liefert Strasse + Hausnummer kombiniert in der Spalte `Adresse` — `splitStreetHouse()` in `import.html` trennt sie (Backend-Felder `Street`/`HouseNumber`), spiegelt `SplitStreetAndHouseNumber` aus `EmployeeStammdatenImportController`.

**Onboarding-Hub:** „Neue Filiale importieren" (Systemeinstellungen) öffnet als Step 1 direkt den normalen Importer (`openImportTool()` → `import.html`) — der erledigt aktive, inaktive und Phantom-MA in einem Lauf. Die alte „Mitarbeiter-Archiv"-Karte mit `+alt`-Suffix-Logik (`EmployeeImportArchivedController`) ist nur noch via direkte URL erreichbar und für Pre-Mirus-Nummernkollisionen reserviert.

### Importer (es gibt mehrere — verwechseln tut weh)

| Importer | Zweck | Aktiviert in Frontend |
|---|---|---|
| `EmployeeImportController` (`/api/employeeimport`) | CSV-Mitarbeiter+Verträge aus easy@work, **nur aktive MA** (`Bis` offen oder zukünftig) | `import.html` (geöffnet via Sidebar „Datenimport → Mitarbeiter & Verträge") |
| `EmployeeImportArchivedController` (`/api/employees/import-archived`) | **Einmaliger** Archiv-Import von ausgetretenen MA mit `+alt`-Suffix an `employee_number` | Sidebar „Systemeinstellungen → Archiv-Import". Voll-Migration-Checkbox MUSS für Suffix-Generierung **abgewählt** sein. |
| `ImportController.ImportStempelzeiten` (`/api/import/stempelzeiten`) | Mirus-PDF (alle MA in einem PDF) — Header-Format `Name #Nummer` | Sidebar „Datenimport → Stempelzeiten" oben |
| `ImportController.ImportMonatlich` (`/api/import/stempelzeiten-monatlich`) | ZIP/PDF mit pro-MA-Layout, Header `Employee #Nummer` | Sidebar „Datenimport → Stempelzeiten" unten |
| `DvelopImportController` | d.velop-ZIP für alte Personalakten | Sidebar „Systemeinstellungen → d.velop Import" |
| `EmployeeStammdatenImportController` (`/api/imports/stammdaten`) | GastroSocial-BVG-XLSX → reichert AHV-Nr / Zivilstand / Sprache / Adresse / Konfession an. MA-Match: AHV → Name+Geb → Name allein. Namensvergleich ist **token-basiert** (`NameTokensMatch`) — fängt zusammengesetzte Nachnamen (Mädchenname+Ehename, „Trajkov Colic" vs. „Colic"), vertauschte Vor-/Nachnamen, Mittelnamen. Bei NO_MATCH/AMBIGUOUS liefert die Preview die `branchEmployees`-Liste → Frontend zeigt einen **manuellen MA-Picker** (Dropdown); Commit nimmt `manualMatches` (`"rowNum:empId,…"`) entgegen, manuelle Zuordnung gewinnt vor allen Auto-Matches. MA-Pool (Match + Picker) = Filial-MA **plus MA ganz ohne Vertrag** (Personaldossiers / Phantom-MA, keiner Filiale fest zugeordnet); inaktive MA im Picker mit `[inaktiv]` markiert | `js/stammdaten-import.js`, Page „Stammdaten-Import" |

**Filial-Mismatch-Schutz:** Bei MA-Import + Stempelzeiten-Import erkennt das System ob die CSV/PDF zur falschen Filiale gehört (Personalnr-Präfix-Match). Beim Stempelzeiten-Import: Funktion `buildStzBranchMismatchWarning(data)` in `index.html`.

### `+alt`-Suffix bei Personalnummern

- Per 1.1.2025 wurde Mirus eingeführt → neue Nummern ab 750001 (Sursee), 580001 (Oftringen), etc.
- Alte (Pre-Mirus) MA mit potentieller Nummernkollision werden über den **Archiv-Import** mit `+alt`-Suffix angelegt (`750038alt`).
- Im normalen Import werden nur AKTIVE MA angelegt (Bis-Datum offen oder ≥ heute). Inaktive werden übersprungen — Logik in `EmployeeImportController.UploadCsv`, Variable `isActiveByNumber`.

## i18n (Phase 1)

- Top-Bar-Flaggen-Toggle (`#langSwitcher` als floating fixed-position Widget) ist auf jedem Bildschirm oben rechts sichtbar. Klick auf DE/UK ruft `i18n.setLang(lang, {persist:true})` — das schreibt sofort in `app_user.preferred_language` (Endpoint `PUT /api/auth/language`) und übersetzt alle Strings ohne Reload.
- **`#langSwitcher` hostet auch den Theme-Toggle (Walter-Vorgabe 14.05.2026):** Reihenfolge `Sprache → DE/EN-Flaggen → 🌙 Theme-Toggle (#themeToggleBtn, als gefüllter Pill-Button)`. **Abmelden ist NICHT im langSwitcher** — es liegt ausschliesslich unten im Sidebar-Footer als kompaktes Icon (⏻, kein Text). `import.html` hat ebenfalls kein eigenes Abmelden mehr (der Importer läuft eingebettet; Logout via Sidebar des HR-Systems). Der Theme-Toggle ist komplett aus dem Sidebar-Footer verschwunden. `doLogout()` / `toggleTheme()` / `#themeToggleBtn` bleiben funktional unverändert — nur umplatziert.
- Statische Strings: `data-i18n="key"` Attribut, `data-i18n-title="key"` für Tooltips, `data-i18n-placeholder="key"`. Übersetzung in `wwwroot/js/i18n.js` Dictionary.
- Dynamische Strings (JS-generierte HTML wie Dashboard-Cards, Toasts): `i18n.t('key')` aufrufen. Bei Sprachwechsel müssen Module re-rendern — Pattern: `i18n.onChange(() => loadDashboard())` in `startApp()`.
- Was NICHT übersetzt wird: PDFs (Lohnzettel, Arbeitsvertrag, QST-Anmeldung, Behördenformulare), E-Mails an MA, Domänen-Codes (UTP/MTP/FIX-M, JobGroups, Lohnpositions-Codes wie 901).
- Default-Sprache: `de`. Phase-1-Scope: Top-Bar, Sidebar, Dashboard. Andere Pages werden in Folge-Phasen ergänzt — bis dahin bleiben sie DE.

## Stolperfallen / wiederkehrende Bugs

1. **Funktionen verschwinden bei Refactor.** Vor jedem Refactor SUCHEN ob es etwas Vergleichbares schon gibt — speziell:
   - `import.html` hat den vollständigen Multi-Vertrag-Erfassungs-Flow. Niemals neu bauen, nur ergänzen.
   - `EmployeeImportArchivedController` hat die `+alt`-Logik, der normale Importer nicht.
   - **Admin-Cards in Systemeinstellungen:** auch wenn ein Eintrag in einem Hub redundant erscheint, NICHT aus der Systemeinstellungen-Übersicht entfernen — Walter braucht den direkten Zugang für den laufenden Betrieb (z.B. d.velop-Import: einmalig im Onboarding-Hub UND laufend in den Systemeinstellungen). Ausnahme von Walter explizit verfügt: **Familienzulagen-Kontrolle Import** ist nur via „Neue Filiale importieren" erreichbar und gehört NICHT in die Systemeinstellungen-Übersicht.
2. **Doppelte DOM-IDs.** Bei neuen Komponenten in `index.html` immer prüfen ob die ID schon existiert. Convention: Modul-Prefix benutzen (`stz...`, `ce...`, `vt...`, `pb...`, `qst...`, `ein...`).
   - **Filial-Einstellungen inline (Walter-Vorgabe 14.05.2026):** Der „Einstellungen"-Tab im Filial-Detail (`branches-detail.js`) ist direkt editierbar — Inline-Felder (`ein…`-IDs) + ein „Speichern"-Button (`saveEinstellungen()`), der die 5 PATCH-Endpoints (`nighthours`, `auto-ferien-geld-dezember`, `karenz`, `lgav`, `thirteenth-payouts`) parallel aufruft. Die alten Popup-Modals in `branch-modals.js` (`openNightHoursModal`, `openKarenzModal`, `openLgavModal`, `openAutoFerienGeldModal`, `openThirteenthPayoutsModal` + deren `save…` + Modal-HTML in `index.html`) sind **toter Code** — bewusst erhalten, aber nicht mehr verdrahtet. Das 13.-ML-Monatsraster gibt es doppelt: `tp…` (altes Modal, tot) und `einTp…` (Inline, aktiv) — NIE mischen. **Periodenregel-Modal komplett entfernt** (Walter-Vorgabe 16.05.2026): keine Periodenregel mehr seit Akonto-Lohn-Modell, Lohnperiode = Kalendermonat. „Normale Wochenstunden / Ferien % / Feiertag %" bleiben im Tab nur Anzeige (kein PATCH-Endpoint).
3. **Frontend-Filter nach `companyProfileId`** — MA mit `companyProfileId === null` müssen gehandhabt werden (Legacy-Daten). Filter in `applyEmpFilter` (employees.js) und `loadVtList` (index.html) nutzen Fallback wenn alle Verträge unzugewiesen sind.
4. **Compliance-Check** im Edit-Modal & Vertrags-Import muss vor dem Lohn-Default-Setzen laufen (asynchron — `await runComplianceCheck(idx)` statt `triggerComplianceCheck`). Der Check füllt das Lohnfeld nur, wenn leer ODER unter Mindestlohn.
5. **Mindestlohn-Regel-Lücken in DB** — wenn `status = 'NO_RULE'` aus `/api/compliance/check-live` kommt, fehlt eine Kombi (z.B. FIX-M + SHIFT_LEADER_*) in `minimum_wage_rule_new`. Tabelle hat optional `age_max`-Spalte für altersabhängige Regeln (z.B. CREW + UTP + Ia + age_max=17 = 16.85 CHF/h für Jugendliche bis zum 18. Geburtstag). Frontend sendet `birthDate` mit, Backend wählt spezifischste Regel via `ORDER BY age_max ASC NULLS LAST`. Compliance-Check fällt für Verträge in der Vergangenheit auf `effectiveDate=heute` zurück (DB hat oft keine alten 2024/2025-Regeln).
6. **DateTime vs DateOnly** — `Employment.ContractStartDate` ist `DateTime`, `PayrollPeriode.PeriodFrom` ist `DateOnly`. Bei Vergleichen mit `DateOnly.FromDateTime(...)` umwandeln.
7. **Startup-Seeds in `Program.cs` müssen echt idempotent sein.** Guard NIE an eine einzelne fest verdrahtete Sentinel-Zeile hängen (`WHERE NOT EXISTS (… code='KTG' AND valid_from='2026-01-01')`) — wird diese Zeile im UI editiert oder gelöscht, läuft der ganze Seed bei JEDEM Start erneut und erzeugt Dubletten. Richtiges Muster: `WHERE NOT EXISTS (SELECT 1 FROM <tabelle>)` (nur in leere Tabelle seeden, wie beim `lohnposition`-Seed) und zusätzlich ein UNIQUE-Index auf den fachlichen Schlüssel. Für `social_insurance_rate` umgesetzt (Index `ux_social_insurance_rate_natural`, Bereinigung in `migrations-archive/fix_social_insurance_rate_dedup.sql`); SV-Sätze werden im Betrieb über `/api/social-insurance-rates` gepflegt, nicht über den Seed.

## Konventionen

- **SQL-Migrationen immer für TablePlus liefern.** Walter führt SQL direkt in TablePlus aus (nicht via `psql` CLI). Daher: reinen SQL-Block geben, kein `psql -d hr_system -U postgres -f ...` Wrapper, keine `\d`/`\i`-Meta-Kommandos. Die `.sql`-Datei in `migrations-archive/` darf den Wrapper-Kommentar als Doku enthalten, aber im Chat antworte ich mit dem reinen SQL zum Copy-Paste.

- **Datums-Anzeige im gesamten Programm immer `dd.mm.yyyy`** (Schweizer Standard). Niemals ISO `yyyy-MM-dd` in der UI zeigen — auch nicht in Tabellen, Tooltips, Badges, Modals, Toasts oder Filtern. Pattern: `new Date(iso).toLocaleDateString('de-CH')` oder `iso.slice(8,10) + '.' + iso.slice(5,7) + '.' + iso.slice(0,4)` für ISO-only Strings. Backend liefert weiterhin ISO — die Konvertierung passiert in der Anzeigeschicht.
- **Mitarbeiter-Listen IMMER nach Vorname sortieren** (Frontend-Anzeige). Tie-Break über Nachnamen. Anzeigeformat: `Vorname Nachname`. Bestätigt von Walter — gilt für **alle** MA-Listen UND **alle** MA-Auswahllisten/Datalists/Dropdowns ohne Ausnahme: Mitarbeiter-Tab, Lohn-Tab, Vertrags-Picker, QST-Anmeldungs-Picker, RAV-Picker, Saldi-Vortrag-Picker, d.velop-Importer-Picker, Bank-Importer-Manuell-Auswahl, Stammdaten-Importer, etc. Pattern: `list.sort((a, b) => (a.firstName||'').localeCompare(b.firstName||'') || (a.lastName||'').localeCompare(b.lastName||''))`. **Niemals** `Lastname, Firstname` als Anzeigeformat — auch nicht in Dropdowns.
- **Felder in Erfassungsmasken NIE eigenmächtig entfernen.** Auch wenn ein Feld leer wirkt oder sein Backend-Pendant fehlt: vorher mit Walter besprechen. Walter und Claude haben gemeinsam Felder definiert (z.B. Briefanrede, Heimatort) — die bleiben sichtbar bis Walter explizit entfernt sagt. Falls Backend-Mapping fehlt, lieber eine kleine Migration bauen statt das UI zu beschneiden.
- **Kein Code löschen**, ohne zu prüfen wer es nutzt (`grep` über Frontend + Backend + SQL-Files).
- **Sicherheits-Endpoints** (z.B. DELETE) haben standardmässig einen Force-Modus mit `?force=true` plus 409-Conflict bei riskanten Konditionen.
- **Unterschriften auf Formularen** (QST-Anmeldung, RAV-Zwischenverdienst, KTG-Schadenmeldung etc.): IMMER die Unterschrift des **eingeloggten Users** mit seinem **Klarnamen** direkt darunter. NIEMALS die Unterschrift einer anderen Person verwenden, auch wenn deren Name oben im Formular als HR-Verantwortliche/AG-Vertreter steht — das wäre Urkundenfälschung. Die getippten Namen oben (Sachbearbeiter, AG-Vertreter) bleiben unverändert aus den Stammdaten / CompanyProfile / QST-Konfig. Falls der eingeloggte User keine Unterschrift hinterlegt hat: Stelle bleibt leer + Hinweis im UI, dass die Unterschrift im Profil hinterlegt werden soll. Unterschrift wird in `app_user.signature_png` (BYTEA) gespeichert, geladen via `/api/users/{id}/signature`.
- **Telefonnummer auf Formularen** (Walter-Vorgabe 13.05.2026): IMMER die Filial-Telefonnummer aus `CompanyProfile.Phone`, NIEMALS die persönliche Nummer der HR-Verantwortlichen aus `AppUser.Phone`. Datenschutz + einheitliches Auftreten gegenüber Behörden/Steuerämtern. Gilt für Lohnausweis-Bestätigungsblock, Barcode `Company Phone`, QST-Anmeldung, RAV-Zwischenverdienst und alle weiteren Formulare. Wenn `CompanyProfile.Phone` leer ist, bleibt die Telefon-Zeile weg statt auf User-Nummer zurückzufallen.
- **Filial-Selektor in Formularen** (Walter-Vorgabe 13.05.2026): Filial-Auswahl-Dropdowns in Sub-Pages (Importer, HR-Formulare, etc.) folgen IMMER dem globalen Sidebar-Selektor `fixedCompanyProfileId` — kein doppeltes Wählen. Konvention:
   1. Beim Page-Init wird das lokale Dropdown auf `fixedCompanyProfileId` gesetzt
   2. Bei Filial-Wechsel via Sidebar muss die Page in `onBranchChange()` (index.html) ihre Init-Funktion erneut aufrufen oder den lokalen Selektor neu setzen
   3. KEINE Persistenz alter Werte zwischen Page-Wechseln — die Sidebar-Filiale gewinnt immer
   Implementiert für: Lohnlauf, Perioden, Posteingang, Saldo-Vortrag, QST-Anmeldung, RAV-Zwischenverdienst, Lohnausweis, LSE-Export, d.velop-Import, Stammdaten-Import, Bank-Import, Mitarbeiter, Verträge, Lohn. Beim Bau neuer Importer/Formulare diese Konvention mit übernehmen.
- **Importer schliessen nach Erfolg** (Walter-Vorgabe 13.05.2026): JEDER Importer navigiert nach erfolgreichem Commit automatisch zurück (`setTimeout(() => showPage('admin-hub'), 2000)`). Erfolgs-Meldung enthält den Hinweis „Fenster wird in 2 Sekunden geschlossen…". Bei Fehlern bleibt die Page offen. Beim „Analysieren"/Dry-Run NICHT schliessen — nur beim echten Commit. Implementiert für: Permit-Import, Familienzulagen-Kontrolle, Bank-Import, d.velop-Import, Stammdaten-Import.
- **Land-Code immer `CH`** (Walter-Vorgabe 13.05.2026): `employee.country`, `company_profile.country` und `employee_address.country` verwenden systemweit den ISO-Code `CH` — NICHT „Schweiz". Grund: PLZ-Lookup (`plzLookup` in employees.js), Bank-Validierung und MA-Edit-Default erwarten alle `CH`. Ausnahme: der **Lohnausweis-Export** (Barcode + Form 11) braucht den Swissdec-Klartext `SWITZERLAND` — dafür mappt `ToSwissdecCountry()` im `LohnausweisController` (CH/Schweiz/Suisse/Svizzera → SWITZERLAND). Neue Adress-Logik immer auf `CH` defaulten.
- **Nationalität: ISO speichern, Volltext anzeigen** (Walter-Vorgabe 14.05.2026). SPEICHERN: ISO-3166-alpha-2-Code — `Employee.NationalityId` → FK auf `Nationality` (`Code`-Spalte; Tabelle hat NUR `Id/Code/IsActive`, KEINE Namensspalte). Legacy-Freitextfeld `Employee.Nationality` nur noch für Alt-Daten. ANZEIGEN: IMMER deutscher Volltext, NIE der nackte ISO-Code. Klartext-Quelle ist die `app_text`-Tabelle (`Module=NATIONALITY`, `TextKey={CODE}.NAME`). Fallback-Kette: AppText → `CountryNamesDe.Resolve()` (statische ISO→DE-Tabelle in `Services/CountryNamesDe.cs`, deckt alle ~250 ISO-Codes) → Code als allerletzter Ausweg. Implementiert in `EmployeesController.GetById` (liefert `nationalityName`) und allen drei `NationalitiesController`-Endpoints. Frontend zeigt `nationalityName` (+ Code grau in Klammern); bei neuem Nationalitäts-UI nie nur `nationalityCode` rendern.
- **Pflicht-Postpace zu Default-Werten:** wenn ein Default eingesetzt wird (z.B. Mindestlohn als Lohn-Default), das im UI sichtbar machen — User soll nicht stillschweigend mit unerwarteten Werten konfrontiert werden.
- **Titel + Aktions-Buttons müssen beim Scrollen fix oben bleiben** (Walter-Vorgabe 15.05.2026, gilt ÜBERALL im Programm): sobald unter einem Bereich eine Liste / Ergebnis-Tabelle scrollt, dürfen Titel und Aktions-Buttons (Speichern, Analysieren, Import, Übertragen, Neu …) NICHT mitscrollen. Drei Muster:
  1. **Tab-/Detail-Ansichten** (Scroll-Container z.B. `.emp-detail-body`): Buttons direkt in die Tab-/Menü-Leiste (rechtsbündig via `margin-left:auto`), pro Tab ein-/ausblenden — Muster `filEinstellungenActions` + `switchFilialenTab` in `branches-detail.js`.
  2. **Voll-Seiten mit Liste darunter** (Import-Seiten etc., Scroll-Container `.main`): Titel + Eingaben + Buttons in `<div class="sticky-page-head">` wrappen, die Ergebnis-Liste scrollt darunter. Umgesetzt für die 5 Admin-Import-Seiten (dvelop / bank / permit / family-children / stammdaten).
  3. **Fallback** ohne Tab-Leiste: `.sticky-section-head` (`position:sticky;top:0`, in `index.html`) auf die Button-Kopfzeile.
  Niemals Aktions-Buttons nur am Ende einer langen Liste platzieren. Bei jeder neuen oder überarbeiteten Seite mit Liste prüfen und anwenden.
- **`Vorgaben/`-Ordner** enthält Domänen-Wissen (L-GAV, GastroSocial-Merkblätter, ESTV-Tarife, Mirus-Manual). Bei Geschäftslogik-Fragen DORT zuerst nachschlagen.

## Externe Endpoints, die im Frontend gerne fehlen

- `/api/swiss-locations/by-plz?plz=XXXX` — PLZ-Lookup (BFS-Liste). Liefert Gemeinde + Kanton + BFS-Nr. Funktion `plzLookup` in `employees.js` (Hauptadresse, hardcoded auf `ef-zip/ef-city/ef-canton`) und `plzLookupGeneric` (für Zusatzadressen-Modal).
- `/api/compliance/check-live` — Mindestlohn-Check. Body: `{jobGroupCode, educationLevelCode, effectiveDate, employmentModel, employmentPercentage, hourlyRate?, monthlySalary?}`. Antwort: `{status: OK|UNDERPAID|NO_RULE|NOT_CHECKED, minimumHourlyRate?, minimumMonthlySalary?, minimumMonthlySalaryFte?, difference?, warningMessage?}`.
- `/api/contracts/employment/{id}/pdf` — generiert Arbeitsvertrag-PDF via `ContractPdfService`.
