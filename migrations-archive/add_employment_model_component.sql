-- ====================================================================
-- Migration: Vertragstyp-Komponenten
-- Ausführen mit: psql -d <datenbank> -f add_employment_model_component.sql
-- ====================================================================
--
-- Zweck:
--   Pro Vertragstyp (FIX, FIX-M, MTP, UTP) konfigurierbar machen, welche
--   Lohnpositionen als Kern-Komponenten anfallen — und mit welchem
--   Default-Parameter (Prozentsatz). Ersetzt perspektivisch die hart
--   verdrahtete Logik im PayrollController (if isFIX / else if isUTP …).
--
--   Phase 1 (jetzt):    Tabelle anlegen, Stammdaten seedn, Admin-UI zum
--                       Anschauen und Bearbeiten.
--   Phase 2 (später):   PayrollController liest diese Tabelle statt
--                       hardcoded zu rechnen.
-- ====================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS employment_model_component (
    id                     SERIAL PRIMARY KEY,
    employment_model_code  VARCHAR(10) NOT NULL,        -- FIX | FIX-M | MTP | UTP
    lohnposition_id        INTEGER NOT NULL REFERENCES lohnposition(id) ON DELETE CASCADE,
    rate                   NUMERIC(8,4),                 -- Prozent / Default-Satz; NULL = keiner
    is_active              BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order             INTEGER NOT NULL DEFAULT 99,
    bemerkung              TEXT,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT employment_model_component_unique
        UNIQUE (employment_model_code, lohnposition_id)
);

CREATE INDEX IF NOT EXISTS idx_employment_model_component_model
    ON employment_model_component (employment_model_code, is_active, sort_order);

COMMENT ON TABLE employment_model_component IS
    'Katalog: welche Lohnpositionen sind pro Vertragstyp aktiv, mit welchem Default-Satz.';
COMMENT ON COLUMN employment_model_component.rate IS
    'Default-Prozentsatz fuer diese Komponente (z.B. 3.595 fuer Feiertagsentschaedigung UTP). NULL wenn nicht zutreffend.';

-- ────────────────────────────────────────────────────────────────────
-- Seed-Daten: Default-Zuordnung pro Vertragstyp, abgeleitet aus der
-- aktuell hart verdrahteten PayrollController-Logik.
-- Wenn eine Lohnposition im System noch nicht existiert, wird der
-- Eintrag still übersprungen (LEFT JOIN auf lohnposition.code).
-- ────────────────────────────────────────────────────────────────────

-- Helper: füge einen Seed-Eintrag hinzu, wenn die Lohnposition existiert
-- und der (model, lohnposition)-Schlüssel noch nicht gesetzt ist.
DO $$
DECLARE
    seeds TEXT[][] := ARRAY[
        -- model, code, rate, sort, bemerkung
        -- ── FIX (Festlohn, Monatslohn-Modell) ──────────────────────
        ARRAY['FIX',  '10.1',  NULL,     '10', 'Festlohn (Monatslohn nach Pensum)'],
        ARRAY['FIX',  '180.1', '8.33',   '80', '13. Monatslohn-Rückstellung (Standard 8.33%)'],

        -- ── FIX-M (Kader, wie FIX) ─────────────────────────────────
        ARRAY['FIX-M', '10.1',  NULL,     '10', 'Festlohn (Monatslohn nach Pensum)'],
        ARRAY['FIX-M', '180.1', '8.33',   '80', '13. Monatslohn-Rückstellung (Standard 8.33%)'],

        -- ── MTP (Monatslohn mit Pensum + Stunden-Saldo) ────────────
        ARRAY['MTP',  '10.1',  NULL,     '10', 'Festlohn (pro-rata nach Pensum)'],
        ARRAY['MTP',  '10.4',  NULL,     '15', 'Zusatzstunden (ueber Sollpensum)'],
        ARRAY['MTP',  '10.3',  '3.595',  '30', 'Festlohn Feiertage (Basis-Tracking)'],
        ARRAY['MTP',  '195.1', '8.33',   '70', 'Ferienentschaedigung (5 Wochen: 8.33%, 6 Wochen: 10.64%)'],
        ARRAY['MTP',  '180.1', '8.33',   '80', '13. Monatslohn (Standard 8.33%)'],

        -- ── UTP (Stundenloehner) ───────────────────────────────────
        ARRAY['UTP',  '20.1',  NULL,     '10', 'Stundenlohn'],
        ARRAY['UTP',  '20.3',  '3.595',  '30', 'Stundenlohn Feiertage (Feiertagsentschaedigung)'],
        ARRAY['UTP',  '195.2', '8.33',   '70', 'Ferienentschaedigung (5 Wochen: 8.33%, 6 Wochen: 10.64%)'],
        ARRAY['UTP',  '180.1', '8.33',   '80', '13. Monatslohn (monatlich ausbezahlt)']
    ];
    s TEXT[];
    lp_id INTEGER;
BEGIN
    FOREACH s SLICE 1 IN ARRAY seeds LOOP
        SELECT id INTO lp_id FROM lohnposition WHERE code = s[2] LIMIT 1;
        IF lp_id IS NULL THEN
            RAISE NOTICE 'Lohnposition code=% nicht gefunden, ueberspringe %-Seed', s[2], s[1];
            CONTINUE;
        END IF;

        INSERT INTO employment_model_component
            (employment_model_code, lohnposition_id, rate, sort_order, bemerkung)
        VALUES
            (s[1], lp_id, NULLIF(s[3], 'NULL')::NUMERIC, s[4]::INTEGER, s[5])
        ON CONFLICT (employment_model_code, lohnposition_id) DO NOTHING;
    END LOOP;
END $$;

-- Kontrolle:
-- SELECT emc.employment_model_code AS model, lp.code, lp.bezeichnung,
--        emc.rate, emc.sort_order, emc.is_active
--   FROM employment_model_component emc
--   JOIN lohnposition lp ON lp.id = emc.lohnposition_id
--  ORDER BY emc.employment_model_code, emc.sort_order, lp.code;

COMMIT;
