-- ====================================================================
-- Migration: swiss_location — Schweizer PLZ/Gemeinden/Kantons-Stammdaten
-- Ausführen:
--   psql -d <datenbank> -f add_swiss_location.sql
-- ====================================================================
--
-- Zweck:
--   PLZ-zu-Ort/Gemeinde/Kanton-Lookup für die Mitarbeiter-Adresse.
--   Datenquelle: Amtliches Ortschaftenverzeichnis der Schweizerischen
--   Post (AMTOVZ_CSV_WGS84).
--
--   Bei Eingabe einer PLZ im MA-Stamm werden die zugehörigen Gemeinden
--   automatisch vorgeschlagen; bei mehreren Treffern erscheint eine
--   Auswahl.
--
--   Aus dem Original-CSV werden nur 4 Spalten übernommen:
--     plz4, gemeindename, bfs_nr, kantonskuerzel
--   Dedupliziert auf unique (plz4, bfs_nr) — pro PLZ/Gemeinde ein Eintrag.
--
-- Nach Schema-Migration den Daten-Import ausführen (siehe README-Block
-- unten am Ende dieses Files).
-- ====================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS swiss_location (
    id               SERIAL PRIMARY KEY,
    plz4             VARCHAR(4)  NOT NULL,
    gemeindename     VARCHAR(80) NOT NULL,
    bfs_nr           INTEGER     NOT NULL,
    kantonskuerzel   VARCHAR(2)  NOT NULL,

    CONSTRAINT swiss_location_plz_bfs_unique UNIQUE (plz4, bfs_nr)
);

CREATE INDEX IF NOT EXISTS idx_swiss_location_plz
    ON swiss_location (plz4);
CREATE INDEX IF NOT EXISTS idx_swiss_location_kanton
    ON swiss_location (kantonskuerzel);

COMMENT ON TABLE swiss_location IS
    'Schweizer PLZ-zu-Gemeinde/Kanton-Stammdaten (Quelle: AMTOVZ Schweiz. Post).';

COMMIT;

-- ====================================================================
-- Daten-Import (separater Schritt, client-seitig mit \copy):
--
--   \copy swiss_location(plz4,gemeindename,bfs_nr,kantonskuerzel) \
--     FROM 'data_swiss_locations.csv' \
--     WITH (FORMAT csv, DELIMITER ';', HEADER true)
--
-- Oder als einzelner Aufruf mit psql:
--   psql -d <datenbank> -c "\copy swiss_location(plz4,gemeindename,bfs_nr,kantonskuerzel) FROM 'data_swiss_locations.csv' WITH (FORMAT csv, DELIMITER ';', HEADER true)"
--
-- Bei erneutem Import die Tabelle vorher leeren:
--   TRUNCATE swiss_location RESTART IDENTITY;
-- ====================================================================
