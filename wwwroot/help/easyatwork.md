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

**System → easy@work API**

- Verbindung testen, Mapping Filiale ↔ Customer
- Syncs anstossen, Logs lesen
- Diagnose-Dumps (z.B. Verfügbarkeit roh)
- **MA zusammenführen** bei Dubletten
- **Probezeiten nachführen** (siehe oben)

## Häufige Fragen

**Stempelzeit falsch?** → In easy@work korrigieren, Sync abwarten. In OneCrew nur Anzeige.

**Verfügbarkeit leer?** → In easy@work pflegen, dann Einzel-Sync am MA.

**Lohn in easy@work leer bei FIX-M?** → Absichtlich möglich; Lohn nur in OneCrew setzen ([Verträge](#vertraege)).

**MA wurde als FLEX statt MTP importiert?** → In easy@work `type_id` / Vertragsart MTP prüfen, dann Sync.
