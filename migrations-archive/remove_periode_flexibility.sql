-- Walter-Vorgabe 20.05.2026: Lohnperiode = IMMER Kalendermonat.
-- Entfernt den Schema-Ballast der alten Periodenflexibilität (Starttag 21/1,
-- Übergangs-Lohnläufe, Periodenregel-Konfiguration).
--
-- HINWEIS: Program.cs führt diesen Cleanup beim Startup automatisch + idempotent
-- aus. Dieses File ist die manuelle TablePlus-Variante / Dokumentation.
--
-- Reihenfolge wichtig: erst Daten normalisieren, dann Spalten/Tabelle droppen.

-- 1) Offene/provisorische Perioden OHNE Snapshots auf Kalendermonat ziehen.
--    Abgeschlossene Perioden (mit eingefrorenen Snapshots) bleiben unangetastet.
UPDATE payroll_periode pp
SET    period_from = make_date(pp.year, pp.month, 1),
       period_to   = (make_date(pp.year, pp.month, 1) + interval '1 month - 1 day')::date
WHERE  NOT EXISTS (SELECT 1 FROM payroll_snapshot ps WHERE ps.payroll_periode_id = pp.id);

-- 2) Schema-Ballast droppen. DROP COLUMN is_transition entfernt automatisch
--    den partiellen UNIQUE-Index (WHERE is_transition=false).
ALTER TABLE payroll_periode  DROP COLUMN IF EXISTS config_id;
ALTER TABLE payroll_periode  DROP COLUMN IF EXISTS is_transition;
ALTER TABLE company_profile  DROP COLUMN IF EXISTS payroll_period_start_day;
DROP TABLE IF EXISTS payroll_periode_config;

-- 3) Vollständigen UNIQUE-Index sicherstellen (1 Periode pro Filiale+Monat).
DROP INDEX IF EXISTS UX_payroll_periode_branch_year_month;
CREATE UNIQUE INDEX IF NOT EXISTS UX_payroll_periode_branch_year_month
    ON payroll_periode(company_profile_id, year, month);

-- 4) Kontrolle
SELECT id, company_profile_id, year, month, period_from, period_to, status
FROM   payroll_periode
ORDER  BY company_profile_id, year, month;
