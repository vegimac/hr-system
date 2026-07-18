-- Schweizer Zeitstempel: timestamptz → timestamp without time zone
-- Walter-Bug 18.07.2026: Sync schreibt DateTime.Now (Lokalzeit);
-- Npgsql lehnt Kind=Local auf timestamptz ab.
-- JETZT in TablePlus ausführen, dann Import / Deploy.

DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND udt_name = 'timestamptz'
          AND (
                (table_name = 'employee_number_alias' AND column_name = 'created_at')
             OR (table_name = 'easyatwork_employee_alias' AND column_name = 'created_at')
             OR (table_name = 'employee_time_entry' AND column_name IN ('created_at','updated_at'))
             OR (table_name = 'easyatwork_sync_state' AND column_name IN ('last_sync_at','last_seen_updated_at'))
          )
    LOOP
        EXECUTE format(
            'ALTER TABLE public.%I ALTER COLUMN %I TYPE timestamp without time zone USING (%I AT TIME ZONE %L)',
            r.table_name, r.column_name, r.column_name, 'Europe/Zurich');
    END LOOP;
END $$;

-- Kontrolle (sollte «timestamp without time zone» zeigen):
-- SELECT table_name, column_name, data_type, udt_name
-- FROM information_schema.columns
-- WHERE table_name IN (
--   'employee_number_alias','easyatwork_employee_alias',
--   'employee_time_entry','easyatwork_sync_state')
--   AND column_name IN ('created_at','updated_at','last_sync_at','last_seen_updated_at')
-- ORDER BY 1, 2;
