# Swissdec-Call Donnerstag 24.09.2026 — Gesprächsliste

Stand 20.09.2026. Testmandant «Muster AG», Lohnjahr 2025. Januar und Februar sind bei uns komplett
durchgerechnet und stimmen mit den Vorgaben überein — ausser an den Stellen unten.

Grundsatz, den wir eingehalten haben: **Wir rechnen aus den Eingabedaten (CSV), nicht aus der Soll-XML.**
Wo die XML etwas anderes zeigt als CSV + Gesetz, haben wir es bewusst NICHT nachgebaut.

---

## A. Fragen an Swissdec (hier brauchen wir eine Antwort)

### 1. Eintritt am 10. Februar — wird der Eintrittstag bezahlt oder nicht? (TF25 Lehmann, TF26 Jenzer)
- **Was in den Testdaten steht:** Monatslohn 12'000, Eintritt 10.02.2025. Die CSV zieht 4'000 ab (= 10 von 30 Tagen) → 8'000. Das heisst: die Tage 1–10 werden abgezogen, der 10. selbst also nicht bezahlt.
- **Was wir rechnen:** 30-Tage-Methode, Eintrittstag zählt mit: 10.–30. = 21 Tage → 12'000 × 21/30 = **8'400**.
- **Warum wir das für richtig halten:** Am 10. arbeitet die Person. Und bei TF12 Casanova (Eintritt 27.02.) zählt Swissdec selbst 4 Tage (27.–30.), also inklusive Eintrittstag. Beides zusammen passt nicht.
- **Folge:** Bei Lehmann/Jenzer sind Februar-Brutto, SV-Beiträge, Quellensteuer und auch der 13. Monatslohn (700 statt 666.65) bei uns anders als in der XML.
- **Frage:** Ist die Abzugsrechnung in der CSV (−4'000) ein Fehler in den Testdaten, oder erwartet Swissdec wirklich 20 Tage?

### 2. Quellensteuer-Jahresmodell Tessin — Juni und Dezember bei TF22 Bucher
- **Was in der XML steht:** Im Juni eine Rückerstattung von **−2'039.50**, im Dezember **−3'987.95**. Damit ist die Jahressteuer am Ende praktisch null (Jahr: 0.05).
- **Was wir rechnen (nach Anhang 1, Jahresmodell):** Juni **+240.80**, Dezember ca. **+174**. Jahr: 4'361.05.
- **Warum:** Die Heirat gilt laut Mutation erst ab 1.7.2025 (Tarif C ab Juli). Im Juni ist sie noch ledig, Tarif A. Eine Rückerstattung im Juni ergibt sich aus der Formel nicht. Die CSV enthält gar keine QST-Beträge — die Zahlen stehen nur in der XML.
- **Frage:** Wie kommen die −2'039.50 (Juni) und −3'987.95 (Dezember) zustande? Ist das eine Falle, ein Fehler im Referenzrechner, oder eine Regel, die wir nicht kennen?

### 3. TF29 Forster — Grenzgänger-Code passt nicht zwischen CSV und XML
- **Was in der CSV steht:** Tarifcode **R0N** (Grenzgänger Italien), Wohnort Varese, 60 % bei uns + 20 % andere Firma, Arbeitstage CH 15 bzw. 10 von 20.
- **Was in der XML steht:** Tarifcode **A0N** mit CH-Tagen. Ein Code-Wechsel R0N → A0N steht nirgends in den Mutationen.
- **Folge:** Alle 12 Monate weichen ab (Jahr: wir 7'631.10, XML 9'570.15).
- **Frage:** Welcher Code gilt? Fehlt im Export eine Mutation?

### 4. TF25 Lehmann — Umzug 1.4. aber Quellensteuer-Wechsel erst 1.5.
- **Was in den Testdaten steht:** Adresse wechselt am **1.4.2025** von Milano nach Malters LU (Bewilligung G → B). Der Steuerkanton wechselt aber erst am **1.5.2025** von TI nach LU. Zusätzlich steht im April ein «Wegzug aus der Schweiz» (PersonDepartureDate 25.04.2025), der im Mai wieder verschwindet.
- **Gesetz (Kreisschreiben 45):** Umzug am 1. des Monats → neuer Kanton ab diesem Monat, also LU ab April.
- **Was wir gemacht haben:** Wir folgen dem Steuerkanton der Testdaten (TI bis 30.4., LU ab 1.5.) und haben den Wohnort intern auf denselben Schnitt gelegt, damit Beleg und QST zusammenpassen.
- **Frage:** Ist der April bewusst «Grenzgängerin in TI mit Schweizer Adresse»? Und was soll das Wegzugsdatum 25.04.?

### 5. Stundenlöhner: 180 oder 182 Stunden pro Monat? (TF18 Blanc, Kanton BE)
- **Was in der XML steht:** Der satzbestimmende Lohn wird mit **182 h** hochgerechnet (42 h × 52 Wochen ÷ 12) → QST 80.30.
- **Was wir rechnen:** Nach ESTV / Kreisschreiben 45 mit **180 h** → QST 79.25.
- **Frage:** Verlangt das Quality Tool 182? Dann widerspricht es dem Kreisschreiben.

### 6. Rundung der SV-Beiträge — 1–2 Rappen in der Statistik-Summe
- **Was in der XML steht:** Die Summe AHV+ALV+NBU (Feld «SocialContributions») ist auf 5 Rappen gerundet (z.B. 145.40).
- **Was wir rechnen:** Jeder Beitrag rappengenau (Swissdec-Beispiel TF01: ALV 114.59, KTG 3.77). Die Summe ist dann z.B. 145.41. Betroffen: TF03, 06, 17, 18, 21, 24, 29, 35 — immer 1–2 Rappen.
- **Frage:** Toleriert das Quality Tool das, oder muss die Statistik-Summe separat gerundet werden?

### 7. TF11 Bosshard, März 2025 — in der CSV fehlt der Lohn zur Mitarbeiterbeteiligung (20'000)
- **Was in der XML steht:** Lohnausweis «OwnershipRight» (Beteiligungsrechte) **20'000**, AHV-Lohn März 36'550, Bruttolohn Jahr 61'645.
- **Was in der CSV steht:** kein Lohnartenposten für die 20'000 (kein 1960/1961). Nur der Gegenposten «5210 Ausgleich geldwerte Vorteile» ist mit **20'250** drin (250 Geschäftswagen + 20'000 Beteiligung).
- **Folge bei uns:** Wir rechnen aus der CSV → AHV-Lohn 16'550, und weil der Ausgleich von 20'250 abgezogen wird, ohne dass die Beteiligung als Lohn drin ist, wird der Nettolohn **negativ (−10'784.60)**. Ab März weichen bei Bosshard alle kumulierten Werte (ALVZ, KTG 12, UVGZ 12, Lohnausweis) um 20'000 ab.
- **Frage:** Fehlt im CSV-Export die Lohnart für die Mitarbeiterbeteiligung (welche Nummer, welcher Text)? Oder ist das absichtlich eine Falle?

---

### 8. TF15 Degelo, März 2025 — ALV-Höchstlohn bei Eintritt am 28. Februar
- **Situation:** Degelo tritt am 28.2.2025 ein (Stundenlöhner, 1 Tag Februar + ganzer März). Sein Lohn (2'800) liegt weit unter dem ALV-Höchstlohn — die Deckelung greift also nur, wenn der Höchstlohn für den angebrochenen Februar sehr klein gerechnet wird.
- **Was in der XML steht:** ALV-pflichtiger Lohn März nur **205.83** = 12'350 ÷ 60. Das sieht so aus, als würde der Höchstlohn für Februar UND März zusammen auf einen einzigen Tagessatz über 60 Tage gerechnet.
- **Was wir rechnen:** Wie bei TF14 Casanova (Eintritt 28.1., dort stimmt es mit der XML überein): angebrochener Monat = Tage ÷ 30 → kumulierte ALV-Basis **1'646.67**, ALV 18.11 / ALVZ 5.77.
- **Unterschied:** rund 18 Franken ALV+ALVZ. Wir wollen nur die Regel verstehen: Wieso 12'350 ÷ 60 bei Degelo, aber Tage ÷ 30 bei Casanova? Gibt es bei Eintritt am letzten Tag des Monats eine Sonderregel?

---

## B. Fallen in den Testdaten, die wir gefunden haben (nur zur Info)

Wir wissen, dass Swissdec absichtlich Fehler einbaut, damit niemand die Soll-XML abschreibt. Diese haben wir erkannt und bewusst NICHT übernommen:

| Wo | Was in der XML steht | Was richtig ist (CSV) |
|---|---|---|
| Filiale ZG, BUR-Nummer | A38197423 | A38197421 |
| Postleitzahl | «3008.00» | 3008 |
| 13. Monatslohn im Vertrag | Feld «Contractual13th» doppelt | einmal |
| TF14 Egli, November, QST-Code | A0N (34.00) | A0Y (36.05) |
| TF16 Aebi, Dezember, ALV-Höchstlohn Teilmonat | 8'233.35 | 8'233.33 (20/30 von 12'350, rappengenau) |

Dazu die CSV-Datei selbst: Die JSON-Spalte mit den Personendaten ist falsch quotiert (doppelte Anführungszeichen) und muss vor dem Einlesen repariert werden.

---

## C. Was wir bewusst anders machen als das Quality Tool (Gastronomie / L-GAV)

Hier werden wir vermutlich «angekreidet» — das ist Absicht, kein Fehler:

1. **Ferientage laufen immer mit**, auch wenn Ferien prozentual im Stundenlohn drin sind (TF14 Egli). Der Arbeitgeber muss die Ferien trotzdem gewähren; der Prozentzuschlag ist Geld, kein Verzicht auf Tage. Es wird kein zweites Mal Geld ausbezahlt.
2. **Ferienanspruch immer 5 oder 6 Wochen** (L-GAV Gastgewerbe), nicht 20/25/30 Arbeitstage wie im Statistikfeld «LeaveEntitlement». Hat keinen Einfluss auf den Lohn.
3. **Saldi in Tagen und Stunden werden nicht in Geld umgerechnet** — beim Testmandanten sind die Auszahlungs-Schalter aus. Ein negativer Stundensaldo auf dem Beleg ist nur Anzeige.

---

## D. Was wir selbst gelernt und korrigiert haben

- **AHV-Referenzalter (AHV 21):** Wir hatten den Freibetrag und das Ende von ALV/BVG bereits im Geburtsmonat gerechnet. Richtig ist: Beitragspflicht bis Ende des Monats, in dem das Referenzalter erreicht wird, Freibetrag ab dem Folgemonat (TF07 Burri, TF16 Aebi, Dezember 2024). Swissdec hatte recht — korrigiert, gilt auch produktiv.
- **13. Monatslohn:** Bei Festlohn jetzt 1/12 der Basis (wie Swissdec 1200), bei Stundenlohn weiter 8.33 %. Auszahlung ausserhalb Dezember (TF11 Bosshard Mai) über eine eigene Lohnart.
- **Quellensteuer auf 5 Rappen** (Anhang 1): pro Tarifcode-Topf gerundet, der Monatsabzug ist die Differenz.
- **Jahresmodell nur für GE, FR, VD, VS, TI** — alle anderen Kantone Monatsmodell. Rückwirkende Code-Wechsel (z.B. Heirat im Juni, gültig ab April) wandern in den neuen Topf; die Korrektur erscheint im Monat, in dem wir es erfahren (TF23, TF31, TF34 stimmen mit der XML überein).

---

## E. Offen bei uns (nicht für Swissdec, nur damit es nicht vergessen geht)

- März 2025 Tessin ist der nächste Prüfmonat (Austritte TF21 Meier Christian 15.3., TF41 Meier Max 31.3.).
- Die elektronische Monatsmeldung QST (ELM-Builder) nimmt die Adresse noch nicht aus der Wohnort-Historie — der Lohnbeleg schon.
- Tarifdateien GE/FR/VS sind noch nicht geladen (für den Testmandanten nicht nötig).
