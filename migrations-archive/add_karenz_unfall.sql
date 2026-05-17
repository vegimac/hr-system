-- ====================================================================
-- Migration: Karenz-Tage Unfall pro Filiale
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_karenz_unfall.sql
-- ====================================================================
--
-- Zweck:
--   Analog zur Krankheits-Karenz (karenz_tage_max, Default 14) wird für
--   Unfall-Absenzen eine eigene Karenz-Grenze eingeführt.
--   Berechnung und Lohn-Logik sind identisch zu Krankheit — nur die
--   Anzahl Tage mit erhöhter Lohnfortzahlung (88%) ist typ. kleiner
--   (Default 2 Tage). Ab Tag (max + 1) greift der reduzierte Satz (80%).
--
--   Das Karenzjahr-Konzept (ARBEITSJAHR / KALENDERJAHR) und die
--   Versicherungs-Übergabe-Schwelle bleiben gemeinsam — nur die
--   Tage-Grenze pro Absenz-Typ ist unterschiedlich.
-- ====================================================================

BEGIN;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS karenz_tage_max_unfall NUMERIC(5,2) NOT NULL DEFAULT 2;

ALTER TABLE company_profile
    DROP CONSTRAINT IF EXISTS company_profile_karenz_tage_max_unfall_check;
ALTER TABLE company_profile
    ADD CONSTRAINT company_profile_karenz_tage_max_unfall_check
        CHECK (karenz_tage_max_unfall >= 0 AND karenz_tage_max_unfall <= 365);

COMMENT ON COLUMN company_profile.karenz_tage_max_unfall IS
    'Max. Karenztage Unfall pro Jahr mit erhöhter Lohnfortzahlung (Default 2). Analog zu karenz_tage_max für Krankheit.';

COMMIT;
