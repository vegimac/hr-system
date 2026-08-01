-- Korrektur Quellensteuer (Mirus-Position 565)
-- Walter-Vorgabe 01.08.2026: manuelle Nachzahlung QST aus Vormonaten.
-- In TablePlus ausführen (optional — Program.cs seedet idempotent beim Start).

INSERT INTO lohnposition
    (code, bezeichnung, kategorie, typ,
     ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
     lohnausweis_code, sort_order, is_active,
     nicht_drucken_wenn_null, nicht_im_vertrag_drucken)
SELECT
    '565', 'Korrektur Quellensteuer', 'Abzüge', 'ABZUG',
    false, false, false, false, false,
    NULL, 565, true,
    true, true
WHERE NOT EXISTS (SELECT 1 FROM lohnposition lp WHERE lp.code = '565');

UPDATE lohnposition
   SET bezeichnung = 'Korrektur Quellensteuer',
       kategorie   = 'Abzüge',
       typ         = 'ABZUG',
       is_active   = true,
       ahv_alv_pflichtig = false,
       nbuv_pflichtig    = false,
       ktg_pflichtig     = false,
       bvg_pflichtig     = false,
       qst_pflichtig     = false
 WHERE code = '565';

-- Fibu-Mapping (falls Kontoplan schon geseedet wurde ohne 565)
INSERT INTO lohn_konto_mapping
    (position, sub_position, fibukonto, gegenkonto, kostenstelle_nr, kostenstelle_name,
     bezeichnung, is_vormonat, soll_buchung, sort_order, is_active)
SELECT
    565, NULL, '1920', '2010', NULL, NULL,
    'Korr. QST-Abzug', false, true, 1770, true
WHERE NOT EXISTS (
    SELECT 1 FROM lohn_konto_mapping
     WHERE position = 565 AND fibukonto = '1920' AND gegenkonto = '2010'
);
