-- Walter-Vorgabe 26.05.2026: JobGroup-Referenz per FK statt String.
-- Bisher hielten employment.job_title und minimum_wage_rule_new.job_group_code
-- den Code als Text (z.B. "ASST_2"). Wird der Code in job_group umbenannt,
-- gehen die Referenzen kaputt. Sauber: FK auf job_group.id.
--
-- Diese Migration fuegt die FK-Spalten hinzu und befuellt sie aus den
-- bestehenden Code-Werten. Die Legacy-Text-Spalten bleiben (noch) erhalten
-- und werden vom Backend als Cache weitergeschrieben — sie koennen spaeter
-- gedroppt werden, sobald keine Lese-Pfade mehr darauf zugreifen.
-- Ausfuehren in TablePlus.

-- ── employment ─────────────────────────────────────────────────────────────
ALTER TABLE employment
    ADD COLUMN IF NOT EXISTS job_group_id integer NULL;

-- Backfill aus dem bestehenden job_title-Text (job_title hielt im alten
-- Modell den JobGroupCode, siehe Employment.cs Kommentar).
UPDATE employment e
   SET job_group_id = jg.id
  FROM job_group jg
 WHERE e.job_group_id IS NULL
   AND e.job_title IS NOT NULL
   AND TRIM(e.job_title) = jg.code;

-- FK-Constraint
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_employment_job_group'
    ) THEN
        ALTER TABLE employment
            ADD CONSTRAINT fk_employment_job_group
            FOREIGN KEY (job_group_id) REFERENCES job_group(id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_employment_job_group_id ON employment(job_group_id);

-- ── minimum_wage_rule_new ─────────────────────────────────────────────────
ALTER TABLE minimum_wage_rule_new
    ADD COLUMN IF NOT EXISTS job_group_id integer NULL;

UPDATE minimum_wage_rule_new r
   SET job_group_id = jg.id
  FROM job_group jg
 WHERE r.job_group_id IS NULL
   AND r.job_group_code IS NOT NULL
   AND TRIM(r.job_group_code) = jg.code;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_minwage_job_group'
    ) THEN
        ALTER TABLE minimum_wage_rule_new
            ADD CONSTRAINT fk_minwage_job_group
            FOREIGN KEY (job_group_id) REFERENCES job_group(id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_minwage_job_group_id ON minimum_wage_rule_new(job_group_id);

-- Sicherheits-Check: zeige Zeilen, fuer die Backfill NICHT erfolgreich war
-- (kein passender Code in job_group). Diese muessen vor dem Drop des
-- code-Felds geprueft werden.
-- SELECT 'employment', e.id, e.job_title FROM employment e WHERE e.job_group_id IS NULL AND e.job_title IS NOT NULL;
-- SELECT 'minwage', r.id, r.job_group_code FROM minimum_wage_rule_new r WHERE r.job_group_id IS NULL AND r.job_group_code IS NOT NULL;

-- HINWEIS: Die Code-Spalten (employment.job_title, minimum_wage_rule_new.job_group_code)
-- bleiben vorerst als Lese-Cache erhalten. Drop erfolgt in einem Folge-Schritt,
-- nachdem alle Backend-Lookups auf job_group_id umgestellt sind.
