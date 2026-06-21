-- Alte Personalnummern in eigene Tabelle (Walter-Vorgabe 21.06.2026)
-- Ersetzt die starren Felder employee.employee_number_alt1/alt2.
-- In TablePlus ausführen. Idempotent (column-existence-guarded).

CREATE TABLE IF NOT EXISTS employee_number_alias (
    id         SERIAL PRIMARY KEY,
    employee_id INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    number     TEXT NOT NULL,
    valid_from DATE,
    valid_to   DATE,
    source     VARCHAR(50) DEFAULT 'manual',
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_emp_number_alias_number ON employee_number_alias(number);
CREATE INDEX IF NOT EXISTS idx_emp_number_alias_emp    ON employee_number_alias(employee_id);

-- Bestehende Alt-Nummern übernehmen + alte Spalten droppen (nur solange sie da sind).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'employee' AND column_name = 'employee_number_alt1') THEN
        INSERT INTO employee_number_alias (employee_id, number, source)
        SELECT id, employee_number_alt1, 'migration'
        FROM employee WHERE employee_number_alt1 IS NOT NULL AND employee_number_alt1 <> '';

        INSERT INTO employee_number_alias (employee_id, number, source)
        SELECT id, employee_number_alt2, 'migration'
        FROM employee WHERE employee_number_alt2 IS NOT NULL AND employee_number_alt2 <> '';

        ALTER TABLE employee DROP COLUMN IF EXISTS employee_number_alt1;
        ALTER TABLE employee DROP COLUMN IF EXISTS employee_number_alt2;
    END IF;
END $$;
