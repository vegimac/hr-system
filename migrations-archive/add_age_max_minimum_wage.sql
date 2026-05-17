-- ════════════════════════════════════════════════════════════════════════════
-- Mindestlohn-Regel: Altersabhängige Regeln (z.B. Lehrlinge, unter 18-Jährige)
--
-- Erweitert minimum_wage_rule_new um eine optionale `age_max` Spalte.
--   age_max = NULL        → Regel gilt für alle Altersgruppen (Default)
--   age_max = 17          → Regel gilt nur für Mitarbeitende ≤ 17 Jahre
--                            (= bis zum Erreichen des 18. Lebensjahres)
--
-- Lookup-Logik im ComplianceController:
--   WHERE (age_max IS NULL OR employee_age <= age_max)
--   ORDER BY age_max ASC NULLS LAST   ← spezifischste Regel zuerst
--
-- Damit gilt: ein 16-jähriger MA bekommt zuerst die age_max=17-Regel
-- (wenn vorhanden), ein 25-jähriger MA bekommt automatisch nur die
-- age_max=NULL-Regel.
-- ════════════════════════════════════════════════════════════════════════════

ALTER TABLE minimum_wage_rule_new
    ADD COLUMN IF NOT EXISTS age_max INT NULL;

COMMENT ON COLUMN minimum_wage_rule_new.age_max IS
    'Maximales Alter (inklusiv) für diese Regel. NULL = keine Altersgrenze.
     Beispiel age_max=17 → gilt bis 18. Geburtstag.';

-- ── Neue Regel: CREW + UTP + Ia + hourly für Mitarbeitende unter 18 Jahre ──
-- L-GAV Anhang II: Stundenlohn-Mindest für Jugendliche
INSERT INTO minimum_wage_rule_new
    (job_group_code, employment_model_code, education_level_id,
     salary_type, amount, valid_from, valid_to, is_active, age_max)
SELECT
    'CREW', 'UTP', el.id,
    'hourly', 16.85, '2026-01-01'::date, NULL, TRUE, 17
FROM education_level el
WHERE el.code = 'Ia'
  AND NOT EXISTS (
      SELECT 1 FROM minimum_wage_rule_new
      WHERE job_group_code        = 'CREW'
        AND employment_model_code = 'UTP'
        AND education_level_id    = el.id
        AND salary_type           = 'hourly'
        AND age_max               = 17
        AND valid_from            = '2026-01-01'::date
  );
