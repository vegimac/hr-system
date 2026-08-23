# OneCrew — Lohn-Formelwerk (Referenz)

**Stand 18.08.2026 · Schritt 1 der Phase 3 (Walter-Vorgabe: Formeln bleiben im Code, hier sauber dokumentiert).**
Dieses Dokument beschreibt JEDE Rechenformel des Lohnlaufs: was gerechnet wird, wie, und wo im Code.
Was hier steht, ist die Referenz — weicht der Code ab, ist eines von beidem ein Bug.

**Daten vs. Code (Grundprinzip):**
*Daten* steuern WAS auf dem Lohnblatt erscheint und WOHIN es zählt: Lohnpositions-Katalog
(SV-Flags AHV/NBU/KTG/BVG/QST/13.ML), Lohnschema pro Vertragsmodell, Absenz-Typ-Matrix, SV-Sätze,
Mindestlöhne, FZ-Tarife. *Code* rechnet WIE: alle Formeln unten. Die Basen-Kontrolle
(`SchattenBasenService`, Kachel «Lohnraster ELM») beweist laufend, dass Flags und Engine dieselben
SV-Basen ergeben.

---

## 0. Code-Landkarte

| Datei | Verantwortung |
|---|---|
| `Services/PayrollCalculationEngine.cs` | Orchestrierung: Daten laden, FLEX/MTP/FIX-Zweige, EO, Dezember-Ausgleich |
| `Services/PayrollCalculationService.cs` | Reine statische Rechen-Helfer (`BuildResult` = SV/QST-Engine, `CalcFerienGeld`, `Round05` …) |
| `Services/KtgTagessatzService.cs` | KTG/UVG-Tagessatz (Krankheit/Unfall/EO-Basis) |
| `Services/FerienAuszahlungService.cs` | Ferien-Pott-Auszahlung im Akonto |
| `Services/AkontoLaufService.cs` | Akonto-Beträge (6-Regel-Werk) |
| `Services/LgavBeitragService.cs` | L-GAV-Jahresbeitrag (Code 600.24) |
| `Services/SchattenBasenService.cs` | Kontroll-Rechnung der SV-Basen rein aus Katalog-Flags |
| `Services/FibuJournalService.cs` | Buchungen + Rückstellungs-Tagessätze (muss mit Engine gleichlauten) |

Rundung überall: **`Round05`** = kaufmännisch auf 5 Rappen (Lohnzettel-Beträge).

---

## 1. Grundgerüst

- **Lohnperiode = immer Kalendermonat** (1. bis letzter Tag). `CalcPeriod(year, month)`.
- **Kurzperiode** (Ein-/Austritt im Monat): alle «pro Monat»-Grössen anteilig über
  `shortPeriodDays / Periodentage` — dieselben Formeln, nur mit der kürzeren Tagesspanne.
- **Vertragsmodelle:** FLEX (Stundenlohn), MTP (garantierte Stunden), FIX (Festpensum Crew),
  FIX-M (Management-Festlohn). Lohnbasis: FLEX/MTP = `HourlyRate`; FIX/FIX-M =
  `MonthlySalary ?? MonthlySalaryFte × Pensum`.

## 2. Stundenlohn-Teil (FLEX, MTP)

```
Grundlohn      = gestempelte Stunden × HourlyRate            (Code 20/22)
Nachtzuschlag  = Nachtstunden gemäss Filial-Nachtfenster     (Saldo, Anzeige alle Modelle)
```

**MTP-Festlohn (pro-rata, Walter 30.05.2026):**
```
Soll-Stunden   = garantierte WoStd / 7 × Periodentage        (31 Tage > 28 Tage!)
Festlohn 10.1  = Soll-Stunden × HourlyRate
Mehrstunden    = gestempelt über Soll → Code 55.3 (MTP-Zusatzstunden)
```
Vom Soll werden abgezogen (nur Stunden, Lohnersatz separat):
- **Ferien:** `WoStd / 7` pro Ferien-KALENDERtag (Sa+So zählen mit)
- **Krank/Unfall:** `WoStd / 5` pro «hätte gearbeitet»-Tag laut Dienstplan-Auswahl
  (`absence.worked_days`); ohne Auswahl: Mo–Fr-Pauschale
- **EO Mutter-/Vaterschaft:** Divisor aus Absenz-Typ-Katalog (`GutschriftModus` 1/7 → ÷7
  alle Kalendertage, 1/5 → ÷5), Default 1/7
- **Cap:** Festlohn nie < 0 (`Math.Max(0, …)`) — bei voll abgedeckter Periode 0.00

**Anzeige 100 %:** Vertrags-Card «Garantiert/Monat» = `WoStd × HourlyRate × 52/12`
(nur Anzeige/Vertragstext — NICHT Periodenlohn).

## 3. Ferien

**Accrual (monatlich):**
```
FLEX/MTP: Ferienentschädigung = Brutto-Basis × Ferien-%     (195.1/195.3 FLEX, 195.5/195.6 MTP)
          Ferien-% : 5 Wochen = Filiale (Standard 10.65 %) · 6 Wochen = 13.04 %
          Engine wählt 6-Wochen-Variante bei vacationPct ≥ 13
FIX/FIX-M: keine CHF-Akkumulation — Ferien-TAGE-Saldo (Accrual gemäss Anspruch)
```

**Bezug (FLEX + MTP, Pott-Prinzip — Walter 09.05./01.08.2026):**
```
Pott CHF   = Vormonats-Feriengeld + Ferienentschädigung dieses Monats
Pott Tage  = Vormonats-Tagessaldo + Tage-Accrual dieses Monats
Tagessatz  = Pott CHF / Pott Tage
Auszahlung = Tagessatz × bezogene Tage        (Code 40.1; Cap = Pott CHF, KEIN Vorbezug!)
Saldo neu  = Pott − Auszahlung (CHF und Tage)
```
Bewusste Mirus-Abweichung: Mirus lässt den Ferien-CHF-Saldo ins Minus laufen — OneCrew nicht.
Grosse Brutto-Differenzen im Parallelvergleich bei vielen Ferientagen sind ERWARTET.

**Ferien-Tagessatz je Modell (für Kürzung/Bewertung — Walter 26.05.2026, ABSOLUT):**
```
MTP       : WoStd × HourlyRate / 7            (konsistent zur 1/7-Gutschrift)
FIX/FIX-M : Monatslohn × 12 / 365             (identisch in FibuJournalService!)
FLEX      : kein fester Satz — Pott-Tagessatz
```
Krankheit/Unfall ist AUSGENOMMEN → eigener KtgTagessatzService (Abschnitt 6).

**FIX-Split beim Bezug:** Festlohn wird aufgeteilt in 10.1 Festlohn ·
10.2 Festlohn für bezogene Ferien · 10.3 Festlohn für bezogene Feiertage (Summe unverändert).

## 4. Feiertage

```
FLEX      : monatliche Feiertagentschädigung = Basis × Filial-%     (195.2, ausbezahlt)
MTP       : Feiertagentschädigung 2.27 %                            (195.4, ausbezahlt)
FIX/FIX-M : Feiertag-TAGE-Saldo (Accrual/Bezug), keine Auszahlung — Bewertung
            beim Bezug über 10.3 (Split), Tagessatz = Monatslohn × 12/365
```
Ausbezahlte Feiertag-Stunden (Austritt/Sonderfall) = Code 50.1.

## 5. 13. Monatslohn

```
Aufschlag  = 8.33 % (= 1/12)
FLEX      : monatlich ausbezahlt (Basis × 8.33 %)                   (180.1 monatlich)
MTP/FIX/FIX-M : monatlich in den CHF-Saldo (180.1 Saldo-Akkumulation),
            Auszahlung NUR in konfigurierten Monaten (Filiale, «13. ML-Raster»)
SV        : bei FLEX monatlich verbeitragt; bei Saldo-Modellen im Auszahlungsmonat
            (korrekt für AHV/ALV; BVG-Monats-Cap-Thema → Dezember-Klärung offen)
```

## 6. Krankheit / Unfall (KTG/UVG)

**Tagessatz-Basis (`KtgTagessatzService`, NICHT der Ferien-Tagessatz!):**
```
StdLohn_brutto = HourlyRate × (1 + Ferien-%) × (1 + 8.33 %)
Regel A (Vertrag ≤ 4 abgeschlossene Perioden):
    FIX/FIX-M : MonthlySalary × 12 / 365
    MTP       : WoStd_garantiert × StdLohn_brutto × 52 / 365
    FLEX      : max_part_time_hours × StdLohn_brutto × 52 / 365
Regel B (≥ 4 Perioden): Ø der effektiven SV-Basen (AHV) der letzten ≤ 12 Monate × 12 / 365
    MTP: Garantie-Anteil wie A + Ø-Überschuss der letzten Perioden
Manueller Tagessatz (emp.KtgTagessatzManuell) übersteuert alles.

Tagessatz88 = Tagessatz100 × 0.88     (Karenzentschädigung, Codes 70.1 Krank / 60.2 Unfall)
Tagessatz80 = Tagessatz100 × 0.80     (Taggeld nach Karenz, Codes 70.2 / 60.3)
```
- Gezahlt wird auf **Kalendertagen** (Versicherung zahlt auch Sa+So).
- **MTP:** Festlohn-Kürzung statt Korrektur-Zeile (Stunden ÷5 laut Dienstplan, Abschnitt 2).
- **FIX/FIX-M:** Korrektur-Modell — Monatslohn läuft weiter, Korrektur Codes 75.1 (Krank) /
  65.1 (Unfall) negativ, Taggeld positiv.
- Karenz-Tage gemäss Filial-Konfiguration (`KarenzService`); Taggeld 80 % ist zugleich
  der Meldebetrag an die Versicherung.

## 7. EO Mutterschaft / Vaterschaft (Walter 17.08.2026)

```
EO-Taggeld = min(Tagessatz100 × 0.80, 220.00) × KALENDERtage   (120.1 Mutter / 120.2 Vater)
             (Art. 16f EOG: 80 %, Deckel CHF 220/Tag, 7 Taggelder/Woche)
MTP       : Soll-Kürzung (Divisor aus Absenz-Typ-Katalog, Default 1/7) — KEINE Korrektur-Zeile
FIX/FIX-M : negative Korrektur 125.1/125.2 = voller Tagessatz × Tage (Monatslohn läuft weiter)
FLEX      : keine Kürzung (kein Soll)
```
EO-Absenzen sind im Stunden-Verteiler NEUTRAL (wie unbezahlter Urlaub) — sonst Doppelzahlung.
SV-Behandlung der EO-Zeilen komplett über Katalog-Flags (EO ist UVG-frei etc.).

## 7b. Militär / Zivildienst / Zivilschutz (L-GAV Art. 28)

```
Stufen pro ARBEITSJAHR (ab Eintrittsdatum, gemeinsamer Tage-Topf, RS zählt gleich):
  Diensttag 1–25          : 100 % Bruttolohn        (80.1 Militär / 90.1 Zivilschutz)
  Tag 26 … Berner Skala   : 88 % des Lohnes         (80.2 / 90.2)
  danach                  : EO-Entschädigung 80 %   (80.3 / 90.3)
Die 25 100%-Tage zählen in die 324a-Frist HINEIN (L-GAV-Kommentar).
Berner Skala (Dienstjahr): 1 → 21 Tg · 2 → 30 · 3–4 → 60 · 5–9 → 90 · 10–14 → 120 · 15–19 → 150 · 20+ → 180
Tagessatz  = KtgTagessatzService (Tagessatz100)
Tages-Caps (Referenz-Lohnarten): 100 % max 275.– · 88 %/80 % max 245.–
Diensttage = KALENDERtage (Dienst läuft Sa+So durch)

MTP       : Soll-Kürzung 1/7 (Divisor aus Katalog) + Stufen-Zeilen
FIX/FIX-M : Tag 1–25 keine Zeilen (Monatslohn = 100 % implizit); ab Tag 26
            NEGATIVE Korrektur 85.1/95.1 (voller Tagessatz) + 88/80-Zeilen
FLEX      : nur Stufen-Zeilen, keine Kürzung
```
SV-Flags aus dem Raster: AHV/QST/KTG/BVG ja, **UVG nein** (Taggeld UND Korrektur → NBU läuft bewusst auf dem vollen Monatslohn weiter, Swissdec-Standard, Walter 19.08.2026), 13. ML nur auf der 100 %-Zeile.
Bewusste Abweichung: «BVG auf 100 % rechnen» (Referenz) nicht übernommen — Basen flag-rein.

## 8. SV-Abzüge (BuildResult — die Abzugs-Engine)

Pro SV-Satz (Tabelle `social_insurance_rate`, versioniert, Filial-Abweichungen möglich):
```
Basis      = Summe aller Lohnzeilen mit passendem Flag (AHV-/NBU-/KTG-/BVG-Basis)
Freibetrag : Basis = max(0, Basis − FreibetragMonthly)          (z.B. AHV-Rentner 1'400/Mt.)
Cap ALV/NBU: Basis = min(Basis, 12'350/Mt.)                     (= 148'200/Jahr)
AN-Abzug   = Basis × Satz AN % · AG-Beitrag = dieselbe Basis × Satz AG % (agBetrag, für Fibu)
```

**BVG (Reihenfolge: Schwelle → Min → Max):**
```
Koordinierte Basis = Brutto-BVG-Basis − Koordinationsabzug (2'205)
Eintrittsschwelle  : Monatsbasis × 12 < 22'680  → nicht versichert (Basis 0)
Minimum            : versichert & Basis < 315   → Basis = 315
Maximum (flach)    : Basis = min(Basis, 5'355)  — FLACHER Monats-Cap, kein Jahresausgleich
BVG_ZUSATZ (Kader) : rechnet auf dem Koordinationsabzug selbst (2'205 fix)
BVG-Staffeln       : alters-/modellabhängige Zeilen (MinAge/MaxAge) — Engine wählt die Stufe
```

**FAK (nur Arbeitgeber):** `FAK = Satz AG (1.635 %) × AHV-Basis` — kein AN-Abzug,
AG-only-Zeilen (Rate 0) erscheinen nie auf dem Lohnzettel.

**Dezember-Jahresausgleich ALV/NBU (Aufrollverfahren):**
```
jahresPflichtig    = min(Σ SvBasisAhv Jan–Nov (alle Filialen!) + Dez-Basis, 148'200)
bereitsVerbeitragt = Σ min(SvBasisAhv_Monat, 12'350)
Dez-Basis          = max(0, jahresPflichtig − bereitsVerbeitragt)
```
→ Dezember-Bonus wird exakt auf den Jahres-Höchstlohn verbeitragt; Zeile trägt
den Zusatz «(Jahresausgleich)».

**L-GAV-Beitrag:** Code 600.24, Jahresbeitrag einmal pro MA/Jahr (`LgavBeitragService.EnsureAsync`,
idempotent, nur in Commit-Pfaden persistiert).

## 9. Quellensteuer (QST)

```
QST-pflichtig  : ausser CH-Bürger, C-Ausweis, Behörden-Befreiung, Ehe mit CH/C (QstPflichtCheckService)
Basis          = Summe der Zeilen mit QST-Flag
Tarifcode      = Tarif (A/B/C/…) + Kinderzahl + Kirchensteuer J/N   (z.B. C2N)
Betrag         = Basis × kantonaler Tarifsatz (QuellensteuerTarifService, Kreisschreiben 45)
Mindestbetrag  : kantonal (z.B. LU 13.–) — greift VOR dem 0-Return
Mehrere Arbeitgeber (Variante B): satzbestimmende Hochrechnung B1/B2/B3

Kurzmonat (Ein-/Austritt unter dem Monat, KS 45 Monatsmodell — 21.08.2026):
  Besteuert wird der IST-Betrag, satzbestimmend zählt der volle Monat.
  Nur der PERIODISCHE Kern wird hochgerechnet, aperiodische Teile
  (13. ML, Schlussabrechnung, Zulagen) ohne Hochrechnung:
    FIX/FIX-M : Satzbasis = IST − Kurz-Monatslohn + voller Monatslohn
    MTP       : Satzbasis = IST + WoStd/7 × (Monatstage − Kurztage) × Stundenlohn
                            × (1 + Feiertags-%)          (Walter 23.08.2026:
                Feiertagsentschädigung ist beim MTP monatlich ausbezahlter,
                periodischer Lohn → gehört in den Vollmonats-Satz. Feriengeld
                + 13. bewusst NICHT — Pott-Modell, fliessen auch im Vollmonat
                nicht zu; sie heben Satz+Steuer erst im Bezugs-/
                Auszahlungsmonat.)
    FLEX      : keine Hochrechnung (Stundenlohn, Variante A: IST zählt)
  Bei gleichzeitigem Nebenjob (Variante B) gilt der HÖHERE der beiden Sätze.
  Schutz: satzbestimmend nie unter IST-Brutto.

Korrekturlohn: bewusst KEIN Auto-QST — Nachzahlungen nach Austritt brauchen
  eine manuelle QST-Korrektur-Position (z.B. 565).
```

## 10. Netto & Auszahlung

```
Netto             = Round05(Σ Lohnzeilen + Σ Abzüge)            (Abzüge negativ)
Auszahlungsbetrag = Netto + Familienzulagen/Spesen (zulagenExtra) − Akonto-Vorauszahlung
```
Familienzulagen liegen bewusst AUSSERHALB des Netto (Fibu bucht sie separat).
Uniformen-Depot: CHF 50 Abzug beim 1. Lohn; Rückgabe/Verfall beim Austritt.

## 11. Akonto (6-Regel-Werk, Walter 16.05.2026)

```
1  Kein Akonto wenn Vertragsende ≤ Periodenende
2  Kein Akonto bei Krank/Unfall/Mutterschaft AM Stichtag
3  FIX   : AkontoProzentFix (Filiale, Std. 80 %) × Definitiv-Auszahlung, abgerundet auf CHF 10
4  FIX-M : wie 3
5  MTP   : AkontoProzentHourly (Std. 100 %) × (Stunden bis Stichtag × Satz + Ferien-Pott − SV)
6  FLEX  : wie 5
Ferien-Pott im Akonto: nur Bezüge mit DateTo ≤ Stichtag; Rest im Definitivlauf.
```

## 12. Schluss- und Austrittsrechnung

Beim letzten Lohn werden alle Saldi ausbezahlt/verrechnet: Zeitsaldo 55.2 (auch Minusstunden),
Nacht-Kompensation 55.10, ausbezahlte Ferientage 40.1, Feiertag-Stunden 50.1,
13.-ML-Saldo 180.1 — SV-Abzüge auf den Auszahlungsbeträgen (Abschnitt 8).

---

## Anhang: bewusste Eigenheiten (nicht «fixen»)

1. Kein Ferien-Vorbezug (Pott-Cap) — Mirus-Abweichung, gleicht sich übers Jahr aus.
2. BVG-Monats-Cap statt Jahresverteilung des 13. ML — Dezember-Thema, bewusst offen.
3. BVG-Eintrittsschwelle als Monats-Proxy (×12) — wie Mirus «Abgrenzung monatlich».
4. FLEX ohne Stunden = Brutto 0.00 ist legitim (kein Fehler); DTA filtert Beträge ≤ 0.
5. MTP-Anzeige «Garantiert/Monat» (52/12) ≠ Periodenlohn (pro-rata) — Anzeige vs. Rechnung.
