# Neue Filiale importieren

Wenn eine Filiale von Mirus / alten Systemen nach OneCrew kommt, gibt es den **Onboarding-Hub**:

**System → Neue Filiale importieren**

Arbeite die Karten **der Reihe nach** ab. Nach erfolgreichem Commit schliesst sich die Seite oft automatisch (zurück zum Hub).

## Die Schritte

| Nr. | Karte | Zweck |
|---|---|---|
| 1 | **Mitarbeiter & Verträge** | easy@work-CSV: aktive MA, Phantom (Supervisor), inaktive Dossiers, Adress-Updates |
| 2 | **HR-Review (Mirus)** | Bewilligung, Geburt, Nationalität, Ein-/Austritt ergänzen |
| 4 | **d.velop Dokumente** | Alte Personalakten (CSV + ZIP) |
| 5 | **Familienzulagen-Kontrolle** | Kinder + Zulagenbeträge prüfen/übernehmen |
| 6 | **Stammdaten-Anreicherung** | GastroSocial-XLSX → AHV, Zivilstand, Sprache … |
| 7 | **QST-Auswertung (Mirus)** | Tarifcodes aus Mirus |
| 8 | **CHF-Saldi** | Mirus «Rückstellungsliste Saldomethode» → Ferien-Geld (905) + 13. ML (906) |
| 9 | **Stunden-Saldi** | Mirus «Monatsblatt» → Zeitsaldo (901), Nacht (904, **auch FLEX**), Ferien-Tage (903), Feiertag-Tage (902) |
| 10 | **Adress-/Kontakt-Vergleich** | Mirus vs. OneCrew (nur Kontrolle) |

(Schritt 3 Bank-Import entfällt — IBAN kommt über easy@work.)

## Saldi-Import — Praxis

- **Vortrags- / Migrationsperiode** = die **älteste noch offene Lohnperiode** der Filiale (nicht «heute»).
- MA-Pool folgt dieser Periode (wer damals aktiv war).
- Vor dem Upload die Mirus-Schritte auf der Import-Seite beachten (welche Liste / welche Spalten).
- Immer zuerst **Analysieren**, dann Commit. Bei NO_MATCH: manueller Picker.

## Tipps

- **Filiale** in der Sidebar = Ziel-Filiale. Falsche Filiale = Chaos.
- Immer zuerst **Analysieren / Vorschau**, dann Commit.
- Bei Namens-Zweifeln: manueller MA-Picker in der Vorschau.
- Nach dem Import: Dashboard-To-dos und einen Probe-Lohnlauf.
- Manche Importer bleiben zusätzlich in den Systemeinstellungen erreichbar (z.B. d.velop für den laufenden Betrieb).

## Saldi-Vortrag manuell

Falls du einzelne Saldi nachtragen musst: System → **Saldi-Vortrag** (nicht nur im Hub).
