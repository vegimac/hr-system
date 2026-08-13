-- BFS Lohnstrukturerhebung (Walter-Vorgabe 13.08.2026), Phase 1.
-- Grundlage: technische BFS-Spezifikation LSE 2024, V1.4/12.2024.
-- Läuft auch idempotent beim Server-Start (Program.cs); die Version-2024-
-- Konfiguration (Codes/Bereiche/Pflichtfelder/Spalten) wird per EF geseedet.
-- Ausführen in TablePlus:

CREATE TABLE IF NOT EXISTS lse_version (
    id           serial PRIMARY KEY,
    survey_year  integer NOT NULL UNIQUE,
    spec_version text,
    is_active    boolean NOT NULL DEFAULT true,
    config_json  text NOT NULL DEFAULT '{}',
    created_at   timestamp without time zone NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS employee_lse (
    id                   serial PRIMARY KEY,
    employee_id          integer NOT NULL UNIQUE REFERENCES employee(id) ON DELETE CASCADE,
    education            integer,
    university_degree    integer,
    position_override    integer,
    practiced_profession varchar(255),
    in_house_id          varchar(50),
    updated_at           timestamp without time zone NOT NULL DEFAULT now(),
    updated_by           text
);

CREATE TABLE IF NOT EXISTS lse_lohnart_mapping (
    id            serial PRIMARY KEY,
    lohnart_code  text NOT NULL,
    bezeichnung   text,
    bfs_kategorie text,
    gueltig_ab    date,
    gueltig_bis   date,
    confirmed     boolean NOT NULL DEFAULT false,
    updated_at    timestamp without time zone NOT NULL DEFAULT now(),
    updated_by    text
);
CREATE INDEX IF NOT EXISTS ix_lse_lohnart_mapping_code ON lse_lohnart_mapping (lohnart_code);

CREATE TABLE IF NOT EXISTS lse_code_mapping (
    id          serial PRIMARY KEY,
    mapping_typ text NOT NULL,
    source_code text NOT NULL,
    bfs_code    integer,
    confirmed   boolean NOT NULL DEFAULT false,
    updated_at  timestamp without time zone NOT NULL DEFAULT now(),
    updated_by  text,
    UNIQUE (mapping_typ, source_code)
);

-- BUR-Nummer (LSE Spalte AR) + UID (Spalte R) pro Filiale/örtlicher Einheit.
ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS bur_nr varchar(8);
ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS uid_bfs varchar(20);
