-- Lohndatenempfänger (Walter-Vorgabe 06.08.2026, Mirus-Vorbild «Lohndatenempfänger»)
-- Zentraler Katalog (Adresse/Kassennummer EINMAL) + Zuordnung pro Filiale mit
-- Mitglied-/Subnummer. Läuft auch idempotent beim Server-Start (Program.cs).
-- In TablePlus ausführen.

CREATE TABLE IF NOT EXISTS lohndaten_empfaenger (
    id            serial PRIMARY KEY,
    art           text NOT NULL,           -- AUSGLEICHSKASSE | FAK | KTG | UVG | BVG | QST | LOHNAUSWEIS | ANDERE
    bezeichnung   text NOT NULL,           -- «AK GastroSocial», «Swica Versicherungen KTG» …
    zusatz        text,
    uid_nummer    text,
    strasse       text,
    postfach      text,
    plz           text,
    ort           text,
    kanton_code   text,                    -- bei QST: Steuer-Kanton
    kassennummer  text,                    -- «046.000» — gehört zur Kasse
    support_email text,
    bemerkung     text,
    is_active     boolean NOT NULL DEFAULT true,
    created_at    timestamp without time zone NOT NULL DEFAULT now(),
    updated_at    timestamp without time zone NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS company_profile_empfaenger (
    id                 serial PRIMARY KEY,
    company_profile_id integer NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
    empfaenger_id      integer NOT NULL REFERENCES lohndaten_empfaenger(id) ON DELETE CASCADE,
    mitgliednummer     text,               -- «629.0714.00» — pro Filiale/GmbH verschieden
    subnummer          text,
    bemerkung          text,
    is_active          boolean NOT NULL DEFAULT true,
    created_at         timestamp without time zone NOT NULL DEFAULT now(),
    updated_at         timestamp without time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_cp_empfaenger_company_profile
    ON company_profile_empfaenger (company_profile_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cp_empfaenger_cp_empf
    ON company_profile_empfaenger (company_profile_id, empfaenger_id);

-- Nachtrag 06.08.2026: Gültig-ab auf der Zuordnung (UVG-Wechsel etc.)
ALTER TABLE company_profile_empfaenger
    ADD COLUMN IF NOT EXISTS gueltig_ab date;
