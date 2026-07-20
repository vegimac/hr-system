-- Mutterschaft: created_at/updated_at auf System-Standard (Walter 20.07.2026)
-- timestamp without time zone + DateTime.Now — wie der Rest der App.
-- Verhindert Npgsql-Fehler «Cannot write DateTime with Kind=Local to … timestamptz».
-- In TablePlus ausführen.

ALTER TABLE employee_pregnancy
    ALTER COLUMN created_at TYPE timestamp without time zone
        USING (created_at AT TIME ZONE 'Europe/Zurich'),
    ALTER COLUMN updated_at TYPE timestamp without time zone
        USING (CASE WHEN updated_at IS NULL THEN NULL
                    ELSE updated_at AT TIME ZONE 'Europe/Zurich' END);
