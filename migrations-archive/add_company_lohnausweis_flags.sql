-- ====================================================================
-- Migration: Lohnausweis Box F + Box G Defaults pro Filiale
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_company_lohnausweis_flags.sql
-- ====================================================================
--
-- Walter (12.05.2026): Form 11 dfe (ESTV Lohnausweis) hat zwei Boxen
-- die für ALLE MA einer Filiale gleich sind:
--   Box F: Unentgeltliche Beförderung Wohn- → Arbeitsort
--   Box G: Kantinenverpflegung / Lunch-Checks kostenlos
--
-- Bei McDonald's Schaub: F = false (kein Werks-Bus), G = false (Crew
-- zahlt 50%-Anteil → keine unentgeltliche Verpflegung).
-- ====================================================================

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS lohnausweis_box_f_freier_transport boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS lohnausweis_box_g_kantine_gratis    boolean NOT NULL DEFAULT false;

-- Optional: Verpflegungs-Geldwert pro Monat (Pos. 2.1 Lohnausweis).
-- Bei McDonald's Standard 0 (50%-Anteil neutralisiert die ESTV-Pauschale).
-- Falls ein Standort doch Restwert aufweist, hier eintragen.
ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS lohnausweis_pos_2_1_verpflegung_monat numeric(10,2) NULL;

SELECT id, restaurant_code, branch_name,
       lohnausweis_box_f_freier_transport,
       lohnausweis_box_g_kantine_gratis,
       lohnausweis_pos_2_1_verpflegung_monat
FROM company_profile
ORDER BY restaurant_code;
