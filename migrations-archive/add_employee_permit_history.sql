-- Bewilligungs-Verlauf pro Mitarbeiter (Audit + LSE-Stichtag-Lookup)
--
-- Pattern wie employee_education_history. Bei jeder Verlängerung, jedem
-- Wechsel (z.B. L → B) oder Einbürgerung (permit_type_id = NULL,
-- note = 'Einbürgerung am ...') kommt ein neuer Eintrag dazu.
--
-- Der Eintrag mit gültigem (valid_from <= heute AND (valid_to IS NULL OR
-- valid_to >= heute)) wird auf employee.permit_type_id und
-- employee.permit_expiry_date synchronisiert (Service-Code).

CREATE TABLE IF NOT EXISTS employee_permit_history (
    id                  SERIAL PRIMARY KEY,
    employee_id         INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    permit_type_id      INTEGER REFERENCES permit_type(id),    -- NULL = keine Bewilligung mehr (z.B. CH-Bürger)
    valid_from          DATE NOT NULL,
    valid_to            DATE,                                   -- NULL = bis auf Weiteres aktuell
    permit_expiry_date  DATE,                                   -- Ablaufdatum der Bewilligung (für Reminder)
    note                TEXT,
    created_at          TIMESTAMP NOT NULL DEFAULT NOW(),
    created_by_user_id  INTEGER
);

CREATE INDEX IF NOT EXISTS idx_employee_permit_history_emp
    ON employee_permit_history(employee_id, valid_from DESC);

-- Initial-Backfill: für jeden MA mit aktueller Bewilligung einen Eintrag
-- erzeugen, valid_from = entry_date (oder Fallback), valid_to = NULL.
-- Idempotent dank NOT EXISTS — kann mehrfach laufen.
INSERT INTO employee_permit_history
    (employee_id, permit_type_id, valid_from, permit_expiry_date, note)
SELECT
    e.id,
    e.permit_type_id,
    COALESCE(e.entry_date::date, '2000-01-01'::date),
    e.permit_expiry_date,
    'Initial aus Stammdaten'
FROM employee e
WHERE e.permit_type_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM employee_permit_history h WHERE h.employee_id = e.id
  );
