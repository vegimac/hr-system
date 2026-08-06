-- ============================================================================
-- Filial-Dokumentenverwaltung (Walter-Vorgabe 06.08.2026)
--
-- 1) Neue Tabelle company_dokument: Dokumente pro FILIALE
--    (Versicherungspolicen, AHV-/SV-Korrespondenz, QST-Unterlagen,
--    Verträge & Behörden, Sonstiges). Datei liegt im Filesystem unter
--    {Documents:StoragePath}/filiale/{company_profile_id}/{storage_filename},
--    NIE als BLOB in der DB. Kategorie = fixer Code (VERSICHERUNG, AHV_SV,
--    QST, VERTRAEGE, SONSTIGES) — keine eigene Verwaltungstabelle.
-- 2) Neue Spalte app_user.can_company_dokumente: Benutzer-Häkchen
--    «Zugriff Filial-Dokumente» in der Benutzerverwaltung. admin hat immer
--    Zugriff; alle anderen brauchen das Häkchen UND einen
--    user_branch_access-Eintrag für die betroffene Filiale.
--
-- Ausführung: direkt in TablePlus (Copy-Paste). Läuft zusätzlich idempotent
-- beim Server-Start (Program.cs) mit — doppeltes Ausführen ist harmlos.
-- Zeitstempel bewusst timestamp WITHOUT time zone (Walter-Vorgabe 30.06.2026).
-- ============================================================================

CREATE TABLE IF NOT EXISTS company_dokument (
    id                 bigserial PRIMARY KEY,
    company_profile_id integer NOT NULL,
    kategorie          text NOT NULL,
    original_filename  text NOT NULL,
    storage_filename   text NOT NULL UNIQUE,
    bemerkung          text,
    uploaded_by_name   text,
    created_at         timestamp without time zone NOT NULL DEFAULT now(),
    zugriff_am         timestamp without time zone,
    zugriff_von        text
);

CREATE INDEX IF NOT EXISTS ix_company_dokument_company_profile
    ON company_dokument (company_profile_id);

ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS can_company_dokumente boolean NOT NULL DEFAULT false;
