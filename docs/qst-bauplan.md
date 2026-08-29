# QST-Bauplan — Etappen bis zur automatischen Tarif-Herleitung

Bauplan OneCrew, **Version 2** (Stand 29.08.2026 — nach 4. Fachkorrektur
und Automatik-Perimeter). **Fachliche Vorgabe ist das Dokument
«QST-Tarif-Schulung»** (Abschnitte 0–10, inkl. Kapitel 10
Automatik-Perimeter); dieses Papier beschreibt WAS in welcher REIHENFOLGE
gebaut wird. Ergänzend: `docs/qst-korrektur-konzept.md` (Etappen K1–K5).

**Leitprinzip (verbindlich, Schulung Kap. 10):** OneCrew automatisiert nur
Tarife, die durch die Eingaben sehr klar bestimmt werden können. In allen
anderen Fällen gilt der höchste logische Tarif der unklaren Dimension
(Fallback-Tabelle) mit der Aufforderung «mit der Steuerbehörde abklären».
Kein universeller Fallback («ROT → A0» ist verboten). Einzige Ausnahme:
unklarer KANTON → vorläufig weiterrechnen, definitive QST-Abrechnung erst
nach Klärung. UX: nur ZWEI Farben (Grün = definitiv, Rot = Handlung
nötig), kein Orange. Kein neues Pflichtfeld für Exoten.

---

## 1 · Ist-Zustand (bereits gebaut und live)

**K1 — Korrektur-Fundament:** Tabelle `qst_korrektur` (ein Posten pro MA +
abgeschlossenem Monat); rückwirkende Version nur mit Pflicht-Grund (409
`KORREKTUR_GRUND_NOETIG`); alt = QST-Zeile aus dem eingefrorenen SlipJson,
neu = Nachrechnung auf derselben Basis; Vorjahr → Status VORJAHR;
↳-Anzeige im QST-Tab.

**Versiegelung & Sperren:** Wiedereröffnung NUR der jüngsten Periode;
QST-Versionssperre verwendungsbasiert (eingefroren ⇔ in ≥1 definitiv
abgeschlossenen Lohn verwendet), selbstheilend.

**Masken-Vorstufe (Modal 1–7):** Tarif = RESULTAT (gross zuunterst, keine
Auswahl); Abschnitt 1 read-only ausser «Gültig ab»; Wohnsituation nur im
Sonderfall (Anzeige); Partner reine Anzeige (Konkubinat-Frage nur bei
«ledig»); Behördenbewilligung A1–A9 nur mit Verfügung (Dokument Pflicht);
Kirchensteuer aus Konfession (Kantons-/Datei-Abhängigkeit: K4).

**Wohnsituation & Ausland:** Wochenaufenthalt = Zusatzadresse (Quelle des
Flags, W8/W9); Auslands-Hauptadresse 1:1 aus easy@work (`country_key`);
Land ≠ CH ⇒ automatisch «Person ohne steuerrechtlichen Wohnsitz CH» —
der ECHTE Grenzgänger-Status ergibt sich erst mit Rückkehr-Frage +
Nachweisen (K4). **Kanton-Fälle: A und B live, Fall C (Ausland +
CH-Wochenadresse → Wochenaufenthaltskanton) = Soll K4.**
Der «älteste laufende Vertrag» ist nur ein Tie-Breaker (Mehrkanton offen).

**Weiteres:** Ersatztarif-Grundsatz verankert; Warnungen W1–W9;
Tarif-Probe-Tool; Ersatzeinkünfte des Partners = erwerbstätig.

## 2 · Etappe K2 — Verrechnung der Korrektur-Posten

1. OFFENE `qst_korrektur`-Posten im nächsten Definitivlauf als eigene
   Lohnzeile verrechnen (Nachbelastung = Abzug, Erstattung = Gutschrift);
   Status VERRECHNET + `verrechnet_periode_id`; ELM-Raster-Code,
   Basen-Flags konsistent (Basen-Kontrolle grün).
2. Ausweis der Korrekturen in der kantonalen QST-Abrechnung (AG zahlt
   der Behörde sofort).
3. VORJAHR-Posten: Liste/Export für die Steuerverwaltung — NICHT über den
   Lohnlauf.
4. Tests + Kunstdaten-Durchlauf auf test.onecrew.ch.

## 3 · Etappe K3 — MA-Darlehen (generisch, zinslos)

Nach Konzept Kap. 4: Tabellen (Darlehen + Raten), aus QST-Korrektur
vorbefüllt; Ratenplan (Anzahl ODER Betrag); Vertrag-PDF mit
Art.-323b-Einwilligung; Abzugszeile pro Periode + Restsaldo auf dem
Lohnbeleg; Fälligkeit bei Austritt; Fibu «Forderung gegenüber Personal».
Erstattungen sind NIE ein Darlehen.

## 4 · Etappe K4 — automatische Tarif-Herleitung + finale Maske

**Kein Rechts-Gate mehr:** Auch Abschnitt 9 A der Schulung (Mehrkanton,
H-Ziffer) blockt den K4-BAU nicht — die Automatik verhält sich bis zur
Klärung gemäss Perimeter: bei Mehrkanton wird KEIN Kanton geraten (keine
definitive Abrechnung), die H-Ziffer wird NICHT um Kinder ausser Haus
erhöht. Die B-Fälle brauchen ohnehin keine juristische Klärung.

1. **Herleitungs-Snapshot pro Version:** JSON-Spalte an
   `employee_quellensteuer` — Server friert die komplette
   Herleitungsbasis ein (Zivilstand + seit, Konfession, Partner inkl.
   Erwerb/Ersatzeinkünfte/Tarif-E-Status, Kinder-Detail mit
   Haushalt/Erstausbildung/Unterhalt, Wohnsituation, Begründung).
   History zeigt das DIFF zur Vorversion.
2. **Auto-Anlass:** erkannte Differenz zur Vorversion → Server schreibt
   den Änderungs-/Korrektur-Grund selbst; manuell nur ohne erkennbare
   Änderung.
3. **Herleitung server-only, 1:1 nach Schulung:** Vorprüfung 0a/0b
   (Ansässigkeit aus OneCrew-Hauptadresse; Partner-Befreiung nur bei
   CH-ansässigem Partner, eindeutig Ausland = pflichtig), Entscheidbaum
   Abschnitt 3 (inkl. Tarif E → B), Halbfamilien-Matrix (KS-45-Vermutung
   volljährig im Haushalt; UX-Vereinfachung minderjährig; H-Ziffer
   automatisch nur Haushaltskinder bis Klärung 9 A), Konkubinats-Tabelle,
   zeitliche Geltung (Folgemonatsregel, Partner-Erwerbsaufnahme-Ausnahme,
   Kind-18, Geburt/Einzug).
4. **Kirchensteuer datengetrieben:** Y NUR, wenn die ESTV-Tarifdatei des
   QST-Kantons Y-Tarife enthält, sonst N — KEINE Kantonsliste im Code;
   ELM-6.0-Konfessionswerte (5) als Eingang. Ersatztarif entsprechend
   A0Y/C0Y ODER A0N/C0N gemäss Datei.
5. **Status-Modell = Automatik-Perimeter (Schulung Kap. 10):** ZWEI
   Farben. Grün = eindeutige Daten + eindeutige Regel. Rot = Handlung
   nötig, mit exakter Lücken-Nennung und dem definierten Fallback der
   Dimension (Partner unbekannt → C · nur Tarif E → B · Gre-1 fehlt →
   ordentliche Tarife · FR-Nachweis/Regel fehlt → kein SFN, ordentliche
   Tarife · Konfession fehlt → Y/N nach Abschnitt 0 · Zivilstand
   unzuverlässig → Ersatztarif). Fachlich komplexe Fälle (Obhut,
   gemischtes Konkubinat, unklare Ansässigkeit, unklarer Unterhalt):
   vorläufiger Tarif wo im Papier vorgegeben (zweifelhaftes H → vorläufig
   A0), sonst «Tarif/Kanton nicht freigegeben» — keine definitive
   Abrechnung bei unklarem Kanton. VERBOTEN: pauschales ROT → A0.
6. **Kanton-Fall C bauen:** Ausland + CH-Wochenadresse →
   Wochenaufenthaltskanton (Priorität vor Filialkanton).
7. **Grenzgänger-Detailfelder** (nur wenn relevant, keine neuen
   Pflichtfelder): Rückkehr-Frage; DE Gre-1/2 mit Dokument + Gültigkeit +
   Nichtrückkehrtage (60-Tage-Grenze, anteilig nach Gre-3); FR jährliche
   Bescheinigung + Grenzgängereigenschaft (≤45 Tage, ≤40 % Telearbeit) +
   Jahresmeldung ab Steuerjahr 2026 (erste Meldung Anfang 2027); IT
   Status neu/ehemalig + ESTV-20-km-Gemeindeliste + ≤45 Tage + Homeoffice
   ≤25 % (ehemalige → ordentliche Tarife); FL 45 Tage anteilig +
   AG-Nachweis bis Ende Februar Folgejahr. Tarife IMMER aus den
   ESTV-Dateien (L/M/N/P/Q, SFN, R/S/T/U); Q und V (G-Zwillinge) NICHT in
   die Lohnberechnung; FL-0 als DBA-Regel.
8. **Behördenentscheid-Block:** Bewilligung A1–A9 (gebaut) + manueller
   Prozentsatz nur mit Verfügung; Medianlohn-Regel = ESTV-Fallback der
   Satzbestimmung (kein Behördenentscheid).
9. **K4b (optional, nach Grün-Lauf):** Auto-Folgeversion bei
   Parameter-Änderung an der Quelle (Verallgemeinerung von Konfessions-
   und Wohnort-Sync); HR bestätigt nur.

## 5 · Etappe K5 — elektronische Korrekturmeldung

Mit Swissdec-Etappe E6: Korrektur-/Ersatzmeldungen aus den
`qst_korrektur`-Posten. Kein eigener Bau vor E6.

---

## 6 · Gates & offene Punkte (Spiegel von Schulung Abschnitt 9)

- **A (fachlich zu klären — blockt den Bau NICHT):** Mehrkanton Ausland ·
  H-Ziffer Haushalts- vs. alle Unterhaltskinder (Swissdec/
  Steuerverwaltung). Automatik bis Klärung = Perimeter-Verhalten.
- **B (kein Blocker):** Obhut, gemischtes Konkubinat, unklare
  Ansässigkeit, unklarer Unterhalt — Verhalten im Perimeter definiert.
- **C (spezifiziert, Bau in K4):** Fall C Kanton, Gre-Felder,
  FR-Meldung/Telearbeit, Herleitungs-Snapshot.
- **Freigabe der Schulung** (Walter + Cursor/ChatGPT-Review +
  K4-Freigabecheck gegen KS 45 / ELM 6.0) = Startsignal.

## 7 · Leitplanken (alle Etappen)

- Abgeschlossene Löhne bleiben abgeschlossen — Änderungen nur über
  Korrektur-Posten; Wiedereröffnung nur jüngste Periode.
- Adresse (inkl. Land) IMMER aus easy@work; Herleitung rechnet nur mit
  OneCrew-Stammdaten (K4 schaut nicht in easy@work); Wochenaufenthalt über
  die Zusatzadresse; kein manuelles Grenzgänger-Kreuz.
- Tarifwerte nie selbst erfinden, keine Prozente ableiten — ESTV-Dateien
  + DBA-Sonderregeln sind massgebend.
- H nur mit Haushalt + Hauptunterhalt; A1–A9 nur mit Verfügung; Alimente
  ändern den Code nicht automatisch; Konkubinatspartner befreit nie.
- Formeln bleiben Code; Basen bleiben flag-rein (Basen-Kontrolle grün).
- Übungs-/Testdaten nur auf test.onecrew.ch; jede Etappe mit
  Kunstdaten-Durchlauf + Tests abschliessen.

---

## Versionslog

- **Version 2 — 29.08.2026:** an 4. Fachkorrektur + Automatik-Perimeter
  angepasst (Leitprinzip höchster logischer Tarif, zwei Farben statt
  Ampel, Gate auf Abschnitt 9 A reduziert, Fallback-Tabelle statt
  Pauschal-ROT, Kirchensteuer datengetrieben, Tarif E, Gre-3/FL-Detail,
  Fall C als K4-Punkt 6).
- Erstfassung 29.08.2026.
