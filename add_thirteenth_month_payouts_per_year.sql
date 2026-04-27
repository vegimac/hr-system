-- ====================================================================
-- Migration: 13.-Monatslohn-Auszahlungsrhythmus pro Firma
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_thirteenth_month_payouts_per_year.sql
-- ====================================================================
--
-- Zweck:
--   Steuert wie oft pro Jahr der angesammelte 13.-ML-Saldo ausbezahlt
--   wird — pro Firma konfigurierbar.
--   12 = monatlich (Default; entspricht heutigem Verhalten)
--    4 = vierteljährlich (Auszahlung in den Monaten 3, 6, 9, 12)
--    2 = halbjährlich  (Auszahlung in den Monaten 6, 12)
--
--   Gilt für die Modelle FIX, FIX-M und MTP.
--   UTP wird unabhängig von dieser Einstellung immer monatlich ausbezahlt.
-- ====================================================================

BEGIN;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS thirteenth_month_payouts_per_year INT NOT NULL DEFAULT 12
        CHECK (thirteenth_month_payouts_per_year IN (12, 4, 2));

COMMENT ON COLUMN company_profile.thirteenth_month_payouts_per_year IS
    'Anzahl 13.-ML-Auszahlungen pro Jahr (12 = monatlich, 4 = quartalsweise [Mt 3/6/9/12], 2 = halbjährlich [Mt 6/12]). Wirkt fuer FIX/FIX-M/MTP — UTP immer monatlich.';

COMMIT;
