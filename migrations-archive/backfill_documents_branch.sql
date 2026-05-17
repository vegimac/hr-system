-- ════════════════════════════════════════════════════════════════════
-- Backfill branch_code für Alt-Dokumente
-- ════════════════════════════════════════════════════════════════════
-- Setzt branch_code auf die restaurant_code der aktiven Anstellung
-- (employment.is_active = TRUE) des Mitarbeiters.
-- Dokumente, deren MA keine aktive Anstellung hat, bleiben NULL und
-- müssen ggf. manuell zugeordnet werden.
-- ════════════════════════════════════════════════════════════════════

UPDATE employee_dokument d
SET branch_code = cp.restaurant_code
FROM employment e
JOIN company_profile cp ON cp.id = e.company_profile_id
WHERE e.employee_id = d.employee_id
  AND e.is_active = TRUE
  AND d.branch_code IS NULL
  AND cp.restaurant_code IS NOT NULL;

-- Übersicht: Was wurde gemacht?
SELECT
    d.id          AS doc_id,
    d.employee_id AS emp_id,
    d.branch_code AS branch,
    d.filename_storage AS file
FROM employee_dokument d
ORDER BY d.id;
