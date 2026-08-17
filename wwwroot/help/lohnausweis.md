# Jahres-Lohnausweis (ESTV Form 11)

OneCrew erstellt den amtlichen Schweizer Lohnausweis als PDF — **inklusive
2D-Barcode (PDF417)** nach Swissdec-Standard, wie ihn Mirus und der offizielle
eLohnausweis der Steuerkonferenz drucken. Die Werte werden automatisch aus
allen Lohnabrechnungen des Jahres zusammengezählt.

## So erstellst du einen Lohnausweis

1. **HR-Hub → Auswertungen & Exporte → Jahres-Lohnausweis** öffnen.
2. Mitarbeiter/in und Jahr wählen (die Liste zeigt nur MA, deren **laufender**
   Vertrag zur gewählten Filiale gehört; Ausgetretene über «Nur Aktive» umstellen).
3. **Vorschau** öffnen — alle Werte sind editierbar (z.B. Spesen ergänzen).
4. Unten entscheiden:
   - **Entwurf-PDF** — zum Prüfen. Trägt im Barcode die Kennung
     «Entwurf - Brouillon - Bozza» und darf beliebig oft neu erzeugt werden.
   - **✓ Finaler Lohnausweis** — das offizielle Dokument.

## Entwurf vs. Final — was ist der Unterschied?

Beim **ersten** finalen Druck vergibt OneCrew eine **definitive Dokument-Nummer
(DocID)** und friert das **Erstellungsdatum** ein. Jeder spätere Wiederdruck
desselben Lohnausweises (gleicher MA, gleiches Jahr) trägt exakt dieselbe
Identifikation — so verlangt es der Standard. Entwürfe sind davon ausgenommen.

Vor dem finalen Druck prüft OneCrew die **Pflichtdaten** (AHV-Nummer,
Geburtsdatum, Periode, Adressen, Ziffern 1/8/11). Fehlt etwas, wird der
Ausweis blockiert und die Meldung zählt auf, **welche Felder fehlen**.

## Automatisch richtig

- **Ganze Franken**: alle Beträge werden kaufmännisch gerundet — auf dem PDF,
  im Barcode und in den Bemerkungen identisch.
- **Periode (Ziffer E)**: aus Ein-/Austritt; ein offener Vertrag zählt als
  «nicht ausgetreten» (alte Vorverträge kappen die Periode nicht).
- **Teilzeit**: bei Pensum unter 100 % steht automatisch z.B. «80%-Stelle.»
  in den Bemerkungen (Ziffer 15).
- **KTG**: der Krankentaggeld-Beitrag erscheint als Bemerkung
  («Krankengeldversicherung CHF …») — nicht in Ziffer 9 (ESTV-Vorgabe).
- **Boxen F/G**: aus den Filial-Einstellungen (Werktransport / Kantine).
- Das PDF ist **fest «eingebrannt»** (geflattet) — es sieht in jedem
  PDF-Programm und auf jedem Ausdruck gleich aus.

## Der Barcode (Feld H)

Der PDF417-Barcode enthält die Lohnausweis-Daten maschinenlesbar
(Swissdec-Format, wie Mirus): die Steuerverwaltung kann den Ausweis scannen
statt abtippen. Grösse und Geometrie entsprechen den amtlichen Referenzen —
mit einem üblichen PDF417-Scanner prüfbar.

## Typische Fragen

| Frage | Antwort |
|---|---|
| Kann ich einen finalen Lohnausweis nochmals drucken? | Ja — er ist identisch (gleiche DocID, gleiches Datum). |
| Warum fehlt ein MA in der Liste? | Sein laufender Vertrag gehört zu einer anderen Filiale — oben die Filiale wechseln. |
| Beträge mit Rappen? | Nein — der Lohnausweis ist immer in ganzen Franken. |
| Woher kommen die Zahlen? | Aus den definitiv abgeschlossenen Lohnabrechnungen des Jahres. |
