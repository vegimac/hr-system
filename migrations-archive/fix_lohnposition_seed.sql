-- ====================================================================
-- Fix: Lohnposition-Seed nochmals einspielen mit ALLEN Spalten explizit.
-- Das ursprüngliche refactor_lohnposition_mirus.sql hat möglicherweise
-- wegen einer NOT-NULL-Spalte ohne Default gefailt → INSERTs leer geblieben.
-- ====================================================================

BEGIN;

-- 1. Bestehende Einträge wegputzen
TRUNCATE TABLE lohnposition RESTART IDENTITY CASCADE;

-- 2. Sicherstellen dass alle erwarteten Spalten existieren (idempotent)
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS dreijehnter_ml_pflichtig boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS lohnausweisfeld         varchar(10);
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS lohnausweis_kreuz       boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS statistik_code          varchar(20);
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS nicht_drucken_wenn_null boolean DEFAULT true;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS nicht_im_vertrag_drucken boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS bvg_auf_100_rechnen     boolean DEFAULT false;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS position_13ml           integer DEFAULT 0;
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS zaehlt_fuer_tagessatz   boolean DEFAULT true;

-- 3. Frische Seeds — alle Spalten explizit, keine Default-Annahmen
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
 false, true,  true,
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
 true,  false, true,
 '1',   false, 'I',
 true,  false, false, 0, true,
 22,   true, now()),

('50',  'Ausbezahlte Feiertage',             'Feiertag',     'ZULAGE',
 true,  true,  true,  true,  true,
 NULL, false,
 false, true,  true,
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

-- 4. Verifikation
SELECT 'Total Lohnpositionen eingespielt: ' || count(*)::text AS status FROM lohnposition;

COMMIT;
