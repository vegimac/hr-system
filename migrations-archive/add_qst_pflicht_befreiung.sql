-- Walter-Vorgabe 26.05.2026: QST-Pflicht-Prüfung beim Lohnlauf.
-- Vier neue Felder am Employee für die Behörden-Befreiung (Bestätigungsschreiben
-- der Steuerbehörde, das den MA von der QST befreit). Die anderen Befreiungs-
-- Gründe (Schweizer, C-Ausweis, Ehepartner-Schweizer, Ehepartner-C) werden zur
-- Laufzeit aus bestehenden Daten (NationalityId, EmployeePermitHistory,
-- EmployeeFamilyMember) errechnet, brauchen keine eigene Spalte.

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS qst_befreit_durch_behoerde boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS qst_befreiung_dokument_id  integer NULL,
    ADD COLUMN IF NOT EXISTS qst_befreiung_gueltig_ab   date    NULL,
    ADD COLUMN IF NOT EXISTS qst_befreiung_gueltig_bis  date    NULL;

-- FK auf employee_dokument (das Bestätigungsschreiben). ON DELETE SET NULL,
-- damit das Löschen eines Dokuments den Befreiungs-Eintrag nicht killt, aber
-- die Referenz aufräumt.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_employee_qst_befreiung_dokument'
    ) THEN
        ALTER TABLE employee
            ADD CONSTRAINT fk_employee_qst_befreiung_dokument
            FOREIGN KEY (qst_befreiung_dokument_id)
            REFERENCES employee_dokument(id)
            ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_employee_qst_befreit_behoerde
    ON employee (qst_befreit_durch_behoerde)
    WHERE qst_befreit_durch_behoerde = true;
