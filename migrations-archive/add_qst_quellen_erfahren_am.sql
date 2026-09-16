-- QST-Quellen: «erfahren am» (Walter 15.09.2026)
-- Wirkung bleibt qst_deductible_from / valid_from / gueltig_ab.
-- NULL = gleich wie die Wirkung (Altbestand).

ALTER TABLE employee_family_member
    ADD COLUMN IF NOT EXISTS erfahren_am date;

ALTER TABLE employee_permit_history
    ADD COLUMN IF NOT EXISTS erfahren_am date;

ALTER TABLE employee_zivilstand_history
    ADD COLUMN IF NOT EXISTS erfahren_am date;
