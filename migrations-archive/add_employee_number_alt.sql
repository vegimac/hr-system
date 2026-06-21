-- Alte/zweite Personalnummern als zusätzliche Match-Schlüssel (Walter 21.06.2026)
-- In TablePlus ausführen.
-- Ein MA kann in easy@work unter einer früheren Nummer geführt sein. Diese Felder
-- erlauben dem MA- und Stempelzeiten-Sync, den MA trotz abweichender aktueller
-- employee_number zu finden.

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS employee_number_alt1 text,
    ADD COLUMN IF NOT EXISTS employee_number_alt2 text;
