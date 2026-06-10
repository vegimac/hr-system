-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 06.06.2026 — Stufe 1a (Erweiterung):
--   13.-Monatslohn-% pro Filiale hinterlegen (L-GAV-Standard 8.33 %).
--   Engine, Importer und Arbeitsvertrags-PDF fallen darauf zurück, wenn
--   der Vertrag keinen Wert hat. Vertrags-Override (Sonderverträge) gewinnt
--   weiterhin, falls explizit gesetzt.
--
-- Lauf in TablePlus, dann ./deploy.sh
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS default_thirteenth_salary_percent numeric(5,2);

-- Backfill: alle bestehenden Filialen auf den L-GAV-Standard 8.33 setzen
UPDATE company_profile
   SET default_thirteenth_salary_percent = 8.33
 WHERE default_thirteenth_salary_percent IS NULL;

-- Sanity-Check
SELECT id, restaurant_code, company_name,
       vacation_six_weeks_from_age, default_thirteenth_salary_percent
  FROM company_profile
 ORDER BY id;
