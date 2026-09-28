# Swissdec Quality Tool — Testprotokoll (Muster AG, ELM 6.0)

Angelegt 28.09.2026 (Walter). Hält fest, welche Monatsmeldungen der Muster AG an das
Quality Tool gegangen sind, was es angestrichen hat und **warum wir dort abweichen**.
Für die Zertifizierung und für Rückfragen von Swissdec.

- **Grundsatz:** Wir rechnen aus den Eingabedaten (CSV `Assets/Swissdec/Testmandant/`),
  nicht aus der Soll-XML. Bewusste Abweichungen stehen mit Begründung im
  `docs/swissdec-abweichungsprotokoll.md`; hier wird nur auf sie verwiesen.
- **Quality Tool:** `https://test.swissdec.ch/qualitytool/stable` → ELM → Calculation Test,
  Benutzer `schaub`, Rundungstoleranz Personen und Totale **CHF 0.00**.
- **Nur Kunstdaten** der Testinstanz test.onecrew.ch — nie produktive Daten.

## Ablauf einer Sendung

1. OneCrew (test.onecrew.ch) → Kachel **«ELM-Meldungen»** → Monat wählen → «Meldung erzeugen»
   → «XML herunterladen» (`DeclareMonthlySalary_JJJJ-MM.xml`). Ein Monat ist nur wählbar, wenn
   der Lohnlauf in allen Filialen definitiv abgeschlossen ist.
2. Refapps Transmitter → **«Testdaten»**: Datei hochladen (alte Datei gleichen Namens vorher
   löschen, sonst wird die alte gesendet).
3. Refapps Transmitter → ELM 6.x → **DeclareMonthlySalary**: Endpoint «Qualitytool Stable»,
   Datei wählen, «DeclareMonthlySalary», danach «GetStatusFromDeclareMonthlySalary».
   Kontrolle: es muss eine **neue DeclarationId** erscheinen.
4. Quality Tool → Actions → **Refresh** → Reiter des Monats.

## Läufe

| Lauf | Referenzdaten | Status |
|---|---|---|
| 1 — 27.09.2026 | alter Stand (wie `SWISSCEC/RefXML/` vom 09./10.09.) | am 28.09. archiviert («Edit → Archive Run»), weil Swissdec die Referenz aktualisiert hatte |
| 2 — ab 28.09.2026 | aktueller Stand («New → V6 Run German») | aktiv |

**Folge:** Die lokalen Dateien in `SWISSCEC/RefXML/` sind seit 28.09.2026 **veraltet**
(sie haben z.B. noch alle sechs Arbeitsorte und ungerundete BVG-Beträge). Massgebend ist
das Quality Tool.

## Ergebnisse je Monat

### 2024-11 — erledigt bis auf F1 (bewusst)

| Sendung | DeclarationId | Ergebnis |
|---|---|---|
| Lauf 1, 27.09.2026 23:58 | 18d94c5daae72ae5f | Egli rot, SalaryTotals rot, catchAll gelb |
| Lauf 2, 28.09.2026 18:50 / 18:54 | 18d98a25524f26049 / 18d98a5d74817bd22 | zusätzlich Aebi BVG rot (neue Referenz) |
| Lauf 2, 28.09.2026 19:48 (nach Korrektur K1 + K2) | **18d98d51b4a008876** | **nur noch F1** (Egli + SalaryTotals) |

Endstand (Sendung 19:48):

| Testfall | Feld | Soll (Referenz) | Ist (OneCrew) | Urteil |
|---|---|---:|---:|---|
| TF14 Egli, LU | TaxAtSourceCode | A0N | A0Y | **bewusst — F1** |
| TF14 Egli, LU | TaxAtSource | 34.00 | 36.05 | **bewusst — F1** (Folge des Codes) |
| SalaryTotals LU | TotalTaxAtSource | 34.00 | 36.05 | **bewusst — F1** (Summe aus Egli) |

Alle übrigen Felder aller fünf Personen (TF07 Burri, TF14 Egli, TF16 Aebi, TF37 Oberli,
TF44 Lusser) stimmen mit der Referenz überein.

### 2024-12 — erledigt bis auf F1 (bewusst)

| Sendung | DeclarationId | Ergebnis |
|---|---|---|
| Lauf 2, 28.09.2026 19:56 | 18d98dc0cf2fd3ad1 | Egli rot (aufgeklappt), Aebi, Lusser, Burri, SalaryTotals, catchAll mit Befunden (nicht aufgeklappt); Oberli ohne Request-Zeile |
| Lauf 2, 28.09.2026 20:41 (nach K3/K4) | 18d9903553ea02485 | Egli nur noch `TaxAtSourceSalaries` fehlt (→ K5); ResidenceCategory, Period, Earnings13th grün |
| Lauf 2, 28.09.2026 22:17 (nach K5 + K6) | 18d99577e3f2a09ee | nicht mehr aufgeklappt (Lauf danach im Quality Tool zurückgesetzt) |
| Lauf 2, 28.09.2026 22:27 (Neusendung nach Reset) | **18d995fb49a10029d** | **nur noch F1** (Egli + SalaryTotals) |

Endstand (Sendung 22:27):

| Testfall | Feld | Soll (Referenz) | Ist (OneCrew) | Urteil |
|---|---|---|---:|---|
| TF14 Egli, LU | Correction/Old TaxAtSourceCode | A0N | A0Y | **bewusst — F1** |
| TF14 Egli, LU | Correction/Old TaxAtSource | −34.00 | −36.05 | **bewusst — F1** |
| SalaryTotals LU | CorrectionMonth TotalTaxAtSource | −34.00 | −36.05 | **bewusst — F1** |

Alle übrigen Felder (TF07 Burri, TF14 Egli, TF16 Aebi, TF37 Oberli, TF44 Lusser, catchAll)
stimmen mit der Referenz überein. Beleg-Check 1:1 (CSV + RefXML Monat/Retrospective) für
Egli, Aebi und Burri Dezember grün, abgesehen von F1 und den dokumentierten Rundungen
A4/A5 (SV rappengenau auf dem Beleg) und K2 (BVG Aebi 758.33 Beleg / 758.35 Meldung).

Befunde Egli (TF14, LU) und daraus abgeleitet — die alte lokale Referenz hatte dasselbe,
es war beim Dezember-Beleg-Check nur nicht geprüft worden:

| Feld | Soll | Ist | Ursache → Korrektur |
|---|---|---|---|
| ResidenceCategory | settled-C | annual-B | Bewilligung vom heutigen MA statt vom Meldemonat → **K3** |
| AnnualValues/Period from | 2024-11-01 | 2024-12-01 | Jahreswerte nur aus dem aktuellen Monat → **K4** |
| AnnualValues/Earnings13th | 479.55 | 304.10 | dito (175.45 Nov + 304.10 Dez) → **K4** |
| TaxAtSourceSalaries | Correction Nov: Old A0N −34.00 → New NON, Withdrawal 01.11. settled-C | fehlt | Korrekturmeldung → **K5** (Old bleibt A0Y / −36.05 wegen F1) |

Aus der Referenz abgeleitet (nicht aufgeklappt, gleiche Ursache): Aebi `WithdrawalDate`
2024-12-20, Period until 20.12., SporadicBenefits 3'000 und OtherBenefits 1'250 kumuliert;
Burri `WithdrawalDate` 2024-12-31; Lusser SporadicBenefits kumuliert; Oberli Period from
2024-11-16; SalaryTotals LU `CorrectionMonth` −2'281.65 / −34.00 → **K4** bzw. offen.

### 2025-01 — gesendet, Korrektur K9 offen zum Neusenden

| Sendung | DeclarationId | Ergebnis |
|---|---|---|
| Lauf 2, 28.09.2026 23:36 | 18d999c6f4f5b7a69 | Bosshard, Blanc, Forster, Müller, SalaryTotals BE, catchAll BE rot |

- **Blanc** `HourlyOrLessonSalary` 19.25 statt 19.23, `SocialContributions` −75.50 statt −75.45 → **K9**.
- **Müller** (MEY) `SporadicBenefits` 5'500 zu viel (auch catchAll) → **K9**.
- **Forster** Adresse, A0N/576.00 statt R0N/459.00 — Swissdec-Frage 3 (Testdaten nennen R0N/Varese).
  Folge: SalaryTotals BE `TotalTaxAtSource` 5'188.00 statt 5'305.00 (Differenz 117 = 576 − 459).
- **Bosshard** `FamilyIncomeSupplement` 260.00 statt 268.00. Unsere Testdaten (Export vom
  07.09.2026, Lohnart 3000) und die alte RefXML sagen 260 — das Quality Tool hat die Referenz am
  28.09. geändert. Nicht angleichen; zuerst prüfen, ob Swissdec neue Testdaten veröffentlicht hat.

Lokaler Vergleich vor dem Senden:

Erste Erzeugung (28.09.2026): 7 Schema-Fehler (Vertragsart AWT/Verwaltungsrat im falschen Block,
QST-Wohnsitz Ausland ohne KindOfResidence, Wohnsitzland der Person) → Commit ab89525. Danach
«Schema in Ordnung, 35 Personen, 21 QST-Zeilen, 35 Statistik-Zeilen». Der inhaltliche Vergleich
gegen `RefXML_2025-01_MONTHLY.xml` ergab die Korrekturen **K7** (Programm) und **K8** (Testdaten).

Bleibt bewusst bzw. offen (nicht angleichen):

| Testfall | Feld | Referenz | OneCrew | Urteil |
|---|---|---|---|---|
| TF28 Arbenz, TF34 Rinaldi | Weekly/ZIP-Code | `3008.00` / `6982.00` | `3008` / `6982` | Artefakt der Referenz (PLZ als Zahl exportiert) |
| TF29 Forster | Personenadresse | Via Dogana 4, 20123 Milano | Via Como 12, 21100 Varese | Eingabedaten (CSV) nennen Varese — wir rechnen aus der CSV |
| TF29 Forster | TaxAtSourceCode / TaxAtSource | A0N / 576.00 | R0N / 459.00 | offene Swissdec-Frage 3 (R0N vs A0N), `docs/swissdec-call-2026-09-24.md` |
| alle mit BVG | BVG-LPP-RegularContribution | ungerundet (z.B. −197.16) | auf 5 Rp. (−197.15) | **K2** — lokale Referenz veraltet, Quality Tool rundet |

## Bewusste Abweichungen (Begründung für das Quality Tool)

### F1 — TF14 Egli, Quellensteuer-Code (November 2024)

> Die Eingabedaten (CSV, `PersonTASCode`) geben für Anna Egli im November 2024 den Tarif
> **A0Y** vor. Die Referenz zeigt **A0N**. Wir rechnen aus den Eingabedaten und nicht aus
> der Referenz: A0Y, Kanton LU, steuerbarer Lohn 2'281.65 → Quellensteuer **36.05**
> (Referenz 34.00). Der steuerbare Lohn stimmt überein; nur Tarifcode und damit Satz weichen
> ab. Die Kantonssumme LU weicht ausschliesslich deswegen ab.

Quelle: Abweichungsprotokoll Teil II, **F1**. Status BEWUSST — nicht angleichen.

## Korrekturen aufgrund des Quality Tools

Keine bewussten Abweichungen, sondern Fehler bzw. Vorgaben, die wir übernommen haben.

### K1 — Arbeitsorte: nur Filialen mit Lohn im Monat (28.09.2026)

- **Befund:** catchAll «different child_nodelist_length, expected 9 actual 13» in
  `CompanyDescription`. Die Referenz führt im November nur **#LU** und **#BE**; OneCrew meldete
  alle sechs Filialen der Muster AG (LU, BE, VD, TI, AG, ZG).
- **Entscheid Walter:** «nur senden, was einen Lohn hat».
- **Umsetzung:** Die Monatsmeldung führt nur Arbeitsorte, an denen im Monat jemand Lohn hat
  (`ElmMonthlyDeclarationBuilder.NurArbeitsorteMitLohn`). Die Arbeitszeitmodelle bleiben
  vollständig (so auch die Referenz). Die **Jahresmeldung bleibt bei allen Filialen**, weil
  die Familienausgleichskasse je Arbeitsort adressiert wird, auch ohne Personen
  (ELM-6.0-Richtlinien S. 116).
- Commit 339fad0, Tests `ElmMonatsArbeitsorteTests`.

### K2 — Keine ungerundeten Beträge in der Meldung (28.09.2026)

- **Befund:** TF16 Aebi `BVG-LPP-RegularContribution` Soll **−758.35**, Ist −758.33. Die CSV
  (Lohnart 5050) und die alte Referenz hatten 758.33.
- **Vorgabe:** Swissdec-Berater (via Walter): in der Meldung dürfen keine ungerundeten Beträge
  stehen. Ersetzt die frühere Vorgabe «BVG nicht auf 5 Rappen runden» (21.09.2026).
- **Umsetzung:** Monatsmeldung: BVG je Abzugszeile auf 5 Rappen (wie die Sozialabgaben).
  Jahresmeldung: AHV- und ALV-Löhne je Person auf 5 Rappen, Totale aus den gerundeten Werten.
  **Lohnbelege bleiben rappengenau** (Aebi-Beleg weiterhin 758.33 = Fixbetrag der Kasse).
  Prozentsätze (13. ML 8.33 %, Ferien 13.04 %) bleiben ungerundet, wie in der Referenz.
- Commit 67de599, Tests `ElmBvgRundungTests`.

### K3 — Bewilligung des Meldemonats (28.09.2026)

- **Befund:** Dezember TF14 Egli `ResidenceCategory` Soll settled-C, Ist annual-B.
- **Umsetzung:** Die Monatsmeldung nimmt die Bewilligung aus der Bewilligungs-Historie, die am
  Monatsende galt und bis dahin bekannt war («erfahren am»); ohne Historie die am MA
  (`ElmMonthlyDeclarationBuilder.BewilligungAmStichtag`).

### K4 — Jahreswerte kumuliert, Austritt im Monat (28.09.2026)

- **Befund:** Dezember TF14 Egli Period from / Earnings13th; aus der Referenz ebenso Aebi,
  Burri, Lusser, Oberli.
- **Regel (aus allen Referenzmonaten 11/2024–03/2025 bestätigt):** AnnualValues laufen
  kumuliert ab Beginn der Anstellung im Jahr (spätestens 1. Januar) bis Monatsende bzw.
  Austritt. Ein Wiedereintritt beginnt neu (Aebi: 01.11.–20.12.2024, dann ab 15.01.2025).
  Ein nahtloser Vertragswechsel ist kein Austritt. Fällt das Ende der Anstellung in den
  Monat, steht es als `WithdrawalDate` im Work-Block.
- **Umsetzung:** `Anstellung` (lückenlose Vertragskette), Töpfe der Vormonate derselben
  Filiale aus den definitiv abgeschlossenen Lohnzetteln des Jahres (`Toepfe`).
- Tests `ElmMonatsJahreswerteTests`.

### K5 — Quellensteuer-Korrektur rückwirkend (28.09.2026)

- **Befund:** Dezember TF14 Egli, `TaxAtSourceSalaries` fehlt. Mutation Dezember: settled-C,
  Code NON, Auslöser `AwaitCorrectionFromCompany`, **ohne** neues «Gültig ab» — das bisherige
  (01.11.2024) bleibt. Egli war also ab Eintritt nicht pflichtig; erfahren im Dezember.
- **Daten (Testmandant 4c):** Bei diesem Auslöser ohne mutiertes Gültig-ab wirkt die Änderung
  ab Beginn der Version, die im Vormonat auf dem Beleg stand (`RueckwirkendAb`). Egli danach:
  Bewilligung B ab 01.11. (bis 30.11.) und C ab 01.11., erfahren 01.12.; QST A0Y 01.11.–30.11.
  und «NON» ab 01.11., erfahren 01.12. Überbleibsel früherer Läufe (die Version
  «1.12.–30.11.») werden gelöscht; auch beim normalen Beenden entsteht kein verkehrter Zeitraum mehr.
- **Lohn:** Der Korrekturposten rechnet NON/NOY mit neu = 0 → November alt A0Y 36.05, neu 0,
  **Erstattung 36.05 auf dem Dezember-Beleg** (Zeile «Quellensteuer-Korrektur»).
- **Meldung:** Im Meldemonat verrechnete Posten erscheinen als `Correction` (Old = Gemeldetes
  negativ, New = Nachrechnung; NON als `CategoryPredefined` mit `Withdrawal` + Grund, sonst
  `Mutation` + Grund aus dem Codewechsel). Ohne laufenden Abzug entsteht eine reine
  Korrektur-Zeile (zählt in `NumberOf-TaxAtSourceSalary-Tags`), die Summen erhalten
  `TotalMonth` 0.00 und `CorrectionMonth` je Monat.
- **Verbleibende Differenz:** Old-Block A0Y / −36.05 statt A0N / −34.00, Summe LU
  `CorrectionMonth` −36.05 statt −34.00 — Folge von **F1**, bewusst.
- Tests `QstKorrekturNonTests`.

### K6 — Testmandant 4a überschrieb Austritte (28.09.2026)

- **Befund (lokaler Vergleich vor dem Senden):** Dezember TF16 Aebi `SocialContributions`
  −765.60 statt −726.55, `WithdrawalDate` fehlte bei Aebi (20.12.) und Burri (31.12.).
- **Ursache:** Kein Rechenfehler. Ein wiederholter Lauf von Schritt 4a (für die Statistikfelder)
  setzte das Vertragsende neu: Aebi auf den Vortag des Wiedereintritts (14.01.2025), Burri auf
  leer. Ohne Austritt rechnete die Engine ALV/NBU mit dem vollen Monats-Höchstlohn statt
  anteilig (148'200 ÷ 360 × 50 SV-Tage = 20'583.33 kumuliert).
- **Umsetzung:** 4a behält ein bestehendes Vertragsende, solange es vor dem nächsten Abschnitt
  liegt (`VertragsEndeBeiWiederholung`). Testdaten Aebi/Burri korrigiert, BE + LU ab November
  neu bestätigt. Aebi Dezember danach: ALV 90.57 auf 8'233.33, ALVZ 5.81, NBU 132.23,
  UVGZ 12 5.90 auf 1'161.67 → `SocialContributions` −726.55 = Referenz.
- Commit d844c85, Tests `Testmandant4aVertragsendeTests`.

### K7 — Januar 2025: Inhalt der Monatsmeldung (28.09.2026)

Aus dem lokalen Vergleich gegen `RefXML_2025-01_MONTHLY.xml`, jede Regel an weiteren
Referenzmonaten gegengeprüft. Lohnberechnung unverändert — nur die Meldung.

| Befund (Testfall) | Regel |
|---|---|
| Blanc `SocialContributions` | ~~AHV/IV/EO + ALV als ein Posten auf 5 Rp.~~ — vom Quality Tool widerlegt, ersetzt durch **K9**. |
| Burri erscheint im Januar nicht | Kein StatisticSalary, wenn die Anstellung vor dem Monat endete (Lohn nach Austritt); QST bliebe. |
| Oberli `EntryDate` 2024-11-16 | Beginn der nahtlosen Vertragskette, nicht des Abschnitts im Monat. |
| Bucher, Châtelain, Koller, Maldini, Rinaldi, Roos: Kinder zu früh | Kind nur, wenn der QST-Abzug im Meldemonat schon begonnen hat. |
| Paganini `Allowances` 90 | Lohnarten 1070–1073 (Schicht, Pikett, Nacht, Sonntag) = Zulagen (auch Farine Dez 2025 Pikett 30'500). |
| Paganini `Vacation` 13.04 | Ferien-/Feiertagsprozent aus der Ferienzeile des Lohnzettels (Lohnart 1160/1161), nicht der Filial-Standard. |
| Paganini Lektionen 20 | `TotalHoursAndLessonsOfWork`; Lektionen = Lohnart 1006, ohne Anzahl Betrag ÷ Lektionenansatz. |
| Fankhauser zweites `Contractual13th` | Neues Vertragsfeld «14. Monatslohn» (`employment.vierzehnter_monatslohn`, Schema-Stand 38). |
| Hasler `LeaveEntitlement` 0, `Unsteady` | Verwaltungsrat: keine Ferientage, keine feste Arbeitszeit. |
| Herz, Blanc, Lamon `Steady` | Stundenlohn **mit vereinbarten Wochenstunden** = Steady (Grad = Wochenstunden ÷ Modell). FLEX aus easy@work (ohne Wochenstunden) bleibt Unsteady. |
| Estermann, Hasler, Rinaldi `doctorate`; Koller, Paganini `universityBachelor` | Ausbildung aus LSE-Ausbildung + LSE-Hochschultitel (1 Doktorat, 2 Master, 3 Bachelor). |
| Arnold, Meier C., Hasler, Müller `ResidenceCategory` | Katalog: `B_EU_EFTA` usw. zählen als Buchstabe; neu MV90, MV120, ANDERE (Schema-Stand 38). |
| Andrey, Arbenz, Arnold, Binggeli, Blanc, Forster, Meier C. `OtherActivities` | Aus «weitere Beschäftigungen» der QST-Erfassung: Monatslohn = Pensum laut Vertrag, Stundenlohn = Stunden ÷ Monats-Vollzeit (Rundung korrigiert in **K9**), plus Gesamtpensum anderswo. |
| Oberli QST `SporadicBenefits` 2'000 | QST-pflichtige Lohnarten mit «einmalig» (Bonus, Sonderzulage, VR-Honorar, Nachzahlungen), ohne 13./14. ML. Ausnahme Lohnausweis Ziffer 4/5 → **K9**. |
| Blanc, Utzinger `MarriagePartner` | Bei Tarif B, C, T: Ehepartner mit AHV-Nr. oder unknown, eigene Adresse sonst die des MA, Wohnsitz, bei Erwerbstätigkeit Arbeitskanton + Beginn. |

Tests `ElmMonatJanuar2025Tests`.

### K8 — Testmandant: Vertragskopien und Stammdaten (28.09.2026)

- **Befund:** Aebi `NoTimeConstraint` fehlte im Januar (Monthly gemeldet). Folgeverträge aus 4c/5b
  (Wiedereintritt, Lohnänderung) erbten die Swissdec-Vertragsart nicht.
- **Umsetzung:** Alle Vertragskopien übernehmen Vertragsart, Jahreslohn ohne Zeitbindung und
  14. ML; 4c führt die Vertragsart nach, wenn die Mutation sie nennt. 4a setzt Hochschultitel,
  14. ML, Bewilligung MV90/MV120/ANDERE und eine eigene Partner-Adresse (Blanc Riehen BS; bei
  gleicher Adresse «im Haushalt», Utzinger).
- **Testdaten:** bestehende Muster-AG-Daten auf der Testinstanz nach dem Deploy nachgeführt.

### K9 — Januar 2025: Befunde des Quality Tools (28.09.2026)

Drei Regeln aus K7 stammten aus der alten RefXML; das Quality Tool rechnet anders. Nur die
Meldung, Lohnberechnung unverändert.

| Befund (Testfall) | Regel neu |
|---|---|
| Blanc `SocialContributions` −75.45 | AHV/IV/EO, ALV, ALVZ, NBU **je einzeln** auf 5 Rp., dann summiert (−62.51 → −62.50, −12.97 → −12.95). |
| Blanc `HourlyOrLessonSalary` 19.23 | Stunden ÷ Monats-Vollzeit × 100 auf **zwei Stellen** (35 ÷ 182), nicht auf 0.05. |
| Müller MEY ohne `SporadicBenefits` | Kapitalleistung (1410, Lohnausweis Ziffer 4) und Beteiligungsrechte (1960–1969, Ziffer 5) zählen nicht: QST-SB-aperiodisch folgt Ziffer 3 (Richtlinie ELM 6.0, Kap. 10.6.1, Tabelle QST-SB-aperiodisch). Gegengeprüft: Müller 1960 Jan–Apr in keiner RefXML mit SporadicBenefits; Hasler VR-Honorar April 10'000 bleibt drin. |

Tests `ElmMonatJanuar2025Tests`.

### K10 — Neue Swissdec-Testdaten, Stand 28.09.2026 (23:50)

Walter hat die CSV-Exporte neu heruntergeladen (vorher Stand 07.09.2026). Swissdec hat die Testdaten
an die Referenz angepasst — damit erledigen sich mehrere offene Fragen:

| Testfall | Änderung | Folge |
|---|---|---|
| TF29 Forster | Milano, Via Dogana 4, Tarif **A0N** statt R0N/Varese | Swissdec-Frage 3 erledigt |
| TF14 Egli | Tarif **A0N** statt A0Y ab 01.11.2024, NON ab Dezember; Konfession ab März 2025 «keine» | F1 sollte wegfallen |
| TF36 Maldini | **T0N/T1N** statt F0N/F1N, Partner-Wegzug nach Como schon ab September, QST-Gültig-ab je Monat | Frage 3b erledigt |
| TF25 Lehmann | QST-Wechsel TI → LU (A0Y, Gemeinde 1061, Mai 1062) schon **ab April** | Frage 4 erledigt |
| TF26 Jenzer | Como, Grenzgänger G statt Milano | — |
| TF11 Bosshard | Kinderzulage 268/483 statt 260/475; Kind 2 (Kevin) als Mutation März 2025 statt Stammdaten; BVG-Basis manuell 153'200 im April | Quality-Tool-Befund Jan 2025 erledigt |
| TF13 Combertaldi, TF22 Bucher | Kind erst ab Geburt (Mutation) statt ab Beginn | — |
| alle mit BVG | Lohnart 5050 auf 5 Rappen | Lohnberechnung: BVG auf 5 Rp. (`docs/claude/fachlogik.md`) |
| Firma | FAK-Ansätze 2025 (LU 215/260/268, BE 250/310, VD 322/365/425/468, TI 215/268, AG 2026 225/278); Arbeitsorte VD/TI/AG/ZG erst ab 2025, ZG 2026 leer | Schritt 3 neu laufen lassen |

Programm: Schritt 4c übernimmt jetzt Kind 1–5 (vorher nur Kind 1). CSV in `Assets/Swissdec/Testmandant`
und `SWISSCEC/Testmandant` ersetzt.

## Offen

- **Muster AG neu aufbauen** (Walter-Entscheid 28.09.2026): Schritte 1–5 mit den neuen CSV, danach
  Test-DB auf Reste der alten Daten prüfen (Maldini F-Codes, Egli A0Y, Lehmann Mai), dann
  November 2024 ff. neu bestätigen und senden.
- **Februar 2025 ff.:** alle Monate neu bestätigen (5c hat alles zurückgesetzt) und senden. Beim Aufklappen immer die **neueste** Request-Zeile prüfen. Im Quality Tool
  nie «Edit → Reset» (löscht die Sendungen des Laufs); Refresh steht rechts unter ACTIONS.
- **Jahresmeldung (YearlyRetrospective), Beobachtung:** Referenz TF16 Aebi 2024
  `AHV-ALV-NBUV-AVS-AC-AANP-Contribution` 1'750.55 = Beiträge auf der **Jahresbasis**, je Zeile
  auf 5 Rp. (AHV 1'184.75 + ALV 226.40 + ALVZ 8.85 + NBU 330.55). Summe der Monate wäre
  1'750.60. Beim Bau der Jahresmeldung so rechnen.

- **Fragenliste an Swissdec** (`docs/swissdec-call-2026-09-24.md`, Teil A, 7 Fragen) — noch
  nicht besprochen, noch nicht gemailt (Stand 28.09.2026). Betrifft Monate ab 2025, nicht
  November 2024.
- Weitere Teilprozesse des Laufs (YearlyRetrospective, EMA, YearlyProspective) sind noch nicht
  gesendet.
