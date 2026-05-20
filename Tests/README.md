# Tests — Schaub HR-System

Test-Projekt für die "königliche Kontrolle" der Lohnlauf-Edit-Sperre (Walter-Vorgabe 17.05.2026).

## Ausführen

```bash
cd /Users/Walter/projects/hr-system
dotnet test Tests/hr-system.Tests.csproj
```

oder einfach aus dem Tests/-Verzeichnis:

```bash
cd Tests
dotnet test
```

## Was getestet wird

### 1. `LohnEditLockServiceTests` — Logik-Tests

Unit-Tests für `Services/LohnEditLockService.cs` mit In-Memory-DB:

- **Bypass**: admin und superuser sind nie gesperrt
- **Akonto-Status-Matrix**: alle 5 Werte (`OFFEN`, `IN_BEARBEITUNG_GF`, `BEI_HR`, `HR_FREIGEGEBEN`, `AUSBEZAHLT`) → was sperrt, was nicht
- **Definitiv-Status-Matrix**: alle 3 Werte (`offen`, `provisorisch_abgeschlossen`, `abgeschlossen`) → was sperrt
- **FirstAllowedDate**: erster Tag des Folgemonats nach spätester gesperrter Periode
- **CheckDateAsync / CheckRangeAsync**: konkrete Datums- und Bereichs-Prüfung
- **Filiale-Trennung**: Lock von Filiale A wirkt nicht auf B
- **Edge-Cases**: keine Perioden, mehrere überlappende Perioden, exakt auf FirstAllowedDate

### 2. `EditLockEndpointAuditTests` — Audit-Test

Scannt **alle Controller-Files** im Repo. Pro Datei:

1. Sucht alle `[HttpPost]` / `[HttpPut]` / `[HttpDelete]` / `[HttpPatch]` Attribute.
2. Prüft ob der Controller `LohnEditLockService` oder `_editLock` referenziert.
3. Wenn nicht: muss der Controller-Name in `LOCK_IRRELEVANT_CONTROLLERS` stehen — mit Begründung.

Wenn ein neuer Edit-Endpoint angelegt wird:
- **Lohn-relevant** → muss `LohnEditLockService` einbauen.
- **Lohn-irrelevant** (z.B. Lookup, Reporting, Login) → muss explizit in die Whitelist eintragen.

Der Audit-Test verhindert, dass jemand "vergisst" einen Lock-Check einzubauen.

## Aktueller Stand (17.05.2026)

| Status | Anzahl | Was |
|---|---|---|
| ✓ Lock eingebaut | 4 Controller | Absences, EmployeeTimeEntries, LohnZulagen, EmployeeBankAccounts |
| ⚠ TODO laut Whitelist | 8 Controller | Employments, Quellensteuer (MA), RecurringWages, PermitHistory, FamilyMembers, FamilyAllowances, LohnAssignments, SaldoVortrag |
| ⚪ Whitegelistet (Lohn-irrelevant) | ~35 | Auth, Lookups, Reports, Importer, etc. |

Die TODO-Einträge in der Whitelist sind die nächsten Etappen — sobald ein TODO-Controller den Lock einbaut, kann der Eintrag aus der Whitelist gelöscht werden.
