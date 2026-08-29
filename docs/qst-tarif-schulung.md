# QST-Tarifbestimmung — Faktoren, Kombinationen, Folgen

Schulungs- und Spezifikations-Dokument OneCrew (Stand 29.08.2026, inkl. 3. Fachkorrektur ChatGPT + Cursor).
Grundlage: ESTV-Kreisschreiben 45, TaxInfo BE (schweizweit gleiche
Tarifcode-Systematik), Auskünfte Steuerverwaltung (Kevin, 29.08.2026).
**Dieses Dokument ist die verbindliche Vorgabe für die automatische
Tarif-Herleitung (K4) — erst wenn hier alles geklärt ist, wird gebaut.**

---

## 0 · Vorprüfung: Ist überhaupt QST geschuldet?

**Schritt 0a — Steuerrechtliche Ansässigkeit in der Schweiz? JA/NEIN.**
Operative Grundlage: die aus easy@work synchronisierte
**Hauptwohnsitzadresse in OneCrew** (Single Source of Truth für
Adresse/Zivilstand). Kein separates Ansässigkeitsfeld, keine doppelte
Adresspflege — K4 schaut NICHT in easy@work, die Herleitung rechnet nur
mit den OneCrew-Stammdaten.

- **Hauptwohnsitz-Land = CH → Ansässigkeit Schweiz:** die fünf
  Befreiungsgründe unten anwenden (CH, C, Behörde, Ehepartner CH/C).
  Die Partner-Befreiung gilt nur bei rechtlich UND tatsächlich
  ungetrennter Ehe/Partnerschaft und den KS-45-Voraussetzungen.
- **Hauptwohnsitz-Land ≠ CH → Ansässigkeit Ausland:** QST auf die in der
  CH ausgeübte unselbständige Erwerbstätigkeit grundsätzlich JA —
  unabhängig von Nationalität und Bewilligung, vorbehaltlich
  DBA/Grenzgängerregel (Abschnitt 7). **CH-Pass oder C-Ausweis befreien
  bei Auslandsansässigkeit NICHT automatisch.**

**Sonderfall:** Liegt dokumentiert vor, dass die steuerrechtliche
Ansässigkeit von der hinterlegten Hauptadresse abweicht → KEIN
Automatismus, **ROT / QST-Behörde**. Kein neues Eingabefeld, kein
Normalprozess.

**Schritt 0b — bei Ansässigkeit CH:** Ein MA ist QST-pflichtig, AUSSER
einer dieser fünf Befreiungsgründe greift:

| # | Befreiungsgrund | Nachweis in OneCrew |
|---|---|---|
| 1 | Schweizer Staatsbürger/in | Nationalität CH |
| 2 | C-Ausweis (Niederlassung) | Bewilligungshistorie |
| 3 | Befreiung durch die Steuerbehörde | Bestätigungsschreiben (Dokument Pflicht) |
| 4 | Verheiratet mit CH-Bürger/in | Familie-Tab (Ehepartner CH) |
| 5 | Verheiratet mit C-Ausweis-Inhaber/in | Familie-Tab (Ehepartner, gültiger C) |

**Wichtig:** Befreiung 4+5 gelten NUR bei Ehe/eingetragener Partnerschaft.
Ein **Konkubinatspartner mit CH/C befreit NIE.**

**Trennung beendet die Partner-Befreiung:** Schon die TATSÄCHLICHE Trennung
beendet die Befreiung über den Ehepartner (CH/C) — nicht erst die Scheidung.
Massgebend ist das Feld «Getrennt seit»; wirksam ab **Folgemonat**
(Trennung 15.08. → QST-Pflicht + Tarif A/H ab 01.09.). Erst danach gilt der
Entscheidbaum in Abschnitt 3.

**Ersatztarif (Art. 19 QSV):** Weist sich die Person nicht zuverlässig aus
(Zivilstand unbestimmt, Angaben fehlen) → Ledige/Unbestimmte = **A0Y**,
Verheiratete = **C0Y** — immer MIT Kirchensteuer, bis die Angaben belegt sind.

---

## 1 · Die drei Bausteine des Tarifcodes

```
   C    2    N
   │    │    └─ Kirchensteuer: Y = ja / N = nein (aus Konfession)
   │    └────── Kinderziffer: Anzahl massgebende Kinder (0–9)
   └─────────── Tarifbuchstabe: aus Zivilstand + Partner + Kindersituation
```

Dazu der **Kanton**: der Hauptwohnsitz-Kanton bestimmt, WELCHE kantonale
Tarifdatei gilt. Wohnsitz Ausland: QST-Kanton richtet sich nach den
gesetzlichen Regeln zum anspruchsberechtigten Kanton bzw. Arbeitsort
(Details Abschnitt 7).

---

## 2 · Faktorenkatalog — was fliesst ein, woher kommt es

Quelle für Adresse und Zivilstand ist der **OneCrew-Stamm** (Sync aus
easy@work) — die Herleitung rechnet ausschliesslich in OneCrew, nie direkt
gegen easy@work.

| Faktor | Quelle in OneCrew | Wirkt auf |
|---|---|---|
| Nationalität / Bewilligung | MA-Stamm / Bewilligungshistorie (easy@work) | Pflicht ja/nein |
| Zivilstand (+ seit) | easy@work / MA-Maske | Buchstabe |
| «Getrennt seit» (tatsächliche Trennung) | MA-Stamm | Pflicht ja/nein (beendet Partner-Befreiung) + Buchstabe, ab Folgemonat |
| Partner erwerbstätig / Ersatzeinkommen (auch Rente, Militärsold, auch im Ausland) | Familie-Tab (Ehepartner) | B vs. C |
| Kinder: Geburtsdatum, Erstausbildung (mit Ausbildungsnachweis), Ausbildungszulage, Unterhalt zur Hauptsache | Familie-Tab + FamZ-Modul | Ziffer — berechtigt = minderjährig ODER volljährig in beruflicher/schulischer Erstausbildung UND der MA kommt für den Unterhalt zur Hauptsache auf |
| Kind lebt im Haushalt (explizites Flag) | Familie-Tab | H-Berechtigung + H-Ziffer |
| Gemeinsames Kind mit Konkubinatspartner | Familie-Tab (Kind) | Konkubinats-Entscheid |
| Konkubinat + «MA hat höheres Einkommen» | Familie-Tab (Konkubinatspartner) | H vs. A0 |
| Konfession | MA-Maske (Landeskirche = pflichtig) | Y/N |
| Hauptwohnsitz-Land | easy@work-Adresse (`country_key`) | Grenzgänger-Logik, Kanton |
| Wochenaufenthalt | Zusatzadresse «Wochenaufenthalt» | kein eigener Tarifcode; bei Ansässigkeit CH ändert er den Kanton NICHT — bei Ansässigkeit Ausland ist der Wochenaufenthaltskanton der QST-Kanton (Abschnitt 7, Fall C) |
| Rückkehr-Frage bei Auslandswohnsitz (täglich/regelmässig?) | QST-Erfassung (K4) | Grenzgänger vs. internationaler Wochenaufenthalter |
| Gre-1/2-Ansässigkeitsbescheinigung (DE) | QST-Erfassung (K4, Dokument) | ohne Gre-1/2: ordentliche A/B/C/H statt L/M/N/P/Q |
| FR-Ansässigkeitsbescheinigung (jährlich, SFN-Kantone) | QST-Erfassung (K4, Dokument) | ohne Bescheinigung: ROT — kein SFN |
| FR-Jahresmeldung ab Steuerjahr 2026 (Telearbeit-/Arbeitstage, alle FR-Ansässigen) | QST-Erfassung (K4) | ELM-/AG-Jahresmeldung, erste Meldung Anfang 2027 |
| IT-Grenzgängerstatus (neu ab 17.07.2023 / ehemalig), Wohnsitzgemeinde (ESTV-20-km-Liste), Nichtrückkehrtage ≤ 45, Homeoffice ≤ 25 % | QST-Erfassung (K4) | R/S/T/U nur für «neue» Grenzgänger |
| Weitere Beschäftigungen des MA | QST-Erfassung | satzbestimmender Lohn gemäss KS 45 (Gesamteinkommen / Gesamtbeschäftigungsgrad / Hochrechnung, sonst Medianlohn) — NICHT der Buchstabe |
| Behördenbewilligung Kinderabzug A | QST-Erfassung + Verfügung (Dokument Pflicht) | A1–A9 statt A0 |
| Behördenentscheid Prozentsatz | QST-Erfassung (Sonderfälle) | ersetzt Tarifrechnung |

---

## 3 · Der Entscheidbaum (Buchstabe)

**Schritt 1 — Zivilstand:**

- **Verheiratet / eingetragene Partnerschaft** (nicht getrennt lebend):
  - Partner erwerbstätig ODER Ersatzeinkommen → **C** (Doppelverdiener)
  - Partner weder noch → **B** (Alleinverdiener)
  - Frage offen → konservativ **C** + Warnung (Familie-Tab vervollständigen)
- **Rechtlich verheiratet, tatsächlich GETRENNT** → tarifseitig wie
  alleinstehend (ab Folgemonat der Trennung) → weiter mit Schritt 2.
- **Ledig / geschieden / verwitwet / getrennt** → Schritt 2.

**Schritt 2 — Alleinstehend, Kindersituation:**

- KEIN QST-berechtigtes Kind → **A0**
- Berechtigte(s) Kind(er) **im selben Haushalt**, kein Konkubinat →
  **H{n}** (Halbfamilie; n = Haushaltskinder)
- Berechtigte Kinder NUR **ausserhalb** des Haushalts → **A0**
  (Ziffer auf A nur mit Behördenbewilligung → A1–A9)
- **Konkubinat** → Entscheidtabelle Abschnitt 5

---

## 4 · Halbfamilien-Matrix (der Kern — Walters Frage)

Vorbedingung: MA ist NICHT verheiratet/eingetragene Partnerschaft und hat
mindestens ein **QST-berechtigtes Kind** — massgebend: **minderjährig ODER
volljährig in beruflicher/schulischer Erstausbildung, UND der MA kommt für
den Unterhalt zur Hauptsache auf** (volljährig: Ausbildungsnachweis).
Für Tarif H zusätzlich: Kind im gleichen Haushalt UND Unterhalt zur
Hauptsache. **Unklare Unterhaltssituation bei einem Kind ausser Haus:
NICHT automatisch in die Ziffer — ROT/orange, nicht raten.**

| Situation | Kind im Haushalt? | Alimente | Tarif | Bemerkung |
|---|---|---|---|---|
| Alleinerziehend, Kind beim MA | ja | keine | **H{n}** | Klassiker; Nachweis Wohnsitzbescheinigung |
| Alleinerziehend, Kind beim MA, MA **erhält** Alimente | ja | erhält | **H{n}** | Erhaltene Alimente ändern den CODE nicht — massgebend ist der Haushalt |
| Kind beim Ex-Partner, MA **zahlt** Alimente | nein | zahlt | **A0** | KEIN H! Abzug der Alimente läuft über Tarifkorrektur/NOV bei der Steuerbehörde — oder A1–A9 NUR mit ausdrücklicher Behördenbewilligung (Verfügung als Dokument Pflicht) |
| Kind beim Ex-Partner, keine Alimente | nein | keine | **A0** | wie oben |
| Volljähriges Kind in Erstausbildung, eigener Haushalt, MA zahlt Unterhalt | nein | Unterhalt | **A0** | Ziffer nur mit Behördenbewilligung; Flag «Unterhalt volljährige Kinder» dient dem Anmeldeformular |
| Mehrere Kinder, GEMISCHT (eins im Haushalt, eins beim Ex) | teils | egal | **H{nur Haushaltskinder}** | H-Ziffer zählt NUR Haushaltskinder; die anderen zählen nicht (ausser Behördenbewilligung) |
| **Alternierende Obhut** (getrennte Eltern, gemeinsame Sorge, Kind je hälftig) | je ~50 % | variabel | **KEIN Automatismus — «mit Behörde klären» (rot)** | Arbeitshypothese (KS 45): nur EIN Elternteil bekommt H — typisch der, der den Unterhalt zur Hauptsache bestreitet; bei gleichwertiger Betreuung ohne Alimente oft der mit dem höheren Einkommen. Solange die Steuerverwaltung den Beispielfall nicht bestätigt hat (Abschnitt 9 B), setzt K4 hier KEINEN Tarif — zudem ist das Flag «lebt im Haushalt» Ja/Nein, 50/50 passt nicht hinein. Kein stilles H |

Zum Vergleich **verheiratet mit Kindern**: Ziffer auf B/C zählt ALLE
QST-berechtigten Kinder (auch ausserhalb des Haushalts) — die
Haushalt-Einschränkung gilt nur für H.

---

## 5 · Konkubinat (Entscheidtabelle, Walter 25.08.2026)

Vorbedingung: Konkubinatspartner im Familie-Tab erfasst, MA alleinstehend,
QST-berechtigte(s) Kind(er) im Haushalt.

| Kind gemeinsam mit K-Partner? | Wer verdient mehr? | Tarif MA |
|---|---|---|
| ja | MA | **H{n}** (nie beide Partner H!) |
| ja | K-Partner | **A0** (H gehört dem Partner) |
| ja | K-Partner NICHT erwerbstätig (kein Erwerb, kein Ersatzeinkommen) | **H{n}** — auch wenn die Einkommensfrage offen ist: der MA ist zwangsläufig Hauptunterhalt (AG/ESTV-Praxis 25.08.2026, so im Live-Code `QstTarifVorschlagLogic`) |
| ja | Frage offen — UND Partner erwerbstätig/Ersatzeinkommen ja oder Erwerbsfrage selbst offen | konservativ **A0** + Warnung |
| nein (Kind aus früherer Beziehung, im Haushalt) | egal | **H{n}** (MA ist alleinerziehend) |
| GEMISCHT (gemeinsame UND nicht-gemeinsame Kinder) | egal | **kein Automatismus — mit QST-Behörde abklären** |

---

## 6 · Kirchensteuer (Y/N)

Die Konfession ist der EINGANG — ob daraus Y oder N wird, hängt zusätzlich
vom **QST-Kanton und der offiziellen ESTV-Tarifdatei** ab: Kantone, die in
der QST keine Kirchensteuer erheben (z.B. **TI, VS**), dürfen durch die
Religionsregel NIE auf Y fallen.

| Konfession | Suffix (in Kantonen MIT Kirchensteuer in der QST) |
|---|---|
| Röm.-katholisch / Christ-katholisch / Evang.-reformiert | **Y** |
| Keine / Andere (z.B. muslimisch, orthodox, freikirchlich) | **N** |
| NICHT erfasst | **Y** (Ersatztarif-Prinzip: lieber zu viel) + Warnung «Konfession erfassen» |

---

## 7 · Wohnsituation: Grenzgänger + Wochenaufenthalter (NEU)

**Grundregel:** Die easy@work-/OneCrew-Hauptadresse IST der Hauptwohnsitz.
Die Zusatzadresse Typ «Wochenaufenthalt» ist der zusätzliche
Aufenthaltsort — **keine zweite Wahrheit.**

**Anspruchsberechtigter Kanton — drei Fälle:**

- **A) Ansässigkeit CH:** QST-Kanton = **Wohnsitzkanton**. Schweizinterner
  Wochenaufenthalt ändert ihn NICHT (Hauptwohnsitz Sursee LU, Wochenzimmer
  Zofingen AG → **LU**).
- **B) Ansässigkeit Ausland OHNE Wochenaufenthalterstatus CH:** QST-Kanton =
  **Kanton der Filiale, in der gearbeitet wird** (Betriebsstätte /
  betriebliche Eingliederung) — NICHT der Kanton des GmbH-Hauptsitzes
  (Meggen). So konkretisiert sich die ESTV-Formel
  «Sitz/Verwaltung/Betriebsstätte» für OneCrew.
- **C) Ansässigkeit Ausland MIT Wochenaufenthalterstatus CH:** QST-Kanton =
  **Wochenaufenthaltskanton**.

Mehrkantonsfälle (parallele Arbeitsverhältnisse in mehreren Kantonen):
**ROT** — keine Automatik (Abschnitt 9 A).

**Beispiele:**

- Hauptadresse Sursee LU + Wochenadresse Zofingen AG → schweizintern
  (Fall A) → QST-Kanton **LU**.
- Hauptadresse Deutschland + Wochenadresse Basel CH → Ansässigkeit Ausland
  + internationaler Wochenaufenthalter (Fall C) → QST-Kanton =
  **Wochenaufenthaltskanton BS**.

**Wochenaufenthalter (schweizintern):** KEIN eigener Tarifcode — normaler
Tarif des Wohnkantons; das Flag ist reine Sachverhaltsinfo (kommt
automatisch aus der Zusatzadresse «Wochenaufenthalt»).

**Hauptwohnsitz im Ausland** = «Person ohne steuerrechtlichen Wohnsitz CH»
(NICHT pauschal «Grenzgänger»!). QST-Kanton nach den drei Fällen oben
(B bzw. C); Mehrkanton = ROT (Abschnitt 9 A).
Untertypen nach Land × Arbeitskanton (Details Konzept Kap. 5.4):

| Wohnsitz | Arbeitskanton | Tarif |
|---|---|---|
| DE | alle | **L/M/N/P/Q** (A→L, B→M, C→N, H→P, G→Q), max. 4.5 % — NUR mit Gre-1; >60 Nichtrückkehrtage → normale Tarife |
| FR | BE BS BL JU NE SO VD VS | **SFN** = keine CH-QST — NUR mit jährlicher Ansässigkeitsbescheinigung (fehlt sie → ROT, kein SFN) |
| FR | übrige (AG, LU, ZH, GE …) | normale A/B/C/H |
| IT | TI GR VS | **NUR «neue» Grenzgänger (ab 17.07.2023): R/S/T/U** (Details unten) — ehemalige Grenzgänger NICHT |
| FL | alle | **CH-QST = 0** (>45 Nichtrückkehrtage → CH-Recht lebt auf) |
| AT / übrige | alle | normale A/B/C/H |

**DE-Mapping — Bedeutung nach 2021 (massgebend ist DIESES Dokument):**

| DE-Tarif | Zwilling von | Bedeutung |
|---|---|---|
| **L** | A | alleinstehend |
| **M** | B | verheiratet, Alleinverdiener |
| **N** | C | verheiratet, Doppelverdiener — NICHT «Nebenerwerb» |
| **P** | H | Alleinerziehend |
| **Q** | G | Ersatzeinkünfte, die der Versicherer direkt auszahlt |

**Q bleibt im Dokument** — für den normalen Arbeitgeber-Lohnlauf in OneCrew
aber NICHT relevant (nicht in die Lohnberechnung einbauen). Alte Labels in
Hilfe/`QstTarifVorschlagLogic` («N = Nebenerwerb», «P = Pauschale») sind
veraltet.

**IT-Detail (kein Pauschal-Mapping!):**

- **«Neue» Grenzgänger ab 17.07.2023:** **R/S/T/U** = Zwillinge von A/B/C/H,
  80 % der ordentlichen QST — IMMER aus der ESTV-Tarifdatei, nie selbst
  rechnen. **V = G-Zwilling** (Ersatzeinkünfte Versicherer): dokumentieren,
  NICHT in den normalen AG-Lohnlauf.
- **Ehemalige Grenzgänger** (steuerlicher Grenzgänger zwischen 31.12.2018
  und 17.07.2023, Übergangsregel): NICHT R/S/T/U.
- **Voraussetzungen:** Wohnsitzgemeinde auf der ESTV-20-km-Liste,
  Arbeitskanton TI/GR/VS, grundsätzlich tägliche Rückkehr, max. 45
  berufliche Nichtrückkehrtage/Jahr, Status neu/ehemalig mit Nachweis +
  «Grenzgänger seit», Homeoffice bis 25 % ohne Statusverlust.

**FR-Detail:** SFN (BE, BS, BL, JU, NE, SO, VD, VS) verlangt die
**jährliche Ansässigkeitsbescheinigung** — fehlt sie → ROT, SFN wird nicht
automatisch angewendet; dazu gehören die Swissdec-**Monats- und
Jahresmeldungen**. Für ALLE in FR ansässigen Arbeitnehmer (auch ausserhalb
der 8 Kantone) gilt ab Steuerjahr 2026 die neue schweizweite
Arbeitgeber-/ELM-**Jahresmeldung** (erste Meldung Anfang 2027), inkl.
Telearbeit-Anteil — das sind K4-Faktoren, nicht nur eine Randnotiz.

Internationaler Wochenaufenthalter (wohnt unter der Woche in der CH, kehrt
nicht täglich zurück) = NICHT Grenzgänger → normale Tarife. Die
Rückkehr-Frage trennt die beiden Fälle (K4).

**Eiserne Regel:** nie Prozente selbst rechnen — immer die ESTV-Tarifcodes
aus den Tarifdateien; einzige Code-Regel ist FL = 0.

---

## 8 · Sonderfälle & zeitliche Geltung

- **Ersatztarif A0Y/C0Y** bei unzuverlässigem Ausweis (Abschnitt 0).
- **Behördenbewilligung A1–A9:** einziger Weg zu einer Kinderziffer auf A —
  Verfügung der Steuerbehörde als Dokument Pflicht.
- **Manueller Prozentsatz:** nur mit Behördenverfügung.
- **Medianlohn-Regel:** ESTV-Fallback, wenn Gesamteinkommen,
  Gesamtbeschäftigungsgrad und Hochrechnung nicht möglich sind — KEIN
  Behördenentscheid.
- **Weitere Beschäftigungen:** Der Tarifbuchstabe bleibt unverändert.
  Tarif D ist seit 2021 für diesen Fall abgeschafft. Weitere
  Erwerbstätigkeiten wirken ausschliesslich auf den **satzbestimmenden
  Lohn**. Dessen Ermittlung erfolgt gemäss KS 45 abhängig von den
  verfügbaren Angaben über Gesamteinkommen, Gesamtbeschäftigungsgrad bzw.
  Hochrechnung; ist keine dieser Methoden möglich, gilt die
  **Medianlohn-Regel**. Nicht pauschal «CH-Einkommen zusammenzählen» und
  nicht Pensum-% als Steuerbasis bezeichnen.
  Kurzmonat: satzbestimmend = voller Monat (so bereits in der Engine).
- **Zeitliche Geltung:** Verhältnisse am MONATSANFANG; Änderungen wirken ab
  FOLGEMONAT. Einzige Ausnahme: nimmt der Ehepartner eine Erwerbstätigkeit
  auf, gilt C beim MA ab Folgemonat, beim Partner sofort.
- **Geburt / Kind zieht in den Haushalt:** neue Ziffer bzw. Wechsel auf H
  ab FOLGEMONAT — analog Heirat/Trennung.
- **Kind wird 18:** Wird das Kind während eines Monats 18 Jahre alt, bleibt
  die bisherige Kinderziffer für diesen Monat bestehen. Ab dem 1. des
  Folgemonats besteht der Kinderabzug nur weiter, wenn die Voraussetzungen
  für ein volljähriges Kind in Erstausbildung erfüllt sind.
- **Heirat / Scheidung / Trennung / Verwitwung:** neuer Buchstabe ab
  Folgemonat; verspätete Meldung → rückwirkende Version mit Korrektur-Grund
  (K1), Differenzen als QST-Korrektur im nächsten Lohnlauf.

---

## 9 · Offene Punkte

### A) Fachlich noch offen (echte Blocker vor K4)

1. **Mehrkanton bei Auslandswohnsitz:** Bei mehreren parallelen
   Arbeitsverhältnissen/Arbeitskantonen ist die genaue Priorisierung des
   anspruchsberechtigten Kantons mit Swissdec bzw. Steuerbehörde zu klären
   («ältester laufender Vertrag» ist nur ein OneCrew-Tie-Breaker, keine
   ESTV-/Swissdec-Regel).
2. **Grenzgänger-Detailfelder** (Gre-1-Verwaltung, Nichtrückkehrtage,
   IT-Grenzgemeindeliste + Status neu/ehemalig, FR-Ansässigkeitsbescheinigung
   + Jahresmeldung/Telearbeit ab 2026): Spezifikation steht, Bau in K4.
3. **Herleitungs-Snapshot pro Version** (Parameter mit dem Tarif einfrieren,
   History zeigt «was hat geändert»): Design steht, Bau erst nach Freigabe
   dieses Dokuments.

### B) Bewusst manuell / ROT (für K4 spezifiziert)

Diese Fälle müssen vor dem Bau NICHT juristisch «gelöst» sein — K4 setzt
hier bewusst KEINEN automatischen Tarif: keine automatische Freigabe,
Entscheid mit der Behörde.

- **Alternierende Obhut** (Arbeitshypothese in Abschnitt 4 dokumentiert;
  Beispielfall mit der Steuerverwaltung bei Gelegenheit durchspielen).
- **Gemischtes Konkubinat** (gemeinsame UND nicht-gemeinsame Kinder).
- **Unklare Unterhaltssituation** bei einem Kind ausserhalb des Haushalts.
- **SFN ohne gültige jährliche Ansässigkeitsbescheinigung** (kein SFN).

---

## Versionslog

- **Nachtrag zur 3. Fachkorrektur 29.08.2026:** Ansässigkeit operativ =
  OneCrew-Hauptadresse (Sync aus easy@work, Single Source of Truth; K4
  schaut nicht in easy@work); dokumentiert abweichende Ansässigkeit =
  ROT/Behörde ohne neues Feld; Wochenaufenthalt-Beispiele (Sursee/Zofingen
  → LU; Deutschland/Basel → Fall C, BS).
- **Dritte Fachkorrektur 29.08.2026 (ChatGPT + Cursor):** Vorprüfung
  Ansässigkeit CH/Ausland (CH-Pass/C-Ausweis befreien bei
  Auslandsansässigkeit nicht automatisch); anspruchsberechtigter Kanton in
  drei Fällen (Wohnsitz / Arbeits-Filiale statt GmbH-Hauptsitz /
  Wochenaufenthaltskanton); IT nur «neue» Grenzgänger R/S/T/U mit
  Voraussetzungen, V = G-Zwilling; QST-berechtigtes Kind mit
  Unterhalt-zur-Hauptsache; FR SFN nur mit jährlicher Bescheinigung +
  Jahresmeldung ab 2026 als K4-Faktoren; Kirchensteuer kantonsabhängig
  (TI/VS nie Y); Abschnitt 9 gegliedert in A (Blocker) und B (bewusst
  manuell/ROT).
- **Zweite Korrektur 28./29.08.2026 (ChatGPT + Cursor):** satzbestimmender
  Lohn gemäss KS 45 (nicht pauschal zusammenzählen); Volljährigkeit =
  Folgemonat; Q = G-Zwilling belassen, im AG-Lohnlauf nicht relevant;
  Mehrkanton bei Auslandswohnsitz als offene Abklärung.
- **Cursor-Korrektur 28.08.2026:** Konkubinat nicht-erwerbstätig,
  Trennung in Vorprüfung, Obhut kein Automatismus, satzbestimmender Lohn,
  Geburt, DE-N-Bedeutung.
- Erstfassung 29.08.2026.
