-- QST Wissens-Datum (Walter 15.09.2026)
-- gültig ab = Wirkung, erfahren_am = ab wann wir den Tarif kennen.
-- Altbestand: erfahren_am = valid_from (wir kannten ihn von Anfang an).

ALTER TABLE employee_quellensteuer
    ADD COLUMN IF NOT EXISTS erfahren_am date;

UPDATE employee_quellensteuer
   SET erfahren_am = valid_from
 WHERE erfahren_am IS NULL;
