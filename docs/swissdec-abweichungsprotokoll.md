# Swissdec-Abweichungsprotokoll (OneCrew)

Stand 12.09.2026 · Walter: bewusste Differenzen zum Quality Tool **nicht wegbiegen**,
sondern begründen. Dieses Dokument ist die Liste für Zertifizierung und Beleg-Checks.

**Massgeblich sind die Eingaben** (`SWISSCEC/Testmandant/*.csv`). Die Soll-XML
(`SWISSCEC/RefXML/`) dient nur zum Finden von Differenzen — sie enthält Fallen
(Walter 07.09.2026, `docs/swissdec-testmandant.md`). Eine Differenz zur XML ist
kein Bug, solange CSV + Gesetz + dieses Protokoll sie tragen.

Status **BEWUSST** = nicht fixen, nicht «in Richtung XML» rechnen.
Neue Einträge nur nach Walter-Entscheid. Offene Bugs gehören nicht hierher.

---

## I. Wir rechnen anders als CSV **und** Soll-XML

Das Quality Tool wird diese Felder ankreiden. Begründung mitnehmen, nicht die Engine verbiegen.

### A1 — AHV 21: Freibetrag, ALV weg, BVG weg ab Referenzmonat

| | |
|---|---|
| **Beleg** | TF07 Heidi Burri, Dez 2024 (geb. 16.12.1960) · TF16 Anna Aebi, Dez 2024 (geb. **31.12.1960**) |
| **OneCrew** | Burri: AHV auf 6'600 = −349.80, kein ALV, kein BVG (CSV 5050 = 640). Aebi: AHV-pflichtig 9'395 − 1'400 = 7'995 → −423.74, kein ALV, kein BVG (CSV 5050 = 758.33). Karte Aebi «63 J. am 1.12.» — Kalenderalter vor dem Geburtstag; SV nutzt den **Monat**, nicht den Tag. |
| **Swissdec** | Kein Freibetrag, ALV und BVG weiter. Burri: `SocialContributions` −640.50, BVG −640, AHV-Jahr 16'000. Aebi: Honorare 8'895 ok, `SocialContributions` −726.55, BVG −758.33, AHV-Jahr 22'353.35 (= Nov 12'958.35 + Dez 9'395, ohne Freibetrag). |
| **Gesetz / Produkt** | Art. 21 AHVG (Reform AHV 21). Frauen ≤ 1960 = Referenzalter 64; 1961–1963 gestaffelt; Männer und Frauen ≥ 1964 = 65. Ab dem Monat, in dem das Alter erreicht ist: AHV mit Freibetrag 1'400/Mt., ALV-Pflicht Ende (AVIG), BVG-Obligatorium Ende. Code: `GetReferenzalterMonate` / `HatReferenzalterErreicht` (`PayrollCalculationService`, Vorgabe 09.06.2026); Engine streicht ALV/BVG/BVG_ZUSATZ und hebt `effectiveAge` auf 65, damit die Freibetrag-Satzzeile greift. |
| **Hinweis** | Im Kommentar stand «ab dem Monat **nach** Erreichen», gebaut war **im** Geburtsmonat (`aktuellerMonat >= Referenzmonat`). NBU/KTG/UVGZ bleiben (Beschäftigung). Label «AHV (65+)» ist der SV-Satz, nicht das Kalenderalter. |
| **Korrektur 11.09.2026** | AHVG Art. 3 Abs. 1: Beitragspflicht «bis zum Ende des Monats, in welchem das Referenzalter erreicht wird» → Swissdec hat recht. `HatReferenzalterErreicht` schaltet jetzt ab dem **Folgemonat** um (`>` statt `>=`). Burri/Aebi Dez 2024 voll AHV/ALV/BVG, Freibetrag ab Jan 2025. Gilt auch produktiv (Walter: «wenn das eine Regel ist, die auch das normale OneCrew betrifft, dann unbedingt einbauen»). Dez 2024 BE + LU müssen dafür neu gerechnet werden. |
| **Status** | BEHOBEN |

### A2 — Ferien-Tage laufen immer, auch bei %-Entschädigung

| | |
|---|---|
| **Beleg** | TF14 Anna Egli, November 2024: +2.92 Tage trotz Ferienvergütung 156.20 |
| **OneCrew** | Gesetzliche Kontrolle in Tagen; Bezug senkt den Saldo, **keine zweite CHF-Auszahlung** (FLEX hat die Entschädigung schon im Stundenlohn). |
| **Swissdec** | XML `LeaveEntitlement` 0 (BFS: «Ferien stecken im Stundenansatz»). Lohnarten 1160/1161/1201 stimmen. |
| **Begründung** | Arbeitgeber muss Ferien wirklich gewähren (OR / ArG). Prozent auf dem Beleg = Geld, nicht Verzicht auf Tage. |
| **Status** | BEWUSST |

### A3 — Ferienwochen nur 5 oder 6 (L-GAV), nicht 4 / 20 / 30 Arbeitstage

| | |
|---|---|
| **Beleg** | Engine: `vacationPct >= 12.5` → 6 Wochen, sonst 5. Accrual = Wochen × 7 / 12 (Kalendertage). 8.33 % → +2.92 Tage/Mt. (Egli). 13.04 % ab 60 (Muster AG, Paganini) → +3.50 Tage/Mt. |
| **Swissdec** | BFS `LeaveEntitlement`: z.B. Oberli 20, Burri 30 (Lehrerin), Egli 0. |
| **Begründung** | OneCrew ist L-GAV Gastronomie (5/6 Wochen). Kein stilles Umbiegen auf das Statistikfeld. Kein Lohn-Effekt, solange Austritts-Tage nicht ausgezahlt werden (Muster AG: Flags aus). |
| **Status** | BEWUSST (BFS). Scharfe Prüfung von `LeaveEntitlement` = eigener Entscheid, nicht hier «fixen». |

### A4 — SV/QST rappengenau, Lohnzeilen und Netto auf 5 Rp.

| | |
|---|---|
| **Beleg** | TF01: ALV 114.59, KTG 3.77, UVGZ 80.63 — wären mit Round05 falsch. |
| **OneCrew** | Lohnzeilen (Ferien/Feiertag/13. ML) + Netto/Auszahlung = `Round05`. AHV/ALV/UVG/UVGZ/KTG/QST = 2 Dezimalen. |
| **Swissdec-Richtlinie** | spricht von kaufmännischer 5er-Rundung — gilt bei uns für die Lohnzeilen, nicht für die Beitragssätze. |
| **Status** | BEWUSST (`docs/claude/fachlogik.md`, 09.09.2026) |

### A5 — 1–2 Rappen in `SocialContributions` (BFS-Summe)

| | |
|---|---|
| **Beleg** | Burri/Lusser NBU 128.48 vs XML 128.50; Summe −640.48 vs −640.50. Jan 2025: TF03 Pia Lusser AHV+ALV+NBU **440.33** vs XML **440.35**; TF06 Zahnd AHV+ALV+ALVZ+NBU **1'055.44** vs XML **1'055.45**; TF17 Binggeli AHV+ALV+NBU **364.27** vs XML **364.25**; TF18 Blanc AHV+ALV **75.48** vs XML **75.50**; TF24 Utzinger AHV+ALV+NBU **640.48** vs XML **640.50**; TF29 Forster AHV+ALV+NBU **480.36** vs XML **480.35**. |
| **Begründung** | Das XML-Feld ist die Statistik-Summe AHV+ALV+NBU, nicht die einzelne AHV-Zeile. Round05 auf UVG/NBU würde TF01 zerlegen. |
| **Status** | BEWUSST — nicht AHV/NBU auf 5 Rp. drehen, nur um die Summe zu treffen. |

---

## II. Wir folgen der CSV, die Soll-XML ist die Falle

Quality-Tool-XML darf hier abweichen. **Nicht** der Engine anpassen.

| ID | Thema | CSV / OneCrew | Soll-XML | Status |
|---|---|---|---|---|
| F1 | QST-Code | Eingabe `PersonTASCode` (Egli Nov: **A0Y** → 36.05) | z.B. **A0N** 34.00 | BEWUSST |
| F2 | ZG BUR-Nummer | `A38197421` | Januar-2025-XML `A38197423` | BEWUSST |
| F3 | weitere Fallen | PLZ `3008.00`, doppeltes `Contractual13th`, … | siehe `docs/swissdec-testmandant.md` | BEWUSST |
| F4 | QST Stundenlöhner: Monat = **180 h** (ESTV) | TF18 Blanc Jan 2025: 35 h, Nebenjob 60 % → satzbestimmend **4'821.42**, B0Y **6.72 %**, QST **79.26** | `AscertainedTaxableEarning` **4'859.35**, QST **80.30** (35 / **182** = 42×52/12) | BEWUSST |

Regel: Tarif, BUR, PLZ, QST-Code immer aus Stammdaten/CSV, nie aus der RefXML kopieren.

**F4 — Rechnung (nicht nachbauen):** Kreisschreiben 45 / ESTV rechnet den Stundenlöhner auf **180 Stunden/Monat**. OneCrew: eigenes Pensum = 35/180 = 19.44 %, plus weitere AG 60 % → 79.44 %; 1'179.45 × 79.44 / 19.44 = **4'821.42**; Stufe B0Y BE 6.72 % × 1'179.45 = **79.26**. Die Soll-XML nimmt die Filial-Woche (42 h × 52 / 12 = **182 h**) → 4'859.35 und 6.81 % = 80.30. Code: `EstimatePensumFromStunden` / `ComputeSatzBruttoForNebenjob` (`PayrollCalculationService`). 182 h nur, um die XML zu treffen, wäre falsch.

---

## III. Sieht anders aus, ist kein Beitrags- oder Lohnfehler

| Thema | Erklärung |
|---|---|
| KTG / UVGZ auf dem Beleg, nicht in `SocialContributions` | `SocialContributions` = AHV+ALV+NBU. KTG 11 / UVGZ 11 stehen in der RETROSPECTIVE-Meldung. Beleg darf sie zeigen. |
| «Jahresausgleich» im Dezember | Label der Aufrollung (ALV/NBU-Cap). Unter 148'200/Jahr sind die Beträge = Monatsrechnung. |
| Stunden-Saldo rot (FIX, Ist = 0) | Nur Anzeige. Muster AG: `StundenSaldoImLohnVerrechnen = false` → **kein CHF**. Schaub-Default = true (Minusstunden am Austritt sind dann Absicht). |
| Ferien-/Feiertags-Saldo in Tagen am Austritt | Muster AG zahlt sie nicht aus (Flags). Saldo bleibt Tage. Schaub zahlt aus. |
| Honorar / NoTimeConstraint als FIX in der Karte | Anzeige aus Vertrags-Mapping (4a). Beträge kommen aus Honoraren/Zulagen, Monatslohn 0 ist richtig (TF16). |

---

## IV. Bewusst **kein** Swissdec-Modus der Fachlogik

| Schaub (Prod, Default) | Muster AG (Testmandant, Schritt 5a) |
|---|---|
| Ferientage am Austritt auszahlen **ja** | **nein** |
| Feiertagstage am Austritt auszahlen **ja** | **nein** |
| Stunden-Saldo im Lohn verrechnen **ja** | **nein** |
| Teilmonat TAGESSATZ365 | TAGE30 |
| Ferien FLEX/MTP oft Pott | monatlich auszahlen |
| Akonto an | Akonto aus |
| L-GAV an | L-GAV aus |

AHV 21, Rundung, Ferien-Tage, QST-aus-Stammdaten gelten **auf beiden** — kein Schalter «Swissdec-AHV».

---

## Kurz fürs Quality Tool (Burri Dez 2024)

Ankreiden erwartet: F4 (Blanc QST 79.26 vs 80.30). A1 seit 11.09.2026 behoben.  
Nicht ankreiden / andere XML-Felder: NBU ±2 Rp. (A5), KTG/UVGZ, Ferientage (A2/A3), Stundensaldo ohne CHF.

Nächster Eintrag: wenn ein Beleg-Check eine neue **bewusste** Differenz ergibt — hier ergänzen (ID, TF, Monat, OneCrew, Swissdec, Begründung, Status).
