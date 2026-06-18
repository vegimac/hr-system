-- ════════════════════════════════════════════════════════════════════════
-- easy@work ID-Verknüpfungen
-- Walter-Vorgabe 17.06.2026
--
-- 1. Employee: easyatwork_employee_id  — die interne easy@work-MA-ID,
--    damit edited_by_id (z.B. 19736) zum echten Manager-Namen auflösbar wird.
-- 2. EmployeeTimeEntry: easyatwork_timepunch_id — die eindeutige Stempel-ID,
--    sauberer Dedup-Key + ermöglicht spätere UPDATE-Sync (wenn easy@work
--    einen Stempel ändert, finden wir den Cowork-Eintrag exakt).
-- ════════════════════════════════════════════════════════════════════════

BEGIN;

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS easyatwork_employee_id INTEGER;
CREATE INDEX IF NOT EXISTS ix_employee_easyatwork_id
    ON employee(easyatwork_employee_id)
    WHERE easyatwork_employee_id IS NOT NULL;

ALTER TABLE employee_time_entry
    ADD COLUMN IF NOT EXISTS easyatwork_timepunch_id INTEGER;
CREATE UNIQUE INDEX IF NOT EXISTS ux_employee_time_entry_easyatwork_id
    ON employee_time_entry(easyatwork_timepunch_id)
    WHERE easyatwork_timepunch_id IS NOT NULL;

COMMIT;
