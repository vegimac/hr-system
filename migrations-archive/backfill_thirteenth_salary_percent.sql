-- Walter-Vorgabe: bei ALLEN Vertragsmodellen wird der 13. Monatslohn mit 8.33%
-- akkumuliert. Die Auszahlungs-Logik (UTP monatlich vs. MTP/FIX nach Vorgabe)
-- liegt im PayrollController, nicht am Prozentsatz.
--
-- Bug-Ursache: bisher hat der Importer thirteenth_salary_percent für FIX und
-- FIX-M auf NULL gesetzt — dadurch wurde im PayrollController nichts in den
-- Saldo akkumuliert. Daher der Saldo „Rückst. 13. Monatslohn (CHF) 0.00".
--
-- Backfill: alle aktiven Verträge ohne 13.ML-Prozent auf 8.33% setzen.
-- Bestehende Werte bleiben unangetastet.

-- 1) Vorschau wer betroffen wäre
SELECT employment_model, COUNT(*) AS leer_count
FROM   employment
WHERE  is_active = TRUE
  AND  contract_end_date IS NULL
  AND  thirteenth_salary_percent IS NULL
GROUP  BY employment_model
ORDER  BY employment_model;

-- 2) Update — nur null-Werte werden gesetzt, bestehende Werte werden nicht überschrieben
UPDATE employment
SET    thirteenth_salary_percent = 8.33
WHERE  is_active = TRUE
  AND  contract_end_date IS NULL
  AND  thirteenth_salary_percent IS NULL;

-- 3) Kontrolle nach Update
SELECT employment_model,
       COUNT(*)                                         AS total_aktiv,
       COUNT(*) FILTER (WHERE thirteenth_salary_percent IS NULL) AS leer,
       AVG(thirteenth_salary_percent)                   AS avg_pct
FROM   employment
WHERE  is_active = TRUE
  AND  contract_end_date IS NULL
GROUP  BY employment_model
ORDER  BY employment_model;
