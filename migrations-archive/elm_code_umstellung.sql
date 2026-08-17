-- ============================================================================
-- ELM-Code-Umstellung (Walter-Entscheid 17.08.2026, Testmodus — keine
-- Rücksicht auf Historie nötig). Codes der Lohnpositionen auf den
-- ELM-Standard; Bezeichnungen bleiben unverändert (Lohnzettel-Texte gleich).
-- ZUSAMMEN mit dem Deploy «ELM-Code-Umstellung» ausführen (Engine bucht ab
-- dann auf die neuen Codes). Reihenfolge der UPDATEs ist WICHTIG
-- (Nummern-Shifts 60.2→60.3 vor 60→60.2).
-- Ausführung: TablePlus, reiner Copy-Paste-Block.
-- ============================================================================

-- 1) Fehlende ELM-Positionen aus dem Raster-Archiv anlegen
--    (195.4/195.5/195.6 Feiertag-/Ferienentschädigung MTP, 55.1/55.2 Überstunden).
--    QST-Pflicht = AHV-Pflicht (Raster-Attribut «qstpfl» ist leer — bekannter Fall).
INSERT INTO lohnposition
    (code, bezeichnung, kategorie, typ,
     ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig,
     qst_pflichtig, zaehlt_als_basis_13ml, sort_order, is_active)
SELECT r.code, r.bezeichnung, COALESCE(r.gruppe, ''), 'ZULAGE',
       COALESCE(r.ahv, true), COALESCE(r.uvg, true), COALESCE(r.ktg, true), COALESCE(r.bvg, true),
       COALESCE(r.ahv, true), COALESCE(r.ml13, false), 99, true
FROM elm_lohnraster r
WHERE r.code IN ('195.4', '195.5', '195.6', '55.1', '55.2')
  AND NOT EXISTS (SELECT 1 FROM lohnposition lp WHERE lp.code = r.code AND lp.is_active = true);

-- 2) Renames (Reihenfolge!). Bezeichnungen bleiben.
UPDATE lohnposition SET code = '60.3'  WHERE code = '60.2' AND is_active = true;
UPDATE lohnposition SET code = '60.2'  WHERE code = '60'   AND is_active = true;
UPDATE lohnposition SET code = '70.1'  WHERE code = '70'   AND is_active = true;
UPDATE lohnposition SET code = '75.1'  WHERE code = '75'   AND is_active = true;
UPDATE lohnposition SET code = '10.1'  WHERE code = '10'   AND is_active = true;
UPDATE lohnposition SET code = '10.2'  WHERE code = '2'    AND is_active = true;
UPDATE lohnposition SET code = '10.3'  WHERE code = '3'    AND is_active = true;
UPDATE lohnposition SET code = '55.3'  WHERE code = '4'    AND is_active = true;
UPDATE lohnposition SET code = '50.1'  WHERE code = '50'   AND is_active = true;

-- 3) Alte Position 65 «Korrektur Unfall» stilllegen — die Engine bucht neu auf
--    die bestehende 65.1 (Raster-Position); der Lohnzettel-Text «Korrektur
--    Unfall» bleibt (literal in der Engine).
UPDATE lohnposition SET is_active = false WHERE code = '65' AND is_active = true;

-- 4) Lohnschema-Zeilen auf die semantisch richtigen Positionen umhängen
--    (die FK-Zeilen zeigen nach den Renames teils auf die falsche Nummer).
-- MTP: Feiertagentschädigung lief auf Code 3 (jetzt 10.3) → 195.4
UPDATE vertragsmodell_lohnschema s
SET lohnposition_id = (SELECT id FROM lohnposition WHERE code = '195.4' AND is_active = true)
WHERE s.modell = 'MTP' AND s.art = 'automatisch'
  AND s.lohnposition_id = (SELECT id FROM lohnposition WHERE code = '10.3' AND is_active = true)
  AND EXISTS (SELECT 1 FROM lohnposition WHERE code = '195.4' AND is_active = true);
-- MTP: Lohnersatz-Feiertagentschädigung 195.2 → 195.4
UPDATE vertragsmodell_lohnschema s
SET lohnposition_id = (SELECT id FROM lohnposition WHERE code = '195.4' AND is_active = true)
WHERE s.modell = 'MTP' AND s.art = 'ereignis'
  AND s.lohnposition_id = (SELECT id FROM lohnposition WHERE code = '195.2' AND is_active = true)
  AND EXISTS (SELECT 1 FROM lohnposition WHERE code = '195.4' AND is_active = true)
  AND NOT EXISTS (SELECT 1 FROM vertragsmodell_lohnschema x
                  WHERE x.modell = 'MTP' AND x.art = 'ereignis'
                    AND x.lohnposition_id = (SELECT id FROM lohnposition WHERE code = '195.4' AND is_active = true));
-- MTP: Ferienentschädigung 195.1 → 195.5
UPDATE vertragsmodell_lohnschema s
SET lohnposition_id = (SELECT id FROM lohnposition WHERE code = '195.5' AND is_active = true)
WHERE s.modell = 'MTP' AND s.art = 'saldo'
  AND s.lohnposition_id = (SELECT id FROM lohnposition WHERE code = '195.1' AND is_active = true)
  AND EXISTS (SELECT 1 FROM lohnposition WHERE code = '195.5' AND is_active = true);
-- FLEX: monatliche Feiertagentschädigung lief auf Code 50 (jetzt 50.1) → 195.2
UPDATE vertragsmodell_lohnschema s
SET lohnposition_id = (SELECT id FROM lohnposition WHERE code = '195.2' AND is_active = true)
WHERE s.modell = 'FLEX' AND s.art = 'automatisch'
  AND s.lohnposition_id = (SELECT id FROM lohnposition WHERE code = '50.1' AND is_active = true);
-- Alte 65er-Schema-Zeilen (Position deaktiviert) auf 65.1 umhängen
UPDATE vertragsmodell_lohnschema s
SET lohnposition_id = (SELECT id FROM lohnposition WHERE code = '65.1' AND is_active = true)
WHERE s.lohnposition_id IN (SELECT id FROM lohnposition WHERE code = '65' AND is_active = false)
  AND NOT EXISTS (SELECT 1 FROM vertragsmodell_lohnschema x
                  WHERE x.modell = s.modell AND x.art = s.art
                    AND x.lohnposition_id = (SELECT id FROM lohnposition WHERE code = '65.1' AND is_active = true));

-- 5) Zeitsaldo-Austritt (neu Code 55.2) in die Schemata aufnehmen
INSERT INTO vertragsmodell_lohnschema (modell, lohnposition_id, art, sort_order)
SELECT v.modell, lp.id, 'austritt', 150
FROM (VALUES ('MTP'), ('FIX'), ('FIX-M')) AS v(modell)
JOIN lohnposition lp ON lp.code = '55.2' AND lp.is_active = true
WHERE NOT EXISTS (SELECT 1 FROM vertragsmodell_lohnschema x
                  WHERE x.modell = v.modell AND x.lohnposition_id = lp.id AND x.art = 'austritt');
