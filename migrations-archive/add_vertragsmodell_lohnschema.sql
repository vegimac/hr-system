-- ============================================================================
-- Lohnschema pro Vertragsmodell (Walter 17.08.2026, Phase 2 des Konzepts
-- docs/lohnschema-vertragsmodelle.docx). Standard-Lohnblatt pro Modell.
-- HINWEIS: Diese Migration läuft auch idempotent beim Server-Start
-- (Program.cs) — manuelles Ausführen in TablePlus ist optional.
-- ============================================================================
CREATE TABLE IF NOT EXISTS vertragsmodell_lohnschema (
    id              serial PRIMARY KEY,
    modell          text NOT NULL,
    lohnposition_id integer NOT NULL REFERENCES lohnposition(id) ON DELETE CASCADE,
    art             text NOT NULL DEFAULT 'automatisch',
    sort_order      integer NOT NULL DEFAULT 0,
    bemerkung       text,
    created_at      timestamp without time zone NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_vm_lohnschema
    ON vertragsmodell_lohnschema (modell, lohnposition_id, art);
-- Seed: siehe Program.cs (läuft nur in leere Tabelle; Codes per Join aufgelöst).
