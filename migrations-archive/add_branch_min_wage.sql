-- Kommunaler/städtischer Mindestlohn pro Filiale (Walter-Vorgabe 23.05.2026)
-- Erfasst wird der JAHRESLOHN (brutto); Monats- (/13) und Stundenlohn
-- (/52/Wochenstunden) werden im Code berechnet. Versioniert über
-- valid_from/valid_to (Generationen). Hebt den L-GAV-Mindestlohn nach oben
-- (effektives Minimum = max(L-GAV, Filial-Floor)). In TablePlus ausführen.

CREATE TABLE IF NOT EXISTS branch_min_wage (
    id                 serial PRIMARY KEY,
    company_profile_id integer       NOT NULL REFERENCES company_profile(id),
    annual_salary      numeric(10,2) NOT NULL,
    applies_to_youth   boolean       NOT NULL DEFAULT false,
    valid_from         date          NOT NULL,
    valid_to           date,
    is_active          boolean       NOT NULL DEFAULT true,
    created_at         timestamp     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_branch_min_wage_branch
    ON branch_min_wage (company_profile_id, valid_from);
