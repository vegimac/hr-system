-- Walter 26.07.2026: Kündigung durch uns (AG) oder durch Mitarbeiter (AN).
-- In TablePlus ausführen.

ALTER TABLE employee
  ADD COLUMN IF NOT EXISTS kuendigung_durch text;

COMMENT ON COLUMN employee.kuendigung_durch IS
  'Kündigung durch: AG = durch uns (Arbeitgeber), AN = durch Mitarbeiter (Arbeitnehmer), NULL = nicht gesetzt';
