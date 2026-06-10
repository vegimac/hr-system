-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 06.06.2026 — Stufe 1a:
--   Alter, ab dem 6 Wochen Ferien gelten, pro Filiale konfigurierbar.
--   L-GAV-Standard = 50 (= bisherige hardcoded Schwelle in Engine).
--   Engine prüft pro Lohnperiode: `dob.AddYears(VacationSixWeeksFromAge) <= periodTo`.
--
-- Lauf in TablePlus, dann ./deploy.sh
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS vacation_six_weeks_from_age int NOT NULL DEFAULT 50;

-- Sanity-Check
SELECT id, restaurant_code, company_name, vacation_six_weeks_from_age
  FROM company_profile
 ORDER BY id;
