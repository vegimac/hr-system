-- Walter-Vorgabe 28.05.2026: Lohnpositionen für die 80%-Variante der
-- Krank-/Unfall-Versicherungsleistung sauber trennen, damit die SV-Pflicht
-- (BVG / QST) NICHT mehr im Engine hardcoded ist, sondern aus den Flags
-- der Lohnposition kommt — wie alle anderen Positionen auch.
--
-- Hintergrund:
--   • Code 70 = Krankheit (Karenzentschädigung 88%) — voll SV-pflichtig
--   • Code 60 = Unfall (Karenzentschädigung 88%)   — voll SV-pflichtig
--   • Code 70.2 = Krankheit (Taggeld 80% Versicherungsleistung) — NUR BVG+QST
--   • Code 60.2 = Unfall (Taggeld 80% Versicherungsleistung)    — NUR BVG+QST
--
-- L-GAV Art. 23 / Versicherungsleistungen sind nicht AHV/ALV/NBU/KTG-pflichtig,
-- aber BVG (Pensionskasse zählt sie als Lohn) und QST (steuerbarer Lohn).
-- Walter kann die Flags in der Lohnpositions-UI jederzeit anpassen, sollten
-- sich die Vorgaben ändern.
--
-- In TablePlus ausführen.

BEGIN;

INSERT INTO lohnposition (
    code, bezeichnung, kategorie, typ,
    ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
    lohnausweis_code, dreijehnter_ml_pflichtig,
    zaehlt_als_basis_feiertag, zaehlt_als_basis_ferien, zaehlt_als_basis_13ml,
    lohnausweisfeld, lohnausweis_kreuz, statistik_code,
    nicht_drucken_wenn_null, nicht_im_vertrag_drucken,
    bvg_auf_100_rechnen, position_13ml, zaehlt_fuer_tagessatz,
    sort_order, is_active, created_at
) VALUES
    ('70.2', 'Krankheit (Taggeld 80%)', 'Karenz', 'ZULAGE',
     FALSE, FALSE, FALSE, TRUE, TRUE,    -- nur BVG + QST = Versicherungsleistung
     '1', FALSE,
     FALSE, FALSE, FALSE,
     '1', FALSE, '0',
     TRUE, FALSE, FALSE, 0, FALSE,
     71, TRUE, now()),

    ('60.2', 'Unfall (Taggeld 80%)', 'Karenz', 'ZULAGE',
     FALSE, FALSE, FALSE, TRUE, TRUE,    -- nur BVG + QST = Versicherungsleistung
     '1', FALSE,
     FALSE, FALSE, FALSE,
     '1', FALSE, '0',
     TRUE, FALSE, FALSE, 0, FALSE,
     61, TRUE, now())
ON CONFLICT (code) DO UPDATE SET
    bezeichnung               = EXCLUDED.bezeichnung,
    kategorie                 = EXCLUDED.kategorie,
    typ                       = EXCLUDED.typ,
    -- SV-Flags bewusst NICHT überschreiben — Walter kann sie pflegen
    sort_order                = EXCLUDED.sort_order;

COMMIT;

-- Kontrolle
SELECT code, bezeichnung, ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig,
       bvg_pflichtig, qst_pflichtig
FROM lohnposition
WHERE code IN ('60', '60.2', '70', '70.2')
ORDER BY code;
