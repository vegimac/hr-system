-- Walter 26.07.2026: employee_address.created_at / updated_at → Lokalzeit.
-- Ursache 500 beim Speichern: Spalten waren timestamptz, Code schreibt DateTime.Now.
-- Wird auch idempotent in Program.cs beim Startup ausgeführt.
-- TablePlus: Block unten ausführen.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'employee_address'
          AND column_name = 'created_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE employee_address
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'employee_address'
          AND column_name = 'updated_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE employee_address
            ALTER COLUMN updated_at TYPE timestamp without time zone
            USING (updated_at AT TIME ZONE 'Europe/Zurich');
    END IF;
END $$;
