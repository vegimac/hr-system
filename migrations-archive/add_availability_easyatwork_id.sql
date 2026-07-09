-- easy@work-Verfügbarkeits-Sync (Walter 09.07.2026):
-- availability.id aus easy@work an der Verfügbarkeits-Version speichern.
-- NULL = manuell erfasst; gesetzte Werte sind der Upsert-Schlüssel des Syncs.
-- (Läuft auch idempotent beim Server-Start in Program.cs — dieser Block ist
-- nur die TablePlus-Doku.)

ALTER TABLE employee_availability ADD COLUMN IF NOT EXISTS easyatwork_availability_id bigint;
CREATE INDEX IF NOT EXISTS ix_employee_availability_eaw ON employee_availability (easyatwork_availability_id);

-- Kontrolle:
SELECT column_name, data_type FROM information_schema.columns
WHERE table_name = 'employee_availability' ORDER BY ordinal_position;
