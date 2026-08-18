# Absenz-Typ-Matrix pro Vertragsmodell (Walter-Konzept 18.08.2026 — FIXIERT, Bau ausstehend)

Ersetzt die verstreuten Felder (Zeitgutschrift, GutschriftModus, BasisStunden,
BasisStundenMtp, UtpAuszahlung) durch eine 3-Spalten-Matrix pro Absenz-Typ.

## Matrix

|              | FIX / FIX-M                  | MTP                        | FLEX                        |
|--------------|------------------------------|----------------------------|-----------------------------|
| Wirkung      | Gutschrift ja/nein           | Gutschrift ja/nein         | als Stundenlohn auszahlen   |
| Zählweise    | KALENDER (1/7) · ARBEITSTAGE (1/5 Mo–Fr) · DIENSTPLAN (1/5 nur «hätte gearbeitet»-Tage, Fallback Mo–Fr) | dito | dito |
| Basis        | BETRIEB / VERTRAG            | GARANTIE / BETRIEB         | BETRIEB (fix, keine Wahl)   |

Gemeinsam bleiben: Reduziert Saldo, Verlängert Probezeit, ALK-Kürzel, Sort, Aktiv.

## Entscheide Walter (18.08.2026)
- FLEX-Basis immer BETRIEB (kein Pensum als Alternative).
- Zählweise PRO Spalte (Fall unterschiedlicher Modi pro Modell möglich).
- «Dienstplan berücksichtigen» = dritte Zählweise, kein separates Häkchen.

## Backfill (verhaltensgleich!)
- wirkung_fix = wirkung_mtp = zeitgutschrift · wirkung_flex = utp_auszahlung
- zaehlweise_* : GutschriftModus 1/7 → KALENDER; sonst KRANK/UNFALL → DIENSTPLAN, übrige → ARBEITSTAGE
- basis_fix = basis_stunden · basis_mtp = basis_stunden_mtp · (flex: fix BETRIEB)
- EO-Typen (MUTT_VATER/MUTTERSCHAFT/VATERSCHAFT): Wirkung überall NEIN
  (Doppelzahlungs-Sperre wird damit regulär statt engine-hart).

## Bau-Etappen
1. Schema (7 neue Spalten) + Backfill im DO-Block (einmalig, kein User-Override).
2. Engine: ComputeAbsenzHours + MTP-GeplantTage-Pfad lesen die Matrix
   (DIENSTPLAN-Zählung generisch: WorkedDays-Auswahl im Periodenfenster).
3. UI: Absenz-Typ-Formular als Matrix (3 Spalten), Liste kompakt.
4. Alte Felder nach Grün-Lauf entfernen (Testmodus — kein Stichtag nötig).
5. Abnahme: Basen-Kontrolle 55/55 grün + Slip-Vergleich (Krank-, Nacht-, EO-Fall).

Die Matrix steuert NUR die Stunden-Rechnung. Fach-Mechanik bleibt typgebunden
(Karenz/Taggeld, Ferien-Pott, EO-Zeilen 120.x/125.x, UNBEZ_URLAUB-Neutralität).
