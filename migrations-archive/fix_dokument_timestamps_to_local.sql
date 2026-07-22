-- Dokument-Zeitstempel → Lokalzeit (Walter 22.07.2026)
-- employee_dokument.hochgeladen_am und mailbox_document.uploaded_at waren
-- timestamptz → Npgsql verlangt UTC. Systemweit gilt: timestamp without time zone
-- + DateTime.Now (Lokalzeit, Europe/Zurich).
-- Auch idempotent in Program.cs beim Startup.
-- In TablePlus ausführen, falls der Startup die Spalte noch nicht umgestellt hat.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'employee_dokument'
          AND column_name = 'hochgeladen_am' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE employee_dokument
            ALTER COLUMN hochgeladen_am TYPE timestamp without time zone
            USING (hochgeladen_am AT TIME ZONE 'Europe/Zurich');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'mailbox_document'
          AND column_name = 'uploaded_at' AND udt_name = 'timestamptz'
    ) THEN
        ALTER TABLE mailbox_document
            ALTER COLUMN uploaded_at TYPE timestamp without time zone
            USING (uploaded_at AT TIME ZONE 'Europe/Zurich');
    END IF;
END $$;
