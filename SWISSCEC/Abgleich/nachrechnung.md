# Nachrechnung Muster AG — echte Abzugs-Engine gegen Swissdec-RefXML

Erzeugt von `Tests/TestmandantNachrechnungTests.cs` (`dotnet test --filter TestmandantNachrechnung`).
Lohnarten aus `SWISSCEC/Testmandant/wagetypes_export.csv`, Sätze aus `company_export.csv`,
Personen + Mutationen aus den beiden anderen CSV — gerechnet mit `PayrollCalculations.BuildResult`,
verglichen mit `SWISSCEC/RefXML/*_RETROSPECTIVE.xml` (YTD-Basen) und `*_MONTHLY.xml` (SV-Abzug).

**384 Monatsabrechnungen, 2565 Vergleiche, 48 offene Abweichungen (0 bewusst).**

Nicht Teil dieser Nachrechnung (kommt im Lohnlauf aus der Datenbank): Stunden, Verträge,
Saldi, Ferien-/Feiertag-Tage, 13.-ML-Rückstellung, Quellensteuer.

## Bekannte Grenzen des Nachrechners

Diese Punkte kann der Nachrechner nicht abbilden — die Abweichungen unten sind dort
**kein** Befund gegen die Lohnrechnung, sondern eine Lücke des Prüfwerkzeugs:

- **Nachzahlung nach Austritt** (TF07 Burri Jan/Feb): die Engine rechnet sie über die
  Anstellungsmonate des Austrittsjahres ab (Alter, Sätze und Höchstlöhne per Austrittsmonat,
  `CalculateCorrectionAsync`). Der Nachrechner kennt nur das laufende Jahr.
- **Monate ohne Lohnart** werden übersprungen; im echten Lohnlauf zählen sie als
  Beschäftigungsmonat für den kumulierten Höchstlohn (betrifft Ein-/Austrittsmonate).
- **Austritt und Wiedereintritt im selben Jahr** (TF40, TF41) bildet der Nachrechner nur
  über ein Vertragsfenster ab.

## Offene Abweichungen

| Testfall | Monat | Prüfung | OneCrew | Swissdec | Differenz | Hinweis |
|---|---|---|---:|---:|---:|---|
| TF03 Lusser Pia | 06 | KTG-Basis | 10,000.00 | 4,700.00 | +5300.00 |  |
| TF03 Lusser Pia | 07 | KTG-Basis | 10,000.00 | 1,500.00 | +8500.00 |  |
| TF03 Lusser Pia | 08 | KTG-Basis | 2,400.00 | 1,500.00 | +900.00 |  |
| TF07 Burri Heidi | 01 | ALV-Basis | 0.00 | 8,700.00 | -8700.00 |  |
| TF07 Burri Heidi | 01 | ALVZ-Basis | 0.00 | 6,300.00 | -6300.00 |  |
| TF07 Burri Heidi | 01 | UVG-Basis | 0.00 | 8,700.00 | -8700.00 |  |
| TF07 Burri Heidi | 01 | UVGZ-Basis | 0.00 | 8,700.00 | -8700.00 |  |
| TF07 Burri Heidi | 01 | KTG-Basis | 0.00 | 4,000.00 | -4000.00 |  |
| TF07 Burri Heidi | 02 | ALV-Basis | 0.00 | -3,200.00 | +3200.00 |  |
| TF07 Burri Heidi | 02 | ALVZ-Basis | 0.00 | -6,300.00 | +6300.00 |  |
| TF07 Burri Heidi | 02 | UVG-Basis | 0.00 | -3,200.00 | +3200.00 |  |
| TF07 Burri Heidi | 02 | UVGZ-Basis | 0.00 | -3,200.00 | +3200.00 |  |
| TF09 Estermann Michael | 12 | KTG-Basis | -4,100.00 | 0.00 | -4100.00 |  |
| TF09 Estermann Michael | 12 | SV-Abzug Monat | -598.90 | -816.20 | +217.30 | AHV + ALV + ALVZ + NBU |
| TF11 Bosshard Peter | 03 | AHV-Basis | 16,550.00 | 36,550.00 | -20000.00 |  |
| TF11 Bosshard Peter | 03 | ALVZ-Basis | 2,600.00 | 22,600.00 | -20000.00 |  |
| TF11 Bosshard Peter | 03 | UVGZ-Basis | 16,550.00 | 36,550.00 | -20000.00 |  |
| TF11 Bosshard Peter | 03 | KTG-Basis | 19,200.00 | 39,200.00 | -20000.00 |  |
| TF11 Bosshard Peter | 03 | SV-Abzug Monat | 1,267.64 | 2,427.65 | -1160.01 | AHV + ALV + ALVZ + NBU |
| TF11 Bosshard Peter | 06 | AHV-Basis | 31,950.00 | 11,950.00 | +20000.00 |  |
| TF11 Bosshard Peter | 06 | ALVZ-Basis | 19,600.00 | -400.00 | +20000.00 |  |
| TF11 Bosshard Peter | 06 | UVGZ-Basis | 31,950.00 | 11,950.00 | +20000.00 |  |
| TF11 Bosshard Peter | 06 | KTG-Basis | 34,600.00 | 14,600.00 | +20000.00 |  |
| TF11 Bosshard Peter | 06 | SV-Abzug Monat | 2,125.54 | 965.55 | +1159.99 | AHV + ALV + ALVZ + NBU |
| TF12 Casanova Renato | 10 | UVGZ-Basis | -6,303.33 | 0.00 | -6303.33 |  |
| TF12 Casanova Renato | 10 | KTG-Basis | -6,350.00 | 0.00 | -6350.00 |  |
| TF12 Casanova Renato | 11 | KTG-Basis | 2,650.00 | 0.00 | +2650.00 |  |
| TF12 Casanova Renato | 12 | UVGZ-Basis | 5,253.33 | 0.00 | +5253.33 |  |
| TF12 Casanova Renato | 12 | KTG-Basis | 17,650.00 | 13,950.00 | +3700.00 |  |
| TF15 Degelo Lorenz | 03 | ALV-Basis | 411.67 | 823.35 | -411.68 |  |
| TF15 Degelo Lorenz | 03 | ALVZ-Basis | 617.50 | 1,235.00 | -617.50 |  |
| TF15 Degelo Lorenz | 03 | UVGZ-Basis | 411.67 | 823.35 | -411.68 |  |
| TF15 Degelo Lorenz | 03 | SV-Abzug Monat | 156.04 | 163.65 | -7.61 | AHV + ALV + ALVZ + NBU |
| TF16 Aebi Anna | 02 | UVG-Basis | 12,350.00 | 18,936.65 | -6586.65 |  |
| TF16 Aebi Anna | 02 | UVGZ-Basis | 7,500.60 | 913.95 | +6586.65 |  |
| TF16 Aebi Anna | 02 | SV-Abzug Monat | 1,176.22 | 1,207.80 | -31.58 | AHV + ALV + ALVZ + NBU |
| TF40 Farine Corinne | 10 | ALV-Basis | 12,350.00 | 4,000.00 | +8350.00 |  |
| TF40 Farine Corinne | 10 | ALVZ-Basis | 18,525.00 | 0.00 | +18525.00 |  |
| TF40 Farine Corinne | 10 | UVG-Basis | 12,350.00 | 7,600.00 | +4750.00 |  |
| TF40 Farine Corinne | 10 | UVGZ-Basis | 12,350.00 | 4,000.00 | +8350.00 |  |
| TF40 Farine Corinne | 10 | SV-Abzug Monat | 638.81 | 378.05 | +260.76 | AHV + ALV + ALVZ + NBU |
| TF40 Farine Corinne | 12 | ALV-Basis | 12,350.00 | 25,450.00 | -13100.00 |  |
| TF40 Farine Corinne | 12 | ALVZ-Basis | 18,525.00 | 17,033.35 | +1491.65 |  |
| TF40 Farine Corinne | 12 | UVG-Basis | 12,350.00 | 17,100.00 | -4750.00 |  |
| TF40 Farine Corinne | 12 | UVGZ-Basis | 12,350.00 | 20,700.00 | -8350.00 |  |
| TF40 Farine Corinne | 12 | SV-Abzug Monat | 2,678.43 | 2,891.35 | -212.92 | AHV + ALV + ALVZ + NBU |
| TF41 Meier Max | 07 | ALVZ-Basis | 18,525.00 | 0.00 | +18525.00 |  |
| TF41 Meier Max | 07 | SV-Abzug Monat | 956.81 | 852.45 | +104.36 | AHV + ALV + ALVZ + NBU |

