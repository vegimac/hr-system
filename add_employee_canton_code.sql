-- ====================================================================
-- Migration: Kanton-Code am Mitarbeiter
-- Ausführen mit:
--   psql -d <datenbank> -f add_employee_canton_code.sql
-- ====================================================================
--
-- Zweck:
--   Kanton des Wohnsitzes pro Mitarbeiter als 2-Zeichen-Code speichern
--   (ZH, BE, AG, ...). Wird im Quellensteuer-Dialog als Info-Anzeige
--   genutzt und kann später auch als Default für den Steuerkanton
--   vorgeschlagen werden.
--
--   Feld ist optional — bestehende MA bleiben auf NULL und können bei
--   Bedarf manuell im Mitarbeiter-Stamm gepflegt werden.
-- ====================================================================

BEGIN;

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS canton_code VARCHAR(2);

COMMENT ON COLUMN employee.canton_code IS
    'Wohnkanton als 2-Zeichen-Code (ZH, BE, AG, ...). NULL = nicht gepflegt.';

COMMIT;
