-- lohn_zulage / behoerde / lohnposition: timestamptz → timestamp without time zone
-- Walter 01.08.2026 — sonst Npgsql 500:
--   Cannot write DateTime with Kind=Local to PostgreSQL type 'timestamp with time zone'
-- In TablePlus ausführen (kein psql-Wrapper).

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'lohn_zulage'
          AND column_name = 'created_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.lohn_zulage
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'lohn_zulage'
          AND column_name = 'updated_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.lohn_zulage
            ALTER COLUMN updated_at TYPE timestamp without time zone
            USING (updated_at AT TIME ZONE 'Europe/Zurich');
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'behoerde'
          AND column_name = 'created_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.behoerde
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'behoerde'
          AND column_name = 'updated_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.behoerde
            ALTER COLUMN updated_at TYPE timestamp without time zone
            USING (updated_at AT TIME ZONE 'Europe/Zurich');
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'lohnposition'
          AND column_name = 'created_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE public.lohnposition
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;
END $$;
