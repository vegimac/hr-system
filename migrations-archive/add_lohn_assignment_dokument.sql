-- Lohnabtretung: Pflicht-Dokument-Verknüpfung (Walter 02.08.2026)
-- Ohne Dokument gilt die Abtretung nicht (kein Lohn-Abzweig ohne Beleg).
-- In TablePlus ausführen. Idempotent.

ALTER TABLE employee_lohn_assignment
    ADD COLUMN IF NOT EXISTS dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_emp_lohn_assignment_dokument
    ON employee_lohn_assignment(dokument_id);
