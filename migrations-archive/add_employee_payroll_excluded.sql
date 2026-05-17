-- ─────────────────────────────────────────────────────────────────────────
-- "Kein Lohn"-Flag pro Mitarbeiter
-- ─────────────────────────────────────────────────────────────────────────
-- Use Case: MA wird im HR-System geführt (z.B. weil er als Restaurant-Manager
-- Stempelzeiten freigeben muss), aber nicht über uns bezahlt — sondern z.B.
-- über McDonald's-Zentrale oder einen anderen Franchisenehmer.
--
-- Wirkung:
--   * Lohn-Tab listet diesen MA nicht auf
--   * Kein Lohnzettel, keine QST-Anmeldung, kein 13. ML
--   * Beim CSV-Re-Import bleibt die Flag erhalten (wird nicht zurückgesetzt)
--
-- Toggle nur durch Rolle admin oder superuser.
-- ─────────────────────────────────────────────────────────────────────────

ALTER TABLE employee
  ADD COLUMN IF NOT EXISTS is_payroll_excluded boolean NOT NULL DEFAULT false;
