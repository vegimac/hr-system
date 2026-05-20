-- Walter-Vorgabe 18.05.2026: separater Akonto-Prozentsatz für FIX-M (Manager).
-- Bisher haben FIX und FIX-M denselben Wert verwendet (akonto_prozent_fix,
-- Default 80 %). Manager mit hohem, sehr planbarem Festlohn rechtfertigen
-- ein höheres Akonto — neue Spalte akonto_prozent_fix_m, Default 90 %.

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS akonto_prozent_fix_m numeric(5,2) NOT NULL DEFAULT 90.00;

-- Sanity: gleiche Validierung wie für die Schwester-Spalte (0..100).
ALTER TABLE company_profile
    DROP CONSTRAINT IF EXISTS company_profile_akonto_prozent_fix_m_check;
ALTER TABLE company_profile
    ADD  CONSTRAINT company_profile_akonto_prozent_fix_m_check
         CHECK (akonto_prozent_fix_m >= 0 AND akonto_prozent_fix_m <= 100);
