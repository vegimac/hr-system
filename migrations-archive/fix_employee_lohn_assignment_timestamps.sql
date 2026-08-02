-- employee_lohn_assignment: timestamptz → timestamp without time zone
-- Walter 02.08.2026 — sonst Npgsql 500 beim Definitiv-Confirm mit Lohnabtretung:
--   Cannot write DateTime with Kind=Local to PostgreSQL type 'timestamp with time zone'
-- Confirm schreibt DateTime.Now auf updated_at (BereitsAbgezogen hochzählen).
-- In TablePlus ausführen (kein psql-Wrapper). Idempotent.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'employee_lohn_assignment'
          AND column_name = 'created_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.employee_lohn_assignment
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'employee_lohn_assignment'
          AND column_name = 'updated_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.employee_lohn_assignment
            ALTER COLUMN updated_at TYPE timestamp without time zone
            USING (updated_at AT TIME ZONE 'Europe/Zurich');
    END IF;
END $$;
