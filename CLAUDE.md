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

- **Lohnperiode = IMMER Kalendermonat (Walter-Vorgabe 20.05.2026, final):** Die Periode ist ausnahmslos der Kalendermonat (1.–letzter Tag). Die frühere Periodenflexibilität (Starttag 21/1, Periodenregel-Konfiguration, Übergangs-Lohnläufe) ist **komplett entfernt** — Code, Schema und UI. Grund: gesetzliche Berechnungen (QST, ALV, AHV) laufen ohnehin kalendermonatlich; der Akonto-Lauf deckt die Zahlung vor Monatsende ab.
  - **Entfernt:** Model `PayrollPeriodeConfig` + Tabelle `payroll_periode_config`, FK `payroll_periode.config_id`, `payroll_periode.is_transition`, `company_profile.payroll_period_start_day`, alle `config*`-Endpoints in `PayrollPeriodeController`, die Übergangs-/Transition-Logik. Migration: `migrations-archive/remove_periode_flexibility.sql` (Program.cs droppt das auch idempotent beim Startup).
  - `CalcPeriod(year, month)` / `CalcPeriodRange(year, month)` geben immer 1.–letzter Tag (kein `startDay`-Parameter mehr). `PayrollController.Calculate` rechnet immer den Kalendermonat, ignoriert gespeicherte `PeriodFrom`/`PeriodTo` (die könnten aus der alten Ära stammen).
  - **Kurzperioden-Pro-Rata** (anteiliger Lohn bei Ein-/Austritt mitten im Monat) bleibt erhalten — das ist gesetzlich korrekt, keine Periodenflexibilität.
  - Konkrete Perioden weiterhin in `payroll_periode` (Spalten `period_from`/`period_to` bleiben, werden aber immer auf Kalendermonat gesetzt).
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
- **GF + HR teilen sich eine einzige Lohn-Seite (Walter-Vorgabe 17.05.2026 final):** Das frühere HR-Lohnlauf-Modul (`page-lohnlauf` mit `hr-saldi-lohnlauf.js`) ist legacy. Sowohl der GF-Sidebar-Eintrag „Lohn" als auch die HR-Card „Lohnlauf" zeigen jetzt auf `page-lohn` (= `akonto-workflow.js` für Akonto-Tab + `payroll.js` für Definitivlauf-Tab). Innerhalb von `_akWfRenderStatusBar` entscheidet `_akIsHr()` welche Aktionen erscheinen: GF sieht „✓ Lohnblatt freigeben" / „An HR senden", HR sieht „✓ HR-bestätigen" pro MA / „↩ Zurück an GF" / „💰 Akonto auszahlen (DTA)" und kann pro MA mit „✎ ändern" den Netto-Akonto-Betrag korrigieren (über `/api/akonto/workflow/hr-override/{id}`). Counter oben zeigt je nach Status den passenden Schritt-Fortschritt: bei IN_BEARBEITUNG_GF „X/N freigegeben", ab BEI_HR „X/N HR-bestätigt". Auf der Akonto-Lohnzettel-MA-Liste hat die HR_BESTAETIGT-Statuspille die Farbe blau (`#1e40af` / `#dbeafe`) — visuell unterscheidbar von „GF freigegeben" (grün) und „ausbezahlt" (orange).
- **HR-Modul Lohnlauf zweigeteilt (Walter-Vorgabe 17.05.2026, überholt seit Konsolidierung — Kommentar bleibt für Historie):** Die HR-Seite `page-lohnlauf` hat oben eine Tab-Bar mit zwei Tabs:
  - **Tab „Akonto-Lauf"** (`#llAkontoView`): HR sieht alle Akonto-Lohnzeilen der Filiale + Periode, kann pro MA den Netto-Akonto-Betrag direkt überschreiben (✎-Button, nur im Status `BEI_HR`), Periode an GF zurückgeben, HR-Freigabe erteilen, auszahlen. Loader: `llLoadAkontoTab()` in `hr-saldi-lohnlauf.js`.
  - **Tab „Definitivlauf"** (`#llDefinitivView`): wie bisher, unverändertes `llStatusCockpit` + `llAuditLog`.
  - Beide Tabs teilen die Periode-Wahl (Filiale aus globalem Selektor, Monat/Jahr in `#llMonthSelect`/`#llYearSelect`). `onchange="llPeriodChanged()"` lädt nur den aktiven Tab neu. Tab-State persistiert in `localStorage.hrLohnlaufTab` (Default `akonto`).
  - HR-Direktkorrektur: `POST /api/akonto/workflow/hr-override/{id}` mit `{ neuerNettoAkonto, grund }` — admin/superuser only, nur in `BEI_HR`-Phase. Audit (vorheriger Wert + Grund + User + Zeit) wird an `AkontoZahlung.KommentarHr` angehängt; kein DB-Schema-Wandel nötig.
  - Alte „Akonto-Lohn-Lauf"-Karte (`onclick="showPage('akonto-lauf')"`) wurde aus dem HR-Bereich entfernt. `page-akonto-lauf` + `akonto-lauf.js` bleiben im Code als Backup, sind aber nicht mehr verlinkt.
- **GF-Read-Only-Banner im Akonto-Modus (Walter-Vorgabe 17.05.2026):** sobald der Akonto-Status `BEI_HR` / `HR_FREIGEGEBEN` / `AUSBEZAHLT` ist UND der eingeloggte User KEIN admin/superuser, zeigt `_akWfRenderStatusBar` in `akonto-workflow.js` anstelle der Aktions-Buttons eine farbige Pille mit Schloss-Icon: „🔒 Bei HR — keine Änderungen möglich" / „🔒 HR-freigegeben — wartet auf Auszahlung" / „🔒 Ausbezahlt …". Backend-seitig sind alle per-MA-Edit-Endpoints (`freigeben`, `zurueckziehen`, `sync-fix-from-slip`) sowieso schon auf `IN_BEARBEITUNG_GF` restringiert; der Banner ist also nur UX-Klarheit. **Walter-Vorgabe 17.05.2026 (Verschärfung):** auch für admin/superuser werden im GF-Workspace (`page-lohn`) KEINE HR-Aktionen (Zurück an GF / HR-Freigabe / Auszahlen) mehr angezeigt. Die HR-Aktionen leben ausschliesslich im HR-Modul → Lohnlauf → Tab Akonto. Klare Trennung GF-Workspace vs HR-Modul.
- **Akonto pro-MA HR-Bestätigung (Walter-Vorgabe 17.05.2026):** Neuer per-MA-Status `HR_BESTAETIGT` zwischen `FREIGEGEBEN_GF` und `AUSBEZAHLT`. Symmetrisch zum GF-Workflow bestätigt HR jeden Lohnzettel einzeln im HR-Akonto-Tab — kein Pauschal-Knopf mehr. Sobald ALLE MA der Periode HR_BESTAETIGT sind, springt die Periode automatisch von `BEI_HR` auf `HR_FREIGEGEBEN` und der DTA-Button erscheint. Endpoints: `POST /api/akonto/workflow/hr-bestaetigen/{id}` + `/hr-zurueckziehen/{id}` (beide admin/superuser only). HR-Override (`/hr-override/{id}`) setzt einen bereits HR_BESTAETIGT'en Datensatz automatisch zurück auf FREIGEGEBEN_GF (Re-Bestätigung nötig) und ggf. auch die Periode von HR_FREIGEGEBEN auf BEI_HR. Legacy-Pauschal-Endpoint `/hr-freigabe` bleibt für Rückwärtskompatibilität, markiert alle FREIGEGEBEN_GF als HR_BESTAETIGT. **CHECK-Constraint**: `akonto_zahlung.status` muss jetzt fünf Werte akzeptieren: `BERECHNET`, `FREIGEGEBEN_GF`, `HR_BESTAETIGT`, `AUSBEZAHLT`, `STORNIERT` — siehe `migrations-archive/add_akonto_status_hr_bestaetigt.sql`.
- **Akonto-Verrechnung im Definitivlauf (Walter-Vorgabe 17.05.2026):** Wenn der Akonto-Lauf einer Periode bereits AUSBEZAHLT ist, fügt `PayrollController.Calculate` automatisch eine Zeile in `abzuegeExtraLines` ein: „Akonto-Vorauszahlung vom dd.MM.yyyy" mit dem Netto-Akonto als negativem Betrag. Der `auszahlungsbetrag` und alle Bankkonto-Splits reduzieren sich entsprechend — die Bank-Zahlung am Monatsende ist also die echte Restzahlung. Beim Definitiv-Abschluss (`ConfirmPayroll`) wird der Akonto-Wert zusätzlich in `PayrollSnapshot.AkontoBereitsAusbezahlt` persistiert (für Audit + Jahresauswertungen).
- **Initial-Passwort = MA-Nummer (Walter-Vorgabe 17.05.2026, Variante B):** `Services/EmployeePostfachService.cs → BuildInitialPassword` gibt nur noch `EmployeeNumber` zurück (keine Filial-Präfixe mehr). Username und Initial-Passwort sind identisch (z.B. beide `750009`). Sicherheit kommt durch `MustChangePassword=true` — beim ersten Login wird ein Wechsel zwingend. `CompanyProfile.LoginPasswordPrefix` ist Dead Code.

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

## Bearbeitungs-Status-Map (Walter-Vorgabe 19.05.2026 — single source of truth)

Pro Lohnperiode gibt es **zwei** Workflows, jeder mit eigenem Status und klarer Rollentrennung. Alle UI-Buttons und alle Lock-Entscheidungen MÜSSEN sich nach diesem einen Status-Schema richten. Wenn ein UI-Verhalten nicht passt, ist es ein Bug.

**Akonto-Workflow** (`payroll_periode.akonto_status` + `akonto_zahlung.status` pro MA):

| Periode-Status | Snapshot-Status pro MA | Wer darf was | UI im Akonto-Tab |
|---|---|---|---|
| `OFFEN` | – (noch keine Zahlungen) | GF | „📅 Akonto vorbereiten" |
| `IN_BEARBEITUNG_GF` | `BERECHNET` / `FREIGEGEBEN_GF` | GF bestätigt jeden MA | „✓ Freigeben" / „↶ Zurückziehen" / „An HR senden" |
| `BEI_HR` | `FREIGEGEBEN_GF` / `HR_BESTAETIGT` | HR bestätigt jeden MA; GF **gesperrt** | HR: „✓ HR-bestätigen", „↶ Zurück an GF", „✎ ändern" |
| `HR_FREIGEGEBEN` | alle `HR_BESTAETIGT` | HR korrigiert noch / klickt DTA; GF gesperrt | „💰 DTA auszahlen", + Per-MA-Override |
| `AUSBEZAHLT` | alle `AUSBEZAHLT` | niemand mehr (Admin: Reset über Lohnperioden-Modul) | „📥 DTA-File", „📄 Liste" |

**Definitiv-Workflow** (`payroll_periode.status` + `payroll_snapshot.status` pro MA):

| Periode-Status | Snapshot-Status pro MA | Wer darf was | UI im Definitiv-Tab |
|---|---|---|---|
| `offen` | `BERECHNET` / `FREIGEGEBEN_GF` | GF bestätigt jeden MA | „✓ Lohn bestätigen" / „↶ Wieder eröffnen" / „An HR senden" |
| `provisorisch_abgeschlossen` | `FREIGEGEBEN_GF` / `HR_BESTAETIGT` | HR bestätigt jeden MA; GF **gesperrt** | HR: „✓ HR-bestätigen", „↶ HR-Bestätigung zurückziehen", „↩ Zurück an GF", „📑 Lohnbelege + DTA" |
| `abgeschlossen` | alle `ABGESCHLOSSEN` | niemand mehr (Admin: Wieder-Öffnen nur bis Zahldatum DTA) | „📑 Lohnbelege ansehen", „📥 DTA-File" |

**GF-Sperre-Regel (final 19.05.2026):**
- GF darf seine zwei Buttons („Lohn bestätigen" / „Wieder eröffnen") **NUR** sehen, wenn der Periode-Status `offen` bzw. `IN_BEARBEITUNG_GF` ist.
- Sobald HR den Stab übernimmt (`BEI_HR` / `provisorisch_abgeschlossen` / `HR_FREIGEGEBEN`), sind GF-Buttons komplett unsichtbar. Auch wenn der einzelne MA-Snapshot noch FREIGEGEBEN_GF ist — der GF kommt da nicht mehr ran.
- Frontend (Walter-Vorgabe 20.05.2026, **Definitiv = Akonto-Architektur**): Beide Workflows haben jetzt je **eine** zentrale Render-Funktion, die als EINZIGE Stelle Status-Pille + Counter + ALLE Aktionsbuttons zeichnet — gespeist aus EINEM State-Cache. Es darf NIRGENDS sonst Button-Sichtbarkeit gesetzt werden.
  - **Akonto:** `akonto-workflow.js → _akWfRenderStatusBar()` (Cache `_akWfData`, geladen via `akWfRefresh` aus `/api/akonto/workflow/status`), rendert in `#akontoStatusBar`.
  - **Definitiv:** `payroll.js → _lohnWfRenderStatusBar()` (Cache `_lohnWfData`, geladen via `lohnWfRefresh` → `loadLohnList` aus `/api/payroll-perioden/current` + `…/snapshots`), rendert in `#lohnDefinitivStatusBar`. `_lohnWfData = { status, periode, periodeId, snapByEmp:{empId:{id,status}}, gfConfirmed, hrConfirmed, activeTotal }`.
  - `loadLohnSlip` rendert NUR noch den Lohnzettel (Berechnung + `renderLohnSlip`) — KEINE Button-Logik mehr. `loadLohnPeriodBanner` ist ein **Shim** auf `lohnWfRefresh` (Alt-Aufrufer funktionieren weiter). MA-Klick mirror `akWfSelectMa`: nur Highlight + `_lohnWfRenderStatusBar()` + `loadLohnSlip` (kein voller Listen-Rebuild pro Klick). Aktions-Handler (`confirmLohn`/`reopenLohn`/`lohnHrBestaetigen`/`lohnHrZurueckziehen`/`lohnZurueckAnGf`/`abschliessePeriode`/`savePeriodeBemerkung`) rufen nach der Aktion `lohnWfRefresh()`. Grund für den Rebau: die früher über `loadLohnSlip` + `loadLohnPeriodBanner` + `loadLohnList` verstreute Button-Logik lief ständig auseinander („Button fehlt / verdeckt"-Bugs).
  - Die alte statische Button-Zeile `#lohnTopActions` + die alte Toolbar-Status-Pille `#lohnPeriodBanner` sind toter, dauerhaft versteckter DOM (bewusst erhalten, nicht mehr verdrahtet).

**Übergänge zwischen den zwei Workflows:**
- Beim Wechsel Akonto-AUSBEZAHLT → Definitivlauf-Start: `payroll_snapshot.status` muss auf `BERECHNET` initialisiert sein (der GF muss im Definitiv jeden MA neu bestätigen — der Akonto-Workflow hat damit nichts zu tun).
- Beim `zurueck-an-gf` (Definitiv): alle Snapshots → `BERECHNET`, alle Saldos → `draft`.
- Beim `wieder-oeffnen` (Definitiv abgeschlossen → provisorisch): Snapshots, die `ABGESCHLOSSEN` waren, → `HR_BESTAETIGT`. HR-Bestätigungen bleiben erhalten, nur der DTA-Klick muss erneut.

**Zahldaten / Bank-Ausführungsdatum (Walter-Vorgabe 19.05.2026):** Beide Workflows erfassen vor dem DTA-Versand das Bank-Ausführungsdatum (ReqdExctnDt im pain.001). Default: morgen. Wird in der Periode persistiert und ist der Cutoff für den Admin-Reset:
- Akonto → `payroll_periode.akonto_auszahlungsdatum` (neu — Migration: `migrations-archive/add_akonto_auszahlungsdatum.sql`)
- Definitiv → `payroll_periode.auszahlungsdatum` (existiert)

**DTA-Bestätigungs-Schritt (Walter-Vorgabe 19.05.2026):** Beide Workflows trennen das Erstellen des DTA vom Versand an die Bank. Klick auf den finalen Knopf läuft als 3-Schritt:
- **Schritt 0**: Datum-Prompt → Bank-Ausführungsdatum erfassen (Default: morgen).
- **Schritt 1**: Confirm-Dialog „DTA erstellen mit Ausführungsdatum xx.xx.xxxx" — DTA wird mit diesem Datum generiert + heruntergeladen.
- **Schritt 2**: Confirm-Dialog „DTA an Bank gesendet?" — erst JA setzt den finalen Status (Akonto AUSBEZAHLT / Definitiv abgeschlossen) und triggert nachgelagerte Aktionen (Postfach-Ablage + E-Mail beim Definitivlauf).

**Admin-Reset / Wieder-Eröffnen — Zahldatum-Lock:**
- Definitiv: Admin (`role=admin`) öffnet `abgeschlossene` Periode via `/api/payroll-perioden/{id}/wieder-oeffnen`. **NUR bis `heute ≤ payroll_periode.auszahlungsdatum`** — danach 409 `PAYOUT_DATE_REACHED`.
- Akonto: Admin setzt via `/api/akonto/workflow/reset-periode` zurück. **NUR bis `heute ≤ akonto_auszahlungsdatum`** — danach 409. (Fallback für Alt-Daten ohne Feld: `AkontoAusbezahltAt.Date`.)
- **Pflicht-Bestätigung**: bevor das Frontend den Reset/Wieder-Eröffnen-Endpoint aufruft, MUSS der Admin eine zweite Confirm-Frage „Hast du den DTA bei der Bank gelöscht/storniert?" mit JA beantworten. Schützt vor Doppelzahlung.
- **Nach dem Zahldatum**: kein Reset mehr möglich — nicht für Admin, nicht für HR, nicht für GF. Notfall-Eingriff nur über direkten DB-Eingriff durch Entwickler.

## Lohnlauf-Edit-Sperre (Walter-Vorgabe 17.05.2026)

Sobald ein Akonto- oder Definitivlauf einer Periode in HR-Verarbeitung oder bereits abgeschlossen ist, sind lohnrelevante datum-bezogene Edits **für JEDEN** (auch admin/superuser) gesperrt. Service: `Services/LohnEditLockService.cs`. Logik: spätestes (Year, Month) finden, das `Status != 'offen'` ODER `AkontoStatus NOT IN ('OFFEN', 'IN_BEARBEITUNG_GF')` ist — Edit-Datum muss > letzter Tag dieser Periode liegen (= FirstAllowedDate = 1. Tag des Folgemonats). `IN_BEARBEITUNG_GF` ist absichtlich NICHT gesperrt, damit der GF in der Vorbereitungsphase noch Stempel- und Absenz-Korrekturen vornehmen kann.

**Kein Rollen-Bypass (Walter-Vorgabe final 17.05.2026):** auch admin/superuser sehen den Lock. Damit ein laufender Lohnlauf editiert werden kann, muss der Admin die Periode aktiv **zurücksetzen / wieder öffnen** — das zwingt eine bewusste Entscheidung mit Audit-Trail (Lohnzettel aus MA-Postfach raus, Akonto-Zahlungen storniert etc.) statt einer stillen Daten-Manipulation im Hintergrund. Reset-Endpoints:
- Akonto: `POST /api/akonto/workflow/reset-periode` (admin-only, Body `{companyProfileId, year, month, grund}`) — setzt AkontoStatus auf OFFEN, löscht BERECHNET/FREIGEGEBEN_GF/HR_BESTAETIGT-Zahlungen, stempelt AUSBEZAHLT-Zahlungen auf STORNIERT (Beleg bleibt), Audit-Eintrag mit Aktion `AKONTO_RESET`.
- Definitiv: `POST /api/payroll-perioden/{id}/wieder-oeffnen` (admin-only, existiert) — setzt Status auf provisorisch_abgeschlossen zurück, löscht Lohnzettel aus MA-Postfächern.
Im Lohnperioden-Modul sehen Admins bei jeder Periode mit `akontoStatus != OFFEN` einen orangen Button „↺ Akonto zurücksetzen" mit Pflicht-Grund-Eingabe.

Geschützte Endpoints (alle POST/PUT/DELETE, alle Rollen):
- **Datum-bezogen**: `AbsencesController`, `LohnZulagenController` (Vorschuss-Rückzahlung wird hier abgebildet).
- **Versioniert (`ValidFrom < FirstAllowedDate` = inLohnVerwendet)**: `EmployeeBankAccountsController`, `EmploymentsController`, `EmployeeQuellensteuerController`, `EmployeeRecurringWagesController`, `EmployeePermitHistoryController`, `EmployeeLohnAssignmentsController`, `FamilyMemberAllowancesController`.
- **Periode-bezogen**: `SaldoVortragController` (Vortrag-Periode darf nicht rückwirkend).

**Stempelzeiten** sind seit Walter-Vorgabe 17.05.2026 komplett **READ-ONLY**: `EmployeeTimeEntriesController` POST/PUT/DELETE liefern 403 mit Klartext-Meldung — Stempelzeiten kommen ausschliesslich aus easy@work via `ImportController.ImportStempelzeiten` / `ImportMonatlich`. Cowork zeigt sie nur an, Korrekturen passieren in easy@work und werden anschliessend neu importiert. Versionierte Daten (Verträge, Bankkonten, Bewilligungen, QST, EmployeeRecurringWages mit ValidFrom/ValidTo) gehören NICHT in den Lock — die haben eigene „neu ab"-Logik und werden separat behandelt (Walter: später, eigenes Thema).

Bei Sperre → HTTP 409 mit `{ error: "LOHN_EDIT_LOCKED", message, firstAllowedDate }`. Frontend-Helper in `wwwroot/js/lohn-edit-lock.js` (global `window.lohnEditLock`): `loadState(branchId)`, `renderBanner(el, state)`, `applyToDateInput(input, state)`, `handleResponse(res)`. Frontend-Pattern: nach jedem `fetch()` bei Edit-Aktion `if (await lohnEditLock.handleResponse(res)) return;` vor dem normalen Fehler-Pfad. Beim Öffnen eines Date-Picker-Modals zusätzlich `applyToDateInput()` aufrufen, damit gesperrte Tage gar nicht erst auswählbar sind.

GET-Endpoint: `/api/lohn-edit-lock/first-allowed-date?branchId=X` liefert `{ firstAllowedDate, reason }` — wird vom Frontend-Helper mit 5s-TTL gecacht.

**Tests:** das Test-Projekt `Tests/hr-system.Tests.csproj` enthält Unit-Tests für `LohnEditLockService` (alle Bypass-Regeln, alle Status-Kombinationen, FirstAllowedDate-Berechnung, Filiale-Trennung) und einen **Audit-Test** (`EditLockEndpointAuditTests`), der alle Controller-Files scannt und sicherstellt, dass jeder POST/PUT/DELETE-Endpoint entweder `LohnEditLockService` einbindet ODER in der Whitelist `LOCK_IRRELEVANT_CONTROLLERS` mit Begründung steht. Wenn ein neuer Edit-Endpoint angelegt wird, der weder das eine noch das andere ist, schlägt der Test fehl — verhindert dass jemand "vergisst" einen Lock einzubauen. Lauf: `dotnet test Tests/hr-system.Tests.csproj`.

**Workflow-Tests (Walter-Vorgabe 19.05.2026):** Vier zusätzliche Test-Files nageln die Lohnlauf-Workflows fest und verhindern Regressionen am 4-Augen-Prinzip:
- `WorkflowSpecAuditTests.cs` — scannt die kritischen Code-Stellen (Defaults, IsFinal-Timing, PAYOUT_DATE_REACHED, ZurueckAnGf-Reset, Auszahlungsdatum-Persistierung) per Regex. Schlägt fehl wenn jemand den Default des PayrollSnapshot wieder auf FREIGEGEBEN_GF setzt oder IsFinal zu früh setzt.
- `WorkflowDefaultsTests.cs` — neue PayrollSnapshot / AkontoZahlung / PayrollPeriode / PayrollSaldo haben die korrekten Status-Defaults (BERECHNET / BERECHNET / offen+OFFEN / draft).
- `AkontoWorkflowTransitionTests.cs` — alle Status-Übergänge im Akonto: OFFEN → IN_BEARBEITUNG_GF → BEI_HR → HR_FREIGEGEBEN → AUSBEZAHLT, plus Auto-Transition wenn alle MA HR_BESTAETIGT sind, plus PAYOUT_DATE_REACHED am Tag nach Auszahlung.
- `DefinitivWorkflowTransitionTests.cs` — alle Status-Übergänge im Definitivlauf: offen → provisorisch_abgeschlossen → abgeschlossen, inkl. der Invariante „provisorisch darf KEIN IsFinal=true setzen" (Walter-Bug 19.05.2026: blockierte HR-Bestätigung), Reset rollt Snapshot+Saldo sauber zurück, WiederOeffnen mappt ABGESCHLOSSEN-Snapshots auf HR_BESTAETIGT.

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
- **UI-Standard: Sprache/Theme oben rechts, Aktionsbuttons NIE dorthin (Walter-Vorgabe 20.05.2026, gilt ÜBERALL):**
  - Der Sprach-/Theme-Schalter (`#langSwitcher`: DE/EN-Flaggen + 🌙 Dunkel) ist `position:fixed` oben rechts auf JEDEM Screen. Er reserviert KEINEN Platz im Layout — Elemente fliessen darunter durch.
  - **Folge-Regel:** Bedien-/Aktionsbuttons (Speichern, Bestätigen, An HR senden, DTA, PDF, Wieder eröffnen, Refresh …) dürfen NIEMALS in die oberste Header-/Toolbar-Zeile rechts platziert werden — dort verdeckt der schwebende `#langSwitcher` sie (wiederkehrender Bug 19./20.05.2026, mehrfach: „An HR senden" + „Lohn bestätigen" + DTA waren unsichtbar).
  - **Standard-Platzierung:** Aktionsbuttons gehören in eine **eigene Zeile unterhalb der Titel-/Toolbar-Zeile** (rechtsbündig via `justify-content:flex-end` oder als eigene Statusleiste, links-beginnend). Gute Referenz-Implementierungen:
    - **Verträge-Seite** (`page-vertraege`): „CSV importieren" + „+ Neuer Vertrag" rechts in der Content-Header-Zeile (unterhalb des globalen Sprach-/Theme-Schalters) — das Vorbild für Voll-Seiten mit Detail-Header.
    - **`akontoStatusBar`**: eigene volle Status-/Aktionszeile über dem 3-Spalten-Grid — das Vorbild für Workflow-Seiten (Akonto + Definitiv).
    - **Lohn-Aktionszeile** in `page-lohn`: eigene Zeile (`justify-content:flex-end`) direkt unter der Toolbar.
    Beim Bau jeder neuen Seite eines dieser Muster einhalten — NIE Buttons in die oberste Zeile rechts.

## Externe Endpoints, die im Frontend gerne fehlen

- `/api/swiss-locations/by-plz?plz=XXXX` — PLZ-Lookup (BFS-Liste). Liefert Gemeinde + Kanton + BFS-Nr. Funktion `plzLookup` in `employees.js` (Hauptadresse, hardcoded auf `ef-zip/ef-city/ef-canton`) und `plzLookupGeneric` (für Zusatzadressen-Modal).
- `/api/compliance/check-live` — Mindestlohn-Check. Body: `{jobGroupCode, educationLevelCode, effectiveDate, employmentModel, employmentPercentage, hourlyRate?, monthlySalary?}`. Antwort: `{status: OK|UNDERPAID|NO_RULE|NOT_CHECKED, minimumHourlyRate?, minimumMonthlySalary?, minimumMonthlySalaryFte?, difference?, warningMessage?}`.
- `/api/contracts/employment/{id}/pdf` — generiert Arbeitsvertrag-PDF via `ContractPdfService`.
