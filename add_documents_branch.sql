-- ════════════════════════════════════════════════════════════════════
-- Dokumenten-Storage nach Filialen strukturieren
-- ════════════════════════════════════════════════════════════════════
-- Storage-Pfad wird:  data/documents/{branch_code}/{employee_id}/{uuid}.ext
-- statt vorher:       data/documents/{employee_id}/{uuid}.ext
--
-- Vorteil: physisch nach Filiale getrennte Ordnerstruktur,
-- pro Filiale leichter rückzulesen, exportieren, archivieren.
--
-- Bestehende Dokumente (vor diesem Update) haben branch_code = NULL
-- und werden weiterhin über den alten Pfad gefunden (Fallback im Code).
-- ════════════════════════════════════════════════════════════════════

ALTER TABLE employee_dokument
    ADD COLUMN IF NOT EXISTS branch_code text;

CREATE INDEX IF NOT EXISTS ix_employee_dokument_branch ON employee_dokument(branch_code);
