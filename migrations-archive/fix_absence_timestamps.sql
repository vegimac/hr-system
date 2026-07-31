-- ============================================================
-- Absenzen: created_at / updated_at → timestamp without time zone
-- Walter 31.07.2026 — Fix «Fehler beim Speichern» im Absenz-Modal
--
-- Ursache: migrate_absence.sql legte TIMESTAMPTZ an. Nach dem Wechsel
-- auf DateTime.Now (Kind=Local) lehnt Npgsql 8 das Schreiben ab → HTTP 500.
-- Analog Fix employee_address (26.07.2026).
--
-- Läuft auch idempotent beim Server-Start (Program.cs).
-- In TablePlus ausführen falls Deploy noch nicht durch ist.
-- ============================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'absence'
          AND column_name = 'created_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE absence
            ALTER COLUMN created_at TYPE timestamp without time zone
            USING (created_at AT TIME ZONE 'Europe/Zurich');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'absence'
          AND column_name = 'updated_at'
          AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE absence
            ALTER COLUMN updated_at TYPE timestamp without time zone
            USING (updated_at AT TIME ZONE 'Europe/Zurich');
    END IF;
END $$;
