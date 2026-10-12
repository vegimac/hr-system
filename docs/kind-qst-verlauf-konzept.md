# Kind: QST-Verlauf (Ausbildung, Haushalt, Unterhalt, QST-Entscheid) — Konzept

Stand: 11.10.2026, v2 (nach Gegenprüfung mit dem Originaltext ESTV-KS 45, Ziff. 4.1/4.3/4.7)
Status: **Konzept, noch nicht gebaut** (Walter liest zuerst)

## Anlass

Fall Azbije Ponik (Sursee): Mirus führte den QST-Code von Hand (C0Y → C4N → C3N → C2N),
ohne erfasste Kinder. OneCrew schlägt den Tarif aus den Kindern vor — dafür fehlt am Kind
ein Verlauf. Heute gibt es nur Häkchen ohne Datum («in Erstausbildung», «lebt im Haushalt»,
«keine Unterhaltspflicht»). Wer ein Häkchen ändert, ändert die Vergangenheit mit. Ein
Lehrabbruch mit neuer Lehre zwei Monate später lässt sich nicht erfassen.

## Grundsätze (Walter 11.10.2026)

1. **Familienzulage und QST komplett getrennt.** Die QST wird nie automatisch aus der
   Familienzulage abgeleitet (und umgekehrt). Zwischen beiden gibt es nur Hinweise.
2. **OneCrew macht nur Vorschläge.** Lohnrelevant sind immer die Bestätigungen:
   - Familienzulage: Entscheid der Ausgleichskasse → von HR als Zulage erfasst (wie heute).
   - QST: QST-Version, die HR nach Vorschlag bzw. Entscheid der Steuerverwaltung selbst
     anlegt (wie heute). Der Vorschlag wird nie automatisch übernommen.
3. **Sachverhalt, steuerliche Würdigung und Lohn sind getrennte Ebenen** (siehe unten).
4. **Fehlende Angaben = «ungeklärt»**, nie ein stilles Nein.

## Rechtsgrundlage (KS 45, Originaltext)

- **Ziff. 4.1:** «Änderungen, die eine neue Tarifeinstufung bedingen (bspw. Heirat, Scheidung,
  Trennung, Geburt von Kindern …), werden bei der Quellenbesteuerung **ab Beginn des auf die
  Änderung folgenden Monats** entsprechend berücksichtigt.» Massgebend sind die Verhältnisse
  im Zeitpunkt der Auszahlung bzw. Fälligkeit.
- **Ziff. 4.7:** Kinderabzug für Kinder, die minderjährig oder «in beruflicher bzw. schulischer
  **Erstausbildung**» sind und für deren Unterhalt der Arbeitnehmer zur Hauptsache aufkommt.
  Bei volljährigen Kindern Nachweis der Erstausbildung, ausdrücklich genannt:
  **Immatrikulationsbestätigung, Lehrvertrag, Zulagenentscheid**. Bei Alleinstehenden
  zusätzlich Nachweis «gleicher Haushalt» (Bescheinigung der Wohnsitzgemeinde).
- **Beispiel Tarif C (Ziff. 4.7):** Ehepaar, beide erwerbstätig, zwei Kinder → **C2 bei
  beiden Arbeitgebern**.
- **Ziff. 4.3:** Konkubinat mit gemeinsamem Kind → H mit Kinderabzug für den Elternteil mit
  dem höheren Bruttoeinkommen, der andere A0; im Zweifel A0 (bereits umgesetzt, siehe
  `docs/konkubinat-qst-konzept.md`).

## Monatsregel: Änderung wirkt ab Folgemonat

**Korrektur gegenüber v1:** Nicht «Verhältnisse am Monatsersten», sondern **Monat des
Ereignisses + 1**. Unterschied nur bei Ereignissen am Monatsersten — bei Lehren aber der
Normalfall (Lehrbeginn meist 1. August).

Gespeichert wird immer das **echte Datum** des Ereignisses; die QST-Wirkung wird daraus
gerechnet:

| Ereignis | Wirkung QST |
|---|---|
| Geburt 01.03. oder 15.03. | Kinderabzug ab **April** |
| 18. Geburtstag 01.03. oder 15.03., keine Ausbildung | März noch, ab **April** kein Abzug |
| Ausbildung beginnt 01.08. oder 15.08. | ab **September** |
| Ausbildung endet 31.07. (oder 15.07.) | Juli noch, ab **August** kein Abzug |
| Lehrabbruch 31.03., neue Lehre 01.06. | bis März; April–Juni keiner; ab **Juli** wieder |
| Lehrstellenwechsel nahtlos (bis 31.07. / ab 01.08.) | **keine Änderung** (Abschnitte, die lückenlos aneinander anschliessen, gelten als eine Ausbildung) |
| Verhältnis ändert (Haushalt, Unterhalt) am Tag D | ab dem Ersten des Monats nach D |
| QST-Entscheid der Steuerverwaltung | **so wie im Entscheid datiert** (keine Verschiebung) |

Formel je Ausbildungs-Abschnitt (nach Zusammenlegen lückenloser Abschnitte):
Wirkung = vom 1. des Monats nach «von» bis zum Ende des Monats von «bis».
Minderjährigkeit: vom 1. des Monats nach der Geburt bis Ende des Monats des 18. Geburtstags.
Das Kind zählt in einem Monat, wenn einer dieser Wirkungszeiträume den Monat abdeckt (eine
Lehre ab 16 geht so nahtlos über den 18. Geburtstag weiter).

Grenzfall Übergangszeit (z.B. Matura im Juni, Studium ab September): nicht automatisch
überbrücken. HR erfasst entweder einen durchgehenden Abschnitt (wenn die Steuerverwaltung das
so anerkennt) oder setzt den QST-Entscheid «in Prüfung».

Gilt für das Monatsmodell. Jahresmodell-Kantone (GE, FR, VD, VS, TI) beim Bau gesondert
prüfen — Schaub Restaurants hat dort heute keine Filiale.

## Vier Ebenen

| Ebene | Inhalt | Wer setzt | lohnrelevant |
|---|---|---|---|
| 1. Ausbildung | tatsächliche Ausbildungszeiträume | HR (Belege) | nein |
| 2. Verhältnisse | Haushalt, Unterhalt | HR | nein |
| 3. QST-Entscheid zum Kind | anerkannt / nicht anerkannt / in Prüfung, mit Zeitraum | HR, i.d.R. nach Steuerverwaltung | nein (fliesst in den Vorschlag) |
| 4. QST-Version | Tarifcode für den Lohn | HR legt selbst an | **ja** |

### Ebene 1: Ausbildung — Abschnitte mit Lücken

| Feld | Bedeutung |
|---|---|
| von | Beginn; **leer = unbekannt** (nur beim Altbestand, siehe unten) |
| bis | Ende; leer = läuft |
| Art | Lehre, Schule, Studium, Brückenangebot, andere |
| Ende-Grund | abgeschlossen / abgebrochen / leer |
| Erstausbildung | ja / nein / ungeklärt (z.B. Studium nach abgeschlossener Lehre = ungeklärt) |
| Beleg | Lehrvertrag, Immatrikulation, **Zulagenentscheid** (Dokument-Ablage) |
| geprüft | ja / nein («nicht geprüft» beim Altbestand) |
| Erfahren am | wann HR davon erfahren hat |
| Erfasst am / von | technisch |

Abbruch = Abschnitt mit «bis» + «abgebrochen»; neue Lehre = neuer Abschnitt. Ein Abbruch
verbraucht die Erstausbildung nicht: neue Lehre, Lehrstellenwechsel, Berufswechsel ohne
Abschluss, Studium nach Matura bleiben Erstausbildung. Studium nach abgeschlossener Lehre
und Zweitausbildungen: OneCrew entscheidet nicht selbst → «ungeklärt», Klärung über Ebene 3.
Für den Vorschlag zählt ein Abschnitt nur mit Erstausbildung = ja.

### Ebene 2: Verhältnisse — Versionen «ab Datum»

| Feld | Werte |
|---|---|
| gültig ab | Datum der Änderung |
| lebt im gleichen Haushalt | ja / nein / ungeklärt |
| Eltern tragen den Unterhalt zur Hauptsache | ja / nein / ungeklärt |
| Erfahren am | wann HR davon erfahren hat |

- **Haushalt** zählt nur bei Tarif H. Massgebend ist der steuerliche Wohnsitz (Bescheinigung
  der Wohnsitzgemeinde), nicht der Aufenthalt: Wer als Wochenaufenthalter auswärts studiert,
  wohnt weiter im Haushalt. Ein Auszug fürs Studium führt zu «ungeklärt» + Hinweis, nie
  automatisch zu «nein».
- **Unterhalt** meint die Eltern (bei Ehepaaren gemeinsam): bei Tarif C zählt das Kind bei
  beiden Ehegatten (KS-45-Beispiel C2). Nein ⇒ zählt nicht. Ungeklärt ⇒ zählt nicht, Hinweis
  (wie heute bei unklarer Unterhaltssituation).
- **Konkubinat:** bleibt bei der bestehenden Entscheidtabelle (höheres Einkommen → H1, der
  andere A0; gemischte Fälle → «mit QST-Behörde abklären»).

### Ebene 3: QST-Entscheid zum Kind (ersetzt «QST-abzugsberechtigt von/bis»)

| Feld | Werte |
|---|---|
| Status | anerkannt / nicht anerkannt / in Prüfung |
| von / bis | Zeitraum laut Entscheid |
| Grund, Beleg | z.B. Verfügung Steueramt, Abklärung per Mail |
| Erfahren am | wann HR davon erfahren hat |

Nur nötig, wenn die Steuerverwaltung abweichend entscheidet oder eine Frage offen ist. Ohne
Eintrag gilt die Regel aus Ebene 1 + 2. «Anerkannt» / «nicht anerkannt» stechen die Regel
für ihren Zeitraum, ohne die Ausbildungsdaten zu verfälschen. «In Prüfung» → Vorschlag mit
gelbem Status. Das heutige Feld «QST-abzugsberechtigt von/bis» wird beim Umbau als
Entscheid «anerkannt» (bzw. Ende) übernommen.

## Tarifvorschlag

Für einen Stichtag (Gültig-ab der neuen QST-Version) zählt OneCrew die Kinder nach der
Monatsregel aus Ebene 1–3 und zeigt pro Kind den Grund. Eine Stelle für die Regel:
`QstTarifVorschlagLogic` (rein, getestet), genutzt von Tarifvorschlag, QST-Pflicht-Check
(Kinderziffer-Abgleich) und der QST-Zeile auf der Kinder-Karte. Status: grün (alles geklärt),
gelb (etwas «ungeklärt», «nicht geprüft» oder «in Prüfung» — mit genauer Angabe).

## Rückwirkend Bekanntes (Wissens-Achse)

- «gültig ab/bis» = was tatsächlich galt; «Erfahren am» = wann HR es wusste. Eine spät bekannte
  Tatsache wird nicht als «nie geschehen» behandelt — sie wird mit dem echten Datum erfasst.
- Der Lohnlauf rechnet weiter mit der QST-Version, die gilt (wie heute). Eine Änderung am Kind
  ändert keine QST-Version und keinen Lohn.
- Betrifft die Änderung bereits abgerechnete Monate, öffnet OneCrew eine **Pendenz
  «QST-Korrektur prüfen»** (Kinderziffer-Abgleich). Eine Korrektur entsteht erst, wenn HR
  bewusst eine rückwirkende QST-Version anlegt (bestehender Weg: QST-Korrektur-Posten, K1).
- Vorjahre: nicht über den Lohn, sondern über die Steuerverwaltung (Neuberechnung bis Ende
  März des Folgejahres, KS 45 Ziff. 4.3/10).

## Was wegfällt

- **Automatische Ableitung aus der Ausbildungszulage** in `QstTarifVorschlagService`,
  `QstPflichtCheckService` und in der QST-Zeile der Kinder-Karte (`QstZeitraeume`, Parameter
  `azPerioden`) — im selben Schritt wie der Umbau, nicht vorher.
- Die Häkchen «in Erstausbildung» / «lebt im Haushalt» / «keine Unterhaltspflicht» und das
  Feld «QST-abzugsberechtigt von/bis» als Quelle für die QST (Spalten bleiben als Altbestand).

## Hinweise statt Übernahme

| Hinweis | Wann | Knopf |
|---|---|---|
| «Ausbildungszulage läuft, aber keine Ausbildung für die QST erfasst» | AZ aktiv, kein Ausbildungs-Abschnitt | **«Als QST-Ausbildung übernehmen (Beleg: Zulagenentscheid)»** — legt nach Bestätigung durch HR einen Abschnitt mit Zeitraum und Beleg der Zulage an (KS 45 nennt den Zulagenentscheid als Nachweis) |
| «Ausbildung für die QST erfasst, aber keine Ausbildungszulage» | Kind 16–25 in Ausbildung, keine AZ | — (Familienzulage prüfen) |
| «Ausbildung nicht geprüft» (Pendenz) | Abschnitt aus dem Altbestand | — |
| «Haushalt/Unterhalt ungeklärt» | volljähriges Kind mit «ungeklärt» | — |
| «QST-Korrektur prüfen» (Pendenz) | Kinderziffer-Abgleich: gültige QST-Version ≠ Vorschlag, auch rückwirkend | — |

Kein Hinweis ändert selbständig eine QST-Version oder eine Zulage.

## Altbestand (v2, ersetzt «ab 18. Geburtstag»)

- Häkchen «in Erstausbildung» = ja → Ausbildungs-Abschnitt mit **Beginn unbekannt**, Ende offen,
  Erstausbildung = ja, **geprüft = nein**. Es wird kein Datum erfunden. Der Vorschlag zählt das
  Kind weiter (gelb «Ausbildung nicht geprüft»), damit für die laufend nachgerechneten Monate
  2026 kein Kind still wegfällt. Pendenz bis HR von–bis + Beleg erfasst hat.
- Kinder, die nur eine Ausbildungszulage haben, bekommen **keinen** Abschnitt — nur den
  Hinweis mit Übernahme-Knopf.
- «lebt im Haushalt» → Verhältnis-Version mit dem heutigen Wert, gültig ab Geburtsdatum.
- «keine Unterhaltspflicht» = ja → Verhältnis-Version «Unterhalt zur Hauptsache = nein».
- «QST-abzugsberechtigt von/bis» → QST-Entscheid zum Kind «anerkannt» mit diesem Zeitraum.
- Bestehende QST-Versionen bleiben unverändert.

## Oberfläche

- **Kinder-Karte:** QST-Zeile zeigt die Wirkungszeiträume nach der Monatsregel, mit Lücken
  und Grund (z.B. «bis 03.2026 · Lehre abgebrochen», «ab 07.2026 · Lehre»), gelb bei
  ungeklärt / nicht geprüft / in Prüfung.
- **Familien-Fenster, Abschnitt «Quellensteuer»:** Ausbildungs-Abschnitte, Verhältnis-
  Versionen, QST-Entscheide (je + Erfassen, ⋮ Bearbeiten/Löschen, Beleg).
- Familienzulagen bleiben, wo sie sind (Zulagen am Kind), mit ihrer eigenen Monatsregel
  (Anspruch ab Monat des Ausbildungsbeginns, 30-Tage-Basis — unverändert).

## Technik (beim Bau)

- Drei neue Tabellen (Namen beim Bau): Ausbildungs-Abschnitte, Verhältnis-Versionen,
  QST-Entscheide je Familienmitglied. Datumsfelder `date`, Zeitstempel
  `timestamp without time zone` + `HasColumnType`, `DateTime.Now`. Drei-Zustands-Felder als
  `boolean NULL` (NULL = ungeklärt). Startblock in `Program.cs` vor dem Schema-Check +
  **SchemaStand +1**; Umwandlung Altbestand als idempotente Einmal-Migration (Sentinel),
  läuft in beiden Datenbanken.
- Kleine Abweichung im heutigen Code mitkorrigieren: Geburt am Monatsersten zählt heute schon
  im selben Monat (`IstQstBerechtigt`: `dob > stichtag`) — richtig ist ab Folgemonat.

## Tests

| Fall | Erwartet |
|---|---|
| Geburt am 1. / am 15. | Abzug ab Folgemonat |
| 18. Geburtstag am 1. / am 15., ohne Ausbildung | Geburtstagsmonat noch, ab Folgemonat nicht |
| Lehre ab 16, 18. Geburtstag während der Lehre | durchgehend |
| Lehrbeginn am 1. / am 15. | ab Folgemonat |
| Lehrende 31.07. | Juli noch, ab August nicht |
| Lehrabbruch 31.03., neue Lehre 01.06. | bis März, April–Juni nicht, ab Juli |
| Lehrstellenwechsel nahtlos | keine Lücke |
| Studium nach Lehrabschluss | «ungeklärt», zählt nicht ohne Entscheid «anerkannt» |
| Verheiratet, beide erwerbstätig | Kind zählt bei C (beide Arbeitgeber) |
| Konkubinat, beide erwerbstätig | bestehende Entscheidtabelle, nie beide H1 |
| Alleinstehend, Kind studiert auswärts (Wochenaufenthalt) | Haushalt bleibt ja; Auszug ⇒ ungeklärt, nicht nein |
| Kind mit eigenem Einkommen, Unterhalt nein | zählt nicht |
| Änderung erst 6 Monate später bekannt | echter Zeitraum gespeichert, Pendenz «QST-Korrektur prüfen», keine automatische Korrektur |
| QST-Entscheid «nicht anerkannt» trotz Ausbildung | zählt nicht im Entscheid-Zeitraum, Ausbildungsdaten unverändert |
| Altbestand Häkchen ohne Datum | Beginn unbekannt, zählt weiter, gelb, Pendenz |
| Nur Ausbildungszulage, keine QST-Ausbildung | zählt nicht, Hinweis mit Übernahme-Knopf |
| Übernahme-Knopf | Abschnitt mit Zeitraum + Beleg der Zulage, erst nach Bestätigung |

## Umsetzung (gebaut 11.10.2026, Schema-Stand 60)

- **«Erfahren am»** (vorher offen, Walter 11.10.2026 angenommen): Der Vorschlag zeigt den
  tatsächlichen Sachverhalt. «Erfahren am» ist nur noch Info; abgerechnete Monate, deren
  QST-Version nicht zum Verlauf passt, meldet die Warnung «QST-Korrektur prüfen» (erledigt,
  sobald HR eine rückwirkende QST-Version anlegt).
- **Eine Regel-Stelle:** `Services/KindQstVerlauf.cs` (rein, Tests `KindQstVerlaufTests`),
  geladen über `Services/KindQstVerlaufLader.cs`. Benutzt von Tarifvorschlag, QST-Pflicht-Check
  (Kinderziffer-Wächter, «QST-Korrektur prüfen», offene Punkte), Familie-Karte, QST-Fenster
  (zählt aus `qstVerlauf.wirkung`) und ELM-Meldung (`ElmZeitraeume`).
- **Tabellen:** `family_member_ausbildung`, `family_member_verhaeltnis`, `family_member_qst_entscheid`.
  Endpunkte unter `api/employees/{id}/family/{kindId}/…` (`qst-verlauf`, `ausbildung`,
  `ausbildung/aus-zulage/{zulageId}`, `verhaeltnis`, `qst-entscheid`).
- **Unterhalt nur noch im Verlauf (Walter 11.10.2026, Schema-Stand 61):** Das Häkchen
  «keine Unterhaltspflicht» ist weg. Beim Umstieg wurde es zu Verhältnis-Versionen
  «Unterhalt zur Hauptsache = nein» (Bemerkung «aus Häkchen …»). Unterhalt kann sich damit ab
  Datum ändern; ein neues Kind bekommt Haushalt + Unterhalt als Startwerte der ersten Version.
  **Familienzulagen werden ohne Unterhalt nicht mehr gesperrt** — die Familienausgleichskasse
  entscheidet; am Kind steht nur der Hinweis «Verfügung prüfen».
- **Snapshot-Felder bleiben:** `in_erstausbildung` (nachgeführt = heute läuft eine Erstausbildung),
  `lebt_im_haushalt` und `keine_unterhaltspflicht` (= Abbild der heute gültigen Verhältnis-Version,
  nachgeführt von `KindQstVerlaufLader.SnapshotNachfuehrenAsync`) lesen weiterhin
  QST-Infoformular, QST-Anmeldung und Herleitungs-Snapshot. Nie von Hand setzen.
- **Maske (Walter 11.10.2026):** Kind in vier Blöcken — Kind (Beziehung, anderer Elternteil,
  Sorge, erwerbsunfähig), Wohnen & Unterhalt (Verlauf + Wohnadresse mit Land), Ausbildung,
  Quellensteuer (Ergebnis + Entscheide). Siehe `docs/famz-anmeldung-konzept.md`. `qst_deductible_from/until` werden nicht mehr gepflegt
  (Altbestand → QST-Entscheide, Swissdec-Testdaten schreiben Entscheide mit Quelle «SWISSDEC»).
  Vereinfacht (Walter 11.10.2026: «beim Kind nicht zuviel über QST — nur das Notwendigste»):
  kein Block «Aufenthalt» beim Kind; Ausbildung erst ab 15 Jahren oder wenn etwas erfasst ist;
  Unterhalts-Hinweis nur bei «Unterhalt nein»; Quellensteuer = eine Zeile «Zählt für die
  Kinderziffer» + Warnungen, Entscheide nur, wenn vorhanden (Knopf «+ Entscheid der Steuerverwaltung»).
  Erfassen/Bearbeiten = je ein eigenes kleines Fenster (`kqvFenster`), kein Formular mehr
  mitten in der Kind-Maske: «Wohnen & Unterhalt» (`kqvVerhaeltnisDialog`: Gültig ab, im
  Haushalt, Unterhalt, Bemerkung), «Ausbildung»
  (`kqvAusbildungDialog`: Von, Bis, Art, Erstausbildung, Beleg, Ende nur mit Bis, Bemerkung,
  Häkchen «Beleg gesehen» = geprüft; mit Beleg immer geprüft) und «Entscheid der
  Steuerverwaltung» (`kqvEntscheidDialog`: Entscheid, Von, Bis, Beleg, Grund). «Erfahren am»
  wird nicht mehr abgefragt (bleibt beim Bearbeiten erhalten).
  Pro Bereich steht nur der heute gültige Eintrag; bei mehreren Einträgen klappt
  «Verlauf (n)» alle auf (`kqvMitVerlauf`). Wohnen, Ausbildung und Quellensteuer sind
  durch eine Linie getrennt.
- **Ausbildungszulage ohne QST-Ausbildung:** Hinweis an der Karte und im QST-Pflicht-Check,
  im Familien-Fenster Knopf «Als QST-Ausbildung übernehmen» (Zeitraum + Beleg der Zulage).

## Geklärt (v1 → v2)

- «Erstausbildung» ist der richtige Begriff (KS 45 Ziff. 4.7).
- «QST-abzugsberechtigt von/bis» bleibt fachlich erhalten, als QST-Entscheid zum Kind (Ebene 3).
- Monatsregel = Änderung ab Folgemonat (KS 45 Ziff. 4.1), nicht Zustand am Monatsersten.
