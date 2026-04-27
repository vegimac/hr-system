-- ====================================================================
-- Migration: Karenz-Konfiguration pro Filiale
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_karenz_konfig.sql
-- ====================================================================
--
-- Zweck:
--   Die Krankheits-Karenz (14-Tage-Grenze für Lohnfortzahlung zu 88%)
--   wird pro Filiale konfigurierbar:
--
--   karenzjahr_basis
--       ARBEITSJAHR  → Karenzjahr beginnt am Eintrittsdatum des MA,
--                      läuft exakt 1 Jahr (13.04.2025 → 12.04.2026,
--                      dann 13.04.2026 → 12.04.2027, …). [Default]
--       KALENDERJAHR → Karenzjahr = Kalenderjahr (01.01. – 31.12.),
--                      für alle MAs gleich.
--
--   karenz_tage_max
--       Anzahl Tage innerhalb eines Karenzjahrs, in denen der
--       Arbeitgeber die erhöhte Lohnfortzahlung (z.B. 88%) bezahlt.
--       Ab Tag (max + 1) greift der reduzierte Satz (z.B. 80%).
--       Default 14 (typisch Schweizer Regelung).
-- ====================================================================

BEGIN;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS karenzjahr_basis VARCHAR(20) NOT NULL DEFAULT 'ARBEITSJAHR',
    ADD COLUMN IF NOT EXISTS karenz_tage_max  NUMERIC(5,2) NOT NULL DEFAULT 14;

ALTER TABLE company_profile
    DROP CONSTRAINT IF EXISTS company_profile_karenzjahr_basis_check;
ALTER TABLE company_profile
    ADD CONSTRAINT company_profile_karenzjahr_basis_check
        CHECK (karenzjahr_basis IN ('ARBEITSJAHR', 'KALENDERJAHR'));

ALTER TABLE company_profile
    DROP CONSTRAINT IF EXISTS company_profile_karenz_tage_max_check;
ALTER TABLE company_profile
    ADD CONSTRAINT company_profile_karenz_tage_max_check
        CHECK (karenz_tage_max >= 0 AND karenz_tage_max <= 365);

COMMENT ON COLUMN company_profile.karenzjahr_basis IS
    'Basis für das Karenzjahr: ARBEITSJAHR (ab MA-Eintritt) oder KALENDERJAHR.';
COMMENT ON COLUMN company_profile.karenz_tage_max IS
    'Max. Karenztage pro Jahr mit erhöhter Lohnfortzahlung (z.B. 14 für 88%).';

COMMIT;
