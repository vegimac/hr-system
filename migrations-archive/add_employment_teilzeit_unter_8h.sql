-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 31.07.2026: «< 8 h / Wo.» (NBU-Befreiung UVG Art. 1a)
-- gehört zum VERTRAG (FLEX), nicht zur Anstellung am MA.
--
-- • Neue Spalte employment.teilzeit_unter_8h_woche
-- • Backfill aus employee.teilzeit_unter_8h_woche für laufende FLEX/UTP-Verträge
--
-- Lauf in TablePlus, dann ./deploy.sh
-- (Program.cs führt denselben ALTER + Backfill idempotent beim Startup aus.)
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE employment
    ADD COLUMN IF NOT EXISTS teilzeit_unter_8h_woche boolean NOT NULL DEFAULT false;

-- Laufende FLEX-Verträge erben den bisherigen MA-Flag (Legacy-UTP inkl.).
UPDATE employment e
   SET teilzeit_unter_8h_woche = true
  FROM employee emp
 WHERE e.employee_id = emp.id
   AND emp.teilzeit_unter_8h_woche = true
   AND UPPER(TRIM(e.employment_model)) IN ('FLEX', 'UTP')
   AND (e.contract_end_date IS NULL OR e.contract_end_date >= CURRENT_DATE)
   AND e.teilzeit_unter_8h_woche = false;

-- Sanity
SELECT COUNT(*) AS flex_unter_8h
  FROM employment
 WHERE teilzeit_unter_8h_woche = true
   AND UPPER(TRIM(employment_model)) IN ('FLEX', 'UTP');
