-- Walter-Vorgabe 26.05.2026: BVG-Zusatz wird zur Person-Entscheidung.
-- Bisher hartcodiert über SocialInsuranceRate.EmploymentModelCode='FIX-M' (jeder
-- FIX-M-MA bekam automatisch BVG_ZUSATZ). Neu: pro MA versioniert pflegen, wer
-- am Stichtag Mitglied im Vorsorge-Programm ist — unabhängig vom Vertragstyp.
--
-- Migrationsweg „Leerer Start" (Walter): die Tabelle ist initial leer; Walter
-- pflegt selbst, welche MA reinkommen. Bis dahin werden für ALLE MA (auch
-- FIX-M) keine BVG_ZUSATZ-Beiträge mehr berechnet.

CREATE TABLE IF NOT EXISTS employee_bvg_zusatz_member (
    id          serial PRIMARY KEY,
    employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    valid_from  date    NOT NULL,
    valid_to    date    NULL,              -- NULL = aktuell offen / unbefristet
    bemerkung   text    NULL,
    created_at  timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by  integer NULL
);

CREATE INDEX IF NOT EXISTS ix_bvg_member_emp_period
    ON employee_bvg_zusatz_member (employee_id, valid_from);

-- Hilfreich für den Aktiv-am-Stichtag-Check (NULL-safe via COALESCE)
CREATE INDEX IF NOT EXISTS ix_bvg_member_emp_active
    ON employee_bvg_zusatz_member (employee_id)
    WHERE valid_to IS NULL;

-- Wichtig: BVG_ZUSATZ-SV-Sätze hatten bisher EmploymentModelCode='FIX-M'.
-- Da der Mitgliedschafts-Filter jetzt das automatische Gating übernimmt,
-- muss EmploymentModelCode auf NULL gesetzt werden — sonst würden
-- BVG-Zusatz-Mitglieder mit anderem Vertragsmodell (UTP/MTP/FIX) am
-- Vertragstyp-Filter scheitern. Walter-Vorgabe 26.05.2026.
UPDATE social_insurance_rate
   SET employment_model_code = NULL
 WHERE code = 'BVG_ZUSATZ';
