SELECT id, employee_id, absence_type, date_from, date_to, worked_days, prozent, hours_credited
FROM absence
WHERE absence_type = 'FEIERTAG'
  AND date_from = '2026-02-24';