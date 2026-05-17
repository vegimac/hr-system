-- ====================================================================
-- Migration: BVG-Wartefrist (3 Monate auf 100%-Lohn) pro Filiale
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_bvg_wartefrist.sql
-- ====================================================================
--
-- Zweck:
--   Während einer Arbeitsunfähigkeit durch Krankheit oder Unfall bleibt
--   die BVG-Basis auf dem bisherigen Lohn (100%) während einer
--   Wartefrist von 3 Monaten — auch wenn effektiv nur 88% oder 80%
--   ausbezahlt werden (Karenz- bzw. Versicherungstaggeld).
--
--   Regel gemäss GastroSocial (Merkblatt "Arbeitsunfähigkeit durch
--   Krankheit oder Unfall", 2025):
--     • Wartefrist wird in Kalendermonaten gezählt
--     • AU-Beginn zwischen 1. und 15. des Monats
--         → Wartefrist startet am 1. DIESES Monats
--     • AU-Beginn ab dem 16. des Monats
--         → Wartefrist startet am 1. des FOLGEMONATS
--     • Wartefrist läuft exakt 3 volle Kalendermonate
--     • Jede neue Arbeitsunfähigkeit startet eine neue Wartefrist
--     • Krankheit und Unfall werden SEPARAT gezählt (2 verschiedene
--       Versicherungen)
--
--   Der Parameter ist konfigurierbar, falls der Betrieb an eine andere
--   Pensionskasse mit abweichender Wartefrist gebunden ist (Default 3).
-- ====================================================================

BEGIN;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS bvg_wartefrist_monate INT NOT NULL DEFAULT 3;

ALTER TABLE company_profile
    DROP CONSTRAINT IF EXISTS company_profile_bvg_wartefrist_check;
ALTER TABLE company_profile
    ADD CONSTRAINT company_profile_bvg_wartefrist_check
        CHECK (bvg_wartefrist_monate >= 0 AND bvg_wartefrist_monate <= 24);

COMMENT ON COLUMN company_profile.bvg_wartefrist_monate IS
    'Dauer der BVG-Wartefrist in Kalendermonaten während der BVG auf 100%-Lohn läuft (Default 3 gemäss GastroSocial).';

COMMIT;
