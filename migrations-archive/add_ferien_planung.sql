-- Ferienplaner für FIX-M-Manager (Walter-Vorgabe 14.08.2026).
-- GF plant Wunsch-Ferien (GEPLANT = orange); «definitiv setzen» erzeugt die
-- echte Ferien-Absenz (absence_id) und der Balken wird grün.
-- Läuft auch idempotent beim Server-Start (Program.cs).

CREATE TABLE IF NOT EXISTS ferien_planung (
    id          serial PRIMARY KEY,
    employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    date_from   date NOT NULL,
    date_to     date NOT NULL,
    status      text NOT NULL DEFAULT 'GEPLANT',
    absence_id  integer REFERENCES absence(id) ON DELETE SET NULL,
    created_at  timestamp without time zone NOT NULL DEFAULT now(),
    created_by  text,
    updated_at  timestamp without time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_ferien_planung_emp
    ON ferien_planung (employee_id, date_from);
