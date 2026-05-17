-- ====================================================================
-- Rollback: Versicherungs-Übergabe-Logik entfernen
-- Ausführen mit:
--   psql -d hr_system -U postgres -f drop_versicherungs_uebergabe.sql
-- ====================================================================
--
-- Zweck:
--   Die automatische "Übergabe an KTG-Versicherung nach N Karenztagen"
--   war fachlich nicht korrekt (BVG bleibt während Lohnfortzahlung auf
--   dem bisherigen Lohn, unabhängig von einer festen Tages-Schwelle).
--   Die Logik wird komplett entfernt — stattdessen wird die
--   Kündigungs-Sperrfrist nach Art. 336c OR ausgewiesen.
--
-- Entfernt:
--   company_profile.versicherungs_uebergabe_tage
--   employment.versicherung_pause_von
--   employment.versicherung_pause_bis
-- ====================================================================

BEGIN;

ALTER TABLE employment
    DROP CONSTRAINT IF EXISTS employment_versicherung_pause_check;

ALTER TABLE employment
    DROP COLUMN IF EXISTS versicherung_pause_von,
    DROP COLUMN IF EXISTS versicherung_pause_bis;

ALTER TABLE company_profile
    DROP CONSTRAINT IF EXISTS company_profile_uebergabe_tage_check;

ALTER TABLE company_profile
    DROP COLUMN IF EXISTS versicherungs_uebergabe_tage;

COMMIT;
