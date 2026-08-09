-- Vorstellungsgespräch-Buchung durch HR (Walter-Vorgabe 09.08.2026, Stufe 2).
-- Ein Termin belegt einen 30-Minuten-Slot in einem interview_fenster
-- (Raster 45 Min = 30 Gespräch + 15 Puffer, verankert am Fensterstart).
-- ABGESAGT gibt den Slot wieder frei (Historie bleibt erhalten).
-- Läuft auch idempotent beim Server-Start (Program.cs).

CREATE TABLE IF NOT EXISTS interview_termin (
    id         serial PRIMARY KEY,
    fenster_id integer NOT NULL REFERENCES interview_fenster(id) ON DELETE CASCADE,
    von_zeit   time NOT NULL,
    kandidat   text NOT NULL,
    telefon    text,
    bemerkung  text,
    status     text NOT NULL DEFAULT 'GEPLANT' CHECK (status IN ('GEPLANT','ABGESAGT')),
    created_at timestamp without time zone NOT NULL DEFAULT now(),
    created_by text
);
CREATE INDEX IF NOT EXISTS ix_interview_termin_fenster ON interview_termin (fenster_id);
