-- Walter 26.07.2026: Austrittsgrund (kurze Codes für Statistik).
-- In TablePlus ausführen (optional — Startup legt die Spalte idempotent an).

ALTER TABLE employee
  ADD COLUMN IF NOT EXISTS austrittsgrund text;

COMMENT ON COLUMN employee.austrittsgrund IS
  'Austrittsgrund-Code: AUSBILDUNG, ANDERER_JOB, UMZUG, FAMILIE, GESUNDHEIT, ARBEITSZEITEN, LOHN, TEAM, PROBEZEIT, LEISTUNG, VERFUEGBARKEIT, VERHALTEN, BEFRISTUNG, DIVERS';
