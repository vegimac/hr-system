-- ──────────────────────────────────────────────────────────────────────
-- Walter-Vorgabe 30.07.2026: Lohnausweis-Kopie an Behörde bei Lohnabtretung.
--
-- Flag pro Abtretung: wenn true, wird beim Definitiv-Lohnabschluss ein
-- Download-Link (kein PDF-Anhang) an die E-Mail der Behörde gesendet.
-- Token-Tabelle analog contract_share_token.
-- ──────────────────────────────────────────────────────────────────────

ALTER TABLE employee_lohn_assignment
    ADD COLUMN IF NOT EXISTS lohnausweis_an_behoerde boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS lohnausweis_share_token (
    id                          serial PRIMARY KEY,
    employee_id                 integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    behoerde_id                 integer NOT NULL REFERENCES behoerde(id) ON DELETE CASCADE,
    employee_lohn_assignment_id integer NOT NULL REFERENCES employee_lohn_assignment(id) ON DELETE CASCADE,
    payroll_periode_id          integer REFERENCES payroll_periode(id) ON DELETE SET NULL,
    year                        integer NOT NULL,
    token_hash                  text NOT NULL,
    expires_at                  timestamp without time zone NOT NULL,
    opened_at                   timestamp without time zone,
    used_at                     timestamp without time zone,
    revoked_at                  timestamp without time zone,
    created_at                  timestamp without time zone NOT NULL DEFAULT now(),
    created_by                  integer
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_lohnausweis_share_token_hash
    ON lohnausweis_share_token (token_hash);
CREATE INDEX IF NOT EXISTS ix_lohnausweis_share_token_assignment
    ON lohnausweis_share_token (employee_lohn_assignment_id);
