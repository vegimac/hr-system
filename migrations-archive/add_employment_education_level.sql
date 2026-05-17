-- Education Level direkt am Vertrag pflegen.
-- Walter-Architekturentscheidung: bei Ausbildungs-Wechsel wird sowieso
-- ein neuer Vertrag angelegt, also gewinnt die Vertrags-Versionierung
-- automatisch die zeitliche Versionierung der separaten History-Tabelle.
--
-- 1) Spalte auf employment hinzufügen
ALTER TABLE employment
    ADD COLUMN IF NOT EXISTS education_level_code VARCHAR(10);

-- 2) Bestehende Verträge aus EmployeeEducationHistory + EducationLevels backfillen
--    Logik: pro Employment den EduLevel-Eintrag der zum Vertragsbeginn gültig war.
UPDATE employment em
SET    education_level_code = el.code
FROM   employee_education_history eh
JOIN   education_level el ON el.id = eh.education_level_id
WHERE  eh.employee_id = em.employee_id
  AND  eh.is_active   = TRUE
  AND  eh.valid_from <= em.contract_start_date
  AND  (eh.valid_to IS NULL OR eh.valid_to >= em.contract_start_date)
  AND  em.education_level_code IS NULL;

-- 3) Verträge ohne EduHistory bekommen den Default 'Ia' (5 Sans qualification),
--    analog Importer-Konvention bei leerer CCNT-Spalte. Konservativer Default —
--    Mindestlohn-Check wird damit nicht zu lasch.
UPDATE employment
SET    education_level_code = 'Ia'
WHERE  education_level_code IS NULL;

-- 4) Vorschau wieviele jetzt welchen Code haben
SELECT education_level_code, COUNT(*) AS n
FROM   employment
WHERE  is_active = TRUE
GROUP  BY education_level_code
ORDER  BY n DESC;
