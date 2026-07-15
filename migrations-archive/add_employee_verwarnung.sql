-- Verwarnungs-Verlauf pro MA (Walter-Vorgabe 14.07.2026).
-- Pro Verwarnung ein Dokument (unterschriebenes Schreiben). Kein Löschen —
-- nur Storno (Flag + Grund), damit der Verlauf für Kündigungen lückenlos bleibt.
-- In TablePlus ausführen (reines SQL, idempotent).

CREATE TABLE IF NOT EXISTS employee_verwarnung (
    id            serial PRIMARY KEY,
    employee_id   integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    datum         date NOT NULL,
    stufe         text NOT NULL DEFAULT 'VERWARNUNG_1',
    gruende       text,
    beschreibung  text,
    dokument_id   integer REFERENCES employee_dokument(id) ON DELETE SET NULL,
    storniert     boolean NOT NULL DEFAULT false,
    storno_grund  text,
    erstellt_von  text,
    erstellt_am   timestamp without time zone NOT NULL DEFAULT now(),
    geaendert_am  timestamp without time zone
);

CREATE INDEX IF NOT EXISTS ix_employee_verwarnung_emp
    ON employee_verwarnung(employee_id, datum);
