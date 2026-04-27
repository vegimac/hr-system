-- ====================================================================
-- Migration: auto_ferien_geld_auszahlung_dezember am company_profile
-- Ausführen mit:
--   psql -d <datenbank> -f add_auto_ferien_geld_dezember.sql
-- ====================================================================
--
-- Zweck:
--   Pro Filiale konfigurierbar, ob im Dezember-Lohnlauf bei UTP- und
--   MTP-Mitarbeitenden das gesamte aktuelle Ferien-Geld-Saldo automatisch
--   als Lohnposition 195.3 ausbezahlt werden soll.
--
--   Default: true (analog zur Jahresend-Auszahlung des 13. Monatslohns).
--   Wer das nicht möchte (z.B. abweichende Vereinbarung), kann das Flag
--   pro Filiale im Admin → Filialen ausschalten.
--
--   Bei Austritt mid-year bleibt es bei der manuellen Auszahlung über
--   eine 195.3-Zulage — der Automat greift nur im Dezember.
-- ====================================================================

BEGIN;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS auto_ferien_geld_auszahlung_dezember
        BOOLEAN NOT NULL DEFAULT TRUE;

COMMENT ON COLUMN company_profile.auto_ferien_geld_auszahlung_dezember IS
    'Wenn TRUE: im Dezember wird bei UTP/MTP das gesamte Ferien-Geld-Saldo automatisch via Lohnposition 195.3 ausbezahlt.';

COMMIT;
