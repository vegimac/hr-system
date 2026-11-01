# Architektur: Schichten, Lohn-Berechnung, Frontend

> Verbatim aus CLAUDE.md ausgelagert am 01.11.2026 (Verschlankung, Walter-Auftrag «Code optimieren»).
> Inhalt gilt UNVERÄNDERT weiter — alle ABSOLUT-Regeln bleiben ABSOLUT.
> Nichts wurde gekürzt oder umformuliert; nur der Speicherort ist neu.

### Schichten

```
wwwroot/index.html (~7.6k Zeilen) ←── Single-Page-App: nur noch HTML (34 Seiten-Divs) + Script-Tags
wwwroot/css/app.css (~5.1k Z)     ←── ausgelagerter Head-Style-Block (Refactor Etappe 1, 08.07.2026)
wwwroot/js/app-core.js (~1.1k Z)  ←── ausgelagerter Inline-Kern (Etappe 2): State, Auth/Login, showPage,
                                      Filial-Selektor, Theme. Script-Tag-POSITION in index.html NICHT
                                      verschieben (läuft VOR save-blob.js & allen Modulen)!
wwwroot/employees.js (4k Zeilen)  ←── MA-Liste, MA-Detail, Tabs, Adressen, Bankkonten
wwwroot/import.html (2k Zeilen)   ←── CSV-Import (eigene Page, eigener Login-Flow)

           ↓ JWT Bearer Token
Controllers/*.cs (47 Stück)       ←── REST-Endpoints, gruppiert pro Domäne
   ↓
Services/*.cs                     ←── Komplexe Logik (PDF-Generierung, Tarife, Karenz)
   ↓ EF Core
Data/AppDbContext.cs              ←── Postgres-Mapping aller ~50 Entities
Models/*.cs (48 Stück)
```

### Lohn-Berechnung: Schichtung (Walter-Vorgabe 20.05.2026)

Der frühere Monolith `PayrollController.cs` (~3'900 Zeilen) wurde in drei Dateien entflochten — gleiche Logik, nur getrennt nach Verantwortung. Bei Änderungen an der Lohn-Mathematik IMMER hier ansetzen, nicht im Controller:

```
Controllers/PayrollController.cs (~720 Z)   ←── nur noch HTTP-Endpoints (dünn)
   Calculate() → delegiert an _calcEngine.CalculateAsync(...)
   Confirm / Reopen / PDF / Saldo / HR-Bestätigen / KTG / Snapshot
        │ ruft auf (DI: PayrollCalculationEngine, AddScoped in Program.cs)
        ▼
Services/PayrollCalculationEngine.cs (~2'490 Z)  ←── die Berechnungs-Orchestrierung
   CalculateAsync()  — Datenladen + FLEX/MTP/FIX-Zweige + Dezember-Jahresausgleich
   ComputeQstDeduction() (private)  — Quellensteuer-Abzug
   Gibt IActionResult zurück (new OkObjectResult/NotFoundObjectResult/...),
   damit Endpoint + ConfirmPayroll + GetPdf ihr .Value unverändert lesen.

Hinweis: `/api/payroll/save` wurde am 09.06.2026 entfernt (war toter Code, der
Geldbeträge aus dem Browser direkt in PayrollSaldo schrieb). Einzig sicherer
Schreibpfad zu PayrollSaldo ist seither `/api/payroll/confirm`, der intern
CalculateAsync ruft und die Beträge selbst regeneriert.
        │ ruft auf (using static)
        ▼
Services/PayrollCalculationService.cs (~750 Z)  ←── reine statische Rechen-Helfer
   public static class PayrollCalculations:
   BuildResult (SV/QST/Abzug-Engine), CalcFerienGeld, BuildSwissStandardDeductions,
   Round05, IsThirteenthPayoutMonth, ComputeSatzBruttoForNebenjob,
   EstimatePensumFromStunden, CalcPeriod, PrevPeriod,
   ScaleAbsenceHoursToPeriod, CountAbsenceDaysInPeriod

Controllers/PayrollModels.cs (~130 Z)  ←── Records + Request-DTOs
   SvBases, SaldoBlock (namespace HrSystem.Controllers — von Engine via using importiert),
   ConfirmPayrollDto, LohnAbtretungConfirmDto, ReopenPayrollDto
```

Wichtig: `PayrollCalculations` ist statisch und seiteneffektfrei (alle Daten als Parameter) — DAS ist die unit-testbare Schicht (v.a. `BuildResult`). `PayrollCalculationEngine` ist DB-gekoppelt (injizierte `_db` + `_tarifService`/`_ktgService`/`_karenz`/`_lgav`/`_ferienKuerzung`). Beim Bau neuer Lohn-Logik: reine Rechnung → `PayrollCalculations`; DB-/Service-gekoppelte Orchestrierung → `PayrollCalculationEngine`; nur Routing/Auth → Controller.

### Frontend-Eigenheiten (wichtig!)

- **Refactor-Regeln index.html (Walter-Vorgabe 08.07.2026):** CSS-Änderungen gehören in `css/app.css` (Cache-Buster im `<link>` hochzählen!), Kern-JS in `js/app-core.js` (Cache-Buster im Script-Tag). Beide wurden byte-identisch VERSCHOBEN, nicht verändert. Kleine seitenlokale `<style>`-Blöcke + Mini-Inline-Scripts (Login-Begrüssung, WebAuthn-Check) bleiben bewusst in index.html. **Etappe 3 (HTML-Seiten aufteilen) bewusst ZURÜCKGESTELLT bis nach dem Lohn-Go-live** — Laufzeit-Nachladen würde getElementById-Timing app-weit ändern.
- `wwwroot/index.html` ist die Haupt-App. **Alle wichtigen Module** sind hier (Verträge, Lohn, Quellensteuer, Posteingang, Filialen, Lohnpositionen, Periode-Config, etc.). Sehr lange Datei — bei Änderungen mit `grep`/`Read` gezielt suchen, nie blind editieren.
- `wwwroot/employees.js` enthält den **Mitarbeiter-Tab** (linke Liste + rechtes Detail mit Sub-Tabs Personal / Familie / **Bank** / Quellensteuer / Stempelzeiten / Absenzen / KTG/UVG / Dokumente). Der Sub-Tab „Bank" (Walter-Vorgabe 14.05.2026) hält nur noch die Bankverbindungs-Liste — aus dem Personal-Tab ausgelagert, damit die Seite nicht so lang ist. Reihenfolge der Tabs steht zusätzlich in `_empTabsOrder` (für Pfeil-Navigation) und muss mit der Tab-Bar in `renderEmployeeDetail` synchron bleiben. Lade-Logik pro Tab in `switchEmpTab`: `bank` → `loadBankAccountsTab`. Phantom-MA (`isPayrollExcluded`) zeigen im Bank-Tab nur den „MA ohne Lohn"-Hinweis. **Postfach-Passwort-Reset:** sitzt als Button im Detail-Header (`#empHeaderActions`, neben „Bearbeiten") und ruft direkt `postfachResetPassword(empId)` — kein eigener Tab/Block mehr. `startEmpEdit()` ersetzt den Inhalt von `#empHeaderActions` durch Speichern/Abbrechen. Backend `ResetPasswordAsync` setzt nebst dem Passwort auch `FailedLoginCount=0` + `LockedUntil=null` — der Reset hebt also eine Login-Sperre gleich mit auf, ein separater Unlock-Button ist nicht nötig. Die Funktionen `loadPostfachAccountBlock` / `renderPostfachAccountBlock` / `postfachUnlock` bleiben als Code erhalten, werden aber nicht mehr aus dem UI aufgerufen.
- `wwwroot/import.html` ist eine **separate Page** für CSV-Import aus easy@work. Wird via `openImportTool()` mit Token+BranchId als URL-Parameter aufgerufen.
- **Doppelte DOM-IDs sind ein wiederkehrender Bug** — `getElementById` liefert das erste Element. Bei Element-Erstellung daher prüfen ob ID bereits existiert (z.B. ehemals `importResult` doppelt vorhanden für CSV-Import und Stempelzeiten-Import).
- **Liquid Glass ist ab sofort die verbindliche UI-Designsprache für neue und überarbeitete Oberflächen (Walter-Vorgabe 25.06.2026).** Details stehen in `docs/liquid-glass-ui-konzept.md`. Kurzfassung: ruhige Apple/VisionOS-Optik mit mehreren Ebenen (Hintergrund, Glaslinse, Glasrand, Highlight), transparenter Standard-Look, bei Formularen/Tabellen/Listen bei Bedarf stärker eingefärbt für Lesbarkeit. Keine nativen Browser-Controls sichtbar lassen, wenn sie aus dem Look fallen (z.B. Select-Menüs → eigenes Liquid-Control bauen, bestehende IDs/Handler im Hintergrund weiterverwenden). Fachlogik, DOM-IDs, Eventhandler, Fetch-Aufrufe und Tab-Logik bleiben unangetastet; der Look wird schrittweise darübergelegt.
  - **NEUE BUTTONS + MODALE IMMER im Liquid-Glass-Stil (Walter-Vorgabe 01.07.2026, ABSOLUT):** Kein grelles Default-Blau (`#3b82f6`) mehr für neue Buttons/Dialoge. Palette aus `docs/liquid-glass-ui-konzept.md`: Hintergrund warmes Off-White (`#f6f3ee`/`#faf8f5`), Glas `rgba(255,255,255,0.38–0.58)`, Glasrand `rgba(255,255,255,0.62)`, Schatten `rgba(60,55,48,0.14–0.22)`, Text `#3f3f3f`/`#646464`/`#8b8b8b`, Akzent sparsam `#6b7280`. Primär-Buttons = gefüllte, ruhige Glas-/Kohle-Pille (dunkel `#3f3f3f`/`#1a1a1a`, weisser Text, `border-radius:12px`), Sekundär = heller Glas-Pill mit Rand. Grelle Fachfarben (Blau/Grün/Rot) NUR für kritische Fachstatus/-aktionen in Detailmodulen, nicht als Standard-Button-Look. Hover hebt max. 2–4 px, weich. Beim Bau jedes neuen Buttons/Modals diese Palette verwenden (Referenz: Kohle-Buttons in `postfach.html`/`moment.html`/`setup.html`).
