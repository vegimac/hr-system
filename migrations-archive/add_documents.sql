-- ════════════════════════════════════════════════════════════════════
-- Dokumentenverwaltung: Personaldossier
-- ════════════════════════════════════════════════════════════════════
-- 3-stufige Hierarchie: Akte (immer "Personaldossier") → Dokumentenart
-- (Kategorie) → Dokumenttyp. Ablage von Scans & externen Dokumenten,
-- KEINE Kopien von generierten PDFs.
-- ════════════════════════════════════════════════════════════════════

-- ── Kategorien ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS dokument_kategorie (
    id          serial PRIMARY KEY,
    name        text NOT NULL,
    sort_order  int  NOT NULL DEFAULT 99,
    aktiv       boolean NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- ── Dokument-Typen (Unterkategorien) ────────────────────────────────
CREATE TABLE IF NOT EXISTS dokument_typ (
    id            serial PRIMARY KEY,
    kategorie_id  int NOT NULL REFERENCES dokument_kategorie(id) ON DELETE RESTRICT,
    name          text NOT NULL,
    sort_order    int  NOT NULL DEFAULT 99,
    aktiv         boolean NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_dokument_typ_kategorie ON dokument_typ(kategorie_id);

-- ── Mitarbeiter-Dokumente ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS employee_dokument (
    id                serial PRIMARY KEY,
    employee_id       int NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    dokument_typ_id   int NOT NULL REFERENCES dokument_typ(id) ON DELETE RESTRICT,

    -- Dateiinformationen
    filename_original text NOT NULL,        -- "Arztzeugnis_Dr_Mueller_2026-04-15.pdf"
    filename_storage  text NOT NULL UNIQUE,  -- UUID + ext, z.B. "a3b1c2d4....pdf"
    mime_type         text NOT NULL,
    groesse_bytes     bigint NOT NULL,

    -- Optionale Metadaten
    bemerkung         text,
    gueltig_von       date,
    gueltig_bis       date,                  -- z.B. Aufenthaltsbewilligung läuft ab

    -- Audit
    hochgeladen_von   int REFERENCES app_user(id) ON DELETE SET NULL,
    hochgeladen_am    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_employee_dokument_employee ON employee_dokument(employee_id);
CREATE INDEX IF NOT EXISTS ix_employee_dokument_typ      ON employee_dokument(dokument_typ_id);
CREATE INDEX IF NOT EXISTS ix_employee_dokument_gueltig  ON employee_dokument(gueltig_bis) WHERE gueltig_bis IS NOT NULL;

-- ════════════════════════════════════════════════════════════════════
-- Default-Taxonomie aus dem d.velop-Aktenplan
-- ════════════════════════════════════════════════════════════════════

-- Kategorien (in der Reihenfolge des Aktenplans)
INSERT INTO dokument_kategorie (name, sort_order) VALUES
    ('Persönliche Angaben',     10),
    ('Vertragsunterlagen',      20),
    ('Lohn / Arbeitszeit',      30),
    ('Absenzen',                40),
    ('Mitarbeiterentwicklung',  50),
    ('Ämter & Behörden',        60)
ON CONFLICT DO NOTHING;

-- Dokumenttypen pro Kategorie
DO $$
DECLARE
    k_persoenlich     int;
    k_vertrag         int;
    k_lohn            int;
    k_absenzen        int;
    k_entwicklung     int;
    k_aemter          int;
BEGIN
    SELECT id INTO k_persoenlich  FROM dokument_kategorie WHERE name = 'Persönliche Angaben';
    SELECT id INTO k_vertrag      FROM dokument_kategorie WHERE name = 'Vertragsunterlagen';
    SELECT id INTO k_lohn         FROM dokument_kategorie WHERE name = 'Lohn / Arbeitszeit';
    SELECT id INTO k_absenzen     FROM dokument_kategorie WHERE name = 'Absenzen';
    SELECT id INTO k_entwicklung  FROM dokument_kategorie WHERE name = 'Mitarbeiterentwicklung';
    SELECT id INTO k_aemter       FROM dokument_kategorie WHERE name = 'Ämter & Behörden';

    -- Persönliche Angaben
    INSERT INTO dokument_typ (kategorie_id, name, sort_order) VALUES
        (k_persoenlich, 'Aufenthaltsbewilligung',  10),
        (k_persoenlich, 'Ausweis',                 20),
        (k_persoenlich, 'Arbeitsbescheinigungen',  30),
        (k_persoenlich, 'Bankdaten',               40),
        (k_persoenlich, 'Bewerbungsunterlagen',    50),
        (k_persoenlich, 'Familienbuch',            60),
        (k_persoenlich, 'Krankenkasse',            70),
        (k_persoenlich, 'Leihgaben',               80),
        (k_persoenlich, 'Mitarbeiterfoto',         90),
        (k_persoenlich, 'Mitarbeiterstammblatt',  100),
        (k_persoenlich, 'Uniformen',              110),
        (k_persoenlich, 'Schlüssel',              120),
        (k_persoenlich, 'Sozialversicherung',     130),
        (k_persoenlich, 'Diverses',               999);

    -- Vertragsunterlagen
    INSERT INTO dokument_typ (kategorie_id, name, sort_order) VALUES
        (k_vertrag, 'Anforderungsprofil',  10),
        (k_vertrag, 'Arbeitsverträge',     20),
        (k_vertrag, 'Arbeitszeugnis',      30),
        (k_vertrag, 'Arbeitsreglemente',   40),
        (k_vertrag, 'Beförderung',         50),
        (k_vertrag, 'Kündigung',           60),
        (k_vertrag, 'Zusatzverträge',      70),
        (k_vertrag, 'Diverses',           999);

    -- Lohn / Arbeitszeit
    INSERT INTO dokument_typ (kategorie_id, name, sort_order) VALUES
        (k_lohn, 'Lohnabrechnung',        10),
        (k_lohn, 'Lohnausweis',           20),
        (k_lohn, 'Monatsblatt',           30),
        (k_lohn, 'Quellensteuer Quittung',40),
        (k_lohn, 'Diverses',             999);

    -- Absenzen
    INSERT INTO dokument_typ (kategorie_id, name, sort_order) VALUES
        (k_absenzen, 'Arztzeugnis',        10),
        (k_absenzen, 'Manuelle Stunden',   20),
        (k_absenzen, 'Militär',            30),
        (k_absenzen, 'Mutter-/Vaterschaft',40),
        (k_absenzen, 'Sonstige Absenzen',  50),
        (k_absenzen, 'Weiterbildung',      60),
        (k_absenzen, 'Zivilschutz',        70),
        (k_absenzen, 'Diverses',          999);

    -- Mitarbeiterentwicklung
    INSERT INTO dokument_typ (kategorie_id, name, sort_order) VALUES
        (k_entwicklung, 'Abmahnung',           10),
        (k_entwicklung, 'Beförderung',         20),
        (k_entwicklung, 'Beurteilung',         30),
        (k_entwicklung, 'Boni',                40),
        (k_entwicklung, 'Mitarbeitergespräch', 50),
        (k_entwicklung, 'Probezeitgespräch',   60),
        (k_entwicklung, 'Weiterbildung',       70),
        (k_entwicklung, 'Zielvereinbarung',    80),
        (k_entwicklung, 'Diverses',           999);

    -- Ämter & Behörden
    INSERT INTO dokument_typ (kategorie_id, name, sort_order) VALUES
        (k_aemter, 'Bescheinigungen',       10),
        (k_aemter, 'Bewilligungen',         20),
        (k_aemter, 'Krankenversicherung',   30),
        (k_aemter, 'Kinderzulagen',         40),
        (k_aemter, 'L-GAV Quittung',        50),
        (k_aemter, 'Pensionskasse',         60),
        (k_aemter, 'Unfallversicherung',    70),
        (k_aemter, 'Zusatzversicherung',    80),
        (k_aemter, 'Diverses',             999);
END $$;
