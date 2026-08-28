-- Hauptsitz/Rechtseinheit (Walter 29.08.2026)
-- Eigene Verwaltung neben den Filialen; mehrere Hauptsitze möglich
-- (Lizenznehmer mit 2 GmbHs). Filiale → Hauptsitz via company_profile.hauptsitz_id.
-- Läuft auch idempotent beim App-Start (Program.cs); TablePlus-Kopie.

CREATE TABLE IF NOT EXISTS hauptsitz (
    id          serial PRIMARY KEY,
    name        varchar(200) NOT NULL,
    uid         varchar(20),
    strasse     varchar(200),
    plz         varchar(15),
    ort         varchar(120),
    kanton_code varchar(2),
    bemerkung   text,
    is_active   boolean NOT NULL DEFAULT true,
    created_at  timestamp without time zone NOT NULL DEFAULT now(),
    updated_at  timestamp without time zone NOT NULL DEFAULT now()
);

ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS hauptsitz_id integer REFERENCES hauptsitz(id) ON DELETE SET NULL;
