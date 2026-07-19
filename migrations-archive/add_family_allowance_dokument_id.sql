-- ──────────────────────────────────────────────────────────────────────
-- Walter-Vorgabe 19.07.2026: Entscheidungsdokument an Familienzulage.
--
-- Beim Erfassen/Ändern einer Kinder-/Ausbildungszulage kann der FAK-
-- Entscheid (Dokument aus dem MA-Dossier) verknüpft werden — und
-- erscheint als Info-Vorschau neben dem Zulage-Modal.
--
-- ON DELETE SET NULL: Doku gelöscht → Verknüpfung fällt still weg.
-- Optional: linked_field_code 'family_allowance' am Dokumenttyp
-- «Kinderzulagen» für die gezielte Auswahl im Picker.
-- ──────────────────────────────────────────────────────────────────────

ALTER TABLE family_member_allowance
    ADD COLUMN IF NOT EXISTS dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_family_member_allowance_dokument
    ON family_member_allowance(dokument_id);

-- Bestehenden Typ «Kinderzulagen» (falls vorhanden) als FAK-Entscheid markieren.
UPDATE dokument_typ
SET linked_field_code = 'family_allowance'
WHERE linked_field_code IS NULL
  AND lower(name) IN ('kinderzulagen', 'kinderzulage', 'familienzulagen', 'fak-entscheid', 'fak entscheid');
