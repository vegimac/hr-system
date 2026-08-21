-- Walter-Vorgabe 20.08.2026: Wohnort-Historie um die genaue STRASSE ergänzen.
-- Wenig Daten, aber wertvoll bei Behörden-Rückfragen («wo wohnte X am
-- Stichtag?»). Rückwirkend leer; ab jetzt schreibt der easy@work-Sync bei
-- jedem Adresswechsel die alte und neue Strasse mit.
-- Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE employee_wohnort_history
    ADD COLUMN IF NOT EXISTS strasse text;

-- Backfill (einmalig, sicher): wo der Historie-Eintrag der AKTUELLEN Adresse
-- des MA entspricht (gleiche PLZ + Ort), die heutige Strasse nachtragen —
-- ältere Einträge bleiben leer (die alte Strasse ist nicht mehr bekannt).
UPDATE employee_wohnort_history h
SET strasse = e.street
FROM employee e
WHERE e.id = h.employee_id
  AND h.strasse IS NULL
  AND h.plz = e.zip_code
  AND h.ort = e.city;
