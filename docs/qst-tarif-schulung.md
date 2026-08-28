# QST-Tarifbestimmung — Faktoren, Kombinationen, Folgen

Schulungs- und Spezifikations-Dokument OneCrew (Stand 29.08.2026).
Grundlage: ESTV-Kreisschreiben 45, TaxInfo BE (schweizweit gleiche
Tarifcode-Systematik), Auskünfte Steuerverwaltung (Kevin, 29.08.2026).
**Dieses Dokument ist die verbindliche Vorgabe für die automatische
Tarif-Herleitung (K4) — erst wenn hier alles geklärt ist, wird gebaut.**

---

## 0 · Vorprüfung: Ist überhaupt QST geschuldet?

Ein MA ist QST-pflichtig, AUSSER einer dieser fünf Befreiungsgründe greift:

| # | Befreiungsgrund | Nachweis in OneCrew |
|---|---|---|
| 1 | Schweizer Staatsbürger/in | Nationalität CH |
| 2 | C-Ausweis (Niederlassung) | Bewilligungshistorie |
| 3 | Befreiung durch die Steuerbehörde | Bestätigungsschreiben (Dokument Pflicht) |
| 4 | Verheiratet mit CH-Bürger/in | Familie-Tab (Ehepartner CH) |
| 5 | Verheiratet mit C-Ausweis-Inhaber/in | Familie-Tab (Ehepartner, gültiger C) |

**Wichtig:** Befreiung 4+5 gelten NUR bei Ehe/eingetragener Partnerschaft.
Ein **Konkubinatspartner mit CH/C befreit NIE.**

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

Dazu der **Kanton**: Hauptwohnsitz-Kanton (bei Auslandswohnsitz der
Arbeitskanton der Filiale) bestimmt, WELCHE kantonale Tarifdatei gilt.

---

## 2 · Faktorenkatalog — was fliesst ein, woher kommt es

| Faktor | Quelle in OneCrew | Wirkt auf |
|---|---|---|
| Nationalität / Bewilligung | MA-Stamm / Bewilligungshistorie (easy@work) | Pflicht ja/nein |
| Zivilstand (+ seit) | easy@work / MA-Maske | Buchstabe |
| Partner erwerbstätig / Ersatzeinkommen (auch Rente, Militärsold, auch im Ausland) | Familie-Tab (Ehepartner) | B vs. C |
| Kinder: Geburtsdatum, Erstausbildung, Ausbildungszulage | Familie-Tab + FamZ-Modul | Ziffer (berechtigt = unter 18 ODER in Erstausbildung) |
| Kind lebt im Haushalt (explizites Flag) | Familie-Tab | H-Berechtigung + H-Ziffer |
| Gemeinsames Kind mit Konkubinatspartner | Familie-Tab (Kind) | Konkubinats-Entscheid |
| Konkubinat + «MA hat höheres Einkommen» | Familie-Tab (Konkubinatspartner) | H vs. A0 |
| Konfession | MA-Maske (Landeskirche = pflichtig) | Y/N |
| Hauptwohnsitz-Land | easy@work-Adresse (`country_key`) | Grenzgänger-Logik, Kanton |
| Wochenaufenthalt | Zusatzadresse «Wochenaufenthalt» | nur Sachverhalt — Kanton bleibt Hauptwohnsitz |
| Weitere Beschäftigungen des MA | QST-Erfassung | Satzbestimmung (Hochrechnung), nicht der Code |
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
mindestens ein QST-berechtigtes Kind (unter 18 ODER in Erstausbildung).

| Situation | Kind im Haushalt? | Alimente | Tarif | Bemerkung |
|---|---|---|---|---|
| Alleinerziehend, Kind beim MA | ja | keine | **H{n}** | Klassiker; Nachweis Wohnsitzbescheinigung |
| Alleinerziehend, Kind beim MA, MA **erhält** Alimente | ja | erhält | **H{n}** | Erhaltene Alimente ändern den CODE nicht — massgebend ist der Haushalt |
| Kind beim Ex-Partner, MA **zahlt** Alimente | nein | zahlt | **A0** | KEIN H! Abzug der Alimente läuft über Tarifkorrektur/NOV bei der Steuerbehörde — oder A1–A9 NUR mit ausdrücklicher Behördenbewilligung (Verfügung als Dokument Pflicht) |
| Kind beim Ex-Partner, keine Alimente | nein | keine | **A0** | wie oben |
| Volljähriges Kind in Erstausbildung, eigener Haushalt, MA zahlt Unterhalt | nein | Unterhalt | **A0** | Ziffer nur mit Behördenbewilligung; Flag «Unterhalt volljährige Kinder» dient dem Anmeldeformular |
| Mehrere Kinder, GEMISCHT (eins im Haushalt, eins beim Ex) | teils | egal | **H{nur Haushaltskinder}** | H-Ziffer zählt NUR Haushaltskinder; die anderen zählen nicht (ausser Behördenbewilligung) |
| **Alternierende Obhut** (getrennte Eltern, gemeinsame Sorge, Kind je hälftig) | je ~50 % | variabel | **H beim Elternteil, der den Unterhalt zur Hauptsache bestreitet**; bei gleichwertiger Betreuung ohne Alimente: der mit dem höheren Einkommen | ⚠ NUR EIN Elternteil bekommt H — nie beide. Einzelfall, im Zweifel Behörde fragen |

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
| ja | Frage offen | konservativ **A0** + Warnung |
| nein (Kind aus früherer Beziehung, im Haushalt) | egal | **H{n}** (MA ist alleinerziehend) |
| GEMISCHT (gemeinsame UND nicht-gemeinsame Kinder) | egal | **kein Automatismus — mit QST-Behörde abklären** |

---

## 6 · Kirchensteuer (Y/N)

| Konfession | Suffix |
|---|---|
| Röm.-katholisch / Christ-katholisch / Evang.-reformiert | **Y** |
| Keine / Andere (z.B. muslimisch, orthodox, freikirchlich) | **N** |
| NICHT erfasst | **Y** (Ersatztarif-Prinzip: lieber zu viel) + Warnung «Konfession erfassen» |

---

## 7 · Wohnsituation: Grenzgänger + Wochenaufenthalter (NEU)

**Grundregel:** Die easy@work-Adresse IST der Hauptwohnsitz.
Der **Hauptwohnsitz bestimmt den QST-Kanton** — nie der Aufenthaltsort.

**Wochenaufenthalter (schweizintern):** Hauptwohnsitz Sursee LU, Wochenzimmer
Zofingen AG → QST-Kanton **LU**. KEIN eigener Tarifcode — normaler Tarif des
Wohnkantons; das Flag ist reine Sachverhaltsinfo (kommt automatisch aus der
Zusatzadresse «Wochenaufenthalt»).

**Hauptwohnsitz im Ausland** = «Person ohne steuerrechtlichen Wohnsitz CH»
(NICHT pauschal «Grenzgänger»!). QST-Kanton = **Arbeitskanton der Filiale**.
Untertypen nach Land × Arbeitskanton (Details Konzept Kap. 5.4):

| Wohnsitz | Arbeitskanton | Tarif |
|---|---|---|
| DE | alle | **L/M/N/P/Q** (A→L, B→M, C→N, H→P), max. 4.5 % — NUR mit Gre-1; >60 Nichtrückkehrtage → normale Tarife |
| FR | BE BS BL JU NE SO VD VS | **SFN** = keine CH-QST (bei erfüllter Grenzgängerregel) |
| FR | übrige (AG, LU, ZH, GE …) | normale A/B/C/H |
| IT | TI GR VS | **R/S/T/U/V** (80 % der ordentlichen QST), ESTV-Grenzgemeindeliste |
| FL | alle | **CH-QST = 0** (>45 Nichtrückkehrtage → CH-Recht lebt auf) |
| AT / übrige | alle | normale A/B/C/H |

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
- **Manueller Prozentsatz / satzbestimmender Medianlohn:** nur auf
  dokumentierten Behördenentscheid.
- **Nebenerwerb:** Der frühere Tarif D ist seit 2021 abgeschafft — bei
  weiteren Beschäftigungen wird der Satz auf das Gesamtpensum HOCHGERECHNET
  (KS 45), der Buchstabe bleibt. (Tarif G = Ersatzeinkünfte, die der
  Versicherer direkt auszahlt — betrifft den AG-Lohnlauf nicht.)
- **Zeitliche Geltung:** Verhältnisse am MONATSANFANG; Änderungen wirken ab
  FOLGEMONAT. Einzige Ausnahme: nimmt der Ehepartner eine Erwerbstätigkeit
  auf, gilt C beim MA ab Folgemonat, beim Partner sofort.
- **Kind wird 18:** Ziffer sinkt ab Folgemonat — AUSSER Erstausbildung
  (oder laufende Ausbildungszulage als Beleg) verlängert.
- **Heirat / Scheidung / Trennung / Verwitwung:** neuer Buchstabe ab
  Folgemonat; verspätete Meldung → rückwirkende Version mit Korrektur-Grund
  (K1), Differenzen als QST-Korrektur im nächsten Lohnlauf.

---

## 9 · Offene Abklärungen (vor dem K4-Bau klären)

1. **Alternierende Obhut:** genaue kantonale Praxis, wem H zusteht
   (Hauptsache-Unterhalt vs. höheres Einkommen) — Beispielfall mit der
   Steuerverwaltung durchspielen.
2. **Gemischtes Konkubinat:** bleibt bewusst «Behörde fragen» — kein
   Automatismus.
3. **Grenzgänger-Detailfelder** (Gre-1-Verwaltung, Nichtrückkehrtage,
   IT-Grenzgemeindeliste, FR-Telearbeit-Meldung ab 2027): Bau in K4.
4. **Herleitungs-Snapshot pro Version** (Parameter mit dem Tarif einfrieren,
   History zeigt «was hat geändert»): Design steht, Bau erst nach Freigabe
   dieses Dokuments.
