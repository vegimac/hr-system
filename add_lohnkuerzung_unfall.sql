-- ====================================================================
-- Migration: Lohnposition 15.2 "Lohnkürzung Unfall" anlegen
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_lohnkuerzung_unfall.sql
-- ====================================================================
--
-- Zweck:
--   Bisher wurde sowohl für Krank- als auch für Unfall-Lohnkürzung der
--   Code 15.1 (Lohnkürzung Krankheit) verwendet. Analog zu 15.1 wird
--   jetzt 15.2 für Unfall-Lohnkürzung eingeführt.
--
--   Die Konfiguration (Typ, SV-Flags, Kategorie, Lohnausweis-Code,
--   Basis-Flags) wird 1:1 von 15.1 kopiert — so bleibt das Verhalten
--   für Unfall identisch zu dem für Krankheit, und der Admin kann sie
--   bei Bedarf in der UI individuell anpassen.
--
-- Idempotent:
--   • Wenn 15.2 bereits existiert, wird nichts geändert.
--   • Wenn 15.1 nicht existiert (frische DB), wird nichts angelegt —
--     der Admin muss dann Lohnkürzung-Codes manuell erfassen.
-- ====================================================================

BEGIN;

INSERT INTO lohnposition (
    code,
    bezeichnung,
    kategorie,
    typ,
    ahv_alv_pflichtig,
    nbuv_pflichtig,
    ktg_pflichtig,
    bvg_pflichtig,
    qst_pflichtig,
    lohnausweis_code,
    dreijehnter_ml_pflichtig,
    zaehlt_als_basis_feiertag,
    zaehlt_als_basis_ferien,
    zaehlt_als_basis_13ml,
    sort_order,
    is_active,
    created_at
)
SELECT
    '15.2',
    'Lohnkürzung Unfall',
    kategorie,
    typ,
    ahv_alv_pflichtig,
    nbuv_pflichtig,
    ktg_pflichtig,
    bvg_pflichtig,
    qst_pflichtig,
    lohnausweis_code,
    dreijehnter_ml_pflichtig,
    zaehlt_als_basis_feiertag,
    zaehlt_als_basis_ferien,
    zaehlt_als_basis_13ml,
    sort_order + 1,
    is_active,
    now()
FROM lohnposition
WHERE code = '15.1'
  AND NOT EXISTS (SELECT 1 FROM lohnposition WHERE code = '15.2');

COMMIT;
