-- Angela Skarcheska (Nr. 580006): alten Vertrag wieder öffnen,
-- damit der CSV-Import ihn als "aktiven Vertrag" findet und die
-- Lohnänderung (neuer Lohn ab 01.01.2026) korrekt erkennt.
--
-- Wenn beim ersten (versehentlichen) Import ein neuer Vertrag erzeugt
-- wurde, hat die POST /api/employments - Auto-Close-Logik den alten
-- Vertrag mit contract_end_date = 31.12.2025 geschlossen. Nach dem
-- Löschen des neuen Vertrags blieb der alte geschlossen zurück.

-- 1) Aktueller Stand prüfen
SELECT id, employee_id, contract_start_date, contract_end_date,
       employment_model, monthly_salary_fte, job_title
FROM   employments
WHERE  employee_id = (SELECT id FROM employees WHERE employee_number = '580006')
ORDER  BY contract_start_date DESC;

-- 2) Vertrag wieder öffnen (NUR ausführen, wenn oben der Vertrag mit
--    start=2022-08-01 ein contract_end_date hat und KEIN neuerer Vertrag
--    existiert)
UPDATE employments
SET    contract_end_date = NULL
WHERE  employee_id = (SELECT id FROM employees WHERE employee_number = '580006')
  AND  contract_start_date = '2022-08-01'
  AND  contract_end_date IS NOT NULL
  AND  NOT EXISTS (
        SELECT 1 FROM employments e2
        WHERE  e2.employee_id = employments.employee_id
          AND  e2.contract_start_date > employments.contract_start_date
  );

-- 3) Ergebnis kontrollieren
SELECT id, contract_start_date, contract_end_date, monthly_salary_fte
FROM   employments
WHERE  employee_id = (SELECT id FROM employees WHERE employee_number = '580006')
ORDER  BY contract_start_date DESC;
