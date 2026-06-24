-- Zweiter Nachtarbeit-Beleg am MA (Walter-Vorgabe 22.06.2026, ArG/SECO):
-- Slot für die unterschriebene „Ausnahmeregelung zum Wechsel zwischen Tag- und
-- Nachtarbeit". Der bestehende night_work_exam_dokument_id bleibt der Slot für
-- Arztbericht/Eignungszeugnis ODER Verzichtserklärung. Beide Belege sind für die
-- spätere Kontrolle nebeneinander verknüpfbar und anzeigbar.
-- In TablePlus ausführen.

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS night_work_ausnahme_dokument_id
    INTEGER REFERENCES employee_dokument(id) ON DELETE SET NULL;
