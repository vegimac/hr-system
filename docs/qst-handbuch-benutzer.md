# QST-Handbuch für OneCrew-Benutzer

Quellensteuer in OneCrew — was sie ist, wie der Tarif entsteht, wo du was
pflegst, und was OneCrew bewusst NICHT selbst entscheidet.

---

## 1 · Was ist die Quellensteuer?

Die Quellensteuer (QST) ist die Steuer, die du bestimmten Mitarbeitenden
**direkt vom Lohn abziehst** und an den Kanton ablieferst. Sie betrifft
grundsätzlich alle MA ohne Schweizer Pass oder C-Bewilligung.

Wichtig zu wissen: **Zu viel abgezogene QST kann der MA zurückfordern —
zu wenig abgezogene haftet der Arbeitgeber.** Darum arbeitet OneCrew im
Zweifel immer mit der sicheren, höheren Variante und fordert dich auf,
Unklarheiten mit der Steuerbehörde zu klären.

## 2 · Ist der MA überhaupt QST-pflichtig?

OneCrew prüft das automatisch, in zwei Schritten.

**Schritt 1 — Wohnt der MA steuerlich in der Schweiz?** Massgebend ist die
Hauptwohnsitzadresse (kommt aus easy@work).

- **Wohnsitz Schweiz** → weiter mit Schritt 2.
- **Wohnsitz Ausland** → QST auf den Schweizer Lohn ist grundsätzlich
  IMMER geschuldet — egal welche Nationalität oder Bewilligung. Auch ein
  Schweizer Pass oder ein C-Ausweis befreit bei Auslandswohnsitz NICHT
  automatisch. Details zu Grenzgängern: Kapitel 8.

**Schritt 2 — Befreiungsgründe (nur bei Wohnsitz Schweiz).** Ein MA ist
QST-pflichtig, AUSSER einer dieser fünf Punkte trifft zu:

| # | Befreiungsgrund | Nachweis |
|---|---|---|
| 1 | Schweizer Bürger/in | Nationalität CH |
| 2 | C-Ausweis (Niederlassung), gültig | Bewilligungshistorie |
| 3 | Befreiung durch die Steuerbehörde | Schreiben als Dokument hinterlegt |
| 4 | Verheiratet mit CH-Bürger/in | Familie-Tab |
| 5 | Verheiratet mit C-Ausweis-Inhaber/in | Familie-Tab |

Drei Stolperfallen:

- **Konkubinat befreit NIE.** Ein Konkubinatspartner mit CH-Pass oder C
  befreit den MA nicht — nur Ehe/eingetragene Partnerschaft zählt. Im
  Familie-Tab darum immer den richtigen Typ wählen.
- **Der Ehepartner muss selbst in der Schweiz wohnen.** Wohnt er eindeutig
  im Ausland, gilt die Befreiung nicht — der MA bleibt pflichtig. Ist die
  Wohnsituation des Partners unklar → mit der Behörde klären.
- **Schon die Trennung beendet die Partner-Befreiung** — nicht erst die
  Scheidung. Wirksam ab Folgemonat (Trennung 15.08. → QST-Pflicht ab
  01.09.).

## 3 · Wie der Tarifcode aufgebaut ist

```
   C    2    N
   │    │    └─ Kirchensteuer: Y = ja / N = nein
   │    └────── Kinderziffer (0–9)
   └─────────── Tarifbuchstabe
```

Dazu kommt der **Kanton** — er bestimmt, welche kantonale Tariftabelle
gilt: bei Wohnsitz Schweiz der Wohnkanton; bei Wohnsitz Ausland der Kanton
der Filiale, in der gearbeitet wird — bzw. bei einem Schweizer
Wochenaufenthalt der Wochenaufenthaltskanton (Kapitel 8).

## 4 · Wo pflege ich was? (die Quellen)

Der Tarif wird NICHT von Hand gewählt — OneCrew leitet ihn aus den
Stammdaten her. Du pflegst nur die Quellen:

| Angabe | Wo pflegen |
|---|---|
| Nationalität, Bewilligung, Adresse, Zivilstand | easy@work (wird synchronisiert) |
| Zivilstand seit, Konfession, «Getrennt seit» | MA-Maske (Übersicht → Personalien) |
| Ehepartner: erwerbstätig / Ersatzeinkünfte, Arbeitgeber | Familie-Tab |
| Konkubinatspartner + Einkommensfrage | Familie-Tab |
| Kinder: Geburtsdatum, im Haushalt, gemeinsames Kind, Erstausbildung | Familie-Tab |
| Wochenaufenthaltsadresse | MA → Weitere Adressen (Typ «Wochenaufenthalt») |
| Gültig-ab der QST-Version, Behördenbewilligung, Sonderfälle | QST-Erfassung |

**Ersatzeinkünfte des Partners** (z.B. Taggelder, Teilinvaliditätsrenten,
Arbeitslosengeld, Erwerbsersatz/EO bei Schweizer Dienst) zählen wie
Erwerbstätigkeit — auch wenn sie im Ausland erzielt oder bezogen werden.

## 5 · Wie der Tarifbuchstabe entsteht

**Verheiratet / eingetragene Partnerschaft** (zusammenlebend):

- Partner erwerbstätig ODER Ersatzeinkünfte → **C** (Doppelverdiener)
- Partner weder noch → **B** (Alleinverdiener)
- Partner-Einkommen läuft nachweislich nur im vereinfachten
  Abrechnungsverfahren (Tarif E) → **B**
- Frage offen → sicherheitshalber **C**, mit Hinweis den Familie-Tab zu
  vervollständigen

**Rechtlich verheiratet, tatsächlich getrennt** → ab Folgemonat wie
alleinstehend.

**Alleinstehend (ledig, geschieden, verwitwet, getrennt):**

- Kein QST-berechtigtes Kind → **A0**
- Berechtigte Kinder im eigenen Haushalt → **H** (Halbfamilie)
- Kinder nur AUSSERHALB des Haushalts → **A0** (eine Kinderziffer auf A
  gibt es nur mit Behördenbewilligung, Kapitel 10)
- Konkubinat → Kapitel 6

**Welche Kinder zählen?** Minderjährige — oder Volljährige in beruflicher/
schulischer Erstausbildung (mit Ausbildungsnachweis), sofern der MA für den
Unterhalt zur Hauptsache aufkommt. Bei minderjährigen Kindern im eigenen
Haushalt stellt OneCrew keine Extrafrage; bei Volljährigen und Kindern
ausser Haus fragt es nach. Ist die Unterhaltssituation bei einem Kind
ausser Haus unklar, zählt es NICHT in die Ziffer — es wird nicht geraten.

**Halbfamilie konkret:**

| Situation | Tarif |
|---|---|
| Kind lebt beim MA, alleinerziehend | **H** |
| Kind beim MA, MA erhält Alimente | **H** — erhaltene Alimente ändern den Code nicht |
| Kind beim Ex-Partner, MA zahlt Alimente | **A0** — der Alimentenabzug läuft über die Steuerbehörde (Tarifkorrektur/NOV), nicht über den Tarif |
| Volljähriges Kind mit eigenem Haushalt, MA zahlt Unterhalt | **A0** |
| Gemischt (ein Kind im Haushalt, eines beim Ex) | **H** — die Ziffer zählt automatisch nur die Haushaltskinder |
| Alternierende Obhut (Kind je hälftig bei beiden Eltern) | **kein Automatismus** — Kapitel 11 |

Zum Vergleich: Bei Verheirateten (B/C) zählt die Ziffer ALLE berechtigten
Kinder, auch ausserhalb des Haushalts.

## 6 · Konkubinat mit Kindern

Nur EIN Elternteil bekommt H — nie beide. OneCrew entscheidet so:

| Kind gemeinsam mit dem Konkubinatspartner? | Wer verdient mehr? | Tarif MA |
|---|---|---|
| ja | MA | **H** |
| ja | Partner | **A0** (H gehört dem Partner) |
| ja | Partner ist gar nicht erwerbstätig (kein Erwerb, keine Ersatzeinkünfte) | **H** — der MA ist zwangsläufig Hauptunterhalt |
| ja | Frage offen | sicherheitshalber **A0** + Hinweis |
| nein (Kind aus früherer Beziehung, im Haushalt) | egal | **H** (MA ist alleinerziehend) |
| gemischt (gemeinsame UND nicht-gemeinsame Kinder) | egal | **kein Automatismus** — Kapitel 11 |

## 7 · Kirchensteuer (Y/N)

Die Konfession wird in der MA-Maske gepflegt. Y-fähig sind:
Röm.-katholisch, Christ-katholisch, Evang.-reformiert und die
Israelitische Kultusgemeinschaft. «Keine» und «Andere» (z.B. muslimisch,
orthodox, freikirchlich) sind immer N.

Zusätzlich zählt der KANTON: In Kantonen ohne Kirchensteuer in der QST
(z.B. GE, NE, VD, VS, TI) gibt es nie ein Y — massgebend ist immer die
offizielle kantonale Tariftabelle. Ist die Konfession nicht erfasst, gilt
das Sicherheitsprinzip: Y, wo der Kanton eines kennt (sonst N), plus die
Aufforderung, die Konfession nachzutragen.

## 8 · Wohnsituation: Wochenaufenthalter und Grenzgänger

**Grundsatz:** Die easy@work-Adresse IST der Hauptwohnsitz. Eine
Wochenaufenthaltsadresse wird als Zusatzadresse (Typ «Wochenaufenthalt»)
beim MA erfasst — sie ist der zusätzliche Aufenthaltsort, keine zweite
Wahrheit.

- **Schweizer Wochenaufenthalt:** ändert am QST-Kanton NICHTS. Beispiel:
  Hauptwohnsitz Sursee LU, Wochenzimmer Zofingen AG → QST-Kanton LU. Es
  gibt keinen eigenen Tarif — das Häkchen setzt OneCrew automatisch aus
  der Zusatzadresse.
- **Wohnsitz Ausland:** QST-Kanton = Kanton der Filiale, in der gearbeitet
  wird — NICHT der Firmensitz. Hat der MA zusätzlich eine Schweizer
  Wochenaufenthaltsadresse, gilt deren Kanton (Beispiel: Wohnsitz
  Deutschland + Zimmer in Basel → QST-Kanton BS).
- **Mehrere parallele Arbeitskantone:** klärt OneCrew nicht selbst —
  Kapitel 11.

**Grenzgänger (Wohnsitz im Nachbarland):** je nach Land gelten
Sondertarife — aber NUR mit den nötigen Nachweisen:

| Wohnsitz | Was gilt |
|---|---|
| Deutschland | Sondertarife L/M/N/P (Entsprechungen von A/B/C/H, max. 4.5 %) — NUR mit gültiger Ansässigkeitsbescheinigung Gre-1. Ohne Nachweis: normale Tarife. Bei mehr als 60 beruflichen Nichtrückkehrtagen pro Jahr (anteilig bei Teilzeit/unterjährig) entfällt der Status |
| Frankreich | In BE/BS/BL/JU/NE/SO/VD/VS: keine CH-QST (SFN) — NUR mit jährlicher Ansässigkeitsbescheinigung und erfüllter Grenzgängerregel (max. 45 Nichtrückkehrtage, max. 40 % Telearbeit). Sonst und in allen anderen Kantonen: normale Tarife. Ab Steuerjahr 2026 gilt zusätzlich eine Arbeitgeber-Jahresmeldung für alle in FR wohnhaften MA |
| Italien | Nur «neue» Grenzgänger (ab 17.07.2023) mit Wohnsitz in einer Grenzgemeinde und Arbeit in TI/GR/VS: Sondertarife R/S/T/U (80 %). Frühere Grenzgänger: normale Tarife |
| Liechtenstein | Echte Grenzgänger: keine CH-QST. Bei mehr als 45 Nichtrückkehrtagen lebt das Schweizer Besteuerungsrecht auf (Arbeitgebernachweis bis Ende Februar des Folgejahres) |
| Österreich / übrige | Normale Tarife |

Ein MA mit Auslandswohnsitz, der unter der Woche in der Schweiz wohnt und
nicht täglich heimkehrt, ist KEIN Grenzgänger — er erhält die normalen
Tarife.

## 9 · Wann gilt was? (zeitliche Regeln)

- Massgebend sind die Verhältnisse am **Monatsanfang**; Änderungen wirken
  ab **Folgemonat**. Das gilt für Heirat, Scheidung, Trennung, Verwitwung,
  Geburt und den Einzug eines Kindes in den Haushalt.
- Einzige Ausnahme: Nimmt der Ehepartner eine Erwerbstätigkeit AUF, gilt C
  beim Partner sofort, beim MA ab Folgemonat.
- **Kind wird 18:** Die Ziffer bleibt für den Geburtstagsmonat bestehen.
  Ab dem Folgemonat zählt das Kind nur weiter, wenn es in Erstausbildung
  ist (Nachweis, z.B. laufende Ausbildungszulage).
- **Verspätet gemeldete Änderungen** (z.B. Heirat drei Monate her): Du
  erfasst die neue QST-Version rückwirkend mit einem Korrektur-Grund. Die
  bereits abgeschlossenen Löhne bleiben unverändert — OneCrew rechnet die
  Differenz pro Monat aus und **verrechnet sie automatisch im nächsten
  Lohnlauf** (Nachbelastung oder Erstattung, als eigene Zeile auf dem
  Lohnzettel). Korrekturen, die ein VORJAHR betreffen, laufen nicht über
  den Lohnlauf, sondern direkt über die Steuerverwaltung.

## 10 · Sonderfälle mit Beleg-Pflicht

- **Ersatztarif:** Weist sich eine Person nicht zuverlässig aus (z.B.
  Zivilstand unbelegt), gilt der Ersatztarif — A0 für Ledige/Unbestimmte,
  C0 für Verheiratete, mit Kirchensteuer, soweit der Kanton eine kennt.
  Er bleibt, bis die Angaben belegt sind.
- **Kinderabzug auf Tarif A (A1–A9):** gibt es NUR mit ausdrücklicher
  Verfügung der Steuerbehörde — das Schreiben muss beim MA als Dokument
  abgelegt und in der QST-Erfassung verknüpft sein, sonst lässt sich das
  Häkchen nicht speichern.
- **Manueller Prozentsatz:** nur mit Behördenverfügung.
- **Behörden-Befreiung:** braucht das Bestätigungsschreiben der
  Steuerbehörde als hinterlegtes Dokument.
- **Weitere Beschäftigungen des MA:** ändern den Tarifbuchstaben NICHT —
  sie erhöhen nur den satzbestimmenden Lohn (die Steuer wird zum Satz des
  Gesamteinkommens gerechnet). Kann das Gesamteinkommen nicht ermittelt
  werden, greift als letzte Stufe die Medianlohn-Regel der ESTV — das ist
  kein Behördenentscheid, sondern der offizielle Fallback.

## 11 · Was OneCrew bewusst NICHT selbst entscheidet

OneCrew automatisiert nur Tarife, die aus den Angaben eindeutig folgen.
Alles andere zeigt es **ROT** an — mit dem genauen Grund und der
Aufforderung, die Situation mit der QST-Behörde zu klären. Es gibt dabei
keinen Einheits-Fallback: jede Lücke hat ihre eigene, sichere Konsequenz.

**ROT, weil Angaben fehlen** (der Lohn kann trotzdem laufen — mit dem
sicheren Übergangswert):

| Lücke | Übergangsweise gilt |
|---|---|
| Erwerbstätigkeit des Ehepartners unbekannt | C |
| Gre-1-Nachweis (DE) fehlt | normale Tarife statt Grenzgängertarife |
| FR-Bescheinigung fehlt | kein SFN — normale Tarife |
| Konfession fehlt | Y, wo der Kanton eines kennt (sonst N) |
| Zivilstand unbelegt | Ersatztarif A0/C0 |

**ROT, weil der Fall fachlich komplex ist** (OneCrew interpretiert nicht):

- **Alternierende Obhut** (Kind je hälftig bei getrennten Eltern) — nur
  ein Elternteil bekommt H; wem er zusteht, entscheidet die Behörde.
  Übergangsweise rechnet OneCrew mit A0.
- **Gemischtes Konkubinat** (gemeinsame und nicht-gemeinsame Kinder) —
  übergangsweise A0, Klärung mit der Behörde.
- **Mehrere parallele Arbeitskantone bei Auslandswohnsitz** — hier setzt
  OneCrew bewusst KEINEN Kanton und macht keine definitive QST-Abrechnung,
  bis die Zuständigkeit geklärt ist.
- **Ansässigkeit, die dokumentiert von der Hauptadresse abweicht** —
  Behördenfall, kein Automatismus.

Und generell: **OneCrew erfindet nie eigene Steuersätze oder Prozente** —
gerechnet wird ausschliesslich mit den offiziellen kantonalen Tariftabellen
der ESTV.

## 12 · Der Ablauf in der Praxis (Kurzfassung)

1. MA-Stammdaten via easy@work komplett (Adresse, Zivilstand, Bewilligung).
2. MA-Maske: Konfession, Zivilstand seit.
3. Familie-Tab vollständig (Partner mit Erwerbsfrage, Kinder mit
   Haushalt/Erstausbildung).
4. QST-Erfassung öffnen: OneCrew zeigt den hergeleiteten Tarif als
   RESULTAT zuunterst — du gibst nur das «Gültig ab» ein und prüfst.
5. Bei Rot: dem Hinweis folgen (Angabe nachtragen oder Behörde fragen).
6. QST-Anmeldung (PDF) an die kantonale Steuerbehörde senden.
7. Bei später gemeldeten Änderungen: neue Version rückwirkend mit Grund —
   die Verrechnung im Lohnlauf macht OneCrew.
