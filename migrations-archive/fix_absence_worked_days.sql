-- Entfernt "2026-02-26" aus worked_days der Absenz mit date_from = 2026-02-27
-- Prüfe zuerst den aktuellen Stand:
SELECT id, date_from, date_to, worked_days FROM absence WHERE date_from = '2026-02-27';

-- Dann korrigieren (entfernt 2026-02-26 aus dem JSON-Array):
UPDATE absence
SET worked_days = (
    SELECT json_agg(day)::text
    FROM jsonb_array_elements_text(worked_days::jsonb) AS day
    WHERE day != '2026-02-26'
),
updated_at = NOW()
WHERE date_from = '2026-02-27';

-- Kontrolle:
SELECT id, date_from, worked_days FROM absence WHERE date_from = '2026-02-27';
