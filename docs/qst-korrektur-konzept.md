# Konzept: QST-Herleitung, rückwirkende Korrekturen & MA-Darlehen

**Status: FREIGEGEBEN durch Walter, 29.08.2026** («wir machen das genau so»).
Grundlagen: TaxInfo BE «Anwendbare Tarifcodes» (Ersatztarif Art. 19 QSV,
Folgemonatsregel), Kevin/Steuerverwaltung (Ersatzeinkommen = erwerbstätig),
Swissdec ELM 6.0 (QST-Monatsmeldung mit Korrektur-/Ersatzmeldungen — E6).

## 1. Leitprinzipien

1. **Der Tarif ist ein Resultat, keine Eingabe.** Er entsteht top-down aus
   Bewilligung/Nationalität → Zivilstand → Partner → Kindern → Konfession →
   besonderen Sachverhalten. Bei vollständigen Daten ist er NICHT editierbar.
2. **Ein Wert, mehrere Türen.** Tarifrelevante Stammdaten (Zivilstand,
   Konfession, Partner-Erwerb, Kinder-Schalter) sind in der QST-Maske INLINE
   änderbar, gespeichert wird IMMER in die Quelle (MA-Stamm/Familie-Tab) —
   keine Kopien.
3. **Abgeschlossene Löhne bleiben abgeschlossen.** Rückwirkendes Wissen
   ändert nie einen Snapshot — es erzeugt Korrektur-Posten, verrechnet im
   Folgemonat.
4. **Der AG zahlt der Behörde sofort.** QST-Nachforderungen gehen in die
   nächste kantonale Abrechnung, unabhängig davon, wann der MA zurückzahlt.
5. **Ersatztarif nach Art. 19 QSV** (A0Y/C0Y) ist der automatische Zustand
   bei unvollständigen Angaben — kein Spezialfall, sondern die orange Stufe
   der Herleitung.

## 2. QST-Erfassungsmaske neu: die Herleitungs-Treppe

Top-down-Stufen, jede mit Wert + Quelle + Inline-Edit bzw. Sprunglink:

| # | Stufe | Quelle | Inline editierbar? |
|---|---|---|---|
| 1 | QST-Pflicht (Nationalität, Bewilligung, Befreiungen) | MA-Stamm / Bewilligungshistorie | Sprunglink (Historie ist eigenes Modul) |
| 2 | Zivilstand + seit | MA-Stamm | **ja** (Select + Datum) |
| 3 | Ehe-/Konkubinatspartner: Nationalität/Bewilligung, Erwerbstätig-/Ersatzeinkommens-Frage, Arbeitgeber | Familie-Tab | **ja** (Frage + Kernfelder); Partner NEU erfassen → Familie-Tab |
| 4 | Kinder: pro Kind die tarifrelevanten Schalter (im Haushalt / Erstausbildung / gemeinsames Kind) | Familie-Tab | **ja** (Schalter); neues Kind → Familie-Tab |
| 5 | Konfession | MA-Stamm | **ja** (Select) |
| 6 | Besondere Sachverhalte: Wohnsitz Ausland, Grenzgänger, Wochenaufenthalter, weitere Beschäftigungen, Halbfamilien-Detail | QST-Erfassung | ja (leben nur hier) |
| 7 | **RESULTAT-Karte** | berechnet | **nein** |

**Resultat-Karte (Ampel):**
- **GRÜN** — alle Angaben vollständig: Tarifcode gross (z.B. C2N) +
  Herleitungszeilen. Nicht editierbar.
- **ORANGE** — Angaben unvollständig: automatisch **Ersatztarif A0Y/C0Y**
  + Liste «das fehlt noch» mit Sprunglinks.
- **ROT** — nicht automatisierbar (z.B. gemischtes Konkubinat):
  Behördenentscheid nötig.

**Einzige Übersteuerung: Block «Abweichender Behördenentscheid»** —
Verfügung vom (Pflicht), Referenz, Dokument-Upload; Resultat wird sichtbar
als «per Verfügung» markiert. Die heutigen Felder «Speziell bewilligt» und
«Prozentsatz manuell» wandern in diesen Block.

**Speichern in einem Rutsch mit Quittung:** Inline-Änderungen werden beim
Speichern GESAMMELT committed; vorher Zusammenfassung: «Wird geändert:
Zivilstand → verheiratet seit 15.07. (MA-Stamm) · Neue Tarif-Version C2Y ab
01.08.» Kein Sofort-Speichern pro Feld (halbe Änderungen bei Abbruch).

**Folgemonats-Automatik (TaxInfo Kap. 3):** Verhältnisse am Monatsanfang;
Änderung wirkt ab Folgemonat → die Maske schlägt das Gültig-ab der neuen
Version automatisch vor. Ausnahme: Partner NIMMT Erwerbstätigkeit AUF → C
beim Partner sofort, beim MA ab Folgemonat.

## 3. Rückwirkende Korrekturen (Wirkung vs. Wissen)

Realität: Heirat/Trennung/Geburt wird oft Monate später gemeldet. ALLE
Angaben zu Tarif, Pflicht und Befreiung müssen rückwirkend korrigierbar
sein.

- **Zwei Zeitachsen:** Wirkung (Gültig-ab der Version, z.B. 01.06.) vs.
  Wissen (CreatedAt + Pflicht-Grund, z.B. 20.08. «Heirat verspätet
  gemeldet»).
- **Rückwirkende Version über abgeschlossene Perioden** ist NICHT mehr per
  Edit-Lock verboten, sondern läuft über den expliziten **Korrektur-Weg**:
  Das System rechnet pro betroffenem abgeschlossenem Monat alt vs. neu und
  erzeugt **`qst_korrektur`-Posten** (MA, Monat, alte/neue Version,
  alter/neuer Betrag, Differenz, Status OFFEN → VERRECHNET/IN_DARLEHEN →
  GEMELDET). Snapshots bleiben unangetastet.
- **Verwendung der Posten:** (1) Korrekturzeile im nächsten OFFENEN
  Lohnlauf (Nachbelastung oder Erstattung; ELM-Raster-Korrekturpositionen);
  (2) Ausweis in der kantonalen QST-Abrechnung des Korrekturmonats — **AG
  zahlt sofort**; (3) ab E6 als Swissdec-Korrekturmeldung der betroffenen
  Monate.
- **Jahresgrenze:** Verrechnung über den Lohnlauf nur INNERHALB des
  laufenden Steuerjahres. Vorjahres-Korrekturen → Hinweis «via
  Steuerverwaltung» (berichtigte Jahresabrechnung / Neuberechnung; MA-Frist
  31.03. des Folgejahres).
- Gilt generisch für alle Auslöser: Heirat, Trennung, Geburt,
  Kirchenein-/austritt, Partner-Erwerbsaufnahme/-aufgabe, rückwirkender
  Bewilligungswechsel, nachträgliche Befreiung (C-Ausweis/Heirat mit CH).

## 4. MA-Darlehen (generisch, zinslos)

Bei hoher Nachbelastung wandelt HR die Forderung in ein **zinsloses
Darlehen** (Walter: «hoch ist relativ» — Entscheid im Einzelfall):

- **Generisches Modul** (nicht QST-exklusiv): Verwendungszweck frei
  («QST-Nachzahlung Juni–Aug 2026», «Vorschuss …»); die QST-Korrektur
  befüllt es automatisch vor.
- **Ratenplan:** Anzahl Raten ODER Monatsbetrag eingeben (das andere wird
  gerechnet); letzte Rate = Restbetrag. Beispiel: 400 → 4 Raten à 100.
- **Darlehensvertrag als PDF** (QuestPDF): Betrag, Zweck, Ratenplan,
  zinslos, Fälligkeit des Restsaldos bei Austritt, **schriftliche
  Einwilligung zur Lohnverrechnung (Art. 323b OR)**. Unterschriften: AG
  links, MA rechts. Ablage beim MA (Dokumente) + Postfach-Zustellung.
- **Lohnlauf:** pro Periode automatische Abzugszeile «Rückzahlung Darlehen»
  + **Restsaldo-Ausweis auf dem Lohnbeleg** (wie Ferien-/13.-Saldi).
  Austritt → Restsaldo wird mit dem letzten Lohn fällig.
- **Fibu:** Darlehen = Aktivkonto «Forderung gegenüber Personal»
  (Kontoplan-Position ergänzen); Auszahlung/Umwandlung bucht auf, Raten
  bauen ab.
- Erstattungsfälle (zu viel QST) sind IMMER eine Gutschriftszeile im
  nächsten Lohn — nie ein Darlehen.

## 5. K4 im Detail: Reihenfolge, Grenzgänger, Wochenaufenthalter (Walter 28.08.2026)

### 5.1 Masken-Reihenfolge (final, Walter 28.08.2026)

Top-down, Resultat GANZ UNTEN gross und nicht editierbar:

1. **Bewilligung** (Anzeige aus der Historie) — bei der Erfassung nur
   **Gültig ab**, bewusst KEIN Gültig-bis («wir wissen nie, wie lange»).
2. **Wohnsituation** (Grenzgänger / Wochenaufenthalter, siehe 5.2/5.3) —
   noch VOR dem Zivilstand.
3. **Zivilstand + Konfession** (nebeneinander, Inline-Edit → MA-Stamm).
4. **Partner** (alle Varianten: Ehe/Konkubinat, Erwerbstätig-/
   Ersatzeinkommens-Frage, Arbeitgeber).
5. **Kinder** (Haushalt / Erstausbildung / gemeinsames Kind).
6. **RESULTAT-Karte** (Ampel GRÜN/ORANGE/ROT, siehe Abschnitt 2).

### 5.2 Grenzgänger = Halbautomatik aus dem Wohnsitz

Wohnsitz im Ausland (Hauptwohnsitz-Land ≠ CH) ⇒ Status «Auslandswohnsitz»
wird vom Programm gesetzt, kein manuelles Kreuz. Einzige manuelle Frage:
**«Kehrt täglich/regelmässig an den ausländischen Wohnsitz zurück?»** —
Ja = Grenzgänger, Nein = internationaler Wochenaufenthalter (CH-Aufent-
haltsadresse Pflicht). DE-Grenzgänger-Tarife (L/M/N/P/Q) NUR mit jährlicher
Ansässigkeitsbescheinigung Gre-1/2 — sonst ordentliche Tarife.

### 5.3 Wochenaufenthalter (schweizintern) — «Hauptwohnsitz gewinnt»

Regeln (ESTV-Praxis, verifiziert 28.08.2026):

- **Der HAUPTWOHNSITZ bestimmt den QST-Kanton** — nie der Wochenaufent-
  haltsort. Beispiel: wohnt Sursee LU, arbeitet Oftringen AG, Wochenzimmer
  Zofingen AG → QST-Kanton **LU**. In OneCrew schon heute korrekt, weil
  der Steuerkanton server-autoritativ aus der Hauptadresse kommt
  (`ApplyWohnadresseAsync`).
- **KEIN eigener Tarifcode** für Wochenaufenthalter — normaler Tarif des
  Wohnkantons; das Flag ist reine Sachverhaltsinfo (Anmeldeformular, ELM).
- **Adress-Konvention (fix, Walter 28.08.2026): die easy@work-Adresse IST
  IMMER der Hauptwohnsitz.** Die Wochenaufenthaltsadresse wird in OneCrew
  als **Zusatzadresse Typ «Wochenaufenthalt»** geführt (Pflicht, wenn
  Wochenaufenthalter; PLZ-Lookup füllt Ort/Kanton/BFS). easy@work-Wunsch
  «zweites Adressfeld» steht auf der Pendenzenliste
  (`docs/easyatwork-pendenzen.md`).
- **Quelle des Flags:** existiert die Wochenaufenthalt-Zusatzadresse, ist
  das QST-Häkchen «Wochenaufenthalter/in» server-autoritativ gesetzt und im
  Modal gesperrt (`ApplyWochenaufenthaltAsync`, umgesetzt 28.08.2026 —
  gleiche Mechanik wie Konkubinat/Kirchensteuer/Wohnadresse). Ohne Adresse
  bleibt das Häkchen editierbar (Alt-Fälle) + Warnung W8 «Adresse fehlt».
- **Wächter W9:** ist die Hauptadresse identisch mit der Wochenaufenthalts-
  adresse (Strasse+PLZ), warnt das System — vermutlich wurde das Wochen-
  zimmer als Hauptwohnsitz in easy@work eingetragen; der QST-Kanton wäre
  falsch. (Beide Warnungen in `QstPflichtCheckService.BuildTarifWarnungenAsync`.)
- **Swissdec/ELM:** das Schema kennt «Mit/Ohne Wochenaufenthalt» und
  verknüpft die Aufenthaltsadresse als eigenen AddressType — beim XML-Bau
  (E5+) die Zusatzadresse dorthin mappen.
- **Internationale Wochenaufenthalter** (Hauptwohnsitz Ausland) sind der
  SPIEGELFALL von 5.2: dort ist die CH-Aufenthaltsadresse Pflicht und der
  Sachverhalt läuft über die Auslands-Logik — dieselbe Datenstruktur
  (Hauptwohnsitz + Aufenthaltsadresse), vertauschte Rollen.

### 5.4 K4-Vorleistungen bereits gebaut (28.08.2026)

Zusatzadresstyp «Wochenaufenthalt» (employees.js, hervorgehobener Chip),
Server-Guard `ApplyWochenaufenthaltAsync`, Modal-Lock
(`qstApplyWochenaufenthaltLock`), Warnungen W8/W9. Der Rest von K4
(Treppen-Layout, Sammel-Speichern, Behördenentscheid-Block) folgt als
eigene Etappe nach K2.

## 6. Etappenplan

| # | Etappe | Inhalt |
|---|---|---|
| **K1** | Korrektur-Fundament | `qst_korrektur`-Tabelle, rückwirkende Versionserfassung mit Pflicht-Grund (Korrektur-Weg statt Edit-Lock), Differenz-Berechnung, Anzeige im QST-Tab |
| **K2** | Verrechnung + Abrechnung | Korrekturzeile im Lohnlauf (ELM-Codes), Ausweis in der kantonalen QST-Abrechnung, Jahresgrenze |
| **K3** | Darlehens-Modul | Tabellen, Erfassung (aus Korrektur vorbefüllt + manuell), Vertrag-PDF, Ratenabzug im Lohnlauf, Lohnbeleg-Saldo, Fibu-Konto |
| **K4** | QST-Maske = Herleitungs-Treppe | Stufen-Layout, Inline-Edits mit Sammel-Speichern + Quittung, Folgemonats-Automatik, Resultat-Ampel, Behördenentscheid-Block |
| **K5** | (mit E6) Elektronische Korrekturmeldung | Swissdec-Korrektur-/Ersatzmeldungen aus den qst_korrektur-Posten |

Reihenfolge bewusst: erst das Korrektur-Fundament (K1/K2), dann Darlehen
(K3), dann die neue Maske (K4) — sie nutzt alles Vorherige.
