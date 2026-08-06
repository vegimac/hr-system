-- SV-Sätze pro Filiale (Walter-Vorgabe 05.08.2026).
-- Jede Filiale ist eine eigene GmbH — KTG/NBU/BU (etc.) können pro Filiale
-- abweichen (Beispiel: KTG ab 01.2026 in einer Filiale 1.945% statt global 2.15%).
-- company_profile_id NULL = globaler Standard für alle Filialen;
-- gesetzt = Override nur für diese Filiale (gewinnt dort gegen die globale
-- Zeile mit gleichem Fach-Schlüssel — Auflösung in
-- PayrollCalculations.SelectSvRatesForBranch).
-- Läuft auch idempotent beim Startup in Program.cs mit — diese Datei ist die
-- TablePlus-Doku (reines SQL, direkt ausführbar).

ALTER TABLE social_insurance_rate
ADD COLUMN IF NOT EXISTS company_profile_id integer;

-- Unique-Index auf den fachlichen Schlüssel neu inkl. Filial-Namensraum:
-- global (NULL → 0) und Filial-Override mit gleichem Schlüssel sind KEIN
-- Duplikat. Alter Index ohne Filial-Spalte wird ersetzt.
DO $$
BEGIN
    DROP INDEX IF EXISTS ux_social_insurance_rate_natural;
    IF NOT EXISTS (
        SELECT 1 FROM (
            SELECT 1 FROM social_insurance_rate
            GROUP BY code, valid_from, COALESCE(min_age, -1),
                     COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
                     basis_type, only_quellensteuer,
                     COALESCE(company_profile_id, 0)
            HAVING COUNT(*) > 1
        ) dup
    ) THEN
        CREATE UNIQUE INDEX IF NOT EXISTS ux_social_insurance_rate_natural2
        ON social_insurance_rate (
            code, valid_from, COALESCE(min_age, -1),
            COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
            basis_type, only_quellensteuer,
            COALESCE(company_profile_id, 0)
        );
    END IF;
END $$;
