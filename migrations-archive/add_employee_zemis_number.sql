-- ZEMIS-Nummer (Zentrales Migrationsinformationssystem) als Stammdatum am MA.
-- Bleibt unverändert auch bei Wechsel der Bewilligung (B → C → CH).
ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS zemis_number VARCHAR(50) NULL;

COMMENT ON COLUMN employee.zemis_number IS
    'ZEMIS-Nr. — bleibt während des ganzen Aufenthalts identisch, auch bei Wechsel der Bewilligung.';
