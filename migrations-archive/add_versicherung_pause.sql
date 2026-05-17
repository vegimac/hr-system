-- ====================================================================
-- Migration: Versicherungs-Übergabe bei langer Krankheit
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_versicherung_pause.sql
-- ====================================================================
--
-- Zweck:
--   Wenn ein Mitarbeiter länger als X Tage im Karenzjahr krank ist
--   (Default 91 Tage = ~3 Monate), wird der Lohn direkt über die
--   KTG-Versicherung abgerechnet. Der Arbeitgeber zahlt in dieser
--   Phase keinen Lohn und erstellt keine Lohnabrechnung mehr.
--
--   Der Mitarbeiter bleibt angestellt (Employment bleibt aktiv); die
--   Pause wird als Zeitraum (von/bis) am Employment erfasst. Nach
--   Rückkehr wird bis befüllt und die normale Lohnabrechnung läuft
--   wieder.
--
-- Neue Spalten:
--   company_profile.versicherungs_uebergabe_tage
--     Schwellenwert an kumulierten Karenztagen pro Karenzjahr, ab dem
--     die Übergabe an die Versicherung empfohlen wird. Default 91.
--
--   employment.versicherung_pause_von / versicherung_pause_bis
--     Aktuelle/frühere Versicherungs-Pausen. NULL = keine Pause.
--     versicherung_pause_bis NULL = Pause läuft noch.
-- ====================================================================

BEGIN;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS versicherungs_uebergabe_tage INT NOT NULL DEFAULT 91;

ALTER TABLE company_profile
    DROP CONSTRAINT IF EXISTS company_profile_uebergabe_tage_check;
ALTER TABLE company_profile
    ADD CONSTRAINT company_profile_uebergabe_tage_check
        CHECK (versicherungs_uebergabe_tage > 0 AND versicherungs_uebergabe_tage <= 730);

ALTER TABLE employment
    ADD COLUMN IF NOT EXISTS versicherung_pause_von DATE,
    ADD COLUMN IF NOT EXISTS versicherung_pause_bis DATE;

ALTER TABLE employment
    DROP CONSTRAINT IF EXISTS employment_versicherung_pause_check;
ALTER TABLE employment
    ADD CONSTRAINT employment_versicherung_pause_check
        CHECK (versicherung_pause_bis IS NULL
            OR versicherung_pause_von IS NULL
            OR versicherung_pause_bis >= versicherung_pause_von);

COMMENT ON COLUMN company_profile.versicherungs_uebergabe_tage IS
    'Max. Karenztage pro Karenzjahr bevor Übergabe an KTG-Versicherung empfohlen wird (Default 91).';
COMMENT ON COLUMN employment.versicherung_pause_von IS
    'Start der aktuellen/letzten Versicherungs-Übergabe-Pause (NULL = keine).';
COMMENT ON COLUMN employment.versicherung_pause_bis IS
    'Ende der Versicherungs-Pause (NULL = läuft noch, oder keine Pause).';

COMMIT;
