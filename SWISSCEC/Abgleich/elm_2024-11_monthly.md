# ELM-Monatsmeldung November 2024 — Abgleich mit der Referenz

Referenz: `SWISSCEC/RefXML/RefXML_202411_MONTHLY.xml` · Stand 27.09.2026

**Ergebnis: deckungsgleich bis auf F1** (Egli QST-Code, bewusste Abweichung).

## Kopf und Firma

| | OneCrew | Referenz | |
|---|---|---|---|
| MonitoringID | `schaub` | — (Referenz ist von Swissdec erzeugt) | ✓ gewollt |
| Empfänger | QST-BE, QST-LU, Statistic | gleich | ✓ |
| UID | CHE-999.999.996 | gleich | ✓ |
| Workplaces | 6, inkl. ZG 6300 / A38197423 | gleich | ✓ |
| Arbeitszeitmodelle | 42 h · 40 h · 21 Lekt. · 20 h + 10 Lekt. | gleich | ✓ |
| Kontaktperson | Hans Muster · MusterAG@xxxxx.ch · 041 218 65 32 | gleich | ✓ |
| Institutions | BE 9217.8 · LU 158.87.6 | gleich | ✓ |

## Personen

| Nr. | Ausbildung | Stellung | Ferientage | Eintritt | Arbeitszeit | Vertragsart | Brutto | Sozialabgaben | BVG | |
|---|---|---|---|---:|---|---|---:|---:|---:|---|
| 7 Burri | teacherCertificate | noCadre | 30 | 01.11.2024 | 40.00 h, Modell 2 | indefiniteSalaryMth | 8'000.00 | −640.50 | −640.00 | ✓ |
| 14 Egli | enterpriseEducation | lowestCadre | 0 | 01.11.2024 | Unsteady | fixedSalaryHrs | 2'106.20 | −36.65 | 0.00 | ✓ |
| 16 Aebi | universityEntranceCertificate | middleCadre | 30 | 01.11.2024 | Unsteady | NoTimeConstraint | 9'458.35 | −1'024.05 | −758.33 | ✓ |
| 37 Oberli | enterpriseEducation | noCadre | 20 | 16.11.2024 | 40.00 h, Modell 2 | indefiniteSalaryMth | 5'000.00 | −400.30 | −800.00 | ✓ |
| 44 Lusser | vocEducationCompl | noCadre | 30 | 01.11.2024 | 42.00 h, Modell 1 | indefiniteSalaryMth | 5'500.00 | −440.35 | −385.00 | ✓ |

Weiter geprüft und gleich: Egli 13. ML 175.45 in `Earnings13th` (nicht im Brutto), Spesen gar nicht
gemeldet · Aebi 3'000 in `SporadicBenefits` und 500 in `OtherBenefits` · Oberli Kinderzulage 250 in
`FamilyIncomeSupplement`, Periodenbeginn 16.11. · Egli `annual-B`, Oberli `shortTerm-L` ·
Oberli Konkubinat mit alleinigem Sorgerecht und Kind Eliane Rossel (18.06.2015, Anspruch
01.07.2015–30.06.2033), Konfession jewishCommunity · QST Oberli H1N 616.35, Summe BE 5'250 → 616.35.

## Einzige Abweichung: F1

| | OneCrew | Referenz |
|---|---|---|
| Egli QST-Code | **A0Y** | A0N |
| Egli QST-Betrag | **36.05** | 34.00 |
| Summe LU | **2'281.65 → 36.05** | 2'281.65 → 34.00 |

Bewusste Abweichung (F1 im Abweichungsprotokoll) — die Kirchensteuer-Ziffer folgt der Konfession
aus den Stammdaten, nicht der Referenz. Der Bemessungslohn stimmt (2'281.65); nur die Ziffer und
damit der Satz weichen ab. **Nicht angleichen.**
