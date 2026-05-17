-- ====================================================================
-- Migration: Basis-Flags für lohnposition
-- Ausführen mit: psql -d <datenbank> -f add_lohnposition_basis_flags.sql
-- ====================================================================
--
-- Zweck:
--   Pro Lohnart konfigurierbar machen, ob sie als Bemessungsgrundlage
--   für Feiertagsentschädigung, Ferienentschädigung und 13. Monatslohn
--   zählt. Ersetzt die heute im PayrollController hart verdrahtete Logik
--   (z. B. "feiertagBasis = festlohn + mtpBasis").
--
--   Die Defaults sind so gewählt, dass das aktuelle Verhalten 1:1
--   abgebildet wird — der PayrollController kann später schrittweise
--   auf diese Flags umgestellt werden, ohne dass sich Berechnungen
--   ändern.
-- ====================================================================

BEGIN;

-- 1. Spalten anlegen (idempotent) ------------------------------------
ALTER TABLE lohnposition
    ADD COLUMN IF NOT EXISTS zaehlt_als_basis_feiertag BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS zaehlt_als_basis_ferien   BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS zaehlt_als_basis_13ml     BOOLEAN NOT NULL DEFAULT FALSE;

COMMENT ON COLUMN lohnposition.zaehlt_als_basis_feiertag IS
    'TRUE = dieser Betrag zaehlt zur Bemessungsgrundlage der Feiertagsentschaedigung';
COMMENT ON COLUMN lohnposition.zaehlt_als_basis_ferien IS
    'TRUE = dieser Betrag zaehlt zur Bemessungsgrundlage der Ferienentschaedigung';
COMMENT ON COLUMN lohnposition.zaehlt_als_basis_13ml IS
    'TRUE = dieser Betrag zaehlt zur Bemessungsgrundlage des 13. Monatslohns';

-- 2. Sinnvolle Defaults pro Code setzen ------------------------------
-- Annahme: die Codes folgen dem bisherigen Schema (10.x Festlohn,
-- 20.x Stundenlohn, 55.x Überstunden, 60.x/70.x Taggelder, 180.x 13. ML,
-- 190.x Familienzulagen, 195.x Ferienentschädigung, 200.x Bonus/Spesen,
-- 900.x Abzüge).

-- 10.1 Festlohn                 → MTP: Basis für Feiertag und 13. ML
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = TRUE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '10.1';

-- 10.2 Festlohn Ferien          → nur 13. ML
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '10.2';

-- 10.3 Festlohn Feiertage       → nur 13. ML
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '10.3';

-- 10.4 Zusatzstunden (MTP)      → Basis für Feiertag, Ferien und 13. ML
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = TRUE,
       zaehlt_als_basis_ferien   = TRUE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '10.4';

-- 20.1 Stundenlohn (UTP)        → Basis für Feiertag, Ferien und 13. ML
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = TRUE,
       zaehlt_als_basis_ferien   = TRUE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '20.1';

-- 20.2 Stundenlohn Ferien       → nur 13. ML
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '20.2';

-- 20.3 Stundenlohn Feiertage    → UTP-Kaskade: zählt als Ferienbasis
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = TRUE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '20.3';

-- 55.x Überstunden              → zählen zur 13.-ML-Basis, aktuell
--                                 nicht zur Feiertags-/Ferienbasis.
--                                 Falls anders gewünscht, einzeln anpassen.
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE kategorie = 'Überstunden';

-- 60.1 / 70.1 Karenzentschädigungen → zur 13.-ML-Basis (SV-pflichtig)
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code IN ('60.1', '70.1');

-- 60.3 UVG / 70.2 KTG Taggelder → keine Basis (nicht AHV-pflichtig,
--                                 nicht 13.-ML-pflichtig)
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = FALSE
 WHERE code IN ('60.3', '70.2');

-- 180.1 13. Monatslohn          → zählt NICHT zu seiner eigenen Basis
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = FALSE
 WHERE code = '180.1';

-- 190.x Familienzulagen         → keine Basis
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = FALSE
 WHERE kategorie = 'Familienzulagen';

-- 195.x Ferienentschädigung     → keine Basis (verhindert Rekursion)
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = FALSE
 WHERE kategorie = 'Ferienentsch.';

-- 200.5 McBonus (Bonus)         → Basis für 13. ML (SV-pflichtig, 13.-ML-Splitting aktiv)
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = TRUE
 WHERE code = '200.5';

-- 200.1 Pauschalspesen          → keine Basis (nicht SV-pflichtig)
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = FALSE
 WHERE code = '200.1';

-- 900.1 Quellensteuer / alle Abzüge → keine Basis
UPDATE lohnposition
   SET zaehlt_als_basis_feiertag = FALSE,
       zaehlt_als_basis_ferien   = FALSE,
       zaehlt_als_basis_13ml     = FALSE
 WHERE typ = 'ABZUG';

-- 3. Kontrolle ------------------------------------------------------
-- Zum Prüfen nach der Migration:
-- SELECT code, bezeichnung, kategorie,
--        zaehlt_als_basis_feiertag AS f,
--        zaehlt_als_basis_ferien   AS v,
--        zaehlt_als_basis_13ml     AS ml13
--   FROM lohnposition
--  ORDER BY sort_order, code;

COMMIT;
