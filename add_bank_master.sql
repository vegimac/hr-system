-- ====================================================================
-- Migration: Bank-Stammdaten-Tabelle
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_bank_master.sql
-- ====================================================================
--
-- Zweck:
--   Lokale Ablage der Schweizer Bank-Stammdaten (SIX Interbank Clearing).
--   Ersetzt die frühere Data/bank_master.csv-Datei — Vorteile: User kann
--   selbst per Admin-UI importieren/aktualisieren, Einträge manuell
--   korrigieren, Backup läuft mit der DB.
--
--   Datenquelle bleibt dieselbe:
--     https://www.six-group.com/interbank-clearing/de/home/bank-master-data/
--
-- Felder:
--   iid        5-stellige Institut-ID (Stellen 5–9 einer CH/LI-IBAN), PK
--   bic        SWIFT/BIC (optional)
--   name       Bankname
--   ort        Stadt
--   strasse    Strasse (optional)
--   plz        Postleitzahl (optional)
--   imported_at  Zeitstempel des letzten Imports/Updates
-- ====================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS bank_master (
    iid          VARCHAR(10)  PRIMARY KEY,
    bic          VARCHAR(15),
    name         VARCHAR(200) NOT NULL,
    ort          VARCHAR(100),
    strasse      VARCHAR(200),
    plz          VARCHAR(10),
    imported_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_bank_master_name
    ON bank_master(LOWER(name));

COMMENT ON TABLE bank_master IS
    'Schweizer Bank-Stammdaten aus der SIX Interbank Clearing Liste. Pro IID ein Eintrag.';

COMMIT;
