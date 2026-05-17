-- ====================================================================
-- Migration: Akonto-Lohn — Datenmodell (Phase 1)
-- ====================================================================
--
-- Zweck:
--   Grundlage für das Akonto-Lohn-Modell (siehe AKONTO-LOHN-PLAN.md).
--   Die Lohnperiode bleibt der Kalendermonat (1.-Letzter); rund eine
--   Woche vor Monatsende fliesst eine geschätzte Netto-Vorauszahlung
--   (Akonto). Der Definitivlauf am Monatsende zieht das Akonto ab
--   → Restzahlung.
--
--   Neue Tabellen:
--     - akonto_termin   : Akonto-Auszahlungsdatum pro Filiale/Jahr/Monat
--     - akonto_zahlung  : berechnetes Akonto pro MA und Lohnperiode
--   Neue Spalten:
--     - payroll_snapshot.akonto_bereits_ausbezahlt
--     - company_profile.akonto_prozent_fix  (Default 80)
--
--   Idempotent — kann mehrfach ausgeführt werden.
-- ====================================================================

BEGIN;

-- 1) akonto_termin — Akonto-Auszahlungsdatum pro Filiale/Jahr/Monat ----
CREATE TABLE IF NOT EXISTS akonto_termin (
    id                  SERIAL       PRIMARY KEY,
    company_profile_id  INT          NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
    year                INT          NOT NULL,
    month               INT          NOT NULL,
    payout_date         DATE         NOT NULL,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT akonto_termin_month_check CHECK (month BETWEEN 1 AND 12)
);

COMMENT ON TABLE akonto_termin IS
    'Akonto-Auszahlungsdatum pro Filiale, Jahr und Monat. Wegen Wochenenden/Feiertagen kein fixer Tag — pro Monat einzeln hinterlegt.';

CREATE UNIQUE INDEX IF NOT EXISTS "UX_akonto_termin_branch_year_month"
    ON akonto_termin (company_profile_id, year, month);

-- 2) akonto_zahlung — berechnetes Akonto pro MA und Lohnperiode --------
CREATE TABLE IF NOT EXISTS akonto_zahlung (
    id                   SERIAL        PRIMARY KEY,
    employee_id          INT           NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    company_profile_id   INT           NOT NULL REFERENCES company_profile(id),
    period_year          INT           NOT NULL,
    period_month         INT           NOT NULL,
    payout_date          DATE          NOT NULL,
    geschaetzter_brutto  NUMERIC(10,2) NOT NULL DEFAULT 0,
    feriengeld_anteil    NUMERIC(10,2) NOT NULL DEFAULT 0,
    geschaetzte_abzuege  NUMERIC(10,2) NOT NULL DEFAULT 0,
    pfaendung_abzug      NUMERIC(10,2) NOT NULL DEFAULT 0,
    netto_akonto         NUMERIC(10,2) NOT NULL DEFAULT 0,
    status               VARCHAR(20)   NOT NULL DEFAULT 'BERECHNET',
    dta_run_id           INT,                       -- Verweis auf DTA-Zahllauf, in Phase 3 verdrahtet
    created_at           TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT akonto_zahlung_month_check  CHECK (period_month BETWEEN 1 AND 12),
    CONSTRAINT akonto_zahlung_status_check CHECK (status IN ('BERECHNET', 'AUSBEZAHLT', 'STORNIERT'))
);

COMMENT ON TABLE akonto_zahlung IS
    'Berechnetes Akonto pro Mitarbeiter und Lohnperiode. Keine echte Lohnabrechnung — keine SV-/BVG-/QST-Buchung. Der Definitivlauf zieht netto_akonto via payroll_snapshot.akonto_bereits_ausbezahlt ab.';

CREATE INDEX IF NOT EXISTS "idx_akonto_zahlung_branch_period"
    ON akonto_zahlung (company_profile_id, period_year, period_month);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_akonto_zahlung_emp_period"
    ON akonto_zahlung (employee_id, period_year, period_month);

-- 3) payroll_snapshot — bereits per Akonto ausbezahlter Betrag ---------
ALTER TABLE payroll_snapshot
    ADD COLUMN IF NOT EXISTS akonto_bereits_ausbezahlt NUMERIC(10,2) NOT NULL DEFAULT 0;

COMMENT ON COLUMN payroll_snapshot.akonto_bereits_ausbezahlt IS
    'Bereits per Akonto ausbezahlter Betrag dieser Periode. Definitivlauf: Restzahlung = berechneter Netto - akonto_bereits_ausbezahlt. 0 = kein Akonto.';

-- 4) company_profile — Akonto-Prozentsatz für FIX/FIX-M ---------------
ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS akonto_prozent_fix NUMERIC(5,2) NOT NULL DEFAULT 80;

COMMENT ON COLUMN company_profile.akonto_prozent_fix IS
    'Akonto-Prozentsatz für FIX/FIX-M (Default 80). UTP/MTP nutzen 100% des voraussichtlichen Netto aus gestempelten Stunden + Feriengeld.';

COMMIT;
