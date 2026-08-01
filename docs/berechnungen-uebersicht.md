# Berechnungsübersicht — HR-System Schaub Restaurants GmbH

Vollständige Zusammenfassung aller Lohn-, Saldo-, SV-, QST-, Akonto- und Fibu-Berechnungen.

**PDF:** [`Berechnungen-Uebersicht.pdf`](./Berechnungen-Uebersicht.pdf)  
**Erzeugen:** `python3 scripts/generate_berechnungen_pdf.py` (benötigt `reportlab`)

Stand: August 2026 · Quelle der Wahrheit = Code in `PayrollCalculationEngine` / `PayrollCalculations`.

---

## 1. Architektur

```
Controllers/PayrollController.cs          → nur HTTP (dünn)
Services/PayrollCalculationEngine.cs      → Orchestrierung (FLEX/MTP/FIX)
Services/PayrollCalculationService.cs     → static Helfer (BuildResult, Round05, …)
```

Geldbeträge schreibt nur `/api/payroll/confirm` (server-autoritativ via `Calculate`).

## 2. Auszahlungsmatrix

| Modell | Ferien | Feiertag | 13. ML |
|---|---|---|---|
| **FLEX** | Saldo CHF — Auszahlung bei Bezug | monatlich | monatlich |
| **MTP** | Saldo Tage + CHF-Pott bei Bezug | monatlich | Saldo; nur Payout-Monate |
| **FIX / FIX-M** | Saldo Tage | Saldo Tage | Saldo CHF; nur Payout-Monate |

**Drei Tagessätze nie mischen:**

- Ferien MTP (Kürzung): `guaranteedH × Stundenlohn / 7`
- Ferien FIX/FIX-M: `Monatslohn × 12 / 365`
- Krank/Unfall: `KtgTagessatzService`

## 3. Grundlagen

- Periode = immer Kalendermonat
- `Round05(x) = Round(x/0.05)×0.05` — nur Netto + Auszahlung
- Kurzperiode FIX: `Monatslohn × 12/365 × Tage`
- Kurzperiode MTP: `guaranteedH / 7 × Tage`

## 4–6. Stempel, Absenzen, Ferien-/Feiertag-Tage

- `nightBonus = nightHours × 0.10` (Zeit, kein CHF)
- Absenz-Stunden: `Tage × weeklyH / divisor × %` (divisor 7 oder 5)
- Ferien-Accrual: `(vacationWeeks × 7) / 12`
- Feiertag-Accrual (FIX): `+0.5 Tag/Monat`
- Art. 329b: Zwölftel ab Schwelle 60/30/90 — nur bei Confirm-Bool

## 7–10. Modelle

Siehe PDF-Kapitel 7–10 für die vollständigen Formeln FLEX / Ferien-Pott / MTP / FIX.

**Ferien-Geld-Pott (FLEX + MTP):**

```
Pott CHF  = Vormonat + Accrual
Pott Tage = Vormonat Tage + Accrual Tage
Auszahlung = (PottCHF/PottTage) × bezogen   Cap = Pott CHF
```

**MTP Festlohn:** Soll = `H/7 × Periodentage`, gekürzt um Ferien×H/7 + Krank/Unfall-Werktage×H/5.

## 11–15. 13. ML, KTG, SV, QST, Netto

- 13. ML: FLEX monatlich; MTP/FIX nur Payout-Monate; Probezeit blockt
- KTG: Regel A (&lt;4 Perioden) vs. Regel B (Ø SvBasisAhv); 88% / 80%
- SV: BVG Schwelle→Min→Max; ALV/NBU Cap 12'350 + Dezember-Jahresausgleich; BVG Cap flach 5'355
- QST: ESTV Variante A/B1/B2/B3; Mindestbetrag (z.B. LU 13)
- Netto = Round05(Lohn+Abzüge); Abtretung; Bank-Split; Akonto-Verrechnung

## 16–20. Akonto, Mindestlohn, LGAV, FamZ, Fibu

- Akonto 6 Regeln (Eligibility + %-Sätze, Floor CHF 10)
- Mindestlohn: FIX vs. Monatslohn, FLEX/MTP vs. Stundenlohn; + kommunaler Floor
- LGAV: 1×/Jahr Code 600.24 (voll/reduziert nach Modell)
- FamZ ausserhalb Snapshot-Netto / Journal-1920
- RST: FIX Tage×Tagessatz; FLEX/MTP CHF-Delta

## 21–22. Fristen & Monatsblatt

- Probezeit blockt 13. ML; Sperrfrist AU OR; Mutterschutz ET−280 / Geburt+16 Wo
- Monatsblatt: Ist/Bezug/Nacht aus Snapshot; Import Codes 901/904/903/902

## Wichtige Dateien

- `Services/PayrollCalculationEngine.cs`
- `Services/PayrollCalculationService.cs`
- `Services/FerienAuszahlungService.cs`
- `Services/AkontoLaufService.cs`
- `Services/KtgTagessatzService.cs`
- `Services/KarenzService.cs`
- `Services/MinimumWageCheckService.cs`
- `Services/QuellensteuerTarifService.cs`
- `Services/LgavBeitragService.cs`
- `Services/FibuJournalService.cs`
- `Services/StundenkontrollePdfService.cs`
- `Services/FerienKuerzungService.cs`
