# Mindestlöhne & Vertragsanpassung

Das System kennt zwei Floors:

1. **L-GAV** — Matrix Funktion × Modell × Ausbildung (System → Mindestlöhne)
2. **Kommunaler Mindestlohn** der Filiale — siehe [Filialen](#filialen)

Beim Check gilt immer der **höhere** Wert. Unterbezahlte MA blockieren den Lohnlauf.

## Matrix pflegen (Admin / HR)

**System → Mindestlöhne**

- Tabelle nach Funktion und Ausbildung
- Sätze, die schon in einem abgeschlossenen Lohnlauf verwendet wurden, sind **grau** (nicht mehr ändern)
- Änderungen nur über **„+ Folge-Version anlegen"** ab einem Datum
- Farben in der Planungsspalte: rot = noch nicht bestätigt, orange = bestätigt unverändert, grün = Betrag geändert

## Verträge automatisch anpassen

Wenn eine Folge-Version in der Zukunft liegt und Verträge darunter fallen:

1. Banner auf [Dashboard](#dashboard) oder Verträge
2. Liste öffnen → neue Löhne prüfen
3. **Übernehmen** → neuer Vertrag ab Stichtag, alter endet Vortag
4. Optional Postfach-Mitteilung an den MA

## Was blockiert den Lohnlauf?

| Problem | Bedeutung |
|---|---|
| **Mindestlohn unterschritten** | Effektiver Lohn &lt; Minimum am Periodenende |
| **Lohnsumme fehlt** | Gar kein Lohn im Vertrag (z.B. FIX-M noch ohne Betrag) |

Beides erscheint als ⚠ in der Lohnliste und als To-do.

## Tipps

- FIX/FIX-M werden gegen **Monatslohn** geprüft, FLEX/MTP gegen **Stundenlohn**.
- Jugendliche können eigene (tiefere) Sätze haben (`age_max`).
- Nie alte, schon verwendete Sätze „korrigieren" — immer neue Version.
