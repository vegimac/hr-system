-- ====================================================================
-- Migration: Kanton-Code auf Filiale (CompanyProfile)
-- Ausführen mit:
--   psql -d <datenbank> -f add_company_profile_kanton_code.sql
-- ====================================================================
--
-- Zweck:
--   Standort-Kanton der Filiale als 2-Zeichen-Code (LU, AG, BE, ZH, ...).
--   Wird im Lohnlauf für die Familienzulagen-Berechnung verwendet —
--   im Gegensatz zur Quellensteuer (wohnortsbasiert) ist die
--   Familienzulage IMMER nach Standort des Betriebs zu berechnen
--   (Sitz der zuständigen FAK / Familienausgleichskasse).
--
--   Beispiele:
--     LU  → FAK Luzern  (z.B. KZ 215.00, AZ 268.00)
--     AG  → FAK Aargau  (z.B. KZ 200.00, AZ 250.00)
--     BE  → FAK Bern    (z.B. KZ 230.00, AZ 290.00)
--
--   Dropdown im Filial-Edit-Modal füllt das Feld; PLZ-Lookup kann
--   einen Vorschlag liefern, Admin bestätigt manuell.
-- ====================================================================

BEGIN;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS kanton_code VARCHAR(2);

COMMENT ON COLUMN company_profile.kanton_code IS
    'Standort-Kanton der Filiale (2-Zeichen-Code). Massgeblich für Familienzulagen-Berechnung (FAK). NULL = nicht gepflegt.';

-- Vorschlag: bestehende Filialen mit LU vorbelegen, da Schaub Restaurants
-- mehrheitlich in Luzern ist. Walter darf das pro Filiale anpassen.
-- (Bewusst KEIN UPDATE — nicht ungewollt überschreiben falls bereits gesetzt.)

COMMIT;
