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

### 8.5 Dark Liquid Glass (Walter 18.07.2026, Mittelweg)

**Zwischen Milch und Flach:** opake Karten (Text klar) + deutlich sichtbarer
Eisblau-Verlaufsrahmen + leichte Tiefe. Kein Backdrop-Blur.

| Token | Wert |
|---|---|
| Hintergrund | dezente Blooms + dunkler Navy-Verlauf |
| Karten-Fill | `--dlg-fill-grad` (opaker Innen-Verlauf für Volumen) |
| Rand | `--dlg-rim` hell-cyan → tief-blau → eisblau (klar sichtbar) |
| Technik | Fill-Grad + Rim (`padding-box` / `border-box`); **kein** `backdrop-filter` |
| Schatten | feiner Eisblau-Ring + kurzer Soft-Glow + Drop-Shadow + Top-Inset |
| Text | Werte `#ffffff`, Labels `--dlg-text-muted` |
| Primär-Button | Kohle `#3f3f3f` |

Familienmitglied-Modal = selber MA-Übersicht-Standard.

CSS-Variablen auf `body.theme-dark`: `--dlg-*` in `wwwroot/css/app.css`.

### 8.6 Hell = Kohle + Tiefe (Walter 18.07.2026)

Gleicher Aufbau wie Dark, andere Palette — **Kohle bleibt**, kein Hellblau-Aktiv:

| Token | Wert |
|---|---|
| Hintergrund | warmes Off-White `#f6f3ee` + dezente Blooms |
| Karten-Fill | `--emp-fill-grad` (opaker warm-weiss → sand) |
| Rand | Pearl/Kohle-Rim (weiss → warmgrau → weiss) |
| Technik | Fill-Grad + Rim (`padding-box` / `border-box`); **kein** `backdrop-filter` |
| Schatten | Kohle-Ring + Ambient + Drop + Top-Inset (wie Dark gestaffelt) |
| Text | stark `#2f2f2f` / normal `#4a4a4a` / Labels `#8b8b8b` |
| Aktiv (Tab/Liste) | Kohle `#3f3f3f` (weiss auf Kohle), **kein** Default-Blau |

Scope: `#page-mitarbeiter.liquid-employee` — Variablen `--emp-*` in `wwwroot/css/app.css`.
Layout der Übersicht (3 Zeilen Personalien, gepinnte Verträge) ist theme-unabhängig.

## Regeln fuer Umsetzung

- Produktive Fachlogik bleibt unberuehrt.
- Der erste Schritt ist ein isolierter Prototyp.
- Keine Framework-Migration.
- Keine Abhaengigkeit von React/Tailwind.
- Bestehende Klassen wie `.btn`, `.card`, `.page-head`, Tabellen und Sticky-Header werden spaeter geordnet angeglichen.
- Jeder neue visuelle Stand muss entweder als Datei im Repo oder als Artefakt gespeichert werden.
