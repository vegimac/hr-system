-- ================================================================
-- Migration: employee_time_entry
-- Stempelzeiten (Ein/Aus-Stempelungen) pro Mitarbeiter
-- ================================================================

CREATE TABLE IF NOT EXISTS employee_time_entry (
    id                  SERIAL PRIMARY KEY,
    employee_id         INTEGER NOT NULL
                            REFERENCES employee(id) ON DELETE CASCADE,
    entry_date          DATE NOT NULL,
    time_in             TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    time_out            TIMESTAMP WITHOUT TIME ZONE,
    comment             TEXT,
    duration_hours      NUMERIC(6,2),
    night_hours         NUMERIC(6,2) DEFAULT 0,
    total_hours         NUMERIC(6,2),
    source              VARCHAR(50) DEFAULT 'manual',   -- 'import' or 'manual'
    created_at          TIMESTAMP WITHOUT TIME ZONE DEFAULT NOW(),
    updated_at          TIMESTAMP WITHOUT TIME ZONE DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_time_entry_employee_id   ON employee_time_entry(employee_id);
CREATE INDEX IF NOT EXISTS idx_time_entry_date          ON employee_time_entry(entry_date);
CREATE INDEX IF NOT EXISTS idx_time_entry_emp_date      ON employee_time_entry(employee_id, entry_date);
