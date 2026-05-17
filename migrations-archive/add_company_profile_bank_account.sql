-- ─────────────────────────────────────────────────────────────────────────
-- Filial-Bankverbindungen mit Historie (analog zu employee_bank_account)
-- ─────────────────────────────────────────────────────────────────────────
-- Pro Filiale können mehrere Bankverbindungen geführt werden, mit
-- Gültigkeitszeitraum (ValidFrom/ValidTo). Bei Bankenwechsel: alter Eintrag
-- bekommt ein ValidTo, neuer Eintrag startet ab Wechseldatum.
--
-- Beim Lohnlauf-DTA wird der Eintrag verwendet, der in der Lohnperiode
-- gültig ist und IsMain=true hat (Hauptbank).
--
-- Migration: bestehende Werte aus company_profile.iban/bic/bank_name werden
-- als initialer Eintrag mit valid_from = '2025-01-01' (Mirus-Stichtag)
-- übernommen, falls iban gesetzt ist. Die alten Spalten bleiben für
-- Backward-Compat in der DB stehen (UI nutzt sie nicht mehr).
-- ─────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS company_profile_bank_account (
    id                  SERIAL PRIMARY KEY,
    company_profile_id  INTEGER NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
    iban                VARCHAR(34) NOT NULL,
    bic                 VARCHAR(15),
    bank_name           VARCHAR(200),
    is_main             BOOLEAN NOT NULL DEFAULT TRUE,
    bemerkung           TEXT,
    valid_from          DATE NOT NULL,
    valid_to            DATE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_cpba_period
    ON company_profile_bank_account (company_profile_id, valid_from, valid_to);

-- Idempotente Daten-Migration: bestehende iban/bic/bank_name aus
-- company_profile übernehmen, sofern die Filiale noch keinen Eintrag hat.
INSERT INTO company_profile_bank_account (
    company_profile_id, iban, bic, bank_name, is_main, valid_from, created_at, updated_at
)
SELECT cp.id, cp.iban, cp.bic, cp.bank_name, TRUE, '2025-01-01'::date, NOW(), NOW()
FROM company_profile cp
WHERE cp.iban IS NOT NULL
  AND cp.iban <> ''
  AND NOT EXISTS (
      SELECT 1 FROM company_profile_bank_account cpba
      WHERE cpba.company_profile_id = cp.id
  );
