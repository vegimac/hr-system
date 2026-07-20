-- Mutterschaft: created_at/updated_at auf System-Standard (Walter 20.07.2026)
-- timestamp without time zone + DateTime.Now — lokal, wie der Rest der App.
-- Verhindert Npgsql «Cannot write DateTime with Kind=Local to … timestamptz».
-- In TablePlus ausführen (idempotent).

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'employee_pregnancy' AND column_name = 'created_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE employee_pregnancy
            ALTER COLUMN created_at TYPE timestamp without time zone
                USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'employee_pregnancy' AND column_name = 'updated_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE employee_pregnancy
            ALTER COLUMN updated_at TYPE timestamp without time zone
                USING (updated_at AT TIME ZONE 'Europe/Zurich');
    END IF;
END $$;
