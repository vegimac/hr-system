-- Walter-Vorgabe 25.08.2026: «Weitere Beschäftigungen des MA» braucht die
-- VOLLE Adresse des anderen Arbeitgebers (kantonales QST-Anmeldeformular:
-- Name / Strasse / PLZ/Ort/Kanton / Land / Gesamtpensum %). Das Einkommen
-- wird bewusst nicht mehr erfasst (Spalte gesamteinkommen_weitere_ag bleibt
-- als Altbestand stehen). Läuft zusätzlich idempotent beim App-Start.

ALTER TABLE employee_quellensteuer
    ADD COLUMN IF NOT EXISTS weitere_ag_name    VARCHAR(150),
    ADD COLUMN IF NOT EXISTS weitere_ag_strasse VARCHAR(150),
    ADD COLUMN IF NOT EXISTS weitere_ag_plz     VARCHAR(10),
    ADD COLUMN IF NOT EXISTS weitere_ag_ort     VARCHAR(120),
    ADD COLUMN IF NOT EXISTS weitere_ag_kanton  VARCHAR(10),
    ADD COLUMN IF NOT EXISTS weitere_ag_land    VARCHAR(60);
