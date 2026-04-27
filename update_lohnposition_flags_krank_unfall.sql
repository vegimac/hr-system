-- ====================================================================
-- Migration: SV-Flags auf Krank/Unfall-Lohnpositionen setzen
-- Ausführen mit:
--   psql -d hr_system -U postgres -f update_lohnposition_flags_krank_unfall.sql
-- ====================================================================
--
-- Zweck:
--   Die SV-Flags (AHV/NBU/KTG/BVG/QST) und den Lohnausweis-Code auf den
--   bestehenden Krank-/Unfall-Lohnpositionen analog zum Original-
--   Lohnraster setzen. Referenz: GastroSocial/McDonald's-Lohnraster.
--
-- Gedeckte Codes:
--   60.1  UVG Karenzentschädigung (88%)         → voll SV-pflichtig, LA=I
--   60.3  UVG Taggeld (80%)                     → nur BVG + QST, LA=7/Y
--   70.1  KTG Karenzentschädigung (88%)         → voll SV-pflichtig, LA=I
--   70.2  KTG Taggeld (80%)                     → nur BVG + QST, LA=7/Y
--   15.1  Lohnkürzung Krankheit                 → voll SV-pflichtig (Abzug)
--   15.2  Lohnkürzung Unfall                    → voll SV-pflichtig (Abzug)
--
-- Hinweis zum 80%-Taggeld:
--   Laut Lohnraster ist das Versicherungstaggeld (60.3 / 70.2) nur für
--   BVG und Quellensteuer pflichtig — nicht für AHV/ALV/NBU/KTG. Das
--   entspricht L-GAV Art. 23 (BVG läuft während Lohnfortzahlung weiter)
--   und der Tatsache dass die Versicherungsleistung selbst nicht
--   AHV-pflichtig ist. Lohnausweis-Ziffer 7 (andere Leistungen).
--
-- Basis-Flags (Feiertag/Ferien/13. ML):
--   Werden hier NICHT überschrieben — die sind in
--   add_lohnposition_basis_flags.sql bereits separat gesetzt.
-- ====================================================================

BEGIN;

-- 60.1 UVG Karenzentschädigung (88%) — voll SV-pflichtig
UPDATE lohnposition SET
    ahv_alv_pflichtig = TRUE,
    nbuv_pflichtig    = TRUE,
    ktg_pflichtig     = TRUE,
    bvg_pflichtig     = TRUE,
    qst_pflichtig     = TRUE,
    lohnausweis_code  = 'I'
WHERE code = '60.1';

-- 60.3 UVG Taggeld (80%) — nur BVG + QST (L-GAV Art. 23)
UPDATE lohnposition SET
    ahv_alv_pflichtig = FALSE,
    nbuv_pflichtig    = FALSE,
    ktg_pflichtig     = FALSE,
    bvg_pflichtig     = TRUE,
    qst_pflichtig     = TRUE,
    lohnausweis_code  = '7'
WHERE code = '60.3';

-- 70.1 KTG Karenzentschädigung (88%) — voll SV-pflichtig
UPDATE lohnposition SET
    ahv_alv_pflichtig = TRUE,
    nbuv_pflichtig    = TRUE,
    ktg_pflichtig     = TRUE,
    bvg_pflichtig     = TRUE,
    qst_pflichtig     = TRUE,
    lohnausweis_code  = 'I'
WHERE code = '70.1';

-- 70.2 KTG Taggeld (80%) — nur BVG + QST (L-GAV Art. 23)
UPDATE lohnposition SET
    ahv_alv_pflichtig = FALSE,
    nbuv_pflichtig    = FALSE,
    ktg_pflichtig     = FALSE,
    bvg_pflichtig     = TRUE,
    qst_pflichtig     = TRUE,
    lohnausweis_code  = '7'
WHERE code = '70.2';

-- 15.1 / 15.2 Lohnkürzung (Krank / Unfall) — voll SV-pflichtig als Abzug
--   Netto-Effekt: -100% Brutto + 88% Taggeld = -12% in allen SV-Basen
UPDATE lohnposition SET
    ahv_alv_pflichtig = TRUE,
    nbuv_pflichtig    = TRUE,
    ktg_pflichtig     = TRUE,
    bvg_pflichtig     = TRUE,
    qst_pflichtig     = TRUE
WHERE code IN ('15.1', '15.2');

-- Kontrolle:
--   SELECT code, bezeichnung, ahv_alv_pflichtig, nbuv_pflichtig,
--          ktg_pflichtig, bvg_pflichtig, qst_pflichtig, lohnausweis_code
--     FROM lohnposition
--    WHERE code IN ('15.1','15.2','60.1','60.3','70.1','70.2')
--    ORDER BY code;

COMMIT;
