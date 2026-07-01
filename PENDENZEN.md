# Pendenzen / Backlog

Offene, bewusst zurückgestellte Punkte (noch NICHT umgesetzt — status quo).
Reihenfolge ohne Priorisierung; Datum = erfasst am.

## Befristete Verträge & Probezeit (erfasst 30.06.2026)

Hintergrund: Bei einem befristeten Vertrag ist eine Probezeit rechtlich
grundsätzlich nicht zulässig. Walter stellt ab **1.7.2026** intern keine
befristeten Verträge mehr aus; die bereits laufenden befristeten Verträge
(noch ~6–7 Monate) behalten ihre Probezeit.

Aktueller Stand im Code:
- Die Auto-Probezeit setzt weiterhin bei ALLEN Erstverträgen eine Probezeit
  (auch befristet) — die Regel „befristet → keine Probezeit" liegt als
  inaktiver Schalter `SkipProbationForBefristet = false` bereit
  (`EmploymentsController.Create` + `EasyAtWorkEmployeeSyncService` Import-Anker).

Pendent:
1. **Dashboard-Warnung bei befristetem Neu-Vertrag ab 1.7.2026.** Wird nach dem
   1.7.2026 ein NEUER (Erst-)Vertrag importiert/angelegt, der befristet ist
   (ContractType „befristet" oder Enddatum gesetzt, ContractStartDate ≥ 1.7.2026),
   eine Warnung auf dem Dashboard zeigen („Befristeter Vertrag entgegen interner
   Regel ab 1.7.2026"). Nur Hinweis, kein Block.
2. **Befristung später ganz eliminieren.** Sobald Verträge ausschliesslich in
   diesem System ausgestellt werden (keine befristeten mehr im Umlauf):
   - `SkipProbationForBefristet` auf `true` setzen (befristet → keine Probezeit), und/oder
   - die Befristungs-Option in der Vertrags-Erfassung entfernen.
