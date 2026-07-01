-- Probezeit-Anker + History (Walter-Vorgabe 29.06.2026)
-- In TablePlus ausführen (nicht via psql-CLI).
--
-- probation_start_date = Anker (= erste Stempelzeit, sobald bekannt). NULL =
--   noch nicht verankert → Probezeit gilt provisorisch ab Vertragsbeginn.
-- employment_probation_log = History jeder Verschiebung (Anker beim ersten
--   Stempel + später pro Absenz). probation_end_date am Vertrag bleibt der
--   aktuelle, nachgeführte Wert (Dashboard-Warnings + PDF hängen dran).

ALTER TABLE employment
    ADD COLUMN IF NOT EXISTS probation_start_date date;

CREATE TABLE IF NOT EXISTS employment_probation_log (
    id                      serial PRIMARY KEY,
    employment_id           integer NOT NULL REFERENCES employment(id) ON DELETE CASCADE,
    event_date              date    NOT NULL,
    event_type             text    NOT NULL,          -- 'ANKER' | 'ABSENZ'
    delta_days             integer NOT NULL DEFAULT 0,
    grund                  text,
    probezeit_ende_nachher  date,
    created_at             timestamp without time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_employment_probation_log_employment
    ON employment_probation_log (employment_id);
