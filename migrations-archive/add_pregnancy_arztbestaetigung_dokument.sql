-- Mutterschaft: Arztbestätigung errechneter Termin verknüpfen (Walter 20.07.2026)
-- In TablePlus ausführen. Beim Speichern der Schwangerschaft wird ein
-- MA-Dokument (typ. Absenzen → Mutter-/Vaterschaft / Arztzeugnis) verknüpft
-- und im Arztbrief-Dialog angezeigt.

ALTER TABLE employee_pregnancy
    ADD COLUMN IF NOT EXISTS arztbestaetigung_dokument_id INTEGER
        REFERENCES employee_dokument(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_pregnancy_arztbestaetigung_dok
    ON employee_pregnancy(arztbestaetigung_dokument_id)
    WHERE arztbestaetigung_dokument_id IS NOT NULL;
