# Swissdec: abgeleitete Regeln und offene Fragen

Stand 04.11.2026 · Walter: «Die abgeleiteten Regeln notieren und bei Swissdec nachfragen.»

Diese Regeln stehen so nicht in den Richtlinien (Ausgabe 06.03.2026). Wir haben sie aus dem Lohntest
abgeleitet: Das Quality Tool ist damit grün, ein Beleg aus den Richtlinien fehlt aber. Solange Swissdec
nicht geantwortet hat, gelten sie als **vorläufig**. Regeln, die sich direkt auf die Richtlinien stützen,
stehen unten separat und sind keine Frage.

**Beim Ändern:** Antwort hier eintragen, Status setzen, Code und Tests anpassen, dann `K…` im
[Testprotokoll](swissdec-qualitytool-protokoll.md) nachführen.

Status: **offen** = noch nicht gefragt oder keine Antwort · **bestätigt** · **geändert**

## Abgeleitete Regeln

| # | Regel im Programm | Beleg aus dem Lohntest | Grenze der Ableitung | Code | Status |
|---|---|---|---|---|---|
| 1 | `CompanyDescription` führt die Arbeitsorte mit Lohn in den letzten **9 Monaten**. | Nov 2024 nur #LU/#BE. Aug–Dez 2025 auch #AG (Lohn zuletzt Mai, später Okt) und #ZG (zuletzt März). Jan 2026 #AG, aber kein #ZG mehr. | Die 9 Monate sind nur der Wert, der zu diesen Monaten passt. Eine andere Regel kann dieselben Fälle ergeben. | `ElmMonthlyDeclarationBuilder.ArbeitsortRueckblickMonate` | offen |
| 2 | Die alte Adresse mit `DepartureDate` bleibt **12 Monate** ab Umzug in der Meldung. | TF40 Farine: Wegzug 28.09.2025, noch im Februar 2026 gemeldet. Bei allen anderen endet sie nur mit dem Austritt. | Belegt sind 5 Monate über den Jahreswechsel. Die 12 Monate sind von uns gesetzt. | `ElmMonthlyDeclarationBuilder`, Aufruf `UmzugImMonat` | offen |
| 3 | Jahresmodell mit mehreren Tarifcodes: **jeder Topf** wird kumuliert auf 5 Rappen gerundet, Monat = Summe der Töpfe − bezahlt. | TF22 Bucher Jul 509.30, Aug 309.65; TF23 Koller Dez 1'014.35; TF36 Maldini Dez 993.05. | Die Referenz-XML vom Stand vor dem 28.09.2026 zeigt teils 5 Rappen anders. Massgebend war das Quality Tool. | `QstJahresmodell.RechneToepfe` | offen |
| 4 | `CorrectionMonth` in `TaxAtSourceTotals` steht in der Reihenfolge einer Java-HashMap. | Juni 2025: 05 vor 04. Juli 2025: 05 vor 06. Sonst rot. | Sieht nach einer Eigenheit des Referenzsystems aus, nicht nach einer fachlichen Regel. | `ElmMonthlyDeclarationBuilder.JavaHashReihenfolge` | offen |
| 5 | Der UVG-Höchstlohn (NBU/BU) rollt über **alle UVG-versicherten Monate** des Jahres auf, auch über Codewechsel. Monate mit Code «…0» zählen nicht. | TF12 Casanova A2 → A1: NBU-Basis Okt 7'303.35. TF40 Farine A1/A0/A1: Okt 7'600. | Monate ohne erfassten Code zählen bei uns als versichert. Das ist nicht getestet. | `PayrollCalculations.UvgVersicherteMonate` | offen |
| 6 | AHV-Freibetrag kumuliert: Ist das Jahrestotal **negativ**, wird es ohne Freibetrag voll erstattet. | TF09 Estermann Dez 2025: Jahr −4'100, AHV-Monat +816.20. | Ein einziger Fall. | `PayrollCalculations.AhvFreibetragKumuliert` | offen |
| 7 | `WorkOrCompensatory` des Ehepartners erscheint erst ab dem **Folgemonat** des Stellenantritts. | TF36 Maldini: Antritt 28.07., gemeldet ab August. | Abgeleitet aus der Tarifregel 10.6.2. | `ElmMonthlyDeclarationBuilder.Ehepartner` | offen |
| 8 | `AnnualValues` zählen nach einem Filialwechsel ohne Unterbruch die Monate der früheren Filiale mit. | TF35 Roos, TF36 Maldini: Sept 2025 SporadicBenefits 30'000. | **Annahme ohne Testfall:** Bei gleichzeitigen Anstellungen in zwei Filialen zählt jeder Monat nur in der eigenen. | `ElmMonthlyDeclarationBuilder`, `ZaehltHier` | offen |
| 9 | Wiedereintritt mit neuem Tarif: `Entry` plus Mutation nur, wenn der **Kanton gleich** bleibt. | TF40 Farine Dez: A1N → A2N mit childrenDeduction. Okt: A1Y → A1N mit Kantonswechsel ohne churchTax. | Je ein Fall pro Richtung. | `ElmMonthlyDeclarationBuilder.BaueQstZeile` | offen |
| 10 | Korrektur des Austrittsmonats: **1001 Lohnkorrektur** zählt nur zusammen mit einem Austrittsguthaben. Allein gilt der Auszahlungsmonat. | TF29 Forster Jan 2026: 1001 + 1067 + 1168 als Korrektur. RL 10.11.1.1 zur Korrektur des Bruttolohns. | Der Fall «1001 allein nach Austritt» ist nicht getestet. | `NachzahlungQstZeit` | offen |
| 11 | Als Anspruch mit dem Austritt gelten auch **1061/1065** (Überstunden), wenn sie nach dem Austritt ausbezahlt werden. | RL 10.6.5 nennt Überzeitguthaben. Die Testfälle haben nur 1067 und 1168. | Unsere Erweiterung für Produktiv, nicht getestet. | `NachzahlungQstZeit.AnspruchMitAustritt` | offen |

| 12 | **Nur Testdaten:** Farines Vertrag ab 01.12. hat Probezeit bis 28.02.2026, damit der 13. ML beim Austritt verfällt. | TF40 Farine Feb 2026: Referenz meldet Contractual13th 8.33, zahlt aber nichts aus. | Die Testdaten nennen keine Probezeit. Fachlich wäre der Anteil geschuldet. | Testinstanz, `ThirteenthVerfaelltInProbezeit` | offen |

| 13 | **Testdaten:** Forsters Umzug nach Bern (Wegzug 28.01.2026, A0Y, QST-Kanton BE, annual-B) fehlt in allen Testdaten-Dateien. | Nur in der Referenz und im Quality Tool Feb 2026. | Aus der Referenz übernommen, Felder ohne Referenzwert von uns ergänzt. Offen ist, ob die Dateien korrigiert werden. | `Assets/Swissdec/Testmandant/testcase_differences_zusatz.csv` | offen |

## Durch die Richtlinien gedeckt (keine Frage)

| Regel | Kapitel | Code |
|---|---|---|
| Teilmonatslohn auf 5 Rappen | 5.1.1 | `PayrollCalculationEngine`, `monthSalary` |
| Wechsel auf C-Bewilligung wirkt ab dem Folgemonat, Austritt «settled-C» per Monatsende | 10.6.2 | `QstPflichtCheckService.CWechselNochNichtWirksam` |
| Leistung mit Anspruch beim Austritt: Korrektur des Austrittsmonats | 10.6.5, 10.11.1.1 | `PayrollCalculationEngine`, `qstAustrittKorrektur` |
| Vorjahres-Korrektur bis 31.03. des Folgejahres über den Lohnlauf | 10.11.1.1 | `QstKorrekturService.PostenStatus` |
| Kantonswechsel: zusätzlich Mutation «residence» | Feldbeschreibung Mutation | `ElmMonthlyDeclarationBuilder.BaueQstZeile` |
| Bonus nach Austritt: Satz = periodisch Austrittsmonat/-jahr + Nachzahlung | 10.6.5 | `PayrollCalculationEngine`, `austrittsStand` |
| Kantonswechsel zwischen Austritt und Nachzahlung: Version des Auszahlungsmonats, Satz nur aus der Zahlung | 10.6.5 | `PayrollCalculationEngine`, `kantonswechselNachAustritt` |

Teilweise abgeleitet ist bei der C-Regel die Ausnahme: Wer schon beim Eintritt C hat oder eine Karte
erneuert, ist sofort befreit. Das ist unsere Auslegung, ein Testfall dazu fehlt.

## Entwurf der Anfrage an Swissdec

Noch nicht gesendet. Empfänger von Walter zu bestimmen, sinnvoll nach Abschluss von Februar 2026.

> **Betreff:** OneCrew, Zertifizierung ELM 6.0.1: Fragen zu Regeln aus dem Lohntest
>
> Guten Tag
>
> Im Lohntest (Testmandant Muster AG, November 2024 bis Februar 2026) sind wir im Quality Tool grün. Einige
> Regeln mussten wir dafür aus den erwarteten Ergebnissen ableiten, weil wir sie in den Richtlinien
> (Ausgabe 06.03.2026) nicht gefunden haben. Bevor wir sie produktiv verwenden, bitten wir Sie um Bestätigung.
>
> 1. **Arbeitsorte in der Monatsmeldung:** Nach welcher Regel gehört ein Arbeitsort ohne Lohn im Meldemonat in
>    `CompanyDescription`? Im Januar 2026 wird #AG (Lohn zuletzt Oktober 2025) erwartet, #ZG (zuletzt März 2025)
>    nicht mehr, im Dezember 2025 noch beide. Wir arbeiten mit einem Rückblick von 9 Monaten.
> 2. **Alte Adresse mit Wegzugsdatum:** Wie lange wird sie gemeldet? TF40 Farine: Wegzug 28.09.2025, noch im
>    Februar 2026 erwartet. Wir melden sie 12 Monate ab Umzug.
> 3. **Jahresmodell mit mehreren Tarifcodes:** Ist es richtig, dass jeder Topf kumuliert auf 5 Rappen gerundet
>    wird und der Monatsabzug die Summe der Töpfe minus das Bezahlte ist (TF22 Bucher Juli 509.30, August 309.65)?
> 4. **Reihenfolge `CorrectionMonth`:** Ist die Reihenfolge in `TaxAtSourceTotals` fachlich relevant? Erwartet
>    wird im Juni 2025 05 vor 04, im Juli 05 vor 06. Dürfen wir chronologisch sortieren?
> 5. **UVG-Höchstlohn:** Rollt er über alle UVG-versicherten Monate des Jahres auf, auch über einen Codewechsel,
>    und zählen Monate mit Code A0 nicht (TF12 Casanova Oktober 7'303.35, TF40 Farine Oktober 7'600)?
> 6. **AHV-Freibetrag bei negativem Jahrestotal:** Wird der negative Betrag ohne Freibetrag voll erstattet
>    (TF09 Estermann Dezember 2025: Jahr −4'100, Monat +816.20)?
> 7. **Erwerbstätigkeit des Ehepartners:** Erscheint `WorkOrCompensatory` immer erst ab dem Folgemonat des
>    Stellenantritts (TF36 Maldini: Antritt 28.07., ab August)?
> 8. **`AnnualValues` bei mehreren Filialen:** Nach einem Filialwechsel zählen die früheren Monate mit (TF35 Roos).
>    Wie ist es bei gleichzeitigen Anstellungen in zwei Filialen derselben Firma?
> 9. **Wiedereintritt mit neuem Tarif:** Ist es richtig, dass zum `Entry` nur im selben Kanton zusätzlich die
>    Mutation gehört (TF40 Farine Dezember mit, Oktober mit Kantonswechsel ohne)?
> 10. **Zahlungen nach Austritt:** Gehört `1001 Lohnkorrektur` nur zusammen mit einem Austrittsguthaben in die
>     Korrektur des Austrittsmonats (TF29 Forster Januar 2026)? Und gelten Überstunden (1061, 1065), die nach dem
>     Austritt ausbezahlt werden, ebenfalls als Anspruch mit dem Austritt?
> 11. **13. Monatslohn beim Austritt (TF40 Farine, 28.02.2026):** Der Vertrag nennt 8.33 %, die Referenz meldet
>     `Contractual13th` 8.33, zahlt für Januar und Februar aber nichts aus. Warum entfällt der anteilige 13. Monatslohn?
> 12. **TF29 Forster Februar 2026:** Das Quality Tool erwartet einen Umzug nach Bern (Wegzug 28.01.2026, A0Y, QST-Kanton
>     BE, annual-B). Diese Mutation fehlt in `testcase_differences_export.csv`. Können Sie die Datei prüfen?
>
> Besten Dank und freundliche Grüsse
>
> Walter Schaub
