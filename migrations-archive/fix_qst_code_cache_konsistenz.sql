-- Fix 23.08.2026 (Fall Hristijan Majstorska): qst_code ist reiner Anzeige-
-- Cache und war vereinzelt inkonsistent zum gerechneten tarif_code
-- (Liste/Lohnzettel zeigten C0N, gerechnet wurde A). Ab sofort leitet der
-- Server qst_code IMMER aus tarif_code+anzahl_kinder+kirchensteuer ab;
-- dieses SQL heilt den Altbestand. Für TablePlus, idempotent.
UPDATE employee_quellensteuer
SET qst_code = upper(btrim(tarif_code)) || anzahl_kinder::text
             || CASE WHEN kirchensteuer THEN 'Y' ELSE 'N' END
WHERE tarif_code IS NOT NULL AND btrim(tarif_code) <> ''
  AND qst_code IS DISTINCT FROM upper(btrim(tarif_code)) || anzahl_kinder::text
             || CASE WHEN kirchensteuer THEN 'Y' ELSE 'N' END;
