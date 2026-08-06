-- Geschlechts-Filter für SV-Sätze (Walter 06.08.2026, KTG-Fall): Versicherer
-- führten beim Krankentaggeld zeitweise getrennte Frauen-/Männer-Sätze.
-- NULL = gilt für alle; «F» = nur Frauen, «M» = nur Männer — bei Trennung
-- zwei Zeilen (F/M) erfassen (per «Duplizieren»). Matching zentral in
-- PayrollCalculations.GenderMatches (employee.gender, Anrede-Fallback).
-- Unique-Index des Fach-Schlüssels neu inkl. COALESCE(gender, '').
--
-- Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE social_insurance_rate
ADD COLUMN IF NOT EXISTS gender varchar(1);

DO $$
BEGIN
    DROP INDEX IF EXISTS ux_social_insurance_rate_natural;
    DROP INDEX IF EXISTS ux_social_insurance_rate_natural2;
    IF NOT EXISTS (
        SELECT 1 FROM (
            SELECT 1 FROM social_insurance_rate
            GROUP BY code, valid_from, COALESCE(min_age, -1),
                     COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
                     basis_type, only_quellensteuer,
                     COALESCE(company_profile_id, 0), COALESCE(gender, '')
            HAVING COUNT(*) > 1
        ) dup
    ) THEN
        CREATE UNIQUE INDEX IF NOT EXISTS ux_social_insurance_rate_natural3
        ON social_insurance_rate (
            code, valid_from, COALESCE(min_age, -1),
            COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
            basis_type, only_quellensteuer,
            COALESCE(company_profile_id, 0), COALESCE(gender, '')
        );
    END IF;
END $$;
