# QST-Bauplan — Etappen bis zur automatischen Tarif-Herleitung

Bauplan OneCrew (Stand 29.08.2026). **Fachliche Vorgabe ist das Dokument
«QST-Tarif-Schulung»** (Faktoren, Kombinationen, Folgen — inkl. 2. Korrektur);
dieses Papier beschreibt WAS in welcher REIHENFOLGE gebaut wird.
Ergänzend: `docs/qst-korrektur-konzept.md` (freigegebenes Korrektur-/
Masken-Konzept, Etappen K1–K5).

**Es wird NICHTS gebaut, bevor die offenen Abklärungen (Abschnitt 6) des
jeweiligen Bausteins grün sind.**

---

## 1 · Ist-Zustand (bereits gebaut und live)

**K1 — Korrektur-Fundament (29.08.2026):**
Tabelle `qst_korrektur` (ein Posten pro MA + abgeschlossenem Monat);
rückwirkende QST-Version über definitiv abgeschlossene Perioden nur mit
Pflicht-Grund (409 `KORREKTUR_GRUND_NOETIG`); alt = QST-Zeile aus dem
eingefrorenen SlipJson, neu = Tarif-Nachrechnung auf derselben Basis;
Jahresgrenze → Status VORJAHR; Anzeige als ↳-Unterzeilen im QST-Tab.

**Versiegelung & Sperren:**
Wiedereröffnung NUR der jüngsten Periode (409 `NICHT_JUENGSTE_PERIODE`);
QST-Versionssperre VERWENDUNGSBASIERT (eingefroren ⇔ in ≥1 definitiv
abgeschlossenen Lohn verwendet, selbstheilend bei Wiedereröffnung).

**Masken-Vorstufe (Modal, Struktur 1–7):**
Tarif ist RESULTAT (gross zuunterst, keine Auswahl); Abschnitt 1 read-only
ausser «Gültig ab»; Wohnsituation nur im Sonderfall (Anzeige); Partner reine
Anzeige (Konkubinat-Frage nur bei «ledig»); Behördenbewilligung A1–A9 nur
mit Verfügung (Dokument Pflicht, Server-Guard); Kirchensteuer aus Konfession.

**Wohnsituation & Ausland:**
Wochenaufenthalt = Zusatzadresse (Quelle des Flags, Server-Guard, W8/W9);
Auslands-Hauptadresse aus easy@work (`country_key`, kein CH-Hardcode mehr);
Land ≠ CH ⇒ automatisch Grenzgänger-Flag; QST-Kanton bei Ausland aus der
Filiale — **Achtung: der «älteste laufende Vertrag» ist nur ein
OneCrew-Tie-Breaker, die Mehrkanton-Priorisierung ist offen (Abschnitt 6).**

**Weiteres:** Ersatztarif A0Y/C0Y verankert; Tarif-Warnungen W1–W9;
Tarif-Probe-Tool (Admin); Partner-Ersatzeinkommen = erwerbstätig.

---

## 2 · Etappe K2 — Verrechnung der Korrektur-Posten

1. **Lohnlauf-Verrechnung:** OFFENE `qst_korrektur`-Posten des MA werden im
   nächsten Definitivlauf als eigene Lohnzeile verrechnet (Nachbelastung =
   Abzug, Erstattung = Gutschrift); Posten → Status VERRECHNET +
   `verrechnet_periode_id`/`verrechnet_at`. Lohnpositions-Code nach
   ELM-Raster (Korrektur-Zeile, Basen-Flags konsistent — Basen-Kontrolle
   muss grün bleiben).
2. **Kantonale QST-Abrechnung:** Ausweis der Korrekturen in der
   Monats-/Quartalsabrechnung an die Behörde (AG zahlt sofort — Konzept
   Kap. 3).
3. **VORJAHR-Posten:** NICHT über den Lohnlauf — Liste/Export für die
   Meldung an die Steuerverwaltung.
4. Tests: Transition-Tests analog Workflow-Tests; Kunstdaten-Durchlauf auf
   test.onecrew.ch (rückwirkende Heirat über 2 abgeschlossene Monate).

## 3 · Etappe K3 — MA-Darlehen (generisch, zinslos)

Nach Konzept Kap. 4: eigene Tabellen (Darlehen + Raten), Verwendungszweck
frei, aus QST-Korrektur vorbefüllt; Ratenplan (Anzahl ODER Betrag, letzte
Rate = Rest); Darlehensvertrag-PDF mit Art.-323b-Einwilligung (AG links,
MA rechts); automatische Abzugszeile pro Periode + Restsaldo auf dem
Lohnbeleg; Fälligkeit bei Austritt; Fibu-Konto «Forderung gegenüber
Personal». Erstattungen sind NIE ein Darlehen (immer Gutschrift).

## 4 · Etappe K4 — automatische Tarif-Herleitung + finale Maske

**Gate:** Abschnitt 6 dieses Plans (offene Abklärungen) muss geklärt sein.

1. **Herleitungs-Snapshot pro Version:** JSON-Spalte an
   `employee_quellensteuer` — beim Speichern friert der Server die komplette
   Herleitungsbasis ein (Zivilstand + seit, Konfession, Partner inkl.
   Erwerb/Ersatzeinkommen, Kinder-Detail mit Haushalt/Erstausbildung,
   Wohnsituation, Begründung). History zeigt pro Version das DIFF zur
   Vorversion («Zivilstand verheiratet → geschieden»).
2. **Auto-Anlass:** erkennt der Server beim Speichern eine Differenz zur
   Vorversion, schreibt er den Korrektur-/Änderungs-Grund selbst; manueller
   Grund nur, wenn nichts Erkennbares geändert hat.
3. **Herleitung server-only, 1:1 nach Schulungs-Dokument:** Entscheidbaum
   Abschnitt 3, Halbfamilien-Matrix Abschnitt 4, Konkubinats-Tabelle
   Abschnitt 5, Kirchensteuer Abschnitt 6, zeitliche Geltung Abschnitt 8
   (Folgemonatsregel, Partner-Erwerbsaufnahme-Ausnahme, Kind-18 =
   Folgemonat, Geburt/Einzug = Folgemonat).
4. **Resultat-Ampel (Konzept Kap. 2):** GRÜN = vollständig hergeleitet;
   ORANGE = unvollständig → Ersatztarif A0Y/C0Y + «das fehlt»-Liste;
   ROT = nicht automatisierbar (gemischtes Konkubinat, alternierende Obhut,
   Mehrkanton-Ausland) → «mit Behörde klären», KEIN stiller Tarif.
5. **Behördenentscheid-Block:** Bewilligung A1–A9 (gebaut) + manueller
   Prozentsatz NUR mit Verfügung; Medianlohn-Regel als ESTV-Fallback der
   Satzbestimmung (kein Behördenentscheid) — Trennung gemäss Schulung
   Abschnitt 8.
6. **Grenzgänger-Detailfelder** (je Land, nur wenn relevant): Rückkehr-Frage
   (Grenzgänger vs. internationaler Wochenaufenthalter); DE: Gre-1/2 mit
   Dokument + Gültigkeit + Nichtrückkehrtage (>60 → ordentliche Tarife);
   IT: Wohnsitzgemeinde gegen ESTV-Grenzgemeindeliste, Homeoffice ≤ 25 %;
   FR: 8er-Kantone-Weiche (SFN), Telearbeit-/Arbeitstage-Felder
   (Meldepflicht ab 2027); FL: Rückkehr/Nichtrückkehrtage → 0-Regel.
   Tarife IMMER aus den ESTV-Tarifdateien (L/M/N/P/Q, SFN, R/S/T/U/V);
   einzige Code-Regel FL = 0. **Q wird NICHT in die Lohnberechnung
   eingebaut** (G-Zwilling, Versicherer-Ersatzeinkünfte).
7. **Auto-Folgeversion (K4b, optional nach Grün-Lauf):** Änderung eines
   tarifrelevanten Parameters an der Quelle (easy/MA-Maske/Familie) →
   System schlägt die Folge-Version per Folgemonat vor (Verallgemeinerung
   von Konfessions- und Wohnort-Sync); HR bestätigt nur.

## 5 · Etappe K5 — elektronische Korrekturmeldung

Mit Swissdec-Etappe E6: Korrektur-/Ersatzmeldungen aus den
`qst_korrektur`-Posten. Kein eigener Bau vor E6.

---

## 6 · Offene Abklärungen = Gates vor K4

1. **Alternierende Obhut:** Beispielfall mit Steuerverwaltung — bis dahin
   ROT, kein Automatismus.
2. **Gemischtes Konkubinat:** bleibt dauerhaft «Behörde fragen».
3. **Mehrkanton bei Auslandswohnsitz:** Priorisierung des
   anspruchsberechtigten Kantons mit Swissdec/Steuerbehörde klären
   («ältester Vertrag» ist nur Tie-Breaker).
4. **Freigabe des Schulungs-Dokuments** durch Walter (+ Cursor-Review) =
   Startsignal für K4.

## 7 · Leitplanken (gelten für ALLE Etappen)

- Abgeschlossene Löhne bleiben abgeschlossen — Änderungen nur über
  Korrektur-Posten; Wiedereröffnung nur jüngste Periode.
- Adresse (inkl. Land) kommt IMMER aus easy@work; Wochenaufenthalt über die
  Zusatzadresse; kein manuelles Grenzgänger-Kreuz.
- Nie Prozente selbst programmieren — ESTV-Tarifdateien; FL = 0.
- H nur Haushaltskinder; A1–A9 nur mit Verfügung; Alimente ändern den Code
  nicht; Konkubinatspartner befreit nie.
- Formeln bleiben Code (keine Formeln in Daten); Basen bleiben flag-rein
  (Basen-Kontrolle grün).
- Übungs-/Testdaten nur auf test.onecrew.ch; jede Etappe mit
  Kunstdaten-Durchlauf + Tests abschliessen, bevor die nächste beginnt.

---

## Versionslog

- Erstfassung Bauplan 29.08.2026 (nach 2. Korrektur des
  Schulungs-Dokuments; für Cursor-Review).
