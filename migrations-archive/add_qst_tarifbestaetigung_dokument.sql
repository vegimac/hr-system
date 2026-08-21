-- Walter-Vorgabe 21.08.2026: Tarifbestätigung der Steuerbehörde als
-- Beleg-Dokument pro QST-Version verknüpfen (gleicher Mechanismus wie
-- Ehepartner-Ausweis / Bewilligungs-Beleg). Läuft auch idempotent beim
-- Server-Start (Program.cs).

ALTER TABLE employee_quellensteuer
    ADD COLUMN IF NOT EXISTS dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;
