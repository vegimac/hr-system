-- Migration: Audit-Felder für Stempelzeit-Änderungen
-- Datum: 2026-04-10
-- Beschreibung: Speichert bei manuellen Änderungen die Originalwerte sowie
--               den Benutzernamen und Zeitstempel der letzten Änderung.

ALTER TABLE employee_time_entry
    ADD COLUMN IF NOT EXISTS original_time_in  timestamptz,
    ADD COLUMN IF NOT EXISTS original_time_out timestamptz,
    ADD COLUMN IF NOT EXISTS edited_by         varchar(100),
    ADD COLUMN IF NOT EXISTS edited_at         timestamptz;
