-- Sachbearbeiter-Stamm pro Behörde (Walter 02.08.2026)
-- Zahlung bleibt an behoerde (IBAN); Lohnmeldung an gewählten SB.
-- In TablePlus ausführen (kein psql-Wrapper). Idempotent.

CREATE TABLE IF NOT EXISTS behoerde_sachbearbeiter (
    id              SERIAL PRIMARY KEY,
    behoerde_id     INTEGER      NOT NULL REFERENCES behoerde(id) ON DELETE CASCADE,
    name            VARCHAR(150) NOT NULL,
    rolle           VARCHAR(100),
    telefon         VARCHAR(30),
    handy           VARCHAR(30),
    email           VARCHAR(200),
    erreichbarkeit  VARCHAR(150),
    bemerkung       TEXT,
    is_active       BOOLEAN      NOT NULL DEFAULT true,
    created_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_behoerde_sachbearbeiter_behoerde
    ON behoerde_sachbearbeiter(behoerde_id);

ALTER TABLE employee_lohn_assignment
    ADD COLUMN IF NOT EXISTS behoerde_sachbearbeiter_id INTEGER
    REFERENCES behoerde_sachbearbeiter(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_emp_lohn_assignment_sb
    ON employee_lohn_assignment(behoerde_sachbearbeiter_id);
