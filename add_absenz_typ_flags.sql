-- ====================================================================
-- Migration: Absenz-Typ Flags für UTP-Auszahlung, Saldo-Reduktion,
--            und Wochenstunden-Basis (Betrieb vs. Vertrag).
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_absenz_typ_flags.sql
-- ====================================================================
--
-- Zweck:
--   Macht die bisher hart verdrahtete Logik im PayrollController
--   konfigurierbar pro AbsenzTyp:
--
--   utp_auszahlung  : bool
--       true  = UTP-Mitarbeiter erhalten die Stunden dieser Absenz als
--               Stundenlohn ausbezahlt (als wären sie gearbeitet).
--               Beispiel: NACHT_KOMP.
--       false = UTP-Mitarbeiter erhalten nichts (Default; heute die
--               generelle Business-Regel).
--
--   reduziert_saldo : string (NULL, 'NACHT_STUNDEN', 'FERIEN_TAGE')
--       Welcher Saldo wird durch diese Absenz reduziert?
--       Ersetzt die heute hart verdrahteten Sonderfälle für NACHT_KOMP
--       und FERIEN.
--
--   basis_stunden   : string ('BETRIEB' oder 'VERTRAG', Default 'BETRIEB')
--       Basis für die 1/5-/1/7-Rechnung:
--       BETRIEB = CompanyProfile.NormalWeeklyHours (z. B. 42h)
--       VERTRAG = Für MTP: Employment.GuaranteedHoursPerWeek;
--                 für andere Modelle fallback auf Betrieb.
-- ====================================================================

BEGIN;

ALTER TABLE absenz_typ
    ADD COLUMN IF NOT EXISTS utp_auszahlung  BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS reduziert_saldo VARCHAR(20),
    ADD COLUMN IF NOT EXISTS basis_stunden   VARCHAR(10) NOT NULL DEFAULT 'BETRIEB';

-- CHECK-Constraints für erlaubte Werte
ALTER TABLE absenz_typ
    DROP CONSTRAINT IF EXISTS absenz_typ_reduziert_saldo_check;
ALTER TABLE absenz_typ
    ADD CONSTRAINT absenz_typ_reduziert_saldo_check
        CHECK (reduziert_saldo IS NULL
            OR reduziert_saldo IN ('NACHT_STUNDEN', 'FERIEN_TAGE'));

ALTER TABLE absenz_typ
    DROP CONSTRAINT IF EXISTS absenz_typ_basis_stunden_check;
ALTER TABLE absenz_typ
    ADD CONSTRAINT absenz_typ_basis_stunden_check
        CHECK (basis_stunden IN ('BETRIEB', 'VERTRAG'));

COMMENT ON COLUMN absenz_typ.utp_auszahlung IS
    'true = UTP-Mitarbeiter erhalten die Stunden dieser Absenz als Stundenlohn ausbezahlt.';
COMMENT ON COLUMN absenz_typ.reduziert_saldo IS
    'Welcher Saldo wird reduziert: NACHT_STUNDEN, FERIEN_TAGE, oder NULL.';
COMMENT ON COLUMN absenz_typ.basis_stunden IS
    'Basis für 1/5- oder 1/7-Rechnung: BETRIEB (Normal-Wochenstunden) oder VERTRAG (MTP-Soll).';

-- Seed-Werte für bestehende Typen: heutiges Verhalten 1:1 erhalten.
-- NACHT_KOMP ist der Hauptfall mit UTP-Auszahlung + Saldo-Reduktion.
UPDATE absenz_typ SET
    utp_auszahlung  = TRUE,
    reduziert_saldo = 'NACHT_STUNDEN',
    basis_stunden   = 'BETRIEB'
 WHERE code = 'NACHT_KOMP';

UPDATE absenz_typ SET
    utp_auszahlung  = FALSE,
    reduziert_saldo = 'FERIEN_TAGE',
    basis_stunden   = 'BETRIEB'
 WHERE code = 'FERIEN';

-- KRANK / UNFALL: 1/5 der Wochenarbeitszeit pro Tag.
-- Basis VERTRAG → MTP nutzt GuaranteedHoursPerWeek; FIX/FIX-M fallen
-- automatisch auf Betriebs-Wochenstunden zurück (Frontend-Logik).
UPDATE absenz_typ SET
    utp_auszahlung  = FALSE,
    reduziert_saldo = NULL,
    basis_stunden   = 'VERTRAG'
 WHERE code IN ('KRANK', 'UNFALL');

-- SCHULUNG / MILITAER / FEIERTAG: Basis bleibt Betriebs-Wochenstunden.
UPDATE absenz_typ SET
    utp_auszahlung  = FALSE,
    reduziert_saldo = NULL,
    basis_stunden   = 'BETRIEB'
 WHERE code IN ('SCHULUNG', 'MILITAER', 'FEIERTAG');

-- Kontrolle (auskommentiert):
-- SELECT code, bezeichnung, zeitgutschrift, gutschrift_modus,
--        utp_auszahlung, reduziert_saldo, basis_stunden
--   FROM absenz_typ ORDER BY sort_order, code;

COMMIT;
