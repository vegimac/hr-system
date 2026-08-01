-- ====================================================================
-- Korrektur UVG/KTG als eigene Lohnpositionen (Mirus 65.1 / 65.2 / 75.1 / 75.2)
-- Walter Aug 2026 — für Korrekturlohn / SWICA-Nachzahlung (z.B. Qazimi 344.00)
--
-- In TablePlus ausführen (reines SQL, kein psql-Wrapper).
--
-- Mirus-Spiegel:
--   65.1  Korrektur UVG Taggeld Karenz AHV pflichtig  → voll SV
--   65.2  Korrektur UVG Taggeld Versicherung          → nur BVG + QST
--   75.1  Korrektur KTG Taggeld Karenz AHV pflichtig  → voll SV
--   75.2  Korrektur KTG Taggeld Versicherung          → nur BVG + QST
--
-- Feiertags-Basis = true (L-GAV Art. 18, Stundenlohn 2.27 %) —
-- Engine erzeugt bei FLEX/MTP automatisch die Feiertagsentschädigung.
-- Altes Code «65» / «75» (ABZUG Festlohn-Kürzung FIX) bleibt unverändert.
-- ====================================================================

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
    -- 65.1 Karenz-Korrektur UVG — voll SV (wie Mirus / 60.1)
    ('65.1', 'Korrektur UVG Taggeld Karenz AHV pflichtig', 'Korrektur Unfall', 'ZULAGE',
     TRUE, TRUE, TRUE, TRUE, TRUE,
     'I', FALSE,
     TRUE, FALSE, TRUE,
     '1', FALSE, 'I',
     TRUE, TRUE,
     TRUE, 0, TRUE,
     651, TRUE, CURRENT_TIMESTAMP),

    -- 65.2 Versicherungs-Taggeld UVG — nur BVG + QST (Screenshot Mirus / Qazimi)
    ('65.2', 'Korrektur UVG Taggeld Versicherung', 'Korrektur Unfall', 'ZULAGE',
     FALSE, FALSE, FALSE, TRUE, TRUE,
     'Y', FALSE,
     TRUE, FALSE, FALSE,
     '1', FALSE, '0',
     TRUE, TRUE,
     TRUE, 0, TRUE,
     652, TRUE, CURRENT_TIMESTAMP),

    -- 75.1 Karenz-Korrektur KTG — voll SV
    ('75.1', 'Korrektur KTG Taggeld Karenz AHV pflichtig', 'Korrektur Krankheit', 'ZULAGE',
     TRUE, TRUE, TRUE, TRUE, TRUE,
     'I', FALSE,
     TRUE, FALSE, TRUE,
     '1', FALSE, 'I',
     TRUE, TRUE,
     TRUE, 0, TRUE,
     751, TRUE, CURRENT_TIMESTAMP),

    -- 75.2 Versicherungs-Taggeld KTG — nur BVG + QST
    ('75.2', 'Korrektur KTG Taggeld Versicherung', 'Korrektur Krankheit', 'ZULAGE',
     FALSE, FALSE, FALSE, TRUE, TRUE,
     'Y', FALSE,
     TRUE, FALSE, FALSE,
     '1', FALSE, '0',
     TRUE, TRUE,
     TRUE, 0, TRUE,
     752, TRUE, CURRENT_TIMESTAMP)
ON CONFLICT (code) DO UPDATE SET
    bezeichnung               = EXCLUDED.bezeichnung,
    kategorie                 = EXCLUDED.kategorie,
    typ                       = EXCLUDED.typ,
    ahv_alv_pflichtig         = EXCLUDED.ahv_alv_pflichtig,
    nbuv_pflichtig            = EXCLUDED.nbuv_pflichtig,
    ktg_pflichtig             = EXCLUDED.ktg_pflichtig,
    bvg_pflichtig             = EXCLUDED.bvg_pflichtig,
    qst_pflichtig             = EXCLUDED.qst_pflichtig,
    lohnausweis_code          = EXCLUDED.lohnausweis_code,
    zaehlt_als_basis_feiertag = EXCLUDED.zaehlt_als_basis_feiertag,
    zaehlt_als_basis_ferien   = EXCLUDED.zaehlt_als_basis_ferien,
    zaehlt_als_basis_13ml     = EXCLUDED.zaehlt_als_basis_13ml,
    bvg_auf_100_rechnen       = EXCLUDED.bvg_auf_100_rechnen,
    zaehlt_fuer_tagessatz     = EXCLUDED.zaehlt_fuer_tagessatz,
    nicht_im_vertrag_drucken  = EXCLUDED.nicht_im_vertrag_drucken,
    sort_order                = EXCLUDED.sort_order,
    is_active                 = TRUE;

COMMIT;

-- Kontrolle
SELECT code, bezeichnung, typ,
       ahv_alv_pflichtig AS ahv, bvg_pflichtig AS bvg, qst_pflichtig AS qst,
       zaehlt_als_basis_feiertag AS fei, zaehlt_als_basis_13ml AS ml13
  FROM lohnposition
 WHERE code IN ('65','65.1','65.2','75','75.1','75.2')
 ORDER BY code;
