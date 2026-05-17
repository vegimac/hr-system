-- ============================================================
-- Migration: Absenzen-Tabelle
-- Ausführen in TablePlus gegen die hr_system Datenbank
-- ============================================================

CREATE TABLE IF NOT EXISTS absence (
    id                SERIAL PRIMARY KEY,
    employee_id       INT NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    absence_type      VARCHAR(20) NOT NULL,   -- 'KRANK', 'UNFALL', 'SCHULUNG', 'FERIEN'
    date_from         DATE NOT NULL,
    date_to           DATE NOT NULL,
    -- JSON-Array der Tage, welche angerechnet werden (yyyy-MM-dd)
    -- Bei KRANK/UNFALL/SCHULUNG: ausgewählte Arbeitstage
    -- Bei FERIEN: alle Tage der Periode
    worked_days       TEXT,
    -- Berechnete Stunden (immer positiv gespeichert)
    hours_credited    NUMERIC(8,2) NOT NULL DEFAULT 0,
    notes             TEXT,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_absence_employee_id ON absence(employee_id);
CREATE INDEX IF NOT EXISTS idx_absence_date_from   ON absence(date_from);
