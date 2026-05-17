-- ====================================================================
-- Migration: Familienzulagen-Tarife pro Kanton (kantonale FAK-Sätze)
-- Ausführen mit:
--   psql -d <datenbank> -f add_familienzulagen_tarif.sql
-- ====================================================================
--
-- Zweck:
--   Familienzulagen sind in der Schweiz kantonal geregelt (FamZG +
--   kantonale Ausführungsgesetze). Jeder Kanton hat eigene Sätze für
--   Kinderzulage (KZ, bis Alter 16) und Ausbildungszulage (AZ, ab 16
--   bis max. 25). Einige Kantone haben Differenzierungen:
--     • LU/ZH: Satz 2 ab Alter 12 (höherer KZ-Satz für 12–16-jährige)
--     • FR/GE/NE/VD/VS: Satz 2 ab dem 3. Kind (höherer Satz pro Kind)
--     • ZG: AZ-Satz 2 ab Alter 18
--
--   Im Gegensatz zur Quellensteuer ist NICHT der Wohnsitz-Kanton des
--   MA, sondern der STANDORT-Kanton der Filiale massgeblich (FAK-
--   Zugehörigkeit des Arbeitgebers). Daher Verknüpfung über
--   company_profile.kanton_code, NICHT über employee.canton_code.
--
--   Versionierung: pro (Kanton, Gültigkeitsperiode) ein Eintrag. Bei
--   Tarif-Anpassung (z.B. AG ab 1.1.2026) wird ein neuer Eintrag mit
--   neuem valid_from angelegt; alter Eintrag bekommt valid_to am
--   Vortag.
--
-- Idempotent:
--   Diese Migration kann mehrfach ausgeführt werden — neue Spalten
--   werden bei Bedarf zugefügt, Seed-Werte upserted (überschreiben
--   bestehende Einträge mit gleichem Kanton+ValidFrom).
-- ====================================================================

BEGIN;

-- ── Tabelle anlegen falls noch nicht vorhanden ─────────────────────
CREATE TABLE IF NOT EXISTS familienzulagen_tarif (
    id                            SERIAL PRIMARY KEY,
    kanton_code                   VARCHAR(2)     NOT NULL,
    valid_from                    DATE           NOT NULL,
    valid_to                      DATE,                       -- NULL = offen
    kinderzulage_satz1            NUMERIC(8,2),
    kinderzulage_satz2            NUMERIC(8,2),
    ausbildungszulage_satz1       NUMERIC(8,2),
    ausbildungszulage_satz2       NUMERIC(8,2),
    schwelle_satz2_anzahl_kinder  INT,
    mindesterwerbseinkommen_jahr  NUMERIC(10,2),
    alters_grenze_kinder          INT            NOT NULL DEFAULT 16,
    alters_grenze_ausbildung      INT            NOT NULL DEFAULT 25,
    quelle                        VARCHAR(200),
    bemerkung                     TEXT,
    is_active                     BOOLEAN        NOT NULL DEFAULT TRUE,
    created_at                    TIMESTAMP      NOT NULL DEFAULT NOW(),
    updated_at                    TIMESTAMP      NOT NULL DEFAULT NOW()
);

-- ── Spalten für Alters-Staffelung nachträglich zufügen (idempotent) ──
-- LU: KZ 215 bis 12. Geburtstag, danach 260 bis 16. Geburtstag.
-- ZG: AZ 330 bis 18. Geburtstag, danach 385 bis 25. Geburtstag.
ALTER TABLE familienzulagen_tarif
    ADD COLUMN IF NOT EXISTS kinderzulage_satz2_ab_alter      INT,
    ADD COLUMN IF NOT EXISTS ausbildungszulage_satz2_ab_alter INT;

COMMENT ON COLUMN familienzulagen_tarif.kinderzulage_satz2_ab_alter IS
    'Wenn gesetzt: KZ Satz2 greift ab diesem Alter pro Kind (z.B. LU 12). NULL = keine Altersstaffel.';
COMMENT ON COLUMN familienzulagen_tarif.ausbildungszulage_satz2_ab_alter IS
    'Wenn gesetzt: AZ Satz2 greift ab diesem Alter pro Kind (z.B. ZG 18). NULL = keine Altersstaffel.';

-- ── Indizes ──
CREATE UNIQUE INDEX IF NOT EXISTS ux_familienzulagen_tarif_kanton_period
    ON familienzulagen_tarif (kanton_code, valid_from);

CREATE INDEX IF NOT EXISTS ix_familienzulagen_tarif_kanton_active
    ON familienzulagen_tarif (kanton_code, valid_from DESC)
    WHERE is_active = TRUE;

COMMENT ON TABLE familienzulagen_tarif IS
    'Kantonale Familienzulagen-Sätze. Massgeblich nach Standort der Filiale (company_profile.kanton_code).';

-- ─────────────────────────────────────────────────────────────────────
-- SEED-DATEN für AG, LU, BE
-- Quelle: ESTV-Übersicht "Arten und Ansätze der Zulagen nach kantonalen
-- Gesetzen", in CHF. Mindesterwerbseinkommen 2025: 7'350 CHF/Jahr.
--
-- AG: alter Tarif bis 31.12.2025 (KZ 200, AZ 250 — bisheriger Wert),
--     neuer Tarif ab 1.1.2026 (KZ 225, AZ 278 — gemäss ESTV-Tabelle).
-- LU: ab 1.1.2025: KZ 215 bis Alter 12, danach 260 bis 16; AZ 268.
-- BE: ab 1.1.2025: KZ 250, AZ 310.
--
-- UPSERT — überschreibt bestehende Einträge mit gleichem Kanton+ValidFrom
-- ─────────────────────────────────────────────────────────────────────

INSERT INTO familienzulagen_tarif (
    kanton_code, valid_from, valid_to,
    kinderzulage_satz1, kinderzulage_satz2, kinderzulage_satz2_ab_alter,
    ausbildungszulage_satz1, ausbildungszulage_satz2, ausbildungszulage_satz2_ab_alter,
    schwelle_satz2_anzahl_kinder, mindesterwerbseinkommen_jahr,
    alters_grenze_kinder, alters_grenze_ausbildung,
    quelle, bemerkung, is_active
) VALUES
    -- ─── LU 1.1.2025 → offen ───
    -- KZ: 215 bis 12. Geburtstag, danach 260 bis 16. Geburtstag.
    -- AZ: 268 (kein Satz 2). Mindesteinkommen 7'350.
    ('LU', '2025-01-01', NULL,
     215.00, 260.00, 12,
     268.00, NULL, NULL,
     NULL, 7350.00,
     16, 25,
     'https://wak.lu.ch/themen/familie_jugend/familienzulagen',
     'ESTV-Tabelle Stand 2025. KZ Satz1 bis 12. Geburtstag, Satz2 vom 12. bis 16. Geburtstag.',
     TRUE),

    -- ─── AG bis 31.12.2025 ───
    -- Bisheriger AG-Tarif: KZ 200, AZ 250.
    ('AG', '2025-01-01', '2025-12-31',
     200.00, NULL, NULL,
     250.00, NULL, NULL,
     NULL, 7350.00,
     16, 25,
     'https://www.ak51.ch/familienzulagen',
     'AG-Tarif bis 31.12.2025 — abgelöst durch neuen Tarif ab 1.1.2026.',
     TRUE),

    -- ─── AG ab 1.1.2026 → offen ───
    -- Neue AG-Sätze gemäss ESTV-Tabelle: KZ 225, AZ 278.
    ('AG', '2026-01-01', NULL,
     225.00, NULL, NULL,
     278.00, NULL, NULL,
     NULL, 7350.00,
     16, 25,
     'https://www.ak51.ch/familienzulagen',
     'ESTV-Tabelle: AG erhöht Sätze ab 1.1.2026 (KZ 200→225, AZ 250→278).',
     TRUE),

    -- ─── BE 1.1.2025 → offen ───
    -- KZ 250, AZ 310 (gemäss ESTV-Tabelle).
    ('BE', '2025-01-01', NULL,
     250.00, NULL, NULL,
     310.00, NULL, NULL,
     NULL, 7350.00,
     16, 25,
     'https://www.gef.be.ch/de/start/themen/familie/familienzulagen.html',
     'ESTV-Tabelle Stand 2025. BE hat höhere Sätze als FamZG-Minimum.',
     TRUE)
ON CONFLICT (kanton_code, valid_from) DO UPDATE SET
    valid_to                          = EXCLUDED.valid_to,
    kinderzulage_satz1                = EXCLUDED.kinderzulage_satz1,
    kinderzulage_satz2                = EXCLUDED.kinderzulage_satz2,
    kinderzulage_satz2_ab_alter       = EXCLUDED.kinderzulage_satz2_ab_alter,
    ausbildungszulage_satz1           = EXCLUDED.ausbildungszulage_satz1,
    ausbildungszulage_satz2           = EXCLUDED.ausbildungszulage_satz2,
    ausbildungszulage_satz2_ab_alter  = EXCLUDED.ausbildungszulage_satz2_ab_alter,
    schwelle_satz2_anzahl_kinder      = EXCLUDED.schwelle_satz2_anzahl_kinder,
    mindesterwerbseinkommen_jahr      = EXCLUDED.mindesterwerbseinkommen_jahr,
    alters_grenze_kinder              = EXCLUDED.alters_grenze_kinder,
    alters_grenze_ausbildung          = EXCLUDED.alters_grenze_ausbildung,
    quelle                            = EXCLUDED.quelle,
    bemerkung                         = EXCLUDED.bemerkung,
    is_active                         = EXCLUDED.is_active,
    updated_at                        = NOW();

COMMIT;
