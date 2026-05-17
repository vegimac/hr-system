-- ════════════════════════════════════════════════════════════════
-- Vertrags-Import aus CSV  (44 Verträge, 8 übersprungen)
-- ════════════════════════════════════════════════════════════════
-- Modell-Logik:
--   Fix + Monatsgehalt (Management) → FIX-M
--   Fix + Monatsgehalt              → FIX   (employment_percentage aus Anzahl)
--   Fix + Stundenlohn               → FIX   (employment_percentage aus Anzahl)
--   MTP/TPM                         → MTP   (weekly_hours + guaranteed_hours aus Anzahl)
--   Flex                            → UTP   (keine Stundenkontrolle)
-- Standardwerte: vacation_percent=8.33%, holiday_percent=2.27%, 13.Monat=8.33%

BEGIN;

DELETE FROM employment;

-- Ksenija Adamovic (580026) → FIX-M | monthly | 42 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'FIX-M', 'monthly',
    '1999-07-12', NULL,
    'unbefristet', 'Shift Coordinator',
    100.0, NULL, NULL,
    NULL, 5150.0,
    8.33, 2.27, 8.33, 'vacation_account', true
FROM employee e WHERE e.employee_number = '580026' LIMIT 1;

-- Feride Alimi (580019) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2019-09-21', NULL,
    'unbefristet', '',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580019' LIMIT 1;

-- Aurelio Artiles Santana (580090) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2025-11-10', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580090' LIMIT 1;

-- Jadranka Cavara (580063) → FIX | hourly | 24 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'FIX', 'hourly',
    '2025-02-11', NULL,
    'unbefristet', 'Crew',
    57.14, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580063' LIMIT 1;

-- Dragana Cuzdi (580093) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2025-12-01', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580093' LIMIT 1;

-- Gamze Demirel (580020) → MTP | hourly | 34 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2013-06-10', NULL,
    'unbefristet', 'Crew Trainer',
    NULL, 34.0, 34.0,
    21.5, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580020' LIMIT 1;

-- Snezana-Nena Dobrosavljevic (580048) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2003-03-01', NULL,
    'unbefristet', '',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580048' LIMIT 1;

-- Satar Eliassi (580045) → MTP | hourly | 33 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2009-03-21', NULL,
    'unbefristet', '',
    NULL, 33.0, 33.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580045' LIMIT 1;

-- Sevim Erdikli (580046) → MTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2012-01-11', NULL,
    'unbefristet', 'Crew Trainer',
    NULL, 17.0, 17.0,
    21.5, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580046' LIMIT 1;

-- Lorin Erdikli (580030) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2023-01-30', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580030' LIMIT 1;

-- Bernarda Gegic (580060) → MTP | hourly | 34 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2025-02-01', NULL,
    'unbefristet', 'Shift Coordinator',
    NULL, 34.0, 34.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580060' LIMIT 1;

-- Liridona Gjoni (580028) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2021-05-21', NULL,
    'unbefristet', 'Crew',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580028' LIMIT 1;

-- Eleni Halili (580014) → FIX | hourly | 24 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'FIX', 'hourly',
    '2024-08-29', NULL,
    'unbefristet', 'Hostess / Host',
    57.14, NULL, NULL,
    20.93, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580014' LIMIT 1;

-- Chanbopha Heng (580011) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2024-07-04', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580011' LIMIT 1;

-- Senada Imsirovic (580082) → FIX-M | hourly | 42 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'FIX-M', 'hourly',
    '2025-10-01', NULL,
    'unbefristet', 'Restaurant Manager - Neveau 1',
    100.0, NULL, NULL,
    NULL, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580082' LIMIT 1;

-- Tanja Ivkovikj (580051) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2019-07-03', NULL,
    'unbefristet', '',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580051' LIMIT 1;

-- Ousman Jammeh (580059) → UTP | hourly | 8.5 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2025-01-01', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.44, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580059' LIMIT 1;

-- Marjana Jangelovski (580032) → MTP | hourly | 26 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2024-05-21', NULL,
    'unbefristet', 'Crew',
    NULL, 26.0, 26.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580032' LIMIT 1;

-- Sukaina Jessa (580049) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2018-02-08', NULL,
    'unbefristet', 'Crew Trainer',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580049' LIMIT 1;

-- Khairoon Jessa (580025) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2018-03-01', NULL,
    'unbefristet', '',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580025' LIMIT 1;

-- Naziktere Jumeri (580035) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2022-11-28', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.4, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580035' LIMIT 1;

-- Sinthuja Kamalrajan (580047) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2022-04-13', NULL,
    'unbefristet', 'Shift Coordinator',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580047' LIMIT 1;

-- Tülay Kara (580053) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2023-06-30', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580053' LIMIT 1;

-- Shallon Kemigisha (580094) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2025-12-12', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580094' LIMIT 1;

-- Tiziana Leotta (580027) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2024-06-10', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580027' LIMIT 1;

-- Zvezdana Mihajlovic (580057) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2021-11-30', NULL,
    'unbefristet', 'Crew',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580057' LIMIT 1;

-- Angela Miteva (580099) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2026-02-16', '2026-06-30',
    'befristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580099' LIMIT 1;

-- Jane MWangi (580022) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2023-06-21', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580022' LIMIT 1;

-- Fatma Oskay (580018) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2024-09-26', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580018' LIMIT 1;

-- Zeljka Pajic (580056) → MTP | hourly | 35 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2011-06-05', NULL,
    'unbefristet', 'Crew Trainer',
    NULL, 35.0, 35.0,
    21.5, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580056' LIMIT 1;

-- Dzenana Rakovic (580097) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2026-01-12', '2026-06-30',
    'befristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580097' LIMIT 1;

-- Pinar Renda (580041) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2024-10-02', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580041' LIMIT 1;

-- Florentina Sahiti (580076) → MTP | hourly | 38 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2025-06-01', NULL,
    'unbefristet', 'Crew',
    NULL, 38.0, 38.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580076' LIMIT 1;

-- Behija Salioska (580009) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2007-02-03', NULL,
    'unbefristet', '',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580009' LIMIT 1;

-- Besim Salioski (580010) → MTP | hourly | 34 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2020-08-21', NULL,
    'unbefristet', 'Crew Trainer',
    NULL, 34.0, 34.0,
    21.5, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580010' LIMIT 1;

-- Ludmila Scheibler-Fockova (580031) → FIX-M | monthly | 100%
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'FIX-M', 'monthly',
    '2011-06-18', NULL,
    'unbefristet', 'Shift Coordinator',
    100.0, NULL, NULL,
    NULL, 4800.0,
    8.33, 2.27, 8.33, 'vacation_account', true
FROM employee e WHERE e.employee_number = '580031' LIMIT 1;

-- Angela Skarcheska (580006) → FIX-M | monthly | 80%
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'FIX-M', 'monthly',
    '2022-08-01', NULL,
    'unbefristet', 'Shift Coordinator',
    80.0, NULL, NULL,
    NULL, 3800.0,
    8.33, 2.27, 8.33, 'vacation_account', true
FROM employee e WHERE e.employee_number = '580006' LIMIT 1;

-- Nazret Tesfazion (580036) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2024-08-16', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580036' LIMIT 1;

-- Rubina Tharmalingam (580098) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2025-11-03', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580098' LIMIT 1;

-- Thusyanthy Thivaparan (580052) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2023-06-21', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580052' LIMIT 1;

-- Kristian Tolic (580091) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2025-11-12', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580091' LIMIT 1;

-- Monika Tomikj (580034) → UTP | hourly | 17 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'UTP', 'hourly',
    '2024-05-21', NULL,
    'unbefristet', 'Crew',
    NULL, NULL, NULL,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580034' LIMIT 1;

-- Aleksandra Tomova (580003) → MTP | hourly | 21 Stunden/Woche
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'MTP', 'hourly',
    '2024-03-29', NULL,
    'unbefristet', 'Crew Trainer',
    NULL, 21.0, 21.0,
    20.36, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580003' LIMIT 1;

-- Natalija Trajchev (580074) → FIX | hourly | 60%
INSERT INTO employment (
    employee_id, company_profile_id, employment_model, salary_type,
    contract_start_date, contract_end_date, contract_type, job_title,
    employment_percentage, weekly_hours, guaranteed_hours_per_week,
    hourly_rate, monthly_salary,
    vacation_percent, holiday_percent, thirteenth_salary_percent, vacation_payment_mode,
    is_active
) SELECT e.id, (SELECT id FROM company_profile LIMIT 1),
    'FIX', 'hourly',
    '2025-05-21', NULL,
    'unbefristet', 'Hostess / Host',
    60.0, NULL, NULL,
    20.93, NULL,
    8.33, 2.27, 8.33, 'paid_with_salary', true
FROM employee e WHERE e.employee_number = '580074' LIMIT 1;


-- ── Übersprungene Mitarbeitende ──────────────────────────────────
-- SKIP Lazar Djordjevic (580072): kein Vertragstyp
-- SKIP Nihat Erdikli (580038): kein Vertragstyp
-- SKIP Parminder Kaur (580083): kein Vertragstyp
-- SKIP Patricia Rei Rodrigues Sobreira (580062): kein Vertragstyp
-- SKIP Tharsini Thileepan (580084): kein Vertragstyp
-- SKIP Melike Toprak (580081): kein Vertragstyp
-- SKIP Ella Torlac (580087): kein Vertragstyp
-- SKIP Hülya Yaver (580077): kein Vertragstyp

COMMIT;
