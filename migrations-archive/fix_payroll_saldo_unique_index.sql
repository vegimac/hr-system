-- ============================================================================
-- FIX: payroll_saldo Unique-Index inkl. company_profile_id
-- ----------------------------------------------------------------------------
-- Problem (Walter-Vorgabe 20.05.2026): der bestehende Index
--   IX_payroll_saldo_emp_period  (employee_id, period_year, period_month)
-- ist NICHT unique und enthält KEIN company_profile_id. Der natürliche
-- Schlüssel ist aber (MA, Filiale, Jahr, Monat) — ein MA in mehreren Filialen
-- braucht pro Filiale einen eigenen Saldo. Ohne company_profile_id im
-- Eindeutigkeits-Schlüssel könnte die DB keine „ein Saldo je MA/Filiale/Monat"-
-- Garantie geben → stille Duplikate möglich, der Upsert (FirstOrDefault) greift
-- dann willkürlich einen.
--
-- Ausführung: in TablePlus. Schritt 0 zuerst prüfen.
-- ============================================================================

-- ── Schritt 0: gibt es echte Duplikate (MA + Filiale + Jahr + Monat)? ─────────
-- Wenn diese Abfrage 0 Zeilen liefert, löscht Schritt 1 nichts (gut).
SELECT employee_id, company_profile_id, period_year, period_month, COUNT(*) AS n
FROM   payroll_saldo
GROUP  BY employee_id, company_profile_id, period_year, period_month
HAVING COUNT(*) > 1
ORDER  BY n DESC;

-- ── Schritt 1: etwaige Duplikate entfernen — neueste Zeile (höchste id) behalten
DELETE FROM payroll_saldo a
USING  payroll_saldo b
WHERE  a.employee_id        = b.employee_id
  AND  a.company_profile_id = b.company_profile_id
  AND  a.period_year        = b.period_year
  AND  a.period_month       = b.period_month
  AND  a.id < b.id;

-- ── Schritt 2: alten, unvollständigen Index droppen ──────────────────────────
DROP INDEX IF EXISTS "IX_payroll_saldo_emp_period";

-- ── Schritt 3: korrekten UNIQUE-Index inkl. company_profile_id anlegen ───────
CREATE UNIQUE INDEX IF NOT EXISTS ux_payroll_saldo_emp_branch_period
    ON payroll_saldo (employee_id, company_profile_id, period_year, period_month);
