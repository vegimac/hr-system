-- ====================================================================
-- Migration: Eintrittsdatum-Backfill für bestehende MAs
-- Ausführen mit:
--   psql -d hr_system -U postgres -f backfill_employee_entry_date.sql
-- ====================================================================
--
-- Walter-Hintergrund (10.05.2026): Viele schon importierte MAs haben
-- kein entry_date, obwohl in der easy@work CSV "Von" (Vertragsbeginn)
-- als Fallback verfügbar ist. Der Import-Code nutzt "Von" zwar als
-- Fallback, aber nur bei NEUEN MAs. Bei bestehenden MAs ohne Wert
-- läuft die Logik nicht durch.
--
-- Backfill-Strategie:
--   1. Wenn employee.entry_date IS NULL
--   2. Setze es auf das früheste contract_start_date aller Employments
--      (frühester Vertragsbeginn = beste Schätzung für Betriebs-Eintritt)
--   3. Wenn der MA keinen Vertrag hat (Phantom-MA / Inaktive):
--      Wert bleibt NULL — Walter muss manuell pflegen.
--
-- Idempotent: kann mehrmals ausgeführt werden, überschreibt keine
-- vorhandenen Werte.
-- ====================================================================

-- Preview: was würde gesetzt?
SELECT
    e.id,
    e.employee_number,
    e.first_name,
    e.last_name,
    e.entry_date AS current_entry,
    MIN(emp.contract_start_date) AS would_set_to
FROM employee e
JOIN employment emp ON emp.employee_id = e.id
WHERE e.entry_date IS NULL
  AND emp.contract_start_date IS NOT NULL
GROUP BY e.id, e.employee_number, e.first_name, e.last_name, e.entry_date
ORDER BY e.first_name;

-- Tatsächlicher Backfill (auskommentieren wenn Preview ok aussieht)
UPDATE employee e
SET entry_date = sub.min_contract
FROM (
    SELECT employee_id, MIN(contract_start_date) AS min_contract
    FROM employment
    WHERE contract_start_date IS NOT NULL
    GROUP BY employee_id
) sub
WHERE e.id = sub.employee_id
  AND e.entry_date IS NULL;

-- Verifikation
SELECT COUNT(*) AS still_null
FROM employee
WHERE entry_date IS NULL AND is_active = true;
