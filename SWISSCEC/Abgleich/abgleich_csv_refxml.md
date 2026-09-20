# Abgleich Swissdec-Testmandant: CSV (Eingabe) ↔ RefXML (Soll)

Quelle Eingabe: `SWISSCEC/Testmandant/wagetypes_export.csv` mit Flags aus `Assets/Swissdec/SwissdecLohnarten.json`.  
Quelle Soll: `SWISSCEC/RefXML/*_RETROSPECTIVE.xml` (Jahres-YTD-Basen) und `*_MONTHLY.xml` (QST + Statistik).  
Toleranz 0.05 CHF. Details je Prüfung: `abgleich_csv_refxml_details.csv`.

**Ergebnis: 7420 Prüfungen, 7169 OK, 251 Differenzen.**

## Übersicht pro Prüfung

| Prüfung | Quelle | Prüfungen | OK | DIFF |
|---|---|---:|---:|---:|
| AHV-Basis (AHV-AVS-BaseSalary) | RETRO | 445 | 442 | 3 |
| FAK-Basis (FAK-CAF-ContributorySalary) | RETRO | 488 | 442 | 46 |
| FAK Kinderzulagen wiederkehrend (3000) | RETRO | 488 | 476 | 12 |
| FAK Zulagen einmalig (3001+3034) | RETRO | 488 | 476 | 12 |
| UVG-Bruttolohn (UVG-LAA-GrossSalary) | RETRO | 525 | 521 | 4 |
| UVG-Basis (UVG-LAA-BaseSalary) | RETRO | 525 | 522 | 3 |
| UVGZ-Basis (UVGZ-LAAC-BaseSalary) | RETRO | 525 | 511 | 14 |
| KTG-Referenzlohn (Reference-AHV-AVS-Salary) | RETRO | 525 | 511 | 14 |
| Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | RETRO | 525 | 511 | 14 |
| Lohnausweis BVG ordentlich (Ziff. 10.1 = 5050+1972) | RETRO | 525 | 525 | 0 |
| Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | MONTHLY | 417 | 371 | 46 |
| Statistik Zulagen (Allowances) | MONTHLY | 417 | 397 | 20 |
| Statistik Familienzulagen (FamilyIncomeSupplement) | MONTHLY | 417 | 414 | 3 |
| Statistik Leistungen Dritter (PaymentsByThird) | MONTHLY | 417 | 408 | 9 |
| Statistik BVG-Beitrag AN (BVG-LPP-RegularContribution) | MONTHLY | 417 | 417 | 0 |
| QST steuerbarer Lohn (TaxableEarning) | MONTHLY | 252 | 211 | 41 |
| Person nur im XML | RETRO | 5 | 4 | 1 |
| Person fehlt im XML | MONTHLY | 9 | 0 | 9 |
| Lohnausweis BVG Einkauf (Ziff. 10.2 = 1973) | RETRO | 10 | 10 | 0 |

## Differenzen pro Testfall

### TF01 Herz Monica (5)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-01 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 1500.00 | 0.00 | 1500.00 |  |
| 2025-02 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 1500.00 | 0.00 | 1500.00 |  |
| 2025-03 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 2200.00 | 0.00 | 2200.00 |  |
| 2025-05 | MONTHLY | Person fehlt im XML | 0.00 | 2300.00 | -2300.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |
| 2025-10 | MONTHLY | Person fehlt im XML | 0.00 | 20000.00 | -20000.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |

### TF02 Paganini Maria (16)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-01 | MONTHLY | Statistik Zulagen (Allowances) | 90.00 | 0.00 | 90.00 |  |
| 2025-02 | MONTHLY | Statistik Zulagen (Allowances) | 50.00 | 0.00 | 50.00 |  |
| 2025-03 | MONTHLY | Statistik Zulagen (Allowances) | 25.00 | 0.00 | 25.00 |  |
| 2025-04 | MONTHLY | Statistik Zulagen (Allowances) | 35.00 | 0.00 | 35.00 |  |
| 2025-05 | MONTHLY | Statistik Zulagen (Allowances) | 40.00 | 0.00 | 40.00 |  |
| 2025-07 | MONTHLY | Statistik Zulagen (Allowances) | 35.00 | 0.00 | 35.00 |  |
| 2025-08 | MONTHLY | Statistik Zulagen (Allowances) | 105.00 | 0.00 | 105.00 |  |
| 2025-09 | MONTHLY | Statistik Zulagen (Allowances) | 89.00 | 0.00 | 89.00 |  |
| 2025-10 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 49044.70 | 50444.70 | -1400.00 |  |
| 2025-10 | MONTHLY | Statistik Zulagen (Allowances) | 81.00 | 0.00 | 81.00 |  |
| 2025-11 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 53806.65 | 56606.65 | -2800.00 |  |
| 2025-12 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 61011.90 | 65211.90 | -4200.00 |  |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 8013.05 | 5969.05 | 2044.00 |  |
| 2025-12 | MONTHLY | Statistik Zulagen (Allowances) | 95.00 | 0.00 | 95.00 |  |
| 2026-01 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 3925.20 | 5325.20 | -1400.00 |  |
| 2026-02 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 4427.05 | 7227.05 | -2800.00 |  |

### TF03 Lusser Pia (7)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-06 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 68000.00 | 69400.00 | -1400.00 |  |
| 2025-07 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 68100.00 | 70900.00 | -2800.00 |  |
| 2025-08 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 68200.00 | 72400.00 | -4200.00 |  |
| 2025-09 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 68300.00 | 73900.00 | -5600.00 |  |
| 2025-10 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 68400.00 | 75400.00 | -7000.00 |  |
| 2025-11 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 68500.00 | 76900.00 | -8400.00 |  |
| 2025-12 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 68600.00 | 78400.00 | -9800.00 |  |

### TF04 Fankhauser Markus (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2026-02 | MONTHLY | Person fehlt im XML | 0.00 | 40000.00 | -40000.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |

### TF05 Moser Johann (10)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-05 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 5400.00 | 6750.00 | -1350.00 |  |
| 2025-06 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 5400.00 | 8100.00 | -2700.00 |  |
| 2025-07 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 5400.00 | 9450.00 | -4050.00 |  |
| 2025-08 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 5400.00 | 10800.00 | -5400.00 |  |
| 2025-09 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 5400.00 | 12150.00 | -6750.00 |  |
| 2025-10 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 5400.00 | 13500.00 | -8100.00 |  |
| 2025-11 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 5400.00 | 14850.00 | -9450.00 |  |
| 2025-12 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 6350.00 | 17550.00 | -11200.00 |  |
| 2026-02 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | -950.00 | -4000.00 | 3050.00 |  |
| 2026-02 | MONTHLY | Person fehlt im XML | 0.00 | 6000.00 | -6000.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |

### TF07 Burri Heidi (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-01 | MONTHLY | Person fehlt im XML | 0.00 | 15000.00 | -15000.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |

### TF08 Lamon René (12)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-03 | MONTHLY | Statistik Zulagen (Allowances) | 25.00 | 0.00 | 25.00 |  |
| 2025-04 | MONTHLY | Statistik Zulagen (Allowances) | 35.00 | 0.00 | 35.00 |  |
| 2025-05 | MONTHLY | Statistik Zulagen (Allowances) | 40.00 | 0.00 | 40.00 |  |
| 2025-07 | MONTHLY | Statistik Zulagen (Allowances) | 35.00 | 0.00 | 35.00 |  |
| 2025-08 | MONTHLY | Statistik Zulagen (Allowances) | 105.00 | 0.00 | 105.00 |  |
| 2025-09 | MONTHLY | Statistik Zulagen (Allowances) | 89.00 | 0.00 | 89.00 |  |
| 2025-10 | MONTHLY | Statistik Zulagen (Allowances) | 81.00 | 0.00 | 81.00 |  |
| 2025-11 | MONTHLY | Statistik Zulagen (Allowances) | 44.00 | 0.00 | 44.00 |  |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 11682.45 | 7638.45 | 4044.00 |  |
| 2025-12 | MONTHLY | Statistik Zulagen (Allowances) | 95.00 | 0.00 | 95.00 |  |
| 2026-01 | MONTHLY | Person fehlt im XML | 0.00 | 20000.00 | -20000.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |
| 2026-02 | MONTHLY | Person fehlt im XML | 0.00 | 21339.25 | -21339.25 | CSV hat Lohn im Monat, MONTHLY ohne Person |

### TF09 Estermann Michael (13)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-01 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 600.00 | 2000.00 | -1400.00 |  |
| 2025-02 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 1200.00 | 4000.00 | -2800.00 |  |
| 2025-03 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 36800.00 | 41000.00 | -4200.00 |  |
| 2025-04 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 37400.00 | 43000.00 | -5600.00 |  |
| 2025-05 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 38000.00 | 45000.00 | -7000.00 |  |
| 2025-06 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 38600.00 | 47000.00 | -8400.00 |  |
| 2025-07 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 39200.00 | 49000.00 | -9800.00 |  |
| 2025-08 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 39800.00 | 51000.00 | -11200.00 |  |
| 2025-09 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 40400.00 | 53000.00 | -12600.00 |  |
| 2025-10 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 10000.00 | 24000.00 | -14000.00 |  |
| 2025-10 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 30000.00 | 0.00 | 30000.00 |  |
| 2025-11 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 11300.00 | 26700.00 | -15400.00 |  |
| 2025-12 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 32000.00 | 0.00 | 32000.00 |  |

### TF10 Ganz Heinz (4)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-02 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 1800.00 | 0.00 | 1800.00 |  |
| 2025-09 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 4500.00 | 0.00 | 4500.00 |  |
| 2025-11 | MONTHLY | Person fehlt im XML | 0.00 | 6400.00 | -6400.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |
| 2025-12 | MONTHLY | Person fehlt im XML | 0.00 | 14600.00 | -14600.00 | CSV hat Lohn im Monat, MONTHLY ohne Person |

### TF11 Bosshard Peter (58)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-01 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 23100.00 | 11550.00 | 11550.00 |  |
| 2025-01 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 23100.00 | 11550.00 | 11550.00 |  |
| 2025-01 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 10100.00 | 11550.00 | -1450.00 |  |
| 2025-02 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 46200.00 | 23100.00 | 23100.00 |  |
| 2025-02 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 46200.00 | 23100.00 | 23100.00 |  |
| 2025-02 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 10100.00 | 11550.00 | -1450.00 |  |
| 2025-03 | RETRO | AHV-Basis (AHV-AVS-BaseSalary) | 59650.00 | 39650.00 | 20000.00 |  |
| 2025-03 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 59650.00 | 39650.00 | 20000.00 |  |
| 2025-03 | RETRO | UVG-Bruttolohn (UVG-LAA-GrossSalary) | 61645.00 | 41645.00 | 20000.00 |  |
| 2025-03 | RETRO | UVG-Basis (UVG-LAA-BaseSalary) | 59650.00 | 39650.00 | 20000.00 |  |
| 2025-03 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 119300.00 | 39650.00 | 79650.00 |  |
| 2025-03 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 119300.00 | 39650.00 | 79650.00 |  |
| 2025-03 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 61645.00 | 41645.00 | 20000.00 |  |
| 2025-03 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 10100.00 | 11550.00 | -1450.00 |  |
| 2025-03 | MONTHLY | Statistik Familienzulagen (FamilyIncomeSupplement) | 475.00 | 1475.00 | -1000.00 |  |
| 2025-04 | RETRO | AHV-Basis (AHV-AVS-BaseSalary) | 79600.00 | 59600.00 | 20000.00 |  |
| 2025-04 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 79600.00 | 59600.00 | 20000.00 |  |
| 2025-04 | RETRO | UVG-Bruttolohn (UVG-LAA-GrossSalary) | 82070.00 | 62070.00 | 20000.00 |  |
| 2025-04 | RETRO | UVG-Basis (UVG-LAA-BaseSalary) | 79600.00 | 59600.00 | 20000.00 |  |
| 2025-04 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 159200.00 | 59600.00 | 99600.00 |  |
| 2025-04 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 159200.00 | 59600.00 | 99600.00 |  |
| 2025-04 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 82070.00 | 62070.00 | 20000.00 |  |
| 2025-04 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 10100.00 | 11550.00 | -1450.00 |  |
| 2025-05 | RETRO | AHV-Basis (AHV-AVS-BaseSalary) | 95150.00 | 75150.00 | 20000.00 |  |
| 2025-05 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 95150.00 | 75150.00 | 20000.00 |  |
| 2025-05 | RETRO | UVG-Bruttolohn (UVG-LAA-GrossSalary) | 148095.00 | 128095.00 | 20000.00 |  |
| 2025-05 | RETRO | UVG-Basis (UVG-LAA-BaseSalary) | 95150.00 | 75150.00 | 20000.00 |  |
| 2025-05 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 190300.00 | 75150.00 | 115150.00 |  |
| 2025-05 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 190300.00 | 75150.00 | 115150.00 |  |
| 2025-05 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 148095.00 | 128095.00 | 20000.00 |  |
| 2025-05 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 10100.00 | 11550.00 | -1450.00 |  |
| 2025-06 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 214200.00 | 107100.00 | 107100.00 |  |
| 2025-06 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 214200.00 | 107100.00 | 107100.00 |  |
| 2025-06 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 30500.00 | 31950.00 | -1450.00 |  |
| 2025-07 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 280500.00 | 140250.00 | 140250.00 |  |
| 2025-07 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 280500.00 | 140250.00 | 140250.00 |  |
| 2025-07 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 30500.00 | 31950.00 | -1450.00 |  |
| 2025-08 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 344400.00 | 172200.00 | 172200.00 |  |
| 2025-08 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 344400.00 | 172200.00 | 172200.00 |  |
| 2025-08 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 30500.00 | 31950.00 | -1450.00 |  |
| 2025-09 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 408300.00 | 204150.00 | 204150.00 |  |
| 2025-09 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 408300.00 | 204150.00 | 204150.00 |  |
| 2025-09 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 30500.00 | 31950.00 | -1450.00 |  |
| 2025-10 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 513200.00 | 256600.00 | 256600.00 |  |
| 2025-10 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 513200.00 | 256600.00 | 256600.00 |  |
| 2025-10 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 50500.00 | 31950.00 | 18550.00 |  |
| 2025-11 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 607100.00 | 303550.00 | 303550.00 |  |
| 2025-11 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 607100.00 | 303550.00 | 303550.00 |  |
| 2025-11 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 45500.00 | 31950.00 | 13550.00 |  |
| 2025-12 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 706000.00 | 353000.00 | 353000.00 |  |
| 2025-12 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 706000.00 | 353000.00 | 353000.00 |  |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 30500.00 | 31950.00 | -1450.00 |  |
| 2026-01 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 63900.00 | 31950.00 | 31950.00 |  |
| 2026-01 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 63900.00 | 31950.00 | 31950.00 |  |
| 2026-01 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 30500.00 | 31950.00 | -1450.00 |  |
| 2026-02 | RETRO | UVGZ-Basis (UVGZ-LAAC-BaseSalary) | 127800.00 | 63900.00 | 63900.00 |  |
| 2026-02 | RETRO | KTG-Referenzlohn (Reference-AHV-AVS-Salary) | 127800.00 | 63900.00 | 63900.00 |  |
| 2026-02 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 30500.00 | 31950.00 | -1450.00 |  |

### TF12 Casanova Renato (22)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-02 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 1600.00 | 12850.00 | -11250.00 |  |
| 2025-03 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 15300.00 | 17300.00 | -2000.00 |  |
| 2025-03 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 12000.00 | 12850.00 | -850.00 |  |
| 2025-04 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 28150.00 | 30150.00 | -2000.00 |  |
| 2025-04 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 12000.00 | 12850.00 | -850.00 |  |
| 2025-05 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 41000.00 | 43000.00 | -2000.00 |  |
| 2025-05 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 12000.00 | 12850.00 | -850.00 |  |
| 2025-06 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 53850.00 | 55850.00 | -2000.00 |  |
| 2025-06 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 12000.00 | 12850.00 | -850.00 |  |
| 2025-06 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 6500.00 | 0.00 | 6500.00 |  |
| 2025-07 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 66700.00 | 68700.00 | -2000.00 |  |
| 2025-07 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 12000.00 | 12850.00 | -850.00 |  |
| 2025-08 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 86550.00 | 88550.00 | -2000.00 |  |
| 2025-08 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 12000.00 | 12850.00 | -850.00 |  |
| 2025-09 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 99400.00 | 101400.00 | -2000.00 |  |
| 2025-09 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 12000.00 | 12850.00 | -850.00 |  |
| 2025-10 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 100400.00 | 102400.00 | -2000.00 |  |
| 2025-10 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 1000.00 | 0.00 | 1000.00 |  |
| 2025-11 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 110400.00 | 112400.00 | -2000.00 |  |
| 2025-11 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 10000.00 | 0.00 | 10000.00 |  |
| 2025-12 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 135400.00 | 137400.00 | -2000.00 |  |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 25000.00 | 0.00 | 25000.00 |  |

### TF13 Combertaldi Renato (4)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-12 | RETRO | UVG-Bruttolohn (UVG-LAA-GrossSalary) | 28425.00 | 28210.00 | 215.00 |  |
| 2025-12 | RETRO | Lohnausweis Bruttolohn (GrossIncome, Ziff. 8) | 28425.00 | 28210.00 | 215.00 |  |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 3200.00 | 2200.00 | 1000.00 |  |
| 2025-12 | MONTHLY | Statistik Familienzulagen (FamilyIncomeSupplement) | 215.00 | 0.00 | 215.00 |  |

### TF16 Aebi Anna (16)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2024-11 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 9458.35 | 0.00 | 9458.35 |  |
| 2024-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 8895.00 | 0.00 | 8895.00 |  |
| 2025-01 | RETRO | Person nur im XML | 22603.35 | 0.00 | 22603.35 | XML führt Person, CSV ohne Lohnarten im Jahr |
| 2025-02 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 17050.60 | 19850.60 | -2800.00 |  |
| 2025-02 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 14350.60 | 0.00 | 14350.60 |  |
| 2025-03 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-03 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 10214.45 | 0.00 | 10214.45 |  |
| 2025-04 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-05 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-06 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-07 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-08 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-09 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-10 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-11 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |
| 2025-12 | RETRO | FAK-Basis (FAK-CAF-ContributorySalary) | 30365.05 | 34565.05 | -4200.00 |  |

### TF20 Arnold Lukas (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-03 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 1000.00 | 2000.00 | -1000.00 |  |

### TF21 Meier Christian (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-03 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 1000.00 | 2000.00 | -1000.00 |  |

### TF22 Bucher Elisabeth (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2026-02 | MONTHLY | Statistik Familienzulagen (FamilyIncomeSupplement) | 215.00 | 3215.00 | -3000.00 |  |

### TF25 Lehmann Nadine (7)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-02 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 8000.00 | 12000.00 | -4000.00 |  |
| 2025-02 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 5714.30 | 8000.00 | -2285.70 | Satz-Lohn 11428.55, QST 925.70 |
| 2025-03 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 5400.00 | 12000.00 | -6600.00 | Satz-Lohn 11764.70, QST 908.15 |
| 2025-04 | MONTHLY | QST steuerbarer Lohn LU A0Y (TaxableEarning) | 10800.00 | 12000.00 | -1200.00 | Satz-Lohn 12000.00, QST 1637.30 |
| 2025-04 | MONTHLY | QST steuerbarer Lohn TI A0Y (TaxableEarning) | 0.00 | 12000.00 | -12000.00 | Satz-Lohn 0.00, QST 0.00 |
| 2025-06 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 6000.00 | 12000.00 | -6000.00 |  |
| 2025-06 | MONTHLY | QST steuerbarer Lohn LU A0Y (TaxableEarning) | 9323.40 | 10166.65 | -843.25 | Satz-Lohn 20333.30, QST 1873.05 |

### TF26 Jenzer Marcel (7)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-02 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 8000.00 | 12000.00 | -4000.00 |  |
| 2025-02 | MONTHLY | QST steuerbarer Lohn TI R0N (TaxableEarning) | 5714.30 | 8000.00 | -2285.70 | Satz-Lohn 11428.55, QST 737.15 |
| 2025-03 | MONTHLY | QST steuerbarer Lohn TI R0N (TaxableEarning) | 5400.00 | 12000.00 | -6600.00 | Satz-Lohn 11764.70, QST 729.95 |
| 2025-04 | MONTHLY | QST steuerbarer Lohn TI R0N (TaxableEarning) | 10800.00 | 12000.00 | -1200.00 | Satz-Lohn 11851.85, QST 1447.50 |
| 2025-05 | MONTHLY | QST steuerbarer Lohn TI R0N (TaxableEarning) | 0.00 | 12000.00 | -12000.00 | Satz-Lohn 11891.90, QST 0.00 |
| 2025-06 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 6000.00 | 12000.00 | -6000.00 |  |
| 2025-06 | MONTHLY | QST steuerbarer Lohn TI R0N (TaxableEarning) | 6382.55 | 10166.65 | -3784.10 | Satz-Lohn 12896.80, QST 1046.95 |

### TF28 Arbenz Esther (11)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-01 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 4500.00 | 6000.00 | -1500.00 | Satz-Lohn 8000.00, QST 659.70 |
| 2025-02 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 15500.00 | 26000.00 | -10500.00 | Satz-Lohn 28000.00, QST 4465.55 |
| 2025-03 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 3300.00 | 6000.00 | -2700.00 | Satz-Lohn 8000.00, QST 483.80 |
| 2025-04 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 4200.00 | 6000.00 | -1800.00 | Satz-Lohn 8000.00, QST 615.70 |
| 2025-06 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 3300.00 | 6000.00 | -2700.00 | Satz-Lohn 8000.00, QST 483.80 |
| 2025-07 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 2400.00 | 6000.00 | -3600.00 | Satz-Lohn 8000.00, QST 351.85 |
| 2025-08 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 3900.00 | 6000.00 | -2100.00 | Satz-Lohn 8000.00, QST 571.75 |
| 2025-10 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 2100.00 | 6000.00 | -3900.00 | Satz-Lohn 8000.00, QST 307.85 |
| 2025-11 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 4800.00 | 6000.00 | -1200.00 | Satz-Lohn 8000.00, QST 703.70 |
| 2025-12 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 6550.00 | 12000.00 | -5450.00 | Satz-Lohn 16000.00, QST 1461.95 |
| 2026-02 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 9625.00 | 15000.00 | -5375.00 | Satz-Lohn 31000.00, QST 2839.40 |

### TF29 Forster Moreno (12)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-01 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 4500.00 | 6000.00 | -1500.00 | Satz-Lohn 8000.00, QST 576.00 |
| 2025-02 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 15500.00 | 26000.00 | -10500.00 | Satz-Lohn 9666.65, QST 2344.00 |
| 2025-03 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 3300.00 | 6000.00 | -2700.00 | Satz-Lohn 9666.65, QST 481.80 |
| 2025-04 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 4200.00 | 6000.00 | -1800.00 | Satz-Lohn 9666.65, QST 613.20 |
| 2025-06 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 3300.00 | 6000.00 | -2700.00 | Satz-Lohn 9666.65, QST 481.80 |
| 2025-07 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 2400.00 | 6000.00 | -3600.00 | Satz-Lohn 9666.65, QST 350.40 |
| 2025-08 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 3900.00 | 6000.00 | -2100.00 | Satz-Lohn 9666.65, QST 569.40 |
| 2025-10 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 2100.00 | 6000.00 | -3900.00 | Satz-Lohn 9666.65, QST 306.60 |
| 2025-11 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 4800.00 | 6000.00 | -1200.00 | Satz-Lohn 9666.65, QST 700.80 |
| 2025-12 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 6550.00 | 12000.00 | -5450.00 | Satz-Lohn 10333.35, QST 1394.15 |
| 2026-02 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 9625.00 | 15000.00 | -5375.00 | Satz-Lohn 15000.00, QST 2049.15 |
| 2026-02 | MONTHLY | QST steuerbarer Lohn TI A0Y (TaxableEarning) | 0.00 | 15000.00 | -15000.00 | Satz-Lohn 0.00, QST 0.00 |

### TF33 Châtelain Pierre (12)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-07 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1000.00 | 250.00 | 750.00 |  |
| 2025-07 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 750.00 | -750.00 |  |
| 2025-08 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1250.00 | 500.00 | 750.00 |  |
| 2025-08 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 750.00 | -750.00 |  |
| 2025-09 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1500.00 | 750.00 | 750.00 |  |
| 2025-09 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 750.00 | -750.00 |  |
| 2025-10 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1750.00 | 1000.00 | 750.00 |  |
| 2025-10 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 750.00 | -750.00 |  |
| 2025-11 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 2000.00 | 1250.00 | 750.00 |  |
| 2025-11 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 750.00 | -750.00 |  |
| 2025-12 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 2250.00 | 1500.00 | 750.00 |  |
| 2025-12 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 750.00 | -750.00 |  |

### TF34 Rinaldi Massimo (12)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-07 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 860.00 | 215.00 | 645.00 |  |
| 2025-07 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 645.00 | -645.00 |  |
| 2025-08 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1075.00 | 430.00 | 645.00 |  |
| 2025-08 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 645.00 | -645.00 |  |
| 2025-09 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1290.00 | 645.00 | 645.00 |  |
| 2025-09 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 645.00 | -645.00 |  |
| 2025-10 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1505.00 | 860.00 | 645.00 |  |
| 2025-10 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 645.00 | -645.00 |  |
| 2025-11 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1720.00 | 1075.00 | 645.00 |  |
| 2025-11 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 645.00 | -645.00 |  |
| 2025-12 | RETRO | FAK Kinderzulagen wiederkehrend (3000) | 1935.00 | 1290.00 | 645.00 |  |
| 2025-12 | RETRO | FAK Zulagen einmalig (3001+3034) | 0.00 | 645.00 | -645.00 |  |

### TF35 Roos Roland (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-09 | MONTHLY | QST steuerbarer Lohn TI B0Y (TaxableEarning) | 0.00 | 5000.00 | -5000.00 | Satz-Lohn 0.00, QST 0.00 |

### TF36 Maldini Fabio (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-09 | MONTHLY | QST steuerbarer Lohn BE T0N (TaxableEarning) | 0.00 | 5000.00 | -5000.00 | Satz-Lohn 0.00, QST 0.00 |

### TF37 Oberli Christine (2)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2024-11 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 5000.00 | 10000.00 | -5000.00 |  |
| 2025-01 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 3000.00 | 5000.00 | -2000.00 |  |

### TF38 Jung Claude (4)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-03 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 8000.00 | 3000.00 | 5000.00 |  |
| 2025-06 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 9500.00 | 3000.00 | 6500.00 |  |
| 2025-09 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 6800.00 | 3000.00 | 3800.00 |  |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 8500.00 | 3000.00 | 5500.00 |  |

### TF40 Farine Corinne (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-12 | MONTHLY | Statistik Zulagen (Allowances) | 30500.00 | 0.00 | 30500.00 |  |

### TF42 Peters Otto (4)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-11 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 4000.00 | 6666.65 | -2666.65 | Satz-Lohn 6666.65, QST 448.00 |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 3333.35 | 6666.65 | -3333.30 |  |
| 2025-12 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 2666.70 | 3333.35 | -666.65 | Satz-Lohn 6666.65, QST 298.65 |
| 2026-02 | MONTHLY | QST steuerbarer Lohn TI A0N (TaxableEarning) | 20000.00 | 30000.00 | -10000.00 | Satz-Lohn 9166.65, QST 2800.00 |

### TF43 Ochsenbein Lea (4)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-11 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 4000.00 | 6666.65 | -2666.65 | Satz-Lohn 6666.65, QST 534.00 |
| 2025-12 | MONTHLY | Statistik Grundlohn (GrossBaseSalaryAndRegularAllowance) | 3333.35 | 6666.65 | -3333.30 |  |
| 2025-12 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 2666.70 | 3333.35 | -666.65 | Satz-Lohn 6666.70, QST 356.00 |
| 2026-02 | MONTHLY | QST steuerbarer Lohn BE A0Y (TaxableEarning) | 20000.00 | 30000.00 | -10000.00 | Satz-Lohn 36666.70, QST 6184.00 |

### TF44 Lusser Hans (1)

| Monat | Quelle | Prüfung | XML | CSV | Diff | Hinweis |
|---|---|---|---:|---:|---:|---|
| 2025-08 | MONTHLY | Statistik Leistungen Dritter (PaymentsByThird) | 28000.00 | 0.00 | 28000.00 |  |
