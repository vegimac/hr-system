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
| `Services/Elm/` | Swissdec ELM: Builder Monat/Jahr/Grenzgänger, FAK (`ElmFakAdressierung`, `ElmFakJahreslohn`), UVG/UVGZ/KTG (`ElmVersicherungJahreslohn`), Übermittlung, SUA/Zertifikat, Dialoge |
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
| `wwwroot/css/app*.css`, `elm.css` | Stile — siehe «CSS» unten (Reihenfolge = Kaskade) |
| `wwwroot/js/ma/` | Mitarbeiter-Maske (früher `employees.js`) — siehe unten |
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

## Mitarbeiter-Maske `wwwroot/js/ma/` (früher `employees.js`, aufgeteilt 01.11.2026)

Klassische Skripte, geladen in dieser Reihenfolge (index.html, `data-modul`). Alle Funktionen sind
global wie vorher; Aufrufe aus anderen Dateien bleiben gleich. Neues zum Mitarbeiter in die
passende Datei — wird eine Datei zu gross (> ~1'500 Zeilen), eine neue `ma-NN-thema.js` anhängen
und in index.html nach den bestehenden laden.

| Datei | Inhalt |
|---|---|
| `ma-01-liste.js` | Adress-Validierung, Liste laden/rendern, Suche/Filter, MA auswählen, Foto, alte Personalnummern, Arbeitszeugnis |
| `ma-02-detail.js` | Detail rendern (`renderEmployeeDetail`), Tab-Leiste |
| `ma-03-uebersicht.js` | Reiter «Übersicht» (Karten), Tab wechseln (`switchEmpTab`), Historie-Tab |
| `ma-04-quellensteuer.js` | Reiter Quellensteuer, Ausweis-Doku-Verknüpfung |
| `ma-05-qst-befreiung.js` | QST-Befreiung, QST-Tab rendern, Umzug/Kantonswechsel |
| `ma-06-familie.js` | Reiter Familie, Familienzulagen-Angaben, AHV-Nummer-Validierung, Hilfsfunktionen, Doku-Symbol |
| `ma-07-bearbeiten.js` | MA bearbeiten, Nationalitäts-Combobox, QST-Prüfroutine |
| `ma-08-familie-modal.js` | Familien-Fenster, Ehegatten-Ausweis, Ortssuche, Adressen, Bewilligungsstatus |
| `ma-09-familienzulagen.js` | Familienzulagen (versioniert), Datumshilfen |
| `ma-10-absenzen.js` | Stempelzeit-Eintrag, Absenzen, Mutterschaft (Bestätigung, Arztbrief, Gespräch) |
| `ma-11-absenz-panels.js` | Sperrfrist, Karenz, Absenz-Fenster, Kalender, Zeitgutschrift, Ferienkürzung |
| `ma-12-zulagen.js` | wiederkehrende Zulagen/Abzüge, Lohnabtretungen, Darlehen, Zahlungsreferenz, Reiter Zulagen |
| `ma-13-stempelzeiten.js` | Reiter Stempelzeiten |
| `ma-14-ktg-formulare.js` | Reiter KTG/UVG (Tagessatz), Stunden & Saldi, Formulare, QST-Anmeldung |
| `ma-15-bank-adressen.js` | Bankverbindungen, weitere Arbeitgeber, Adressen, Postfach-Zugang, MA löschen |
| `ma-16-neuzugang.js` | «Neuer MA aus easy@work», Ladekarte |
| `ma-17-bewilligung.js` | Bewilligungsverlauf, BVG-Zusatz, Bewilligung mit Ausweis-OCR |
| `ma-18-verwarnung-probezeit.js` | Verwarnungen, Probezeit |
| `ma-19-umzug-notfall.js` | Umzug bestätigen, Notfallkontakt, «Warum dieser Tarif?», Änderungen, Zivilstand, Ausweis-OCR Familie, Betriebszugehörigkeit |

## CSS `wwwroot/css/` (früher eine `app.css`, aufgeteilt 01.11.2026)

Die Dateien werden direkt hintereinander geladen; **die Reihenfolge ist die Kaskade** — nie
umsortieren. Die alte app.css war historisch geschichtet, darum sind die Teile nach ihrem
Hauptinhalt benannt, nicht streng nach Modul. Regeln für ein Modul findet man per `grep` nach
dem Präfix (z.B. `#page-mitarbeiter`, `.ov-`, `#e2App`). Neue Regeln ans Ende der passenden
Datei; Cache-Buster (`?v=`) der geänderten Datei in index.html hochzählen.

| Datei | Hauptinhalt |
|---|---|
| `farben.css` | **Zentrale Farben** `--oc-*` für hell und dunkel (zuerst geladen) |
| `app.css` | Reset, Grundlayout, Listen, Basis-Komponenten |
| `app-02-login-liquid.css` | Login, Liquid Glass, Selects, Mitarbeiter-Grundlagen |
| `app-03-mitarbeiter.css` | Mitarbeiter-Maske |
| `app-04-ma-uebersicht.css` | Reiter «Übersicht» (ov-Karten) |
| `app-05-absenzen-ktg.css` | Absenzen, KTG, Stempelzeiten |
| `app-06-seiten.css` | Seiten wie Austrittsumfrage, SV-Sätze, AHV, fixe Köpfe |
| `app-07-dokumente-familie.css` | Dokumente, Familien-Fenster, Warnungen |
| `app-08-familie-todos.css` | Familienmitglied-Maske, To-dos |
| `app-09-vertraege-kalender.css` | Vertragsfenster, Absenz-Kalender, Familienzulagen-Anmeldung, Manager-Dienstplan |
| `app-10-lohnschema-darkmode.css` | Lohnschema, Mindestlöhne, ov-Eingabemasken-Standard, Dark Mode |
| `app-11-diverses.css` | Dokument-Struktur, Gesprächsmodus, Sitzungen, Lohnart-Maske u.a. |
| `elm.css` | ELM Cockpit (`#e2App`) — Vorbild: nur `var(--oc-…)` |
| `dark.css` | Dunkel-Sicherheitsnetz für feste Farben in Inline-Stilen (zuletzt geladen, **erzeugt** von `Scripts/dark-css-erzeugen.py` — nicht von Hand ändern) |

**Dunkel-Modus (02.11.2026):** Neue Oberflächen nehmen nur die Variablen `--oc-*` aus `farben.css`, nie feste Farben wie `#fff` oder `#3f3f3f`. Elemente, die im Dunkel-Modus bewusst hell bleiben (Unterschrift, Briefvorschau), bekommen die Klasse `hell-bleiben`.

## Ältere Verweise
In `docs/claude/*.md` (wörtlich übernommene Regeln) steht oft noch `employees.js` oder
`css/app.css`: gemeint ist heute `js/ma/ma-NN-….js` bzw. die passende `css/app-NN-….css`.
