-- ====================================================================
-- Migration: basis_stunden für FEIERTAG auf 'VERTRAG' setzen
--   psql -d <datenbank> -f update_absenz_typ_feiertag_vertrag.sql
-- ====================================================================
--
-- Grund:
--   Bei FIX/FIX-M muss die Feiertag-Gutschrift 1/7 vom Wochensoll sein
--   (d.h. pensum-adjustiert — 80%-Vertrag: 1/7 × 33.6 h = 4.8 h pro Tag),
--   nicht 1/7 von der Betriebs-Wochenarbeitszeit (42 h / 7 = 6 h).
--
--   Mit basis_stunden='VERTRAG' und dem erweiterten Frontend-Code
--   (calcAbsHoursPreview) wird für FIX/FIX-M die Wochensoll aus
--   Employment.WeeklyHours bzw. CompanyProfile.NormalWeeklyHours × Pensum/100
--   berechnet. MTP nutzt weiterhin GuaranteedHoursPerWeek.
--
-- Gleiche Logik war früher schon für FERIEN, KRANK und UNFALL eingespielt
-- worden (siehe update_absenz_typ_ferien_vertrag.sql).
-- ====================================================================

BEGIN;

UPDATE absenz_typ
   SET basis_stunden = 'VERTRAG'
 WHERE code = 'FEIERTAG';

-- Kontrolle:
-- SELECT code, bezeichnung, basis_stunden, gutschrift_modus, reduziert_saldo
--   FROM absenz_typ
--  ORDER BY sort_order, code;

COMMIT;
