-- ====================================================================
-- Migration: Historie der Lohnabtretungs-Abzüge pro Lohnlauf
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_payroll_lohn_abtretung_entry.sql
-- ====================================================================
--
-- Zweck:
--   Pro bestätigtem Lohnlauf × Abtretung ein unveränderlicher Eintrag
--   mit Betrag, Behörden-Snapshot und Zahlungs-Referenzen. Grundlage für:
--     • Reporting "was ging in welchem Monat an welches Amt"
--     • DTA/pain.001-Zahlungsexport (aggregiert pro Behörde)
--     • Abacus-FIBU-Export (Buchungen, mit Beleg-Nr.)
--     • Korrekte Rück-Abwicklung bei Re-Confirm (BereitsAbgezogen)
--
-- Snapshot-Felder (behoerde_name, iban, qr_iban, referenz_amt,
-- zahlungs_referenz, bezeichnung) werden zum Zeitpunkt des Bestätigens
-- kopiert. Damit bleibt der Eintrag auch dann korrekt dokumentiert,
-- wenn später die Behörde umbenannt wird oder Referenzen ändern.
-- ====================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS payroll_lohn_abtretung_entry (
    id                             SERIAL       PRIMARY KEY,

    -- Zuordnung ------------------------------------------------------
    payroll_snapshot_id            INT          NOT NULL
        REFERENCES payroll_snapshot(id)           ON DELETE CASCADE,
    employee_lohn_assignment_id    INT          NOT NULL
        REFERENCES employee_lohn_assignment(id)   ON DELETE RESTRICT,
    employee_id                    INT          NOT NULL
        REFERENCES employee(id)                   ON DELETE RESTRICT,
    behoerde_id                    INT          NOT NULL
        REFERENCES behoerde(id)                   ON DELETE RESTRICT,

    -- Periode (denormalisiert, für schnelle Filter) ------------------
    period_year                    INT          NOT NULL,
    period_month                   INT          NOT NULL,

    -- Snapshot der Abtretungs-Regel zum Zeitpunkt ---------------------
    bezeichnung                    VARCHAR(100),
    referenz_amt                   VARCHAR(100),
    zahlungs_referenz              VARCHAR(50),

    -- Snapshot der Behörde zum Zeitpunkt (für Zahlungsauftrag) -------
    behoerde_name                  VARCHAR(200),
    iban                           VARCHAR(34),
    qr_iban                        VARCHAR(34),

    -- Beträge --------------------------------------------------------
    betrag                         NUMERIC(10,2) NOT NULL,
    bereits_abgezogen_vorher       NUMERIC(10,2) NOT NULL DEFAULT 0,
    bereits_abgezogen_nachher      NUMERIC(10,2) NOT NULL DEFAULT 0,

    -- FIBU-/Abacus-Export (später befüllbar) -------------------------
    fibu_belegnr                   VARCHAR(50),
    fibu_exportiert_am             TIMESTAMPTZ,   -- NULL = noch nicht exportiert

    -- DTA-/Zahlungs-Export (später befüllbar) ------------------------
    dta_exportiert_am              TIMESTAMPTZ,   -- NULL = noch nicht exportiert
    dta_export_ref                 VARCHAR(50),   -- z.B. pain.001-Auftrags-ID

    bemerkung                      TEXT,
    created_at                     TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT payroll_lohn_abtretung_entry_betrag_check
        CHECK (betrag >= 0),
    CONSTRAINT payroll_lohn_abtretung_entry_unique_per_snapshot
        UNIQUE (payroll_snapshot_id, employee_lohn_assignment_id)
);

CREATE INDEX IF NOT EXISTS idx_plae_snapshot
    ON payroll_lohn_abtretung_entry(payroll_snapshot_id);
CREATE INDEX IF NOT EXISTS idx_plae_assignment
    ON payroll_lohn_abtretung_entry(employee_lohn_assignment_id);
CREATE INDEX IF NOT EXISTS idx_plae_employee_period
    ON payroll_lohn_abtretung_entry(employee_id, period_year, period_month);
CREATE INDEX IF NOT EXISTS idx_plae_behoerde_period
    ON payroll_lohn_abtretung_entry(behoerde_id, period_year, period_month);
CREATE INDEX IF NOT EXISTS idx_plae_fibu_pending
    ON payroll_lohn_abtretung_entry(fibu_exportiert_am)
    WHERE fibu_exportiert_am IS NULL;
CREATE INDEX IF NOT EXISTS idx_plae_dta_pending
    ON payroll_lohn_abtretung_entry(dta_exportiert_am)
    WHERE dta_exportiert_am IS NULL;

COMMENT ON TABLE payroll_lohn_abtretung_entry IS
    'Historie der tatsächlich abgezogenen Beträge pro Lohnlauf × Abtretung. Basis für DTA-Zahlungsexport und Abacus-FIBU-Buchungen.';

COMMIT;
