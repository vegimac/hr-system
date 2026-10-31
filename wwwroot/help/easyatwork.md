# easy@work

easy@work ist die Quelle für **Personalnummern, Verträge (meist), Stempelzeiten und Verfügbarkeit**. OneCrew spiegelt diese Daten — korrigieren von Stempelzeiten geht nur in easy@work.

## Was synchronisiert wird

| Daten | Wann |
|---|---|
| **Stempelzeiten** | Täglicher Auto-Sync (früh morgens) + manuell |
| **MA / Verträge** | Beim Auto-Sync (Stufe 1) und beim Einzel-MA-Sync |
| **Verfügbarkeit** | Beim **Einzel-MA-Sync** („easy@work synchronisieren" im MA-Detail) |

## Einzelnen MA synchronisieren

Im Mitarbeiter-Detail oben: **easy@work synchronisieren**.  
Holt Stammdaten/Vertrag und Verfügbarkeit für genau diesen MA (best effort).

## Neuer MA

Nur so: zuerst in easy@work anlegen → in OneCrew **„＋ Neuer MA aus easy@work"**.  
Details: [Mitarbeiter](#mitarbeiter).

## Strict-Import (wichtig)

Aktive/zukünftige Verträge mit Fehlern → **CONFLICT**, kein Import bis easy@work korrigiert ist:

- FLEX/MTP: Stunden **pro Woche** (nie „pro Monat")
- Lohn Pflicht (Ausnahme FIX-M vertraulich — siehe [Verträge](#vertraege))
- Keine überlappenden Verträge
- **Vertragsart / `type_id` entscheidet MTP vs. FLEX** — nicht die Heuristik «17 h → FLEX». Stimmt das Modell nicht, zuerst in easy@work die Vertragsart prüfen.
- Zivilstand-Code **E** = Getrennt (wird korrekt gespiegelt)

## Probezeiten nachführen

Im **easy@work-Modul** (Admin): Button **«⚓ Probezeiten nachführen»**.

- Legt fehlende Probezeiten an und verankert sie an der **ersten Stempelzeit ab Eintritt** (auch bei Vertrags-Split im Sync)
- Nur für MA mit **Eintritt ≤ 4 Monate** zurück (ältere Eintritte werden übersprungen)
- In der MA-Übersicht / im Lohn-Kopf erscheint die Probezeit nur, solange sie in der Periode **aktiv** ist

## Admin: easy@work-Modul

Verbindung, Filial-Zuordnung (Customer), Syncs, Logs und das Zusammenführen von Dubletten. Den einzelnen MA synchronisierst du im Mitarbeiter-Detail, nicht in diesem Modul.

## Häufige Fragen

**Stempelzeit falsch?** → In easy@work korrigieren, Sync abwarten. In OneCrew nur Anzeige.

**Verfügbarkeit leer?** → In easy@work pflegen, dann Einzel-Sync am MA.

**Lohn in easy@work leer bei FIX-M?** → Absichtlich möglich; Lohn nur in OneCrew setzen ([Verträge](#vertraege)).

**MA wurde als FLEX statt MTP importiert?** → In easy@work `type_id` / Vertragsart MTP prüfen, dann Sync.


## Absenzen

Absenzen erfasst du in OneCrew, Tab Absenzen. Ein Import aus easy@work gibt es für Krankheit und Ähnliches (Vorschau, dann übernehmen):

- **NEU**: nur in easy@work → wird angelegt. Einzelne Zeilen lassen sich vorher abwählen.
- **SCHON ERFASST**: Beginn, Ende und Typ stimmen → wird nur verknüpft.
- **ABWEICHUNG**: Datum oder Typ weicht von der erfassten Absenz ab → wird nicht überschrieben. Von Hand klären.
- Bei Krankheit und Unfall (FIX, FIX-M, MTP) danach die **Arbeitstage** in der Absenz ergänzen. Der Import kennt den Dienstplan nicht.
- **Ferien** kommen nicht aus easy@work (dort sind oft nur Freiwünsche). Ferien erfasst du in OneCrew.
