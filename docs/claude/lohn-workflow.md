> Verbatim aus CLAUDE.md ausgelagert am 29.08.2026 (Kosten-Verschlankung).
> Inhalt gilt UNVERÄNDERT weiter — alle ABSOLUT-Regeln bleiben ABSOLUT.
> Nichts wurde gekürzt oder umformuliert; nur der Speicherort ist neu.

## Bearbeitungs-Status-Map (Walter-Vorgabe 19.05.2026 — single source of truth)

Pro Lohnperiode gibt es **zwei** Workflows, jeder mit eigenem Status und klarer Rollentrennung. Alle UI-Buttons und alle Lock-Entscheidungen MÜSSEN sich nach diesem einen Status-Schema richten. Wenn ein UI-Verhalten nicht passt, ist es ein Bug.

**Akonto-Workflow** (`payroll_periode.akonto_status` + `akonto_zahlung.status` pro MA):

| Periode-Status | Snapshot-Status pro MA | Wer darf was | UI im Akonto-Tab |
|---|---|---|---|
| `OFFEN` | – (noch keine Zahlungen) | GF | „📅 Akonto vorbereiten" |
| `IN_BEARBEITUNG_GF` | `BERECHNET` / `FREIGEGEBEN_GF` | GF bestätigt jeden MA | „✓ Freigeben" / „↶ Zurückziehen" / „An HR senden" |
| `BEI_HR` | `FREIGEGEBEN_GF` / `HR_BESTAETIGT` | HR bestätigt jeden MA; GF **gesperrt** | HR: „✓ HR-bestätigen", „↶ Zurück an GF", „✎ ändern" |
| `HR_FREIGEGEBEN` | alle `HR_BESTAETIGT` | HR korrigiert noch / klickt DTA; GF gesperrt | „💰 DTA auszahlen", + Per-MA-Override |
| `AUSBEZAHLT` | alle `AUSBEZAHLT` | niemand mehr (Admin: Reset über Lohnperioden-Modul) | „📥 DTA-File", „📄 Liste" |

**Definitiv-Workflow** (`payroll_periode.status` + `payroll_snapshot.status` pro MA):

| Periode-Status | Snapshot-Status pro MA | Wer darf was | UI im Definitiv-Tab |
|---|---|---|---|
| `offen` | `BERECHNET` / `FREIGEGEBEN_GF` | GF bestätigt jeden MA | „✓ Lohn bestätigen" / „↶ Wieder eröffnen" / „An HR senden" |
| `provisorisch_abgeschlossen` | `FREIGEGEBEN_GF` / `HR_BESTAETIGT` | HR bestätigt jeden MA; GF **gesperrt** | HR: „✓ HR-bestätigen", „↶ HR-Bestätigung zurückziehen", „↩ Zurück an GF", „📑 Lohnbelege + DTA" |
| `abgeschlossen` | alle `ABGESCHLOSSEN` | niemand mehr (Admin: Wieder-Öffnen nur bis Zahldatum DTA) | „📑 Lohnbelege ansehen", „📥 DTA-File" |

**GF-Sperre-Regel (final 19.05.2026):**
- GF darf seine zwei Buttons („Lohn bestätigen" / „Wieder eröffnen") **NUR** sehen, wenn der Periode-Status `offen` bzw. `IN_BEARBEITUNG_GF` ist.
- Sobald HR den Stab übernimmt (`BEI_HR` / `provisorisch_abgeschlossen` / `HR_FREIGEGEBEN`), sind GF-Buttons komplett unsichtbar. Auch wenn der einzelne MA-Snapshot noch FREIGEGEBEN_GF ist — der GF kommt da nicht mehr ran.
- Frontend (Walter-Vorgabe 20.05.2026, **Definitiv = Akonto-Architektur**): Beide Workflows haben jetzt je **eine** zentrale Render-Funktion, die als EINZIGE Stelle Status-Pille + Counter + ALLE Aktionsbuttons zeichnet — gespeist aus EINEM State-Cache. Es darf NIRGENDS sonst Button-Sichtbarkeit gesetzt werden.
  - **Akonto:** `akonto-workflow.js → _akWfRenderStatusBar()` (Cache `_akWfData`, geladen via `akWfRefresh` aus `/api/akonto/workflow/status`), rendert in `#akontoStatusBar`.
  - **Definitiv:** `payroll.js → _lohnWfRenderStatusBar()` (Cache `_lohnWfData`, geladen via `lohnWfRefresh` → `loadLohnList` aus `/api/payroll-perioden/current` + `…/snapshots`), rendert in `#lohnDefinitivStatusBar`. `_lohnWfData = { status, periode, periodeId, snapByEmp:{empId:{id,status}}, gfConfirmed, hrConfirmed, activeTotal }`.
  - `loadLohnSlip` rendert NUR noch den Lohnzettel (Berechnung + `renderLohnSlip`) — KEINE Button-Logik mehr. `loadLohnPeriodBanner` ist ein **Shim** auf `lohnWfRefresh` (Alt-Aufrufer funktionieren weiter). MA-Klick mirror `akWfSelectMa`: nur Highlight + `_lohnWfRenderStatusBar()` + `loadLohnSlip` (kein voller Listen-Rebuild pro Klick). Aktions-Handler (`confirmLohn`/`reopenLohn`/`lohnHrBestaetigen`/`lohnHrZurueckziehen`/`lohnZurueckAnGf`/`abschliessePeriode`/`savePeriodeBemerkung`) rufen nach der Aktion `lohnWfRefresh()`. Grund für den Rebau: die früher über `loadLohnSlip` + `loadLohnPeriodBanner` + `loadLohnList` verstreute Button-Logik lief ständig auseinander („Button fehlt / verdeckt"-Bugs).
  - Die alte statische Button-Zeile `#lohnTopActions` + die alte Toolbar-Status-Pille `#lohnPeriodBanner` sind toter, dauerhaft versteckter DOM (bewusst erhalten, nicht mehr verdrahtet).

**Übergänge zwischen den zwei Workflows:**
- Beim Wechsel Akonto-AUSBEZAHLT → Definitivlauf-Start: `payroll_snapshot.status` muss auf `BERECHNET` initialisiert sein (der GF muss im Definitiv jeden MA neu bestätigen — der Akonto-Workflow hat damit nichts zu tun).
- Beim `zurueck-an-gf` (Definitiv): alle Snapshots → `BERECHNET`, alle Saldos → `draft`.
- Beim `wieder-oeffnen` (Definitiv abgeschlossen → provisorisch): Snapshots, die `ABGESCHLOSSEN` waren, → `HR_BESTAETIGT`. HR-Bestätigungen bleiben erhalten, nur der DTA-Klick muss erneut.

**Snapshot-Frische / Auto-Neuberechnung (Walter-Vorgabe 22.05.2026 — Grundprinzip):** Ein Snapshot (`payroll_snapshot`: Brutto/Netto/SlipJson) ist die EINGEFRORENE Lohnabrechnung. Solange eine Periode OFFEN/wieder-geöffnet ist, darf er NICHT veralten — sonst laufen Snapshot und Live-Rechnung auseinander (z.B. Lohn nach Bestätigen korrigiert → Fibu-Journal/DTA stimmen nicht mehr; genau dieser Bug am 22.05.2026). Regel: **jedes `zurueck-an-gf` UND `wieder-oeffnen` rechnet ALLE Snapshots der Periode sofort frisch** — `Services/SnapshotRecomputeService.cs` (`RecomputeAsync(cpId,year,month)`) überschreibt Brutto + Netto + SlipJson GEMEINSAM aus EINER `CalculateAsync`-Rechnung (zieht Saldo Gross/Net mit), Workflow-Status bleibt. Beide Endpoints rufen das nach ihrem Status-Reset auf. Damit ist ein veralteter Snapshot strukturell unmöglich. Bei ABGESCHLOSSENER Periode ist der Snapshot eingefroren UND die Quelldaten gesperrt (`LohnEditLockService`) → Live-Rechnung ergäbe ohnehin dasselbe. **Niemals** einen Snapshot nur teilweise patchen (nur SlipJson ohne Brutto/Netto) — das war die Ursache der LGAV-Inflation. Manueller Admin-Knopf „♻️ Snapshots neu berechnen" (`POST /api/payroll/recompute-snapshots`, delegiert an denselben Service) bleibt als Notfall-Werkzeug; Routine-Fall ist die Auto-Neuberechnung.

**Zahldaten / Bank-Ausführungsdatum (Walter-Vorgabe 19.05.2026):** Beide Workflows erfassen vor dem DTA-Versand das Bank-Ausführungsdatum (ReqdExctnDt im pain.001). Default: morgen. Wird in der Periode persistiert und ist der Cutoff für den Admin-Reset:
- Akonto → `payroll_periode.akonto_auszahlungsdatum` (neu — Migration: `migrations-archive/add_akonto_auszahlungsdatum.sql`)
- Definitiv → `payroll_periode.auszahlungsdatum` (existiert)

**DTA-Bestätigungs-Schritt (Walter-Vorgabe 19.05.2026):** Beide Workflows trennen das Erstellen des DTA vom Versand an die Bank. Klick auf den finalen Knopf läuft als 3-Schritt:
- **Schritt 0**: Datum-Prompt → Bank-Ausführungsdatum erfassen (Default: morgen).
- **Schritt 1**: Confirm-Dialog „DTA erstellen mit Ausführungsdatum xx.xx.xxxx" — DTA wird mit diesem Datum generiert + heruntergeladen.
- **Schritt 2**: Confirm-Dialog „DTA an Bank gesendet?" — erst JA setzt den finalen Status (Akonto AUSBEZAHLT / Definitiv abgeschlossen) und triggert nachgelagerte Aktionen (Postfach-Ablage + E-Mail beim Definitivlauf).

**DTA-Inhalt: KEINE Auszahlungsbeträge ≤ 0 (Walter-Vorgabe 04.06.2026, ABSOLUT):** Im pain.001-XML darf NIE eine `<CdtTrfTxInf>`-Zahlung mit `InstdAmt ≤ 0` enthalten sein — manche Banken weisen den ganzen Auftrag zurück, wenn auch nur eine 0-/Negativ-Zeile drin steht. Konkret bedeutet das: ein MA mit Brutto/Netto 0.00 (z.B. FLEX ohne gestempelte Stunden im Monat) erscheint NICHT im DTA, obwohl Lohnzettel/Snapshot/Periode regulär abgeschlossen sind. Der Filter sitzt in DREI Generatoren und MUSS bei jedem Refactor erhalten bleiben:
- `LohnlaufService.GenerateDtaMaAsync` (Definitiv-MA-DTA) — `if (betrag <= 0) continue;` pro `auszahlungEmpfaenger[BANK]`-Eintrag (Z.211).
- `LohnlaufService.GenerateDtaBehoerdenAsync` (Lohnabtretungs-DTA an Behörden) — `if (betrag <= 0) continue;` pro `auszahlungEmpfaenger[BEHOERDE]`-Eintrag (Z.305).
- `AkontoLaufService` (Akonto-DTA) — `if (z.NettoAkonto <= 0) continue;` pro `akonto_zahlung` (Z.799).
Wenn nach dem Filter `payments.Count == 0`, werfen alle drei `InvalidOperationException` („DTA leer") — ein vollständig leeres DTA-File entsteht nie. `Iso20022PainService` selbst macht KEINEN eigenen Betragscheck; die Pflicht liegt beim Caller. Beim Bau neuer DTA-Pfade (z.B. Sonderzahlungen, Spesen-Auszahlungen) IMMER denselben `≤0`-Guard einbauen, BEVOR `payments.Add(...)` aufgerufen wird.

**Admin-Reset / Wieder-Eröffnen — Zahldatum-Lock:**
- Definitiv: Admin (`role=admin`) öffnet `abgeschlossene` Periode via `/api/payroll-perioden/{id}/wieder-oeffnen`. **NUR bis `heute ≤ payroll_periode.auszahlungsdatum`** — danach 409 `PAYOUT_DATE_REACHED`.
- Akonto: Admin setzt via `/api/akonto/workflow/reset-periode` zurück. **NUR bis `heute ≤ akonto_auszahlungsdatum`** — danach 409. (Fallback für Alt-Daten ohne Feld: `AkontoAusbezahltAt.Date`.)
- **Pflicht-Bestätigung**: bevor das Frontend den Reset/Wieder-Eröffnen-Endpoint aufruft, MUSS der Admin eine zweite Confirm-Frage „Hast du den DTA bei der Bank gelöscht/storniert?" mit JA beantworten. Schützt vor Doppelzahlung.
- **Nach dem Zahldatum**: kein Reset mehr möglich — nicht für Admin, nicht für HR, nicht für GF. Notfall-Eingriff nur über direkten DB-Eingriff durch Entwickler.

## Lohnlauf-Edit-Sperre (Walter-Vorgabe 17.05.2026)

Sobald ein Akonto- oder Definitivlauf einer Periode in HR-Verarbeitung oder bereits abgeschlossen ist, sind lohnrelevante datum-bezogene Edits **für JEDEN** (auch admin/superuser) gesperrt. Service: `Services/LohnEditLockService.cs`. Logik: spätestes (Year, Month) finden, das `Status != 'offen'` ODER `AkontoStatus NOT IN ('OFFEN', 'IN_BEARBEITUNG_GF')` ist — Edit-Datum muss > letzter Tag dieser Periode liegen (= FirstAllowedDate = 1. Tag des Folgemonats). `IN_BEARBEITUNG_GF` ist absichtlich NICHT gesperrt, damit der GF in der Vorbereitungsphase noch Stempel- und Absenz-Korrekturen vornehmen kann.

**Kein Rollen-Bypass (Walter-Vorgabe final 17.05.2026):** auch admin/superuser sehen den Lock. Damit ein laufender Lohnlauf editiert werden kann, muss der Admin die Periode aktiv **zurücksetzen / wieder öffnen** — das zwingt eine bewusste Entscheidung mit Audit-Trail (Lohnzettel aus MA-Postfach raus, Akonto-Zahlungen storniert etc.) statt einer stillen Daten-Manipulation im Hintergrund. Reset-Endpoints:
- Akonto: `POST /api/akonto/workflow/reset-periode` (admin-only, Body `{companyProfileId, year, month, grund}`) — setzt AkontoStatus auf OFFEN, löscht BERECHNET/FREIGEGEBEN_GF/HR_BESTAETIGT-Zahlungen, stempelt AUSBEZAHLT-Zahlungen auf STORNIERT (Beleg bleibt), Audit-Eintrag mit Aktion `AKONTO_RESET`.
  - **Re-Aktivierung nach Reset (Walter-Vorgabe 20.05.2026):** Der STORNIERT-Beleg bleibt — aber beim nächsten „Akonto vorbereiten / Neu berechnen" (`AkontoWorkflowController.Start`) wird ein STORNIERT-Datensatz eines wieder berechtigten MA mit frischen Werten **auf BERECHNET reaktiviert** (sonst bliebe der MA nach dem Reset auf STORNIERT hängen und GF/HR könnten ihn nicht mehr freigeben). `Start` lässt FREIGEGEBEN_GF / AUSBEZAHLT / HR_BESTAETIGT intakt (skip) und (re)aktiviert nur BERECHNET/STORNIERT.
- Definitiv: `POST /api/payroll-perioden/{id}/wieder-oeffnen` (admin-only, existiert) — setzt Status auf provisorisch_abgeschlossen zurück, löscht Lohnzettel aus MA-Postfächern.
Im Lohnperioden-Modul sehen Admins bei jeder Periode mit `akontoStatus != OFFEN` einen orangen Button „↺ Akonto zurücksetzen" mit Pflicht-Grund-Eingabe.

Geschützte Endpoints (alle POST/PUT/DELETE, alle Rollen):
- **Datum-bezogen**: `AbsencesController`, `LohnZulagenController` (Vorschuss-Rückzahlung wird hier abgebildet).
- **Versioniert (`ValidFrom < FirstAllowedDate` = inLohnVerwendet)**: `EmployeeBankAccountsController`, `EmploymentsController`, `EmployeeQuellensteuerController`, `EmployeeRecurringWagesController`, `EmployeePermitHistoryController`, `EmployeeLohnAssignmentsController`, `FamilyMemberAllowancesController`.
- **Periode-bezogen**: `SaldoVortragController` (Vortrag-Periode darf nicht rückwirkend).

**Stempelzeiten** sind seit Walter-Vorgabe 17.05.2026 komplett **READ-ONLY**: `EmployeeTimeEntriesController` POST/PUT/DELETE liefern 403 mit Klartext-Meldung — Stempelzeiten kommen **ausschliesslich über die easy@work-API** (`EasyAtWorkTimepunchSyncService`: manueller Sync + täglicher Auto-Sync). **ACHTUNG (Walter erfahren 08.07.2026): der Auto-Sync (05:00 + Catch-up nach jedem Server-Neustart nach 05:00) macht als STUFE 1 auch einen vollen MA-/VERTRAGS-Sync-Commit über ALLE Filialen** (`EasyAtWorkAutoSyncService.RunEmployeeSyncAsync`, seit 05.07.2026) — ein Deploy am Nachmittag baut also z.B. eine geleerte employment-Tabelle sofort wieder auf. Seit 08.07.2026 gilt dabei der STRICT-Import: Fehler an AKTIVEN Verträgen → CONFLICT/übersprungen (Lohn-Pflicht ausser FIX-M, keine Überlappungen, FLEX/MTP nie Stunden/Monat); fehlerhafte ABGELAUFENE Verträge werden still weggelassen. Der frühere PDF-/ZIP-Import (`ImportController`) ist seit 19.06.2026 entfernt (nur noch `410 Gone`). Cowork zeigt die Stempelzeiten nur an, Korrekturen passieren in easy@work und werden beim nächsten Sync übernommen. Versionierte Daten (Verträge, Bankkonten, Bewilligungen, QST, EmployeeRecurringWages mit ValidFrom/ValidTo) gehören NICHT in den Lock — die haben eigene „neu ab"-Logik und werden separat behandelt (Walter: später, eigenes Thema).

**PDF-/ZIP-Stempelzeiten-Import komplett entfernt (Walter-Vorgabe 19.06.2026):** Sowohl der „Stempelzeiten PDF"-Importer (`ImportStempelzeiten`) als auch die Monats-ZIP-Variante (`ImportMonatlich`) sind **vollständig entfernt** — UI-Seite, Upload-Zone, Buttons, der iText-PDF-Parser, `import-stempelzeiten.js` und die Count-/Dedupe-Wartungsfunktionen. `ImportController` ist auf reine **`410 Gone`-Stubs** reduziert (`{ error: "STEMPELZEITEN_PDF_IMPORT_REMOVED", message: "Stempelzeiten werden nur noch über easy@work API synchronisiert." }`), falls irgendwo noch ein alter Link darauf zeigt. **Stempelzeiten kommen ab sofort ausschliesslich über die easy@work-API** (manueller Sync + täglicher Auto-Sync, siehe `EasyAtWorkController` / `EasyAtWorkTimepunchSyncService`). Bestehende importierte Stempelzeiten in der DB bleiben unverändert.

**Stempelzeiten-Tab: Wochentotal Mo–So + Max-Warnung (Walter-Vorgabe 24.05.2026):** Die Anzeige-Tabelle (`employees.js → stempelRenderTable`) ist verdichtet (Padding 4px) und gruppiert die Zeilen nach ISO-Woche (Mo–So, Helfer `stempelWeekMonday` = Montag als Schlüssel, `stempelIsoWeek` = KW-Nr.). Beim **letzten Eintrag jeder Woche** steht rechts in der Kommentar-Spalte fett das Wochentotal der gestempelten Dauer („∑ KW07 42.50 h"), darunter eine 2px-Trennlinie. Übersteigt das Wochentotal die **Max. Stunden/Woche der Filiale** (`CompanyProfile.MaxWeeklyHours`, `numeric(5,2)`, NULL = keine Grenze; Migration `add_company_max_weekly_hours.sql`; PATCH `/api/companyprofiles/{id}/max-weekly-hours` admin-only; UI im Filial-Einstellungen-Tab Arbeitszeit-Gruppe `#einMaxWeeklyHours`, gespeichert über `saveEinstellungen`), wird das Badge ROT mit „⚠ &gt; max" gezeigt. Max-Wert wird im Frontend aus `allBranches.find(id==fixedCompanyProfileId).maxWeeklyHours` gelesen. **Monatsgrenzen-Logik (Walter-Vorgabe 24.05.2026):** `stempelLadeEintraege` lädt NICHT nur die Periode, sondern den ERWEITERTEN Bereich `stempelWeekMonday(periodFrom) … stempelWeekSunday(periodTo)` (volle ISO-Wochen an beiden Rändern, via `dateFrom/dateTo`). Angezeigt + monatlich aufsummiert (Summe-Zeile) werden nur die Zeilen INNERHALB `[pFrom..pTo]`; die Wochentotale werden über die VOLLEN Wochen (`allRows`) gebildet und das Badge erscheint NUR beim ECHTEN letzten Eintrag der Woche (`lastIdOfWeek[wk] === r.id`, über alle geladenen Wochen). Effekt: läuft die letzte Woche im Monat über den Monatswechsel und hat der Folgemonat Einträge dieser Woche → KEIN Total am Monatsende, es erscheint stattdessen am ersten Eintrag der Woche im Folgemonat (mit Voll-Wochen-Summe inkl. Vormonats-Teil). Hat der Folgemonat KEINE Einträge dieser Woche → Total trotzdem am letzten verfügbaren Tag (z.B. Fr/Sa). Cache-Buster `employees.js?v=20260524a` / `branches-detail.js?v=20260524b`. **Reihenfolge beim Deploy: ZUERST Migration in TablePlus, DANN deployen** — sonst bricht jeder `GET /api/companyprofiles` (EF selektiert die noch fehlende Spalte).

Bei Sperre → HTTP 409 mit `{ error: "LOHN_EDIT_LOCKED", message, firstAllowedDate }`. Frontend-Helper in `wwwroot/js/lohn-edit-lock.js` (global `window.lohnEditLock`): `loadState(branchId)`, `renderBanner(el, state)`, `applyToDateInput(input, state)`, `handleResponse(res)`. Frontend-Pattern: nach jedem `fetch()` bei Edit-Aktion `if (await lohnEditLock.handleResponse(res)) return;` vor dem normalen Fehler-Pfad. Beim Öffnen eines Date-Picker-Modals zusätzlich `applyToDateInput()` aufrufen, damit gesperrte Tage gar nicht erst auswählbar sind.

GET-Endpoint: `/api/lohn-edit-lock/first-allowed-date?branchId=X` liefert `{ firstAllowedDate, reason }` — wird vom Frontend-Helper mit 5s-TTL gecacht.

**Tests:** das Test-Projekt `Tests/hr-system.Tests.csproj` enthält Unit-Tests für `LohnEditLockService` (alle Bypass-Regeln, alle Status-Kombinationen, FirstAllowedDate-Berechnung, Filiale-Trennung) und einen **Audit-Test** (`EditLockEndpointAuditTests`), der alle Controller-Files scannt und sicherstellt, dass jeder POST/PUT/DELETE-Endpoint entweder `LohnEditLockService` einbindet ODER in der Whitelist `LOCK_IRRELEVANT_CONTROLLERS` mit Begründung steht. Wenn ein neuer Edit-Endpoint angelegt wird, der weder das eine noch das andere ist, schlägt der Test fehl — verhindert dass jemand "vergisst" einen Lock einzubauen. Lauf: `dotnet test Tests/hr-system.Tests.csproj`.

**Workflow-Tests (Walter-Vorgabe 19.05.2026):** Vier zusätzliche Test-Files nageln die Lohnlauf-Workflows fest und verhindern Regressionen am 4-Augen-Prinzip:
- `WorkflowSpecAuditTests.cs` — scannt die kritischen Code-Stellen (Defaults, IsFinal-Timing, PAYOUT_DATE_REACHED, ZurueckAnGf-Reset, Auszahlungsdatum-Persistierung) per Regex. Schlägt fehl wenn jemand den Default des PayrollSnapshot wieder auf FREIGEGEBEN_GF setzt oder IsFinal zu früh setzt.
- `WorkflowDefaultsTests.cs` — neue PayrollSnapshot / AkontoZahlung / PayrollPeriode / PayrollSaldo haben die korrekten Status-Defaults (BERECHNET / BERECHNET / offen+OFFEN / draft).
- `AkontoWorkflowTransitionTests.cs` — alle Status-Übergänge im Akonto: OFFEN → IN_BEARBEITUNG_GF → BEI_HR → HR_FREIGEGEBEN → AUSBEZAHLT, plus Auto-Transition wenn alle MA HR_BESTAETIGT sind, plus PAYOUT_DATE_REACHED am Tag nach Auszahlung.
- `DefinitivWorkflowTransitionTests.cs` — alle Status-Übergänge im Definitivlauf: offen → provisorisch_abgeschlossen → abgeschlossen, inkl. der Invariante „provisorisch darf KEIN IsFinal=true setzen" (Walter-Bug 19.05.2026: blockierte HR-Bestätigung), Reset rollt Snapshot+Saldo sauber zurück, WiederOeffnen mappt ABGESCHLOSSEN-Snapshots auf HR_BESTAETIGT.

