-- Migration: UidNummer zu company_profile hinzufügen
-- Ausführen mit: psql -d <datenbank> -f add_uid_nummer.sql

ALTER TABLE company_profile
ADD COLUMN IF NOT EXISTS uid_nummer VARCHAR(20);

-- Bestehenden Wert für McDonald's Oftringen eintragen (aus Screenshot: CHE-262.373.037)
-- UPDATE company_profile SET uid_nummer = 'CHE-262.373.037' WHERE id = <your_id>;
