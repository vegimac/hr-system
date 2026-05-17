-- ====================================================================
-- Fix: Basis-Flags korrekt setzen (Walter-Regel präzisiert 25.04.2026)
--   psql -d <datenbank> -f fix_lohnposition_basis_flags.sql
-- ====================================================================
--
-- Walter: "die basis für feiertag-gutschrift und für ferien-gutschrift
--          ist DER LOHN. nicht der lohn + feiertage und darauf noch ferien"
--
-- Konkret:
--   Feiertag-Basis = nur Lohn (Stundenlohn UTP / Festlohn FIX)
--   Ferien-Basis   = nur Lohn (gleich)
--   13.-ML-Basis   = Lohn + Ferien + Feiertag (alle Auszahlungen)
--
-- Das verhindert "Doppel-Aufschlag": Feiertag wird nicht in Ferien-Basis
-- gerechnet, Ferien nicht in Feiertag-Basis. Nur 13.-ML kumuliert alles.
--
-- Korrigierte Flag-Belegung:
--   Code | feiertag | ferien | 13ml
--   10 Festlohn               | true  | true  | true
--   2  Festlohn Ferien        | false | false | true   (Auszahlung — kein Rekursionsbeitrag)
--   3  Festlohn Feiertag      | false | false | true
--   4  Zusatzstunden (MTP)    | true  | true  | true
--   20 Stundenlohn            | true  | true  | true
--   22 Stundenlohn Ferien     | false | false | true
--   50 Ausbezahlte Feiertage  | false | false | true
--   60 Unfall (Karenz)        | false | false | false  (Versicherungsleistung)
--   65 Korrektur Unfall       | false | false | true   (negativer Lohn — reduziert Basis)
--   70 Krankheit (Karenz)     | false | false | false
--   75 Korrektur Krankheit    | false | false | true
-- ====================================================================

BEGIN;

-- Lohn-Positionen: zählen für alle 3 Basen
UPDATE lohnposition SET
    zaehlt_als_basis_feiertag = true,
    zaehlt_als_basis_ferien   = true,
    zaehlt_als_basis_13ml     = true
 WHERE code IN ('10','4','20');

-- Auszahlungen für Ferien/Feiertag: nur 13.ML (kein Rekursionsbeitrag)
UPDATE lohnposition SET
    zaehlt_als_basis_feiertag = false,
    zaehlt_als_basis_ferien   = false,
    zaehlt_als_basis_13ml     = true
 WHERE code IN ('2','3','22','50','195.3');

-- Korrektur-Lohnkürzungen (negativer Lohn): reduzieren 13.ML-Basis
UPDATE lohnposition SET
    zaehlt_als_basis_feiertag = false,
    zaehlt_als_basis_ferien   = false,
    zaehlt_als_basis_13ml     = true
 WHERE code IN ('65','75');

-- Karenzentschädigungen (Versicherungsleistung): zählen nirgends
UPDATE lohnposition SET
    zaehlt_als_basis_feiertag = false,
    zaehlt_als_basis_ferien   = false,
    zaehlt_als_basis_13ml     = false
 WHERE code IN ('60','70');

-- 13. ML selbst: nicht zur 13.ML-Basis (Rekursion)
UPDATE lohnposition SET
    zaehlt_als_basis_feiertag = false,
    zaehlt_als_basis_ferien   = false,
    zaehlt_als_basis_13ml     = false
 WHERE code = '180';

-- Verifikation
SELECT code, bezeichnung,
       zaehlt_als_basis_feiertag AS fei,
       zaehlt_als_basis_ferien   AS fer,
       zaehlt_als_basis_13ml     AS ml
  FROM lohnposition
 WHERE code IN ('10','2','3','4','20','22','50','60','65','70','75','180','195.3')
 ORDER BY sort_order, code;

COMMIT;
