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

-- Nachtrag 08.08.2026: Adresswechsel aus easy@work → Datum-offen-Flag +
-- Dashboard-Warnung «Umzugsdatum bestätigen (QST)».
ALTER TABLE employee_wohnort_history
    ADD COLUMN IF NOT EXISTS datum_offen boolean NOT NULL DEFAULT false;
INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('umzug_datum_offen', 'Umzugsdatum bestätigen (QST)', TRUE, NULL, NULL, 'warning', NULL, FALSE, 27, 17, 'red')
ON CONFLICT (category) DO NOTHING;
