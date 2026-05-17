-- fix_social_insurance_rate_dedup.sql
-- ---------------------------------------------------------------------------
-- Zweck: Doppelte SV-Sätze (social_insurance_rate) bereinigen + Unique-Index
--        als dauerhaften Schutz gegen erneute Dubletten.
--
-- Hintergrund: Das Startup-Seeding in Program.cs hing an einer einzigen
-- Sentinel-Zeile (code='KTG' AND valid_from='2026-01-01'). Wurde diese Zeile
-- gelöscht oder ihr valid_from im UI geändert, hat der Seed bei JEDEM Deploy
-- alle 11 Sätze erneut eingefügt. Program.cs ist parallel umgestellt auf
-- "nur seeden wenn Tabelle leer" + denselben Unique-Index.
--
-- Walter-Vorgabe (15.05.2026): bei einem Konflikt (gleicher Satz mit
-- 2024-01-01 UND 2026-01-01) wird die 2024-01-01-Version behalten.
--
-- Ausführung in TablePlus. Reihenfolge: dieses SQL ZUERST, dann ./deploy.sh.
-- Schritte 1 und 5 sind reine Kontroll-SELECTs (verändern nichts).
-- ---------------------------------------------------------------------------

-- 1) Kontrolle VORHER: aktueller Bestand
SELECT id, code, name, rate, valid_from, min_age, max_age,
       employment_model_code, basis_type, is_active
FROM   social_insurance_rate
ORDER  BY sort_order, code, valid_from, min_age;

-- 2) Konflikt-Dubletten entfernen: eine 2026-01-01-Zeile wird gelöscht, wenn es
--    denselben Satz (gleicher fachlicher Schlüssel) mit früherem valid_from gibt.
--    Sätze die es NUR mit 2026-01-01 gibt (z.B. NBUV, GastroSocial-BVG-Bänder)
--    bleiben erhalten — es wird nichts ersatzlos gelöscht.
DELETE FROM social_insurance_rate young
WHERE young.valid_from = '2026-01-01'
  AND EXISTS (
      SELECT 1 FROM social_insurance_rate old
      WHERE old.valid_from < '2026-01-01'
        AND old.code               = young.code
        AND COALESCE(old.min_age, -1)               = COALESCE(young.min_age, -1)
        AND COALESCE(old.max_age, -1)               = COALESCE(young.max_age, -1)
        AND COALESCE(old.employment_model_code, '') = COALESCE(young.employment_model_code, '')
        AND old.basis_type         = young.basis_type
        AND old.only_quellensteuer = young.only_quellensteuer
  );

-- 3) Restliche echte Dubletten innerhalb desselben valid_from entfernen (falls
--    ein Satz mehrfach eingefügt wurde). Behalten wird pro fachlichem Schlüssel
--    die aktive Zeile mit der kleinsten id.
DELETE FROM social_insurance_rate a
USING social_insurance_rate b
WHERE a.code               = b.code
  AND a.valid_from         = b.valid_from
  AND COALESCE(a.min_age, -1)               = COALESCE(b.min_age, -1)
  AND COALESCE(a.max_age, -1)               = COALESCE(b.max_age, -1)
  AND COALESCE(a.employment_model_code, '') = COALESCE(b.employment_model_code, '')
  AND a.basis_type         = b.basis_type
  AND a.only_quellensteuer = b.only_quellensteuer
  AND ( a.is_active < b.is_active
        OR (a.is_active = b.is_active AND a.id > b.id) );

-- 4) Unique-Index: verhindert künftige Dubletten auf DB-Ebene. COALESCE, weil
--    min_age/max_age/employment_model_code NULL sein dürfen und Postgres NULLs
--    in Unique-Indizes sonst als verschieden behandelt.
CREATE UNIQUE INDEX IF NOT EXISTS ux_social_insurance_rate_natural
ON social_insurance_rate (
    code, valid_from,
    COALESCE(min_age, -1),
    COALESCE(max_age, -1),
    COALESCE(employment_model_code, ''),
    basis_type,
    only_quellensteuer
);

-- 5) Kontrolle NACHHER: Bestand nach Bereinigung
SELECT id, code, name, rate, valid_from, min_age, max_age,
       employment_model_code, basis_type, is_active
FROM   social_insurance_rate
ORDER  BY sort_order, code, valid_from, min_age;
