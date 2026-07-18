# Liquid Glass UI Konzept

## Ziel

Die HR-Loesung bekommt einen ruhigen, modernen und einheitlichen Look. Der Stil orientiert sich am letzten Liquid-Glass-Mockup: helle Flaechen, weiche Transparenz, Glas-Ebenen, dezente Schatten und klare Primaeraktionen.

Diese Datei ist ab jetzt der gespeicherte Ankerpunkt fuer die visuelle Richtung. Neue Screens oder CSS-Aenderungen sollen diese Regeln erweitern, nicht nur in einem Chat oder Screenshot existieren.

## Referenzbild

Letzter bekannter Stand aus dem Chat:

- sehr heller Hintergrund mit weicher, skizzenhafter Struktur
- linke schmale Glass-Sidebar mit Filiale, Datum, Avatar, Navigation und Schnellaktionen
- zentrale grosse Glass-Kacheln fuer Hauptmodule
- rechtes halbtransparentes To-do-Panel
- fast monochrome Bedienung mit wenig Farbe
- Apple/VisionOS-artiger Eindruck: hell, ruhig, weich, schwebend

## Layout-Prinzipien

### 1. Hintergrund

- Basis: warmes Off-White statt hartes Weiss
- dezente graue Linien oder abstrakte Form im Hintergrund
- keine lauten Verlaeufe, keine starken Farben
- Inhalt liegt als Glas-Ebene ueber dem Hintergrund

### 2. Sidebar

- schmaler vertikaler Balken links
- Glas-Effekt mit Blur, heller Kontur und weichem Schatten
- oben Filialauswahl
- darunter Datum und Benutzeravatar
- mittig Icon-Navigation
- unten Schnellaktionen wie Vertrag, Dokument, Brief

### 3. Hauptnavigation

- grosse, atmende Kacheln statt dichter Admin-Listen
- Hauptkacheln:
  - Vertraege
  - Dokumente
  - Mitarbeiter
  - Postfach
  - Auswertungen
- jede Kachel hat ein grosses Line-Icon und ein kurzes Label
- Hover: minimal anheben, Glas wird etwas klarer

### 4. Rechte Kontextkarte

- To-dos und naechste Aktionen rechts
- halbtransparent, weniger dominant als Hauptkacheln
- Aufgabenzeilen mit Icon, Text und Chevron
- geeignet fuer:
  - QST pruefen
  - Vertrag erstellen
  - Dokument freigeben
  - Lohnlauf wartet

### 5. Farbe

Grundpalette:

- Hintergrund: `#f6f3ee`
- Text stark: `#3f3f3f`
- Text normal: `#646464`
- Text leise: `#8b8b8b`
- Glas: `rgba(255, 255, 255, 0.38)`
- Glas stark: `rgba(255, 255, 255, 0.58)`
- Kontur: `rgba(255, 255, 255, 0.62)`
- Schatten: `rgba(60, 55, 48, 0.14)`
- Akzent sparsam: `#6b7280`

Keine grellen Primaerfarben auf der Home-Ansicht. Blau/Gruen/Rot bleiben fuer Fachstatus und kritische Aktionen in Detailmodulen.

### 6. Typografie

- Systemschrift oder Inter
- Labels klein, ruhig, mit viel Weissraum
- Seitentitel optional sehr reduziert
- keine fetten, grossen Dashboard-Zahlen auf der Home-Ansicht

### 7. Interaktion

- Hover hebt Elemente maximal 2-4 px an
- keine schnellen, harten Animationen
- Fokuszustand gut sichtbar, aber weich
- aktive Navigation als gefuellte Glas-Pille

### 8. Uebertragung auf bestehende HR-App

Nicht alles auf einmal ersetzen. Reihenfolge:

1. Standalone Home-Prototyp bestaetigen
2. Dashboard/Home als neue Einstiegsseite ableiten
3. Sidebar optisch angleichen
4. Cards, Tabellen und Statusleisten in Detailseiten schrittweise konsolidieren
5. Dark Mode separat definieren, nicht automatisch vom hellen Liquid-Glass-Look ableiten

### 8.5 Dark Liquid Glass (Walter 18.07.2026, elevated)

Moderner Standard (Apple/VisionOS/Material): **nicht Tiefschwarz**, sondern
mitteldunkler elevated Nachthimmel + weiss-getönte Glasflächen.

| Token | Wert |
|---|---|
| Hintergrund | soft blues + `#1a2433 → #1e2a3c → #182232` |
| Glas | `rgba(255,255,255,.08)` |
| Glas stark | `rgba(255,255,255,.12)` |
| Feld | `rgba(255,255,255,.07)` |
| Rand | `rgba(180,200,220,.22)` |
| Text stark | `#f4f7fb` |
| Text | `#d5dee8` |
| Text soft | `#9aabbc` |
| Active | `rgba(96,140,190,.38)` / Text `#e8f3ff` |
| Primär-Button | Kohle `#3f3f3f` (wie hell) |
| Theme-Toggle (im Dark) | helle Glas-Pille, kein Gelb |

CSS-Variablen auf `body.theme-dark`: `--dlg-*` in `wwwroot/css/app.css`.

## Regeln fuer Umsetzung

- Produktive Fachlogik bleibt unberuehrt.
- Der erste Schritt ist ein isolierter Prototyp.
- Keine Framework-Migration.
- Keine Abhaengigkeit von React/Tailwind.
- Bestehende Klassen wie `.btn`, `.card`, `.page-head`, Tabellen und Sticky-Header werden spaeter geordnet angeglichen.
- Jeder neue visuelle Stand muss entweder als Datei im Repo oder als Artefakt gespeichert werden.
