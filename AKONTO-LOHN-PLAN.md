# Akonto-Lohn — Umsetzungsplan

Stand: 14.05.2026 · gemeinsam mit Walter erarbeitet

## 1. Konzept (das vereinbarte Modell)

Teilzeit-Mitarbeitende sollen nicht erst am 5./6. des Folgemonats Geld sehen.
Lösung: **ein** echter Lohnlauf pro Monat plus eine **Akonto-Zahlung** als
Vorauszahlung auf Rechnung.

- **Lohnperiode** ist immer der Kalendermonat (1. – Letzter). Die alte
  Periodenregel (Beginn am 20./25. …) entfällt komplett.
- **Akonto-Termin** pro Filiale, Jahr und Monat frei definierbar (wegen
  Wochenenden/Feiertagen kein fixer Tag).
- Am Akonto-Termin fliesst eine **grosszügig geschätzte Netto-Vorauszahlung**.
  Kein Lohnbeleg, keine Buchung von AHV/ALV/NBU/KTG/BVG/QST, keine Saldi.
- Nach Monatsende läuft der **Definitivlauf** — die einzige echte
  Lohnabrechnung. Er rechnet alles korrekt, zieht das bereits ausbezahlte
  Akonto als Zeile ab → **Restzahlung**, und führt alle Saldi nach.
- Zwei Zahlläufe pro Monat: DTA am Akonto-Termin, DTA nach Monatsende.

## 2. Akonto — Berechtigung & Schätzung

### 2.1 Berechtigung — wer bekommt überhaupt ein Akonto

Ein Akonto gibt es **nur für MA, die „normal laufen"**. Sobald ein
Sonderfall vorliegt: kein Akonto — der MA bekommt seinen Lohn dann einfach
mit dem Definitivlauf. Ausgeschlossen sind:

- MA in der **Probezeit** (`Employment.ProbationEndDate` noch nicht erreicht)
- **Austritt geplant** in der aktuellen oder der darauffolgenden Periode
  (`Employee.ExitDate` bzw. befristeter Vertrag `Employment.ContractEndDate`)
- **Krankheit / Unfall / Mutterschaft AM STICHTAG aktiv** (Walter-Vorgabe
  15.05.2026): nur Absenzen, die das Akonto-Auszahlungsdatum *überlappen*,
  hindern den Akonto. Kurze Absenzen vor dem Stichtag (z.B. 1-Tages-Krank
  Anfang Monat) sind irrelevant — der MA ist am Akonto-Termin wieder fit.

Damit fallen praktisch alle riskanten Konstellationen weg — das Akonto geht
nur an stabile, planbare Fälle. So entsteht gar nie eine negative Restzahlung.

### 2.2 Akonto-Basis pro Vertragsmodell

Das Akonto ist immer der **voraussichtlich ausbezahlte Lohn** (= geschätzter
Netto, siehe 2.3) — je Modell anders ermittelt:

| Modell | Akonto-Basis (Brutto) → Akonto |
|---|---|
| **UTP** | Bis zum Akonto-Stichtag **gestempelte Stunden** × Ansatz **+ Feriengeld** für bis dahin bezogene Ferientage → **100%** des daraus geschätzten Netto. |
| **MTP** | Gleich wie UTP: **gestempelte Stunden** × Ansatz **+ Feriengeld** für bezogene Ferientage → **100%** des geschätzten Netto. |
| **FIX / FIX-M** | Voraussichtlich ausbezahlter **Monatslohn** → **Filial-% (Default 80%)** davon. |

Logik dahinter: bei UTP und MTP wird nur ausbezahlt, was bis zum Stichtag
schon gearbeitet *und* gestempelt ist — plus das Feriengeld für effektiv
bezogene Ferien (sonst hätte ein MA, der gerade Ferien hatte, zu wenig
Akonto, weil keine Stunden gestempelt). Dieses Geld ist sicher → 100%. Bei
FIX/FIX-M ist die Basis der ganze Monatslohn, obwohl der Monat noch nicht
fertig ist → der konfigurierbare Sicherheitsabschlag (Default 80%) fängt
das ab. Die Restzahlung ist damit bei allen drei Modellen immer positiv.

Die Feriengeld-Berechnung bei Bezug folgt der bestehenden Ferien-Logik
(MTP: „Pott"-Mechanismus; UTP: in Phase 3 gegen die aktuelle
Ferienbezugs-Abrechnung verifizieren) — im Akonto eher konservativ geschätzt.

Der Akonto-Prozentsatz für FIX/FIX-M ist ein **neuer Filialparameter**
(Default 80%, pro Filiale änderbar — im Einstellungen-Tab).

### 2.3 „Voraussichtlich ausbezahlter Lohn" = geschätzter Netto

- Brutto-Basis je Modell (siehe 2.2) **minus geschätzte Abzüge** — SV
  (AHV/ALV/NBU/KTG/L-GAV), BVG und QST, mit den echten Sätzen aus dem
  System (kein Raten, QST-Satz des MA wird berücksichtigt).
- **BVG / BVG-Zusatz**: praktisch nur bei FIX/FIX-M/MTP relevant — wird auf
  dem **vollen Monatslohn** gerechnet.
- Das Netto-Akonto wird zum Schluss **auf CHF 10 abgerundet**.

Nicht im Akonto enthalten: 13. ML, Aufbau der Ferien-/Nacht-Saldi,
Nachtzulagen-Spitzen, rückwirkende Korrekturen — alles ausschliesslich im
Definitivlauf. (Das Feriengeld für *bezogene* Ferientage ist dagegen Teil
der Akonto-Basis, siehe 2.2.)

## 3. Lohnpfändung / Lohnabtretung im Akonto

Die Daten liegen bereits am `EmployeeLohnAssignment` (`Freigrenze`,
`Zielbetrag`, `BereitsAbgezogen`, `ZahlungsReferenz`, `Behoerde`,
`ValidFrom`/`ValidTo`).

Regel fürs Akonto bei einem MA mit aktiver Lohnabtretung:

- **Akonto an den MA = min(Netto-Akonto-Vorschlag, Freigrenze)**
  Die `Freigrenze` (Existenzminimum) ist der Betrag, der dem MA bei einer
  Pfändung *immer* zusteht — damit null Rückforderungsrisiko.
- **`Freigrenze` = 0 → gar kein Akonto.** Der MA bekommt nichts vorab,
  sondern erst im Definitivlauf seinen effektiven Anteil (falls einer
  bleibt).
- **Zahlung ans Betreibungsamt erst im Definitivlauf** — der genau
  pfändbare Betrag steht exakt erst am vollen Monatseinkommen fest.

## 4. Datenmodell (neu)

### 4.1 `akonto_termin` (neue Tabelle)
Akonto-Auszahlungsdatum pro Filiale/Jahr/Monat.

| Spalte | Typ | Bemerkung |
|---|---|---|
| `id` | int PK | |
| `company_profile_id` | int FK | Filiale |
| `year` | int | |
| `month` | int (1–12) | |
| `payout_date` | date | das tatsächliche Auszahlungsdatum |
| Unique-Index | | (`company_profile_id`, `year`, `month`) |

UX: pro Filiale ein Jahr auf einmal generieren (Default z.B. „der 23., bei
Wochenende auf den Freitag davor"), dann von Hand korrigieren.

### 4.2 `akonto_zahlung` (neue Tabelle)
Ergebnis des Akonto-Laufs — eine Zeile pro MA und Periode.

| Spalte | Typ | Bemerkung |
|---|---|---|
| `id` | int PK | |
| `employee_id` | int FK | |
| `company_profile_id` | int FK | |
| `period_year` / `period_month` | int | |
| `payout_date` | date | aus `akonto_termin` |
| `geschaetzter_brutto` | decimal | |
| `geschaetzte_abzuege` | decimal | |
| `pfaendung_abzug` | decimal | beim MA gekürzter Pfändungsanteil (Schätzung) |
| `netto_akonto` | decimal | tatsächlich an den MA ausbezahlt |
| `status` | text | z.B. `BERECHNET`, `AUSBEZAHLT`, `STORNIERT` |
| `dta_run_id` | int? FK | Verweis auf den Akonto-Zahllauf |
| `created_at` / `updated_at` | timestamp | |

### 4.3 `payroll_snapshot` — neues Feld
- `akonto_bereits_ausbezahlt` decimal NOT NULL DEFAULT 0
  Der Definitivlauf liest hier das ausbezahlte Akonto und zieht es vom
  berechneten Netto ab → Restzahlung.

### 4.4 `company_profile` — neuer Filialparameter
- `akonto_prozent_fix` decimal NOT NULL DEFAULT 80
  Akonto-Prozentsatz für FIX/FIX-M (siehe 2.2), pro Filiale änderbar im
  Einstellungen-Tab. UTP/MTP nutzen keinen Prozentsatz (immer 100%).

### 4.5 SQL-Migration
Reine SQL für TablePlus (Walter-Konvention) — `CREATE TABLE` für die zwei
neuen Tabellen + `ALTER TABLE payroll_snapshot …` + `ALTER TABLE
company_profile …`.

## 5. Periodenlogik vereinfachen

Weil die Periode jetzt immer der Kalendermonat ist:

- `PayrollPeriodeConfig`-Periodenregel (fromDay/toDay/validFrom…) wird
  **deprecated**. Die Periodenfindung im `PayrollController` (bisher
  `PayrollPeriode` → `PayrollPeriodeConfig` → Legacy
  `CompanyProfile.PayrollPeriodStartDay`) vereinfacht sich auf „Monat = Periode".
  Das ist genau die Stelle, die laut `CLAUDE.md` „mehrfach Bug-Quelle"
  war — fällt weg.
- Im Filial-Einstellungen-Tab (`branches-detail.js`) ersetzt eine
  **„Akonto-Termine"**-Konfiguration den heutigen Periodenregel-Button.
- Bestehende `payroll_periode`-Datensätze bleiben; nur neue Perioden werden
  fix als Kalendermonat erzeugt.

## 6. Akonto-Lauf (Backend)

Neuer Ablauf, angelehnt an den bestehenden Lohnlauf, aber bewusst „leicht":

1. Endpoint `POST /api/payroll/akonto` mit Filiale + Periode.
2. Pro aktivem MA der Filiale: Akonto-Schätzung nach Modell (Abschnitt 2),
   Pfändungs-Cap (Abschnitt 3).
3. Schreibt `akonto_zahlung`-Datensätze (`status = BERECHNET`).
4. **Kein** `PayrollSnapshot`, **keine** SV-/QST-/Saldi-Buchung.
5. DTA/pain.001 für die Akonto-Auszahlung über `Iso20022PainService`
   (wiederverwenden) → `status = AUSBEZAHLT`.

## 7. Definitivlauf-Anpassung

Der bestehende Monatsend-Lauf bleibt weitgehend unverändert. Ergänzungen:

- Liest die `akonto_zahlung` der Periode → setzt
  `payroll_snapshot.akonto_bereits_ausbezahlt`.
- **Restzahlung = berechneter Netto − Akonto.**
- **Guard gegen negative Restzahlung:** durch die Berechtigungs-Filter
  (Abschnitt 2.1) + die anteilige Kürzung sollte das gar nicht vorkommen.
  Als reines Sicherheitsnetz wird eine negative Restzahlung trotzdem
  abgefangen: nichts zurückfordern, Restzahlung = 0, im Lohnlauf warnen.
- **Betreibungsamt-Zahlung** passiert hier — bestehende
  `EmployeeLohnAssignment`-Logik, `BereitsAbgezogen` wird hochgezählt.
- DTA/pain.001 für die **Restzahlungen**.
- Die Lohnabrechnung zeigt eine Zeile „− Akontozahlung vom TT.MM.JJJJ".

## 8. Frontend

- **Akonto-Termine-Konfiguration** im Filial-Einstellungen-Tab
  (`branches-detail.js`) — Jahr generieren + pro Monat editierbar.
- **Akonto-Lauf** im Lohn-Modul, neben dem Definitivlauf — Filiale +
  Periode wählen, Vorschau (Liste der Akonto-Beträge pro MA, inkl.
  Pfändungs-Hinweis), bestätigen → DTA.
- **Kein Lohnbeleg** für die Akonto-Zahlung (Walter-Vorgabe) — nur eine
  Übersichtsliste / Bestätigung.
- Definitiv-Lohnabrechnung: zusätzliche „Akontozahlung"-Zeile.

## 9. Offene Detailfragen / Risiken

**Geklärt (14.05.2026):**
- Negative Restzahlung: durch die Berechtigungs-Filter (Abschnitt 2.1) +
  anteilige Kürzung bei FIX/FIX-M/MTP praktisch ausgeschlossen — keine
  Rückforderungs-/Vortrags-Mechanik nötig, nur ein Sicherheitsnetz-Guard.
- Austritt: kein Akonto bei geplantem Austritt in aktueller/nächster Periode.
- BVG: auf dem vollen Monatslohn gerechnet.
- Rundung: Netto-Akonto auf CHF 10 abrunden.
- QST: nicht im Akonto, nur im Definitivlauf.

**Noch zu verifizieren in Phase 1:**
- **Erkennung Krankheit / Unfall / Mutterschaft:** muss aus den Absenzen-/
  KTG-/UVG-Daten zuverlässig pro Periode ableitbar sein (Absenz-Typen +
  überlappende Zeiträume). Wahrscheinlich vorhanden — gegen die Datenmodelle
  prüfen.
- **Vergessener Akonto-Lauf:** der Definitivlauf muss sauber laufen, wenn
  für einen Monat kein Akonto erfasst wurde (`akonto_bereits_ausbezahlt = 0`).

## 10. Vorgeschlagene Reihenfolge

1. ✅ **Datenmodell + Migration** — erledigt 14.05.2026: Tabellen
   `akonto_termin` + `akonto_zahlung`, Felder
   `payroll_snapshot.akonto_bereits_ausbezahlt` +
   `company_profile.akonto_prozent_fix`, EF-Models + DbContext-Mappings,
   TablePlus-Migration `migrations-archive/add_akonto_lohn_phase1.sql`.
2. **Akonto-Termine-Konfiguration** (Backend + Frontend im Einstellungen-Tab)
   **+ Periodenlogik-Vereinfachung** auf „immer Kalendermonat" — die beiden
   gehören thematisch zusammen, weil die Akonto-Termine die Periodenregel
   ablösen. Die Periodenlogik wurde bewusst aus Phase 1 hierher verschoben:
   sie berührt drei Controller und ist laut CLAUDE.md Bug-Quelle Nr. 1 —
   das wird fokussiert und mit Test-Möglichkeit gemacht, nicht blind.
3. **Akonto-Schätzung + Akonto-Lauf + DTA** (inkl. Pfändungs-Cap).
4. **Definitivlauf-Reconciliation** + Akonto-Zeile auf der Lohnabrechnung.
5. **Edge Cases**: negative Restzahlung, Austritt, vergessener Akonto-Lauf.

---

*Hinweis: Einzelne Backend-Details (genaue Struktur des `PayrollController`,
exakte Feldnamen im `PayrollSnapshot`) werden beim Bau von Phase 1 nochmal
gegen den Code verifiziert.*
