-- Phantom-MAs („MA ohne Lohn") markieren
-- ---------------------------------------------------------------
-- Hintergrund: Supervisor wie Nihat Erdikli sind in jeder Filiale als
-- easy@work-User angelegt, damit sie Stempelzeiten freigeben können.
-- Sie haben aber KEINEN Lohnvertrag bei uns — sie werden zentral über
-- McDonald's bezahlt. Im neuen Importer setzen wir is_payroll_excluded=true
-- automatisch wenn Group membership = 'Supervisor' ist. Für bereits
-- importierte Datensätze müssen wir das einmalig nachziehen.
--
-- Vorsicht: Walter hat „MA ohne Lohn" bisher manuell für einige MAs
-- gesetzt. Diese Markierungen NIE überschreiben — wir setzen nur
-- zusätzlich auf TRUE, niemals zurück auf FALSE.
-- ---------------------------------------------------------------

-- 1) Konkret bekannte Phantom-MAs (Walter-bestätigt: Nihat Erdikli)
UPDATE employee
SET    is_payroll_excluded = TRUE
WHERE  is_payroll_excluded = FALSE
  AND  first_name = 'Nihat'
  AND  last_name  = 'Erdikli';

-- 2) Vorschau weiterer Kandidaten ohne Lohn-Kontext:
--    MA ohne aktiven Vertrag UND ohne IBAN UND in mehreren Filialen.
--    Bitte VOR Ausführung prüfen!
SELECT e.id,
       e.first_name,
       e.last_name,
       e.employee_number,
       e.is_payroll_excluded,
       (SELECT COUNT(*) FROM employment WHERE employee_id = e.id) AS contract_count,
       (SELECT COUNT(*) FROM bank_account WHERE employee_id = e.id) AS bank_count
FROM   employee e
WHERE  e.is_payroll_excluded = FALSE
  AND  NOT EXISTS (SELECT 1 FROM employment   WHERE employee_id = e.id)
  AND  NOT EXISTS (SELECT 1 FROM bank_account WHERE employee_id = e.id)
ORDER  BY e.last_name, e.first_name;

-- 3) Wenn die Kandidatenliste oben nur Supervisor enthält:
-- UPDATE employee
-- SET    is_payroll_excluded = TRUE
-- WHERE  is_payroll_excluded = FALSE
--   AND  NOT EXISTS (SELECT 1 FROM employment   WHERE employee_id = employee.id)
--   AND  NOT EXISTS (SELECT 1 FROM bank_account WHERE employee_id = employee.id);
