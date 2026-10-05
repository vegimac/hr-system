# Zeitgutschrift bei Krankheit und Unfall (FIX, FIX-M, MTP)

Stand 06.10.2026 — besprochen mit Walter am 05.10.2026, gebaut in der Nacht auf den 06.10.2026.

## Worum es geht

Ist ein MA krank oder verunfallt, bekommt er Stunden gutgeschrieben, damit er nicht ins Minus fällt.
Bisher galt pauschal «Wochenstunden ÷ 5 pro angekreuztem Tag». Das passt nicht zur Gastro:

- Wer 80 % an 4 Tagen arbeitet, fehlt an einem Arbeitstag 8.4 h (33.6 ÷ 4), nicht 6.72 h.
- Ein Arztzeugnis über freie Tage darf kein Plus geben.
- Nach dem bekannten Dienstplan weiss niemand mehr, wann der MA gearbeitet hätte — dann gilt der Durchschnitt pro Kalendertag.

## Die Regel

Eine Stelle im Code: `Services/KrankUnfallZeitgutschrift.cs`. Lohnrechnung, Absenz-Maske, Speichern,
easy@work-Sync, Mirus-Dienstplan-Import und die Neuberechnung rufen sie auf.

Die Filiale wählt die Methode (Filiale → Einstellungen → Arbeitszeit → «Zeitgutschrift Krank/Unfall»):

| Methode | Rechnung pro Tag |
|---|---|
| `DIENSTPLAN_1_7` (Standard) | bis «Dienstplan bekannt bis»: eingeplanter Tag = Wochenstunden ÷ Arbeitstage, freier Tag = 0 h; danach bzw. ohne Datum: Wochenstunden ÷ 7 pro Kalendertag |
| `KALENDER_1_7` | jeder Kalendertag Wochenstunden ÷ 7 |
| `MO_FR_1_5` | Mo–Fr Wochenstunden ÷ 5, Sa/So 0 h |

- Das Ausfall-Prozent wirkt auf jeden Tag (50 % krank = halbe Stunden).
- Keine Wochengrenze: wer mehr Tage eingeplant war als üblich, bekommt Plusstunden.
- Wochenstunden: FIX/FIX-M aus dem Vertrag (`WeeklyHours`, sonst Betrieb × Pensum), MTP = Garantie.

## Arbeitstage pro Woche

Neues Feld am Vertrag (`employment.arbeitstage_pro_woche`, Schritte von 0.5, erlaubt 0.5–6).
Leer = Vorschlag:

- FIX/FIX-M: 5 × Pensum (80 % → 4, 50 % → 2.5)
- MTP: 5 × Garantie ÷ Betriebs-Wochenstunden, auf 0.5 gerundet (21 h bei 42 h → 2.5; 25 h → 3), höchstens 5
- MTP ohne Garantie: 5

Gesetzt wird es im Mitarbeiter → Vertrag → ⋮ «Arbeitstage pro Woche» (HR-Team), Endpunkt
`PATCH /api/employments/{id}/arbeitstage`. Bewusst ein eigener Weg: easy@work kennt das Feld nicht,
der Import würde es sonst wegräumen. Erzeugt der easy@work-Sync einen neuen Vertragsabschnitt,
gilt dort wieder der Vorschlag.

## «Dienstplan bekannt bis»

Neues Feld an der Absenz (`absence.dienstplan_bis`, nur Krank/Unfall). In der Maske:

- Datum eintragen, dann die eingeplanten Tage bis zu diesem Datum ankreuzen.
- Knöpfe «Plan deckt ganze Absenz» und «Kein Dienstplan».
- Tage nach dem Datum sind grau mit «1/7».
- Neue Absenzen starten ohne Dienstplan und ohne Kreuze (zählt 1/7).

Der Server rechnet die Stunden selbst (`POST /api/absences/zeitgutschrift-vorschau` für die Maske,
beim Speichern `KrankUnfallStundenSetzenAsync`). Kreuze nach dem Planende werden nicht gespeichert.

**Importe** (easy@work, Mirus-Dienstplan) kennen keinen Dienstplan → 1/7. Bleiben beim easy@work-Sync
Datum und Typ gleich, bleiben Kreuze und Datum erhalten; ändert sich das Datum, werden beide geleert.

**Bestehende Absenzen** (einmalige Startmigration, Schema-Stand 51): Bei FIX/FIX-M, von Hand erfasst
und mit Tagesauswahl, wird «Dienstplan bis» auf das Absenz-Ende gesetzt — die Kreuze zählen weiter.
MTP-Absenzen und Importe bleiben ohne Datum (1/7).

## Geld (MTP)

Die Zeitgutschrift ist nur die Zeitseite. Das Geld läuft pro Kalendertag:

- FIX/FIX-M: wie bisher Korrektur 75.x/65.x und Taggeld.
- MTP (seit Schema-Stand 52 wie FIX): Festlohn läuft voll, Korrektur 75.1/65.1 = Garantie ÷ 7 ×
  Stundenlohn × Krank-% pro Kalendertag, dazu Karenz 88 % / Taggeld 80 %. Die Zeitgutschrift kürzt
  nur das Saldo-Soll (Minus wird nachgeholt, Plus über 55.3 bezahlt). Formeln: `docs/lohn-formeln.md`,
  Abschnitt «MTP Krankheit/Unfall». Der MTP-Stundenbericht zählt mit derselben Regel.

## Swissdec

Die Zeitgutschrift selbst nicht — Stunden-Saldi gehen nicht in die Lohnmeldung. Das MTP-Geldmodell
(75.1/65.1) ändert Lohnzeilen nur bei MTP mit Krankheit/Unfall; hat die Muster AG solche Monate,
müssen sie nach dem Deploy neu gerechnet und gegen RefXML geprüft werden.

## Tests

`Tests/KrankUnfallZeitgutschriftTests.cs` (Walters Beispiele, erfundene Daten).
