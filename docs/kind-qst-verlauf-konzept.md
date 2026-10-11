# Kind: QST-Verlauf (Ausbildung, Haushalt, Unterhalt) — Konzept

Stand: 11.10.2026 · Status: **Konzept, noch nicht gebaut** (Walter liest zuerst)

## Anlass

Fall Azbije Ponik (Sursee): Mirus führte den QST-Code von Hand (C0Y → C4N → C3N → C2N),
ohne erfasste Kinder. OneCrew schlägt den Tarif aus den Kindern vor — dafür fehlt am Kind
ein Verlauf. Heute gibt es nur Häkchen ohne Datum («in Erstausbildung», «lebt im Haushalt»,
«keine Unterhaltspflicht»). Wer ein Häkchen ändert, ändert die Vergangenheit mit. Ein
Lehrabbruch mit neuer Lehre zwei Monate später lässt sich nicht erfassen.

## Grundsätze (Walter 11.10.2026)

1. **Familienzulage und QST komplett getrennt.** Die QST darf nicht automatisch aus der
   Familienzulage abgeleitet werden (und umgekehrt). Zwischen beiden gibt es nur interne
   Hinweise.
2. **OneCrew macht nur Vorschläge.** Lohnrelevant sind immer die Bestätigungen:
   - Familienzulage: Entscheid der Ausgleichskasse → von HR als Zulage erfasst (wie heute).
   - QST: QST-Version, die Walter/HR nach Vorschlag bzw. Entscheid der Steuerverwaltung
     selbst anlegt (wie heute). Der Vorschlag wird nie automatisch übernommen.
3. **Verhältnisse am Monatsanfang.** Ein Kind zählt im Monat M, wenn die Voraussetzungen am
   1. von M erfüllt sind. Änderungen im Monat wirken ab dem Folgemonat.
   - Geburt im März → zählt ab April.
   - 18. Geburtstag im März → zählt im März noch; ab April nur mit Ausbildung.
   - Lehre abgebrochen 31.03. → am 01.04. keine Ausbildung → zählt ab April nicht mehr.
   - Neue Lehre ab 01.06. → zählt ab Juni; ab 15.06. → zählt ab Juli.
4. **Haushalt und Unterhalt bekommen ebenfalls einen Verlauf** (Auszug fürs Studium,
   Kind finanziert sich selbst).

## Was am Kind neu geführt wird (nur für die QST)

### A) Ausbildung (QST) — Abschnitte mit Lücken

| Feld | Bedeutung |
|---|---|
| von / bis | Ausbildungszeitraum (bis leer = läuft) |
| Art | Lehre, Schule, Studium, Brückenangebot, andere |
| Ende-Grund | abgeschlossen / abgebrochen / leer |
| Beleg | Lehrvertrag, Immatrikulation … (Dokument-Ablage) |
| Erfahren am | Wissens-Achse wie bei den Zulagen (rückwirkend ⇒ QST-Korrektur-Posten) |
| übernommen | aus dem alten Häkchen umgewandelt → «Daten prüfen» |

Abbruch = Abschnitt mit Bis + «abgebrochen»; neue Lehre = neuer Abschnitt. Die Lücke ergibt
sich von selbst.

### B) Verhältnisse — Versionen «ab Datum»

| Feld | Bedeutung |
|---|---|
| gültig ab | ab wann der Zustand gilt |
| lebt im Haushalt | ja / nein (zählt nur bei Tarif H) |
| Unterhalt zur Hauptsache | ja / nein (nein ⇒ zählt nie, auch nicht bei B/C) |
| Erfahren am | Wissens-Achse |

## Regel «zählt das Kind im Monat M?»

Am 1. von M:
1. Kind geboren (Geburt vor dem 1.) und Unterhalt zur Hauptsache = ja
2. UND (noch nicht 18 — der 18. Geburtstag liegt am oder nach dem 1.
   ODER ein Ausbildungs-Abschnitt läuft am 1.)
3. Für Tarif H zusätzlich: lebt im Haushalt = ja
4. Wissens-Achse: der massgebende Eintrag war zum Stichtag bekannt (Erfahren am)

Eine Stelle für die Regel: `QstTarifVorschlagLogic` (rein, getestet). Genutzt von
Tarifvorschlag, QST-Pflicht-Check (Kinderziffer-Abgleich), QST-Zeile auf der Kinder-Karte.

## Was wegfällt

- **Ableitung aus der Ausbildungszulage** in `QstTarifVorschlagService`,
  `QstPflichtCheckService` und in der QST-Zeile der Kinder-Karte (`QstZeitraeume`, Parameter
  `azPerioden`). Ersetzt durch einen Hinweis (siehe unten).
- Die Häkchen «in Erstausbildung» / «lebt im Haushalt» / «keine Unterhaltspflicht» als
  Quelle für die QST (Spalten bleiben als Altbestand, werden nicht mehr gelesen).

## Hinweise statt Übernahme

| Hinweis | Wann |
|---|---|
| «Ausbildungszulage läuft, aber keine Ausbildung für die QST erfasst» | AZ am 1. aktiv, kein QST-Ausbildungs-Abschnitt |
| «Ausbildung für die QST erfasst, aber keine Ausbildungszulage» | Kind 16–25 in Ausbildung, keine AZ — Familienzulage prüfen |
| «Ausbildungszeitraum prüfen» (Pendenz) | Abschnitt mit Markierung «übernommen» |
| Kinderziffer-Abgleich (besteht schon) | erfasste QST-Ziffer ≠ Vorschlag am Stichtag |

Alle nur Information — keiner ändert eine QST-Version oder eine Zulage.

## Umwandlung Altbestand (Walter 11.10.2026: so umsetzen)

- Häkchen «in Erstausbildung» = ja → Ausbildungs-Abschnitt **ab 18. Geburtstag, bis offen**,
  Markierung «übernommen – Daten prüfen» + Pendenz. Am Vorschlag ändert sich dadurch nichts.
- Kinder, die nur eine Ausbildungszulage haben, bekommen **keinen** Abschnitt (das wäre eine
  Ableitung aus der Familienzulage) — nur den Hinweis.
- «lebt im Haushalt» → eine Verhältnis-Version ab Geburtsdatum mit dem heutigen Wert.
- «keine Unterhaltspflicht» = ja → Verhältnis-Version ab Geburtsdatum «Unterhalt zur
  Hauptsache = nein».
- Die Ableitung aus der Ausbildungszulage fällt **im selben Schritt** weg (nicht vorher).

## Oberfläche

- **Kinder-Karte:** QST-Zeile zeigt die Abschnitte nach der Monatsregel, mit Lücken
  (z.B. «bis 03.2026 · Lehre abgebrochen», «ab 06.2026 · Lehre»).
- **Familien-Fenster, Abschnitt «Quellensteuer»:** Liste der Ausbildungs-Abschnitte
  (+ Erfassen, ⋮ Bearbeiten/Löschen, Beleg) und der Verhältnis-Versionen.
- Familienzulagen bleiben, wo sie sind (Zulagen am Kind).

## Technik (beim Bau)

- Zwei neue Tabellen (Namen beim Bau): Ausbildungs-Abschnitte und Verhältnis-Versionen je
  Familienmitglied; Datumsfelder `date`, Zeitstempel `timestamp without time zone`,
  `DateTime.Now`. Startblock in `Program.cs` + **SchemaStand +1**; Umwandlung als
  idempotente Einmal-Migration (Sentinel), in beiden Datenbanken.
- Tests: Monatsregel (Geburt, 18, Abbruch/Neubeginn am 1. und mitten im Monat, Haushalt nur
  bei H, Unterhalt nein), Umwandlung, Hinweise, keine Ableitung aus AZ.

## Offen (vor dem Bau klären)

1. **«Erstausbildung» oder «Ausbildung»?** Das DBG (Art. 35) spricht beim Kinderabzug von
   Kindern «in der beruflichen oder schulischen Ausbildung», nicht ausdrücklich von
   Erstausbildung. Mit Steueramt LU / KS 45 klären — davon hängt ab, ob z.B. ein Studium nach
   abgeschlossener Lehre zählt.
2. **«QST-abzugsberechtigt von/bis»** (heutiges Feld): als manuelle Begrenzung behalten oder
   in den Verlauf überführen?
