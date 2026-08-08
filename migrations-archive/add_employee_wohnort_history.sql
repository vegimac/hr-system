-- Wohnort-Historie (Walter-Vorgabe 07.08.2026): PLZ/Ort/Kanton mit Gültig-ab.
-- Umzugs-Zeitpunkt für die QST — Kantonswechsel wirkt ab Folgemonat, der
-- angebrochene Monat wird im alten Kanton abgerechnet.
-- Läuft auch idempotent beim Server-Start (Program.cs). In TablePlus optional.
CREATE TABLE IF NOT EXISTS employee_wohnort_history (
    id          serial PRIMARY KEY,
    employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    plz         text,
    ort         text,
    kanton_code text,
    gueltig_ab  date,          -- NULL = «seit jeher» (initialer Bestand)
    bemerkung   text,
    created_at  timestamp without time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_wohnort_history_employee
    ON employee_wohnort_history (employee_id);
