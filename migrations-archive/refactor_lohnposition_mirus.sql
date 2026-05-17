-- ====================================================================
-- Refactor: Mirus-style Lohnposition + AbsenzTyp Mapping + Vertragstyp Auto-Assign
--   psql -d <datenbank> -f refactor_lohnposition_mirus.sql
-- ====================================================================
-- Walter hat nur Testdaten → TRUNCATE CASCADE räumt lohn_zulage,
-- employee_recurring_wage und employment_model_component automatisch mit ab.
-- ====================================================================

BEGIN;

-- ── 1. Lohnposition Schema erweitern (idempotent) ──────────────────
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS dreijehnter_ml_pflichtig boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS lohnausweisfeld          varchar(10);
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS lohnausweis_kreuz        boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS statistik_code           varchar(20);
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS nicht_drucken_wenn_null  boolean DEFAULT true;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS nicht_im_vertrag_drucken boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS bvg_auf_100_rechnen      boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS position_13ml            integer DEFAULT 0;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS zaehlt_fuer_tagessatz    boolean DEFAULT true;

-- Eindeutiger Index auf code (kann scheitern wenn Duplikate da sind — bei
-- Testdaten kein Problem)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_lohnposition_code" ON lohnposition (code);

-- ── 2. AbsenzTyp Schema erweitern (idempotent) ─────────────────────
ALTER TABLE absenz_typ ADD COLUMN IF NOT EXISTS lohnposition_auszahlung_code varchar(20);
ALTER TABLE absenz_typ ADD COLUMN IF NOT EXISTS lohnposition_kuerzung_code   varchar(20);
ALTER TABLE absenz_typ ADD COLUMN IF NOT EXISTS pattern                      varchar(20) DEFAULT 'KEIN';

-- ── 3. vertragstyp_lohnposition Tabelle anlegen ────────────────────
CREATE TABLE IF NOT EXISTS vertragstyp_lohnposition (
    id                  serial PRIMARY KEY,
    vertragstyp_code    varchar(10)  NOT NULL,
    lohnposition_code   varchar(20)  NOT NULL,
    is_required         boolean      DEFAULT false,
    is_default_active   boolean      DEFAULT true,
    sort_order          integer      DEFAULT 99,
    created_at          timestamp    DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_vertragstyp_lohnposition_unique"
    ON vertragstyp_lohnposition (vertragstyp_code, lohnposition_code);

-- ── 4. Bestehende Lohnpositionen + abhängige Tabellen wegputzen ────
-- TRUNCATE CASCADE räumt automatisch:
--   - lohn_zulage (FK lohnposition_id)
--   - employee_recurring_wage (FK lohnposition_id)
--   - employment_model_component (FK lohnposition_id)
-- Walter: nur Testdaten, also kein Datenverlust kritisch.
TRUNCATE TABLE lohnposition RESTART IDENTITY CASCADE;
TRUNCATE TABLE vertragstyp_lohnposition RESTART IDENTITY;

-- ── 5. Mirus-aligned Lohnpositionen anlegen ────────────────────────
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
('10',  'Festlohn',                          'Festlohn',     'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 true,  true,  true,
 '1',   false, 'I',
 true,  false, false, 0, true,
 10,   true, now()),

('2',   'Festlohn für bezogene Ferien',      'Festlohn',     'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,
 '1',   false, 'I',
 true,  false, false, 0, true,
 11,   true, now()),

('3',   'Festlohn für bezogene Feiertage',   'Festlohn',     'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,    -- Auszahlung: nur 13.ML, weder Feiertag- noch Ferien-Basis
 '1',   false, 'I',
 true,  false, false, 0, true,
 12,   true, now()),

('4',   'Zusatzstunden (MTP)',               'Festlohn',     'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 true,  true,  true,
 '1',   false, 'I',
 true,  false, false, 0, true,
 13,   true, now()),

('20',  'Stundenlohn',                       'Stundenlohn',  'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 true,  true,  true,
 '1',   false, 'I',
 true,  false, false, 0, true,
 20,   true, now()),

('22',  'Stundenlohn Ferien',                'Stundenlohn',  'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,    -- Auszahlung: nur 13.ML, weder Feiertag- noch Ferien-Basis (kein Rekursions-Aufschlag)
 '1',   false, 'I',
 true,  false, false, 0, true,
 22,   true, now()),

('50',  'Ausbezahlte Feiertage',             'Feiertag',     'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,    -- Feiertag-Auszahlung zählt nur für 13. ML, nicht für Ferien-/Feiertag-Basis
 '1',   false, 'I',
 true,  false, false, 0, true,
 50,   true, now()),

('60',  'Unfall (Karenzentschädigung)',      'Karenz',       'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, false,
 '2',   false, 'II',
 true,  false, true,  0, false,
 60,   true, now()),

('70',  'Krankheit (Karenzentschädigung)',   'Karenz',       'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, false,
 '2',   false, 'II',
 true,  false, true,  0, false,
 70,   true, now()),

('65',  'Korrektur Unfall',                  'Karenz',       'ABZUG',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,
 '1',   false, 'I',
 true,  false, false, 0, false,
 65,   true, now()),

('75',  'Korrektur Krankheit',               'Karenz',       'ABZUG',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,
 '1',   false, 'I',
 true,  false, false, 0, false,
 75,   true, now()),

('195.3', 'Ferien-Geld-Auszahlung',          'Ferien',       'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, true,
 '1',   false, 'I',
 true,  false, false, 0, true,
 195,  true, now()),

('180', '13. Monatslohn',                    '13. ML',       'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, false,
 '1',   false, 'I',
 true,  false, false, 0, false,
 180,  true, now()),

('103', 'AHV / IV / EO',                     'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 103,  true, now()),

('104', 'Arbeitslosenversicherung',          'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 104,  true, now()),

('107', 'Krankentaggeldversicherung',        'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 107,  true, now()),

('110', 'Nichtberufsunfallversicherung',     'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 110,  true, now()),

('111', 'Berufliche Vorsorge (BVG)',         'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 111,  true, now()),

('120', 'Quellensteuer',                     'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 120,  true, now()),

('130', 'GastroSocial Uno Basis',            'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 130,  true, now()),

('131', 'Uno Int McD Zusatz',                'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 131,  true, now()),

('140', 'LGAV-Beitrag',                      'SV-Abzug',     'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 140,  true, now()),

('200', 'Diverse Zulagen',                   'Sonstiges',    'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, false, false,
 '1',   false, 'I',
 true,  false, false, 0, true,
 200,  true, now()),

('210', 'Diverse Abzüge',                    'Sonstiges',    'ABZUG',
 false, false, false, false, false,
 NULL, false,
 false, false, false,
 NULL,  false, NULL,
 true,  false, false, 0, false,
 210,  true, now());

-- ── 6. AbsenzTyp → Lohnposition Mapping ────────────────────────────
UPDATE absenz_typ SET lohnposition_auszahlung_code = '70', lohnposition_kuerzung_code = '75', pattern = 'KORREKTUR' WHERE code = 'KRANK';
UPDATE absenz_typ SET lohnposition_auszahlung_code = '60', lohnposition_kuerzung_code = '65', pattern = 'KORREKTUR' WHERE code = 'UNFALL';
UPDATE absenz_typ SET lohnposition_auszahlung_code = '2',  lohnposition_kuerzung_code = NULL, pattern = 'SPLIT'     WHERE code = 'FERIEN';
UPDATE absenz_typ SET lohnposition_auszahlung_code = '3',  lohnposition_kuerzung_code = NULL, pattern = 'SPLIT'     WHERE code = 'FEIERTAG';
UPDATE absenz_typ SET lohnposition_auszahlung_code = NULL, lohnposition_kuerzung_code = NULL, pattern = 'KEIN'
    WHERE code NOT IN ('KRANK','UNFALL','FERIEN','FEIERTAG');

-- ── 7. Vertragstyp → Lohnposition Mapping ──────────────────────────
INSERT INTO vertragstyp_lohnposition (vertragstyp_code, lohnposition_code, is_required, is_default_active, sort_order) VALUES
('FIX', '10',   true,  true,  10),
('FIX', '2',    false, true,  11),
('FIX', '3',    false, true,  12),
('FIX', '60',   false, true,  60),
('FIX', '65',   false, true,  65),
('FIX', '70',   false, true,  70),
('FIX', '75',   false, true,  75),
('FIX', '180',  false, true, 180),
('FIX', '103',  true,  true, 103),
('FIX', '104',  true,  true, 104),
('FIX', '107',  true,  true, 107),
('FIX', '110',  true,  true, 110),
('FIX', '111',  true,  true, 111),
('FIX', '120',  false, false,120),
('FIX', '130',  true,  true, 130),
('FIX', '131',  false, true, 131),
('FIX', '140',  true,  true, 140),
('FIX-M', '10',  true,  true,  10),
('FIX-M', '2',   false, true,  11),
('FIX-M', '3',   false, true,  12),
('FIX-M', '60',  false, true,  60),
('FIX-M', '65',  false, true,  65),
('FIX-M', '70',  false, true,  70),
('FIX-M', '75',  false, true,  75),
('FIX-M', '180', false, true, 180),
('FIX-M', '103', true,  true, 103),
('FIX-M', '104', true,  true, 104),
('FIX-M', '107', true,  true, 107),
('FIX-M', '110', true,  true, 110),
('FIX-M', '111', true,  true, 111),
('FIX-M', '120', false, false,120),
('FIX-M', '130', true,  true, 130),
('FIX-M', '131', false, true, 131),
('FIX-M', '140', true,  true, 140),
('MTP', '10',    true,  true,  10),
('MTP', '2',     false, true,  11),
('MTP', '4',     false, true,  13),
('MTP', '60',    false, true,  60),
('MTP', '65',    false, true,  65),
('MTP', '70',    false, true,  70),
('MTP', '75',    false, true,  75),
('MTP', '195.3', false, true, 195),
('MTP', '180',   false, true, 180),
('MTP', '103',   true,  true, 103),
('MTP', '104',   true,  true, 104),
('MTP', '107',   true,  true, 107),
('MTP', '110',   true,  true, 110),
('MTP', '111',   false, true, 111),
('MTP', '120',   false, false,120),
('MTP', '130',   true,  true, 130),
('MTP', '131',   false, true, 131),
('MTP', '140',   true,  true, 140),
('UTP', '20',    true,  true,  20),
('UTP', '22',    false, true,  22),
('UTP', '50',    false, true,  50),
('UTP', '60',    false, true,  60),
('UTP', '65',    false, true,  65),
('UTP', '70',    false, true,  70),
('UTP', '75',    false, true,  75),
('UTP', '195.3', false, true, 195),
('UTP', '180',   false, true, 180),
('UTP', '103',   true,  true, 103),
('UTP', '104',   true,  true, 104),
('UTP', '107',   true,  true, 107),
('UTP', '110',   true,  true, 110),
('UTP', '120',   false, false,120),
('UTP', '130',   true,  true, 130),
('UTP', '140',   true,  true, 140);

-- ── 8. Verifikation ────────────────────────────────────────────────
SELECT 'lohnposition: ' || count(*)::text AS ergebnis FROM lohnposition;
SELECT 'vertragstyp_lohnposition: ' || count(*)::text AS ergebnis FROM vertragstyp_lohnposition;
SELECT 'absenz_typ Pattern-Verteilung:' AS info;
SELECT pattern, count(*) FROM absenz_typ GROUP BY pattern;

COMMIT;
