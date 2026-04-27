-- ====================================================================
-- Migration: basis_stunden für FERIEN auf 'VERTRAG' setzen
-- Ausführen mit:
--   psql -d <datenbank> -f update_absenz_typ_ferien_vertrag.sql
-- ====================================================================
--
-- Grund:
--   Bei MTP-Verträgen müssen Ferien-Stunden auf Basis der vertraglich
--   garantierten Wochenstunden (Employment.GuaranteedHoursPerWeek)
--   berechnet werden, NICHT auf Basis der betrieblichen Wochenarbeitszeit
--   (CompanyProfile.NormalWeeklyHours).
--
--   Beispiel MTP mit 33 h/Woche garantiert, Ferien 21 Tage:
--     heute (BETRIEB):  21 × 42 ÷ 7 = 126 h  (zu hoch)
--     künftig (VERTRAG): 21 × 33 ÷ 7 =  99 h  (korrekt)
--
--   Für FIX / FIX-M / UTP bleibt BETRIEB als Fallback — die Frontend-
--   Logik (calcAbsHoursPreview) nimmt VERTRAG nur bei empModel === 'MTP'.
--
-- Siehe auch update_absenz_typ_krank_unfall_vertrag.sql (gleiche Logik
-- wurde früher schon für KRANK und UNFALL eingespielt).
-- ====================================================================

BEGIN;

UPDATE absenz_typ
   SET basis_stunden = 'VERTRAG'
 WHERE code = 'FERIEN';

-- Kontrolle:
-- SELECT code, bezeichnung, basis_stunden, gutschrift_modus, reduziert_saldo
--   FROM absenz_typ
--  ORDER BY sort_order, code;

COMMIT;
