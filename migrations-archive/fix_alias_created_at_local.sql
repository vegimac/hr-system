-- Alias-created_at: timestamptz → timestamp without time zone
-- Walter-Bug 18.07.2026: Sync schreibt Schweizer Lokalzeit (DateTime.Now),
-- Npgsql lehnt Kind=Local auf timestamptz ab.
-- JETZT in TablePlus ausführen, dann Import erneut versuchen.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'employee_number_alias'
          AND column_name = 'created_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.employee_number_alias
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'easyatwork_employee_alias'
          AND column_name = 'created_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.easyatwork_employee_alias
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;
END $$;

-- Kontrolle (sollte «timestamp without time zone» zeigen):
-- SELECT table_name, column_name, data_type, udt_name
-- FROM information_schema.columns
-- WHERE table_name IN ('employee_number_alias','easyatwork_employee_alias')
--   AND column_name = 'created_at';
