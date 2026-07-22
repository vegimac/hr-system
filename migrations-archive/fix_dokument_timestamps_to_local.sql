-- Dokument-Zeitstempel → Lokalzeit (Walter 22.07.2026)
-- employee_dokument.hochgeladen_am und mailbox_document.uploaded_at waren
-- timestamptz → Npgsql verlangt UTC. Systemweit gilt: timestamp without time zone
-- + DateTime.Now (Lokalzeit, Europe/Zurich).
-- Auch idempotent in Program.cs beim Startup.

ALTER TABLE employee_dokument
    ALTER COLUMN hochgeladen_am TYPE timestamp without time zone
    USING hochgeladen_am AT TIME ZONE 'Europe/Zurich';

ALTER TABLE mailbox_document
    ALTER COLUMN uploaded_at TYPE timestamp without time zone
    USING uploaded_at AT TIME ZONE 'Europe/Zurich';
