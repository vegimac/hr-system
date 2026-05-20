ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS akonto_prozent_fix_m numeric(5,2) NOT NULL DEFAULT 90.00;