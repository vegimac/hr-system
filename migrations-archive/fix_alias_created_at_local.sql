-- Alias-created_at: timestamptz → timestamp without time zone
-- Walter-Bug 18.07.2026: Sync schreibt Schweizer Lokalzeit (DateTime.Now),
-- Npgsql lehnt Kind=Local auf timestamptz ab.
-- In TablePlus ausführen (reines SQL, kein psql-Wrapper).

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'employee_number_alias'
          AND column_name = 'created_at'
          AND data_type = 'timestamp with time zone'
    ) THEN
        ALTER TABLE employee_number_alias
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING created_at AT TIME ZONE 'Europe/Zurich';
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'easyatwork_employee_alias'
          AND column_name = 'created_at'
          AND data_type = 'timestamp with time zone'
    ) THEN
        ALTER TABLE easyatwork_employee_alias
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING created_at AT TIME ZONE 'Europe/Zurich';
    END IF;
END $$;
