-- RÜCKBAU 18.07.2026: Sync-Metadaten wieder auf timestamptz
-- (Stand vor dem Zeitzonen-Experiment). Der Sync schreibt dort
-- DateTime.UtcNow — so wie es vorher funktioniert hat.
-- Stempel-Wanduhrzeiten (time_in/out) bleiben timestamp without time zone.
--
-- Optional in TablePlus; Program.cs macht dasselbe beim Start.

DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND udt_name = 'timestamp'
          AND (
                (table_name = 'employee_number_alias' AND column_name = 'created_at')
             OR (table_name = 'easyatwork_employee_alias' AND column_name = 'created_at')
             OR (table_name = 'employee_time_entry' AND column_name IN ('created_at','updated_at'))
             OR (table_name = 'easyatwork_sync_state' AND column_name IN ('last_sync_at','last_seen_updated_at'))
          )
    LOOP
        EXECUTE format(
            'ALTER TABLE public.%I ALTER COLUMN %I TYPE timestamptz USING (%I AT TIME ZONE %L)',
            r.table_name, r.column_name, r.column_name, 'Europe/Zurich');
    END LOOP;
END $$;
