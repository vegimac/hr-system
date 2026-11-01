# Code-Landkarte OneCrew

Wo liegt was? Vor jedem Neubau hier nachsehen und mit `grep` prüfen, ob es das schon gibt.
Neues kommt in die passende bestehende Datei oder als neue kleine Datei in den Modul-Ordner —
keine neue Riesendatei (Richtwert ≤ ~1'500 Zeilen). Stand: 01.11.2026.

## Backend (ASP.NET Core 8, EF Core, PostgreSQL)

| Ort | Inhalt |
|---|---|
| `Program.cs` | Start, Dienst-Registrierung, **Start-SQL** (`SchemaStand`, Blöcke `if (dbStand < N)`), Schema-Check |
| `Data/AppDbContext.cs` | EF-Mapping aller Tabellen |
| `Models/` | Entitäten |
| `Controllers/` | REST-Endpunkte, dünn — Logik gehört in `Services/` |
| `Services/PayrollCalculationEngine.cs` | Lohnberechnung (Orchestrierung, DB-gekoppelt) |
| `Services/PayrollCalculationService.cs` | reine Rechen-Helfer (`PayrollCalculations`, unit-testbar) |
| `Services/LohnFormelwerk/` | Lohncodes (`LohnCodes`), Aliase, Formeln |
| `Services/Elm/` | Swissdec ELM: Builder Monat/Jahr/Grenzgänger, FAK (`ElmFakAdressierung`, `ElmFakJahreslohn`), Übermittlung, SUA/Zertifikat, Dialoge |
| `Services/EasyAtWork/` | easy@work-Abgleich (MA, Verträge, Stempelzeiten, Absenzen) |
| `Services/Personaldaten/` | Plausibilitätsprüfung Personaldaten |
| `Services/DokumentAblage/` | Ablage nach Angabe (Zielliste) |
| `Services/WebStamp/` | Briefpost über die Post |
| `Services/Vorsystem/` | Mirus-Import, Lohn-Simulation |
| `Tests/` | xUnit-Tests (`dotnet test Tests/hr-system.Tests.csproj`) |

## Frontend (statisch, kein Build-Schritt)

| Ort | Inhalt |
|---|---|
| `wwwroot/index.html` | Haupt-App: alle Seiten-Divs + Script-Tags (Module über `data-modul`, Reihenfolge zählt) |
| `wwwroot/js/app-core.js` | Kern: Login, `showPage`, Filial-Selektor, Sichtbarkeit (`applyAreaVisibility`) |
| `wwwroot/css/app.css` | Grund-Stile; Modul-Stile siehe unten |
| `wwwroot/employees.js` | Mitarbeiter-Maske (wird aufgeteilt, siehe unten) |
| `wwwroot/js/elm.js` | ELM Cockpit (Seite `page-elm`, Präfix `e2`) |
| `wwwroot/js/elm-schnittstelle.js`, `elm-meldungen.js`, `elm-zertifikat.js` | ELM-Entwicklungsseiten unter System |
| `wwwroot/js/payroll.js`, `akonto-workflow.js` | Lohnlauf |
| `wwwroot/js/documents.js`, `posteingang.js` | Dokumente, Postfach |
| `wwwroot/js/liquid-select.js`, `save-blob.js`, `file-preview.js` | globale Bausteine (Selects, Speichern unter, Vorschau) |
| `wwwroot/import.html` | easy@work-CSV-Import (eigene Seite) |

## Regeln (Kurzfassung)
- Fachliche Regeln stehen in `CLAUDE.md` (Eiserne Kurzregeln) und `docs/claude/*.md`.
- Gibt es für ein Thema schon eine Regel-Klasse (z.B. `Dienstalter`, `MindestlohnAlter`,
  `PersonaldatenPruefung`, `KrankUnfallZeitgutschrift`), immer diese verwenden.
