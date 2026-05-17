-- ====================================================================
-- Migration: Lohnpositionen für Familienzulagen (Code 190.x)
-- Ausführen mit:
--   psql -d <datenbank> -f add_familienzulagen_lohnpositionen.sql
-- ====================================================================
--
-- Zweck:
--   Zwei Lohnpositionen für die automatische Verbuchung von
--   Familienzulagen aus FamilyMemberAllowance ins Brutto:
--     • 190.1  Kinderzulage          (KZ — bis Alter 16)
--     • 190.2  Ausbildungszulage     (AZ — Alter 16 bis 25 in Ausbildung)
--
-- SV-Eigenschaften (Schweizer Recht / ESTV-Lohnausweis-Wegleitung):
--   • Familienzulagen sind NICHT AHV/ALV/NBU/KTG/BVG-pflichtig
--     → werden nicht in die Bemessungsgrundlage dieser Versicherungen
--       aufgenommen (delta_ahv/nbuv/ktg/bvg bleibt 0)
--   • Familienzulagen sind QST-pflichtig
--     → fliessen in die Quellensteuer-Bemessung ein
--   • Im Brutto werden sie ausgewiesen (zulagenSvTotal), aber die
--     SV-Berechnung pro Versicherung erfolgt aus mainLohn + delta_X,
--     also ohne Doppel-Abzug.
--   • Lohnausweis-Feld 5 (Sozialleistungen / Familienzulagen)
--   • Zählen NICHT zur Feiertag-/Ferien-/13.-ML-Basis
--
-- Idempotent: ON CONFLICT (code) DO UPDATE überschreibt vorhandene
-- Einträge mit den korrekten Flags — falls eine frühere Migration die
-- Codes anders gesetzt hatte.
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
    ('190.1', 'Kinderzulage',         'Familienzulagen', 'ZULAGE',
     false, false, false, false, true,
     '5', false,
     false, false, false,
     '5', false, 'II',
     true, false, false, 0, false,
     190, true, now()),

    ('190.2', 'Ausbildungszulage',    'Familienzulagen', 'ZULAGE',
     false, false, false, false, true,
     '5', false,
     false, false, false,
     '5', false, 'II',
     true, false, false, 0, false,
     191, true, now())
ON CONFLICT (code) DO UPDATE SET
    bezeichnung               = EXCLUDED.bezeichnung,
    kategorie                 = EXCLUDED.kategorie,
    typ                       = EXCLUDED.typ,
    ahv_alv_pflichtig         = EXCLUDED.ahv_alv_pflichtig,
    nbuv_pflichtig            = EXCLUDED.nbuv_pflichtig,
    ktg_pflichtig             = EXCLUDED.ktg_pflichtig,
    bvg_pflichtig             = EXCLUDED.bvg_pflichtig,
    qst_pflichtig             = EXCLUDED.qst_pflichtig,
    zaehlt_als_basis_feiertag = EXCLUDED.zaehlt_als_basis_feiertag,
    zaehlt_als_basis_ferien   = EXCLUDED.zaehlt_als_basis_ferien,
    zaehlt_als_basis_13ml     = EXCLUDED.zaehlt_als_basis_13ml,
    lohnausweisfeld           = EXCLUDED.lohnausweisfeld,
    statistik_code            = EXCLUDED.statistik_code,
    zaehlt_fuer_tagessatz     = EXCLUDED.zaehlt_fuer_tagessatz,
    sort_order                = EXCLUDED.sort_order,
    is_active                 = EXCLUDED.is_active;

COMMIT;
