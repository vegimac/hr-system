-- ====================================================================
-- Migration: gutschrift_modus für FEIERTAG auf '1/7' setzen
--   psql -d <datenbank> -f update_absenz_typ_feiertag_modus.sql
-- ====================================================================
--
-- Walter, 24.04.2026:
--   Bei FIX/FIX-M muss die Feiertag-Gutschrift 1/7 vom Wochensoll sein
--   (analog FERIEN). Bisher stand gutschrift_modus='1/5', das gab
--   80%-Vertrag: 4 Feiertage × 33.6/5 = 26.88 h (falsch).
--   Mit '1/7': 4 Feiertage × 33.6/7 = 19.20 h (korrekt).
--
-- Voraussetzung: basis_stunden='VERTRAG' ist bereits gesetzt
-- (siehe update_absenz_typ_feiertag_vertrag.sql).
-- ====================================================================

BEGIN;

UPDATE absenz_typ
   SET gutschrift_modus = '1/7'
 WHERE code = 'FEIERTAG';

-- Kontrolle:
-- SELECT code, bezeichnung, gutschrift_modus, basis_stunden
--   FROM absenz_typ
--  ORDER BY sort_order, code;

COMMIT;
