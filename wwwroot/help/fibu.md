# Buchhaltung (Fibu)

Der Fibu-Bereich verbindet den abgeschlossenen Lohnlauf mit der Finanzbuchhaltung: **Fibu-Journal** (Buchungszeilen nach Mirus/McDonald's-Schema) und **Buchhaltungs-Saldo-Liste**. Sichtbar für die Rollen **buchhaltung** und **admin**.

## Wo finde ich was?

Sidebar **FIBU** → Filiale kommt aus dem globalen Sidebar-Selektor, Jahr/Monat wählst du auf der Seite. PDFs öffnen im Vorschaufenster (Drucken / Herunterladen / Schliessen).

## Fibu-Journal

Erzeugt aus dem **definitiv abgeschlossenen** Lohnlauf die Buchungszeilen: Bruttolohn nach Kostenstelle, SV-/QST-Abzüge, Arbeitgeber-Beiträge (AHV/ALV/FAK, BVG, NBU/KTG), LGAV, Rückstellungen Ferien/Feiertage/13. ML und Nettolohn.

- **Kostenstellen:** 100 Crew Fix (MTP/FIX) · 200 Crew Flex (FLEX) · 300 Management (FIX-M).
- **Konto 1920** ist das Lohn-Durchlaufkonto — es muss auf **0** aufgehen. Weist das Journal eine Differenz aus, stimmen Slip-Abzüge und eingefrorenes Netto nicht überein → Hinweise am Ende des Journals lesen.
- Die Konten-Zuordnung pflegst du (als Admin) in **Systemeinstellungen → Kontoplan (Fibu)**.

## Buchhaltungs-Saldo-Liste

A4-quer-PDF pro Filiale + Periode mit allen Saldi pro MA: Stunden, Nacht, Ferien (Tage + CHF), Feiertage, 13. ML, Brutto/Netto, IBAN — inkl. Summenzeile.

## Voraussetzung: Periode abgeschlossen

Beide Auswertungen gibt es erst, wenn der **Definitivlauf** der Periode mindestens **provisorisch abgeschlossen** ist. Vorher kommt die Meldung „Periode nicht abgeschlossen" — das ist Absicht: die Listen entstehen aus den eingefrorenen Lohn-Snapshots, nicht aus Live-Daten.

## Rolle „buchhaltung" — was darf sie?

- Alles wie **superuser** (volle HR-Funktionen) **plus** diesen Fibu-Bereich.
- Aber **filial-beschränkt**: sie sieht nur die in der Benutzerverwaltung zugeteilten Filialen.

## Häufige Fragen

**Das Journal geht auf 1920 nicht auf — was nun?**
Meist wurde die Periode nach dem Bestätigen noch verändert. Faustregel: Periode zurücksetzen und neu bestätigen erzeugt konsistente Snapshots. Im Zweifel Admin/Walter beiziehen.

**Warum sehe ich als superuser den Fibu-Bereich nicht?**
Absicht — Fibu ist der Rolle `buchhaltung` (und admin) vorbehalten.

**Wohin gehen die Zahlen danach?**
Aktuell als PDF für die manuelle Erfassung; ein direkter Abacus-Export ist als Ausbaustufe vorgesehen.
