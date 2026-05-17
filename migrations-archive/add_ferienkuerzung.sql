-- ====================================================================
-- Stufe 2: Ferienanspruch-Kürzung (Art. 329b OR)
--   psql -d <datenbank> -f add_ferienkuerzung.sql
-- ====================================================================
--
-- Walter, 25.04.2026:
-- Implementierung in 3 Teilen:
--   1. Lohnpositionen 110/115 für Unbezahlter Urlaub (Tracking + Korrektur)
--   2. AbsenzTyp UNBEZ_URLAUB
--   3. PayrollSaldo-Felder für Kürzungs-Tracking (Vorschlag/Angewendet)
-- ====================================================================

BEGIN;

-- ── 1. Lohnpositionen für Unbezahlter Urlaub ────────────────────────
-- 110 zeigt nur die Information (ZULAGE mit Betrag 0 oder Saldo-Tage)
-- 115 ist die negative Lohnkürzung (analog 75 Korrektur Krankheit)
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
('110', 'Unbezahlter Urlaub',                'Urlaub',       'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, false,
 '1',   false, 'I',
 true,  false, false, 0, false,
 110,   true, now()),

('115', 'Korrektur Unbezahlter Urlaub',      'Urlaub',       'ABZUG',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,    -- Lohnkürzung reduziert 13.ML-Basis (analog 75)
 '1',   false, 'I',
 true,  false, false, 0, false,
 115,   true, now())
ON CONFLICT (code) DO NOTHING;

-- ── 2. AbsenzTyp UNBEZ_URLAUB ───────────────────────────────────────
INSERT INTO absenz_typ (
    code, bezeichnung, zeitgutschrift, gutschrift_modus, basis_stunden,
    utp_auszahlung, reduziert_saldo,
    lohnposition_auszahlung_code, lohnposition_kuerzung_code, pattern,
    sort_order, aktiv, created_at
) VALUES
('UNBEZ_URLAUB', 'Unbezahlter Urlaub',
 false, NULL, 'BETRIEB',
 false, NULL,
 '110', '115', 'KORREKTUR',
 80, true, now())
ON CONFLICT (code) DO NOTHING;

-- ── 3. PayrollSaldo: Felder für Kürzungs-Tracking ───────────────────
ALTER TABLE payroll_saldo ADD COLUMN IF NOT EXISTS ferienkuerzung_vorschlag_tage  numeric(8,4) DEFAULT 0;
ALTER TABLE payroll_saldo ADD COLUMN IF NOT EXISTS ferienkuerzung_angewendet_tage numeric(8,4) DEFAULT 0;
ALTER TABLE payroll_saldo ADD COLUMN IF NOT EXISTS ferienkuerzung_grund          text;

-- Verifikation
SELECT code, bezeichnung, typ, sort_order FROM lohnposition WHERE code IN ('110','115') ORDER BY code;
SELECT code, bezeichnung, pattern, lohnposition_auszahlung_code, lohnposition_kuerzung_code
  FROM absenz_typ WHERE code = 'UNBEZ_URLAUB';

COMMIT;
