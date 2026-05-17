-- ====================================================================
-- Migration: Behörden-Stammdaten + Mitarbeiter-Lohnabtretungen
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_behoerde_lohnabtretung.sql
-- ====================================================================
--
-- Zweck:
--   Lohnpfändungen und andere Lohn-Abtretungen (z. B. Sozialamt-Vorschuss)
--   werden zentral pro Mitarbeiter mit Gültigkeitszeitraum, Existenz-
--   Minimum und optionalem Zielbetrag erfasst. Empfänger (Betreibungs-
--   ämter, Sozialämter) werden einmalig als Stammdaten geführt und
--   mehrfach referenziert.
-- ====================================================================

BEGIN;

-- 1) behoerde – Ämter-Stammdaten ------------------------------------
CREATE TABLE IF NOT EXISTS behoerde (
    id            SERIAL      PRIMARY KEY,
    name          VARCHAR(200) NOT NULL,
    typ           VARCHAR(30)  NOT NULL DEFAULT 'BETREIBUNGSAMT',
    adresse1      VARCHAR(200),
    adresse2      VARCHAR(200),
    adresse3      VARCHAR(200),
    plz           VARCHAR(10),
    ort           VARCHAR(100),
    telefon       VARCHAR(30),
    email         VARCHAR(200),
    iban          VARCHAR(34),
    qr_iban       VARCHAR(34),
    bic           VARCHAR(20),
    bank_name     VARCHAR(100),
    is_active     BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT behoerde_typ_check
        CHECK (typ IN ('BETREIBUNGSAMT', 'SOZIALAMT', 'ANDERE'))
);

COMMENT ON TABLE behoerde IS
    'Stammdaten von Behörden (Betreibungsämter, Sozialämter) als Empfänger von Lohnabtretungen.';

-- 1a) Idempotenter Upgrade-Pfad: falls Tabelle schon mit "strasse" existiert,
--     umbenennen und fehlende Adresszeilen ergänzen. Safe bei wiederholtem Run.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'behoerde' AND column_name = 'strasse')
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_name = 'behoerde' AND column_name = 'adresse1') THEN
        ALTER TABLE behoerde RENAME COLUMN strasse TO adresse1;
    END IF;
END $$;

ALTER TABLE behoerde ADD COLUMN IF NOT EXISTS adresse1 VARCHAR(200);
ALTER TABLE behoerde ADD COLUMN IF NOT EXISTS adresse2 VARCHAR(200);
ALTER TABLE behoerde ADD COLUMN IF NOT EXISTS adresse3 VARCHAR(200);

-- 2) employee_lohn_assignment – Zuweisungen pro MA ------------------
CREATE TABLE IF NOT EXISTS employee_lohn_assignment (
    id                 SERIAL      PRIMARY KEY,
    employee_id        INT         NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    behoerde_id        INT         NOT NULL REFERENCES behoerde(id),
    bezeichnung        VARCHAR(100) NOT NULL,   -- z.B. "Lohnpfändung", "Vorschuss Sozialamt"
    freigrenze         NUMERIC(10,2) NOT NULL DEFAULT 0,   -- Existenz-Minimum, das dem MA bleibt
    zielbetrag         NUMERIC(10,2) NOT NULL DEFAULT 0,   -- 0 = unbegrenzt
    bereits_abgezogen  NUMERIC(10,2) NOT NULL DEFAULT 0,   -- wird beim Lohn-Bestätigen hochgezählt
    valid_from         DATE        NOT NULL,
    valid_to           DATE,                               -- NULL = offen bis Widerruf
    referenz_amt       VARCHAR(100),                       -- z.B. "Pfändungsgruppe Nr. 22520697"
    zahlungs_referenz  VARCHAR(50),                        -- QR-Ref 27-stellig oder IBAN-Ref
    bemerkung          TEXT,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT employee_lohn_assignment_valid_range
        CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CONSTRAINT employee_lohn_assignment_amounts_check
        CHECK (freigrenze >= 0 AND zielbetrag >= 0 AND bereits_abgezogen >= 0)
);

CREATE INDEX IF NOT EXISTS idx_employee_lohn_assignment_employee
    ON employee_lohn_assignment(employee_id);

CREATE INDEX IF NOT EXISTS idx_employee_lohn_assignment_period
    ON employee_lohn_assignment(employee_id, valid_from, valid_to);

COMMENT ON TABLE employee_lohn_assignment IS
    'Lohnabtretungen pro Mitarbeiter (Lohnpfändung, Sozialamt-Vorschuss). Fliesst in jeden Lohnlauf im Gültigkeitszeitraum: Betrag = max(0, Netto - Freigrenze), gedeckelt auf Zielbetrag - bereits_abgezogen falls Zielbetrag > 0.';

COMMIT;
