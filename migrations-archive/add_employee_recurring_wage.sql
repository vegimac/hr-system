-- ====================================================================
-- Migration: Wiederkehrende Zulagen/Abzüge pro Mitarbeiter
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_employee_recurring_wage.sql
-- ====================================================================
--
-- Zweck:
--   Pro Mitarbeiter beliebig viele wiederkehrende Zulagen (z. B. Fahrzeug-
--   zulage, Handy-Pauschale) oder Abzüge mit Gültigkeitszeitraum hinterlegen.
--   Beim monatlichen Lohnlauf werden aktive Einträge automatisch wie
--   LohnZulagen behandelt — mit SV-Logik, Basis-Flags und korrekter
--   Platzierung (Zulage im Brutto, nicht-SV-Zulage + Abzug nach Netto).
--
--   valid_to = NULL  → unbefristet aktiv
--   valid_to gesetzt → letzter Tag der Gültigkeit (inklusive)
-- ====================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS employee_recurring_wage (
    id              SERIAL      PRIMARY KEY,
    employee_id     INT         NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    lohnposition_id INT         NOT NULL REFERENCES lohnposition(id),
    betrag          NUMERIC(10,2) NOT NULL CHECK (betrag > 0),
    valid_from      DATE        NOT NULL,
    valid_to        DATE,
    bemerkung       TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT employee_recurring_wage_valid_range
        CHECK (valid_to IS NULL OR valid_to >= valid_from)
);

CREATE INDEX IF NOT EXISTS idx_employee_recurring_wage_employee
    ON employee_recurring_wage(employee_id);

CREATE INDEX IF NOT EXISTS idx_employee_recurring_wage_period
    ON employee_recurring_wage(employee_id, valid_from, valid_to);

COMMENT ON TABLE employee_recurring_wage IS
    'Wiederkehrende Zulagen/Abzüge pro Mitarbeiter mit Gültigkeitszeitraum. Werden bei jedem Lohnlauf automatisch berücksichtigt.';

COMMIT;
