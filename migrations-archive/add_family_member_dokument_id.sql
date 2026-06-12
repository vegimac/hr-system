-- ──────────────────────────────────────────────────────────────────────
-- Walter-Vorgabe 13.06.2026: explizite Verknüpfung Spouse → Beleg-Doku.
--
-- Analog zu employee.id_pass_dokument_id / c_ausweis_dokument_id, nur
-- am Familienmitglied. Damit kann beim Ehepartner explizit das
-- hinterlegte Ausweis-Dokument (ID/Pass für Schweizer Spouse, oder
-- Bewilligung für C-Ausweis-Spouse) verlinkt werden.
--
-- Bisher prüfte QstPflichtCheckService den Spouse-Beleg über einen
-- unscharfen linked_field_code='spouse'-Scan über alle MA-Dokumente.
-- Das kollidierte mit der neuen Logik, die für MA selbst explizite FKs
-- nutzt — und konnte falsche „Belege" enthalten (z.B. ein altes Spouse-
-- Doku, das zu einem inzwischen geschiedenen Ehepartner gehörte).
--
-- Mit der expliziten Verknüpfung am Family-Member-Datensatz ist der
-- Beleg eindeutig und automatisch konsistent — wenn der Family-Member
-- gelöscht wird, ist die Verknüpfung weg.
--
-- ON DELETE SET NULL: wenn der MA das Beleg-Doku in den Dokumenten
-- löscht, wird die FK still aufgehoben.
-- ──────────────────────────────────────────────────────────────────────

ALTER TABLE employee_family_member
    ADD COLUMN IF NOT EXISTS dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;
