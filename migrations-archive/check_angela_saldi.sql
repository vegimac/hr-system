-- Welcher Saldo ist tatsächlich in der DB für Angela (580006)?
-- Vergleiche mit dem April-Lohnzettel.

SELECT
    period_year,
    period_month,
    status,
    hour_saldo,
    ferien_tage_saldo,
    feiertag_tage_saldo,
    ferien_geld_saldo,
    thirteenth_month_accumulated,
    company_profile_id,
    updated_at
FROM   payroll_saldos
WHERE  employee_id = (SELECT id FROM employees WHERE employee_number = '580006')
ORDER  BY period_year DESC, period_month DESC, company_profile_id;

-- Falls der Saldo für April 2026 stale ist (z.B. 46.67 statt 10.67),
-- am einfachsten: April-Lohn neu öffnen, die Werte stimmen automatisch,
-- dann wieder bestätigen → DB ist aktuell.

-- Alternative: direkt korrigieren
-- UPDATE payroll_saldos
-- SET    ferien_tage_saldo = 10.67
-- WHERE  employee_id = (SELECT id FROM employees WHERE employee_number = '580006')
--   AND  period_year = 2026
--   AND  period_month = 4;
