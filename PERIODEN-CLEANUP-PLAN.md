# Plan: Kalendermonat als einzige Lohnperiode + Definitivlauf = Akonto

**Walter-Vorgabe 20.05.2026.** Ausgangslage: Ursprünglich war die Lohnperiode
flexibel (Starttag 21 oder 1). Das ist unpraktisch, weil gesetzliche
Berechnungen (Quellensteuer, ALV, AHV …) IMMER kalendermonatlich (1.–letzter
Tag) laufen. Damit MA trotzdem vor Monatsende Geld bekommen, wurde die
**Akonto-Zahlung** eingeführt. Die alte Flexibilität ist damit überflüssig —
sie hängt aber als Ballast im Code und ist die Wurzel der wiederkehrenden
Definitivlauf-Bugs (der Definitiv-Code rechnet teils noch mit variabler
Periode, der Akonto-Code nimmt schon stur den Kalendermonat).

**Ziel:** (1) Kalendermonat ist die einzige Wahrheit. (2) Definitivlauf
strukturell gleich wie Akonto.

---

## Wichtig vorab: Was bleibt erhalten

- **Kurzperioden-Pro-Rata** (Eintritt/Austritt mitten im Monat → anteiliger
  Lohn per Tagessatz) bleibt. Das ist KEINE alte Periodenlogik, sondern
  gesetzlich korrekte Teilmonatsberechnung.
- **Historische abgeschlossene Perioden** behalten ihre eingefrorenen
  `SlipJson`-Snapshots — die werden nie neu berechnet, alte Lohnbelege
  bleiben also exakt erhalten. Wir droppen nur Spalten, nicht die Snapshot-Daten.

---

## ETAPPE 1 — Kalendermonat als einzige Wahrheit (Backend-Cleanup)

Risikoarm, weil `startDay=1` faktisch schon überall gilt. Wir entfernen nur
ungenutzte Flexibilität.

### 1A. Periode-Berechnung vereinfachen
- **`Controllers/PayrollController.cs`** (`Calculate`, ~Zeile 98–117):
  `existingPeriod.PeriodFrom/PeriodTo` nicht mehr blind übernehmen. Immer
  `CalcPeriod(year, month)` = 1.–letzter Tag. Kurzperioden-Logik darunter
  bleibt unverändert.
- **`CalcPeriod(startDay, year, month)`** → `CalcPeriod(year, month)` ohne
  startDay-Parameter (3 Controller: PayrollController, PayrollPeriodeController
  `CalcPeriodDates`, AbsencesController `CalcPeriodRange`).

### 1B. `PayrollPeriodeConfig` entfernen (Periodenregel-Tabelle)
- **`Models/PayrollPeriodeConfig.cs`** → löschen.
- **`Data/AppDbContext.cs`** → `DbSet<PayrollPeriodeConfig>` + Mapping raus.
- **`Controllers/PayrollPeriodeController.cs`** → `config*`-Endpoints
  (`GetConfig`, `SaveConfig`, …) raus.
- **`Models/PayrollPeriode.cs`** → `ConfigId` + Navigation `Config` raus.
- **`Program.cs`** (~Zeile 612) → Seed-Logik für Config raus.

### 1C. `IsTransition` + Übergangs-Lohnläufe entfernen
- **`Models/PayrollPeriode.cs`** → `IsTransition` raus.
- **`Controllers/PayrollPeriodeController.cs`** → Transition-Anlege-Logik
  (`extraTransition`) raus; alle `!p.IsTransition`-Filter entfernen (nicht
  mehr nötig).
- **`Controllers/PayrollController.cs`** → `&& !p.IsTransition` im
  existingPeriod-Query raus.

### 1D. `CompanyProfile.PayrollPeriodStartDay` entfernen
- **`Models/CompanyProfile.cs`** → Feld raus.
- **`Data/AppDbContext.cs`** → Mapping raus.

### 1E. SQL-Migration (TablePlus)
```sql
-- Spalten + Tabelle der alten Periodenregel abräumen.
-- Snapshot-Daten + Periode-Stammdaten bleiben unangetastet.
ALTER TABLE payroll_periode  DROP COLUMN IF EXISTS config_id;
ALTER TABLE payroll_periode  DROP COLUMN IF EXISTS is_transition;
ALTER TABLE company_profile  DROP COLUMN IF EXISTS payroll_period_start_day;
DROP TABLE IF EXISTS payroll_periode_config;
```
> `PeriodFrom`/`PeriodTo` bleiben als Spalten (sie sind nützlich + werden vom
> Code immer auf Kalendermonat gesetzt). Optional: bestehende OFFENE Perioden
> auf Kalendermonat normalisieren — abgeschlossene NICHT anfassen.

### 1F. Tests / Doku
- Workflow-Tests anpassen (Periode-Erstellung ohne ConfigId/IsTransition).
- `CLAUDE.md` aktualisieren (Config/Transition-Abschnitte raus, Kalendermonat
  als alleinige Regel dokumentieren).

---

## ETAPPE 2 — Definitivlauf strukturell wie Akonto (Frontend + 1 Backend-Endpoint)

Aufwändiger. Kernidee: der Definitivlauf bekommt — wie Akonto — **eine**
Status-Antwort, aus der die geteilte Render-Logik die MA-Liste + Status-Bar baut.

### 2A. Gemeinsamer Status-Endpoint (Backend)
- Neuer `GET /api/payroll/workflow/status?branchId&year&month` liefert analog
  zu `/api/akonto/workflow/status` EINE Antwort: Periode-Status + pro MA
  { id, name, modell, snapshotStatus, qst, betrag }. So muss das Frontend nicht
  mehr employees + snapshots + calculate einzeln zusammenstückeln.

### 2B. Geteiltes `lohnWorkflow.js` (Frontend)
- Gemeinsame Funktionen mit `mode='akonto'|'definitiv'`:
  - `renderMaList` (Häkchen-Logik BERECHNET/FREIGEGEBEN_GF/HR_BESTAETIGT)
  - `renderStatusBar` (Buttons je Status + Rolle, aus der Status-Map)
  - `jumpToNext` (✅ heute schon vereinheitlicht)
- `akonto-workflow.js` + der Definitiv-Teil von `payroll.js` rufen nur noch
  diese geteilten Funktionen mit ihrem Mode auf.

### 2C. Status-Map als Code-Konstante
- Die Tabelle aus CLAUDE.md (OFFEN→…→AUSBEZAHLT bzw. offen→…→abgeschlossen)
  als JS-Objekt — beide Modi lesen daraus, welche Buttons wann erscheinen.

### Risiko Etappe 2
- Größeres Frontend-Refactoring. Absicherung: Backend-Status-Logik ist durch
  61 grüne Tests gedeckt; das Frontend wird Schritt für Schritt umgestellt und
  nach jedem Schritt manuell durchgetestet (GF → HR → DTA).

---

## Empfohlene Reihenfolge
1. **Etappe 1 zuerst** (Fundament). Danach rechnet alles garantiert
   kalendermonatlich — erst dann ist Etappe 2 sauber möglich.
2. **Etappe 2 in Teilschritten**: erst 2A (Status-Endpoint), dann 2B/2C
   (geteilte Render-Logik), jeweils mit Zwischen-Test.

## Was NICHT angefasst wird
- Akonto-Workflow (läuft sauber, bleibt Referenz).
- Lohnberechnung selbst (SV, QST, Ferien-Pott, 13. ML).
- Kurzperioden-Pro-Rata bei Ein-/Austritt.
- Historische Snapshots / alte Lohnbelege.
