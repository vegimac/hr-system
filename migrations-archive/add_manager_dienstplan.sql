-- Manager-Dienstplan (Walter-Vorgabe 08.08.2026, ersetzt Excel «Manager DP»)
-- Läuft idempotent beim Server-Start (Program.cs). In TablePlus optional.
CREATE TABLE IF NOT EXISTS manager_dienstplan (
    id          serial PRIMARY KEY,
    employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    datum       date NOT NULL,
    code        text NOT NULL,          -- Kürzel aus dienstplan_code
    updated_at  timestamp without time zone NOT NULL DEFAULT now(),
    updated_by  text
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_manager_dienstplan_emp_datum
    ON manager_dienstplan (employee_id, datum);

CREATE TABLE IF NOT EXISTS dienstplan_code (
    id          serial PRIMARY KEY,
    code        text NOT NULL UNIQUE,
    bezeichnung text NOT NULL,
    farbe       text,                   -- Zellfarbe, NULL = weiss
    sort_order  integer NOT NULL DEFAULT 0,
    is_active   boolean NOT NULL DEFAULT true
);
INSERT INTO dienstplan_code (code, bezeichnung, farbe, sort_order) VALUES
    ('F',   'Früh',                          NULL,      10),
    ('M',   'Mittel',                        NULL,      20),
    ('S',   'Spät',                          NULL,      30),
    ('-',   'frei',                          '#fef9c3', 40),
    ('SK',  'Shake-Maschine reinigen',       '#dbeafe', 50),
    ('SKM', 'Shake-Maschine reinigen + Mittel', '#dbeafe', 60)
ON CONFLICT (code) DO NOTHING;

-- Planungsrecht pro User-Filiale (Pflege im Filial-Tab «Unterzeichner»).
ALTER TABLE user_branch_access
    ADD COLUMN IF NOT EXISTS can_dienstplan boolean NOT NULL DEFAULT false;
