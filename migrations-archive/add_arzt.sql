-- Aerzte-Verzeichnis (Walter-Vorgabe 16.07.2026) — behandelnde Aerzte der MA,
-- verwendet im Mutterschafts-Modul (Brief an den behandelnden Arzt).
-- Laeuft auch idempotent beim Server-Start in Program.cs (inkl. Erst-Seed
-- Frauenzentrum Sursee in die leere Tabelle).

CREATE TABLE IF NOT EXISTS arzt (
    id           serial PRIMARY KEY,
    titel        text,
    vorname      text NOT NULL DEFAULT '',
    nachname     text NOT NULL,
    fachgebiet   text,
    praxis_name  text,
    strasse      text,
    plz          text,
    ort          text,
    telefon      text,
    email        text,
    bemerkung    text,
    aktiv        boolean NOT NULL DEFAULT true,
    created_at   timestamp without time zone NOT NULL DEFAULT now()
);
