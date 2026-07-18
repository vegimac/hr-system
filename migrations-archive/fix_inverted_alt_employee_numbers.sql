-- ════════════════════════════════════════════════════════════════════
-- Invertierte Archiv-Personalnummern korrigieren (Walter 18.07.2026)
-- Fall: Sweeba Akhtar — Hauptnummer «581039», Alias «581039alt»
-- Ursache: easy@work-Sync hat das «alt»-Suffix abgestreift.
--
-- TablePlus: diesen Block ausführen (kein psql-Wrapper).
-- Idempotent: nur Zeilen, bei denen Haupt = N und Alias = N||'alt'.
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- Vorschau (optional, vor dem UPDATE anschauen):
-- SELECT e.id, e.first_name, e.last_name, e.employee_number AS haupt,
--        a.number AS alias_alt, e.entry_date
-- FROM employee e
-- JOIN employee_number_alias a ON a.employee_id = e.id
-- WHERE e.employee_number ~ '^\d+$'
--   AND lower(a.number) = lower(e.employee_number || 'alt')
--   AND COALESCE(e.is_hidden, false) = false
-- ORDER BY e.employee_number;

-- 1) Hauptnummer zurück auf …alt
UPDATE employee e
SET employee_number = e.employee_number || 'alt'
FROM employee_number_alias a
WHERE a.employee_id = e.id
  AND e.employee_number ~ '^\d+$'
  AND lower(a.number) = lower(e.employee_number || 'alt')
  AND COALESCE(e.is_hidden, false) = false
  -- Kollisionsschutz: …alt darf noch keinem anderen MA gehören
  AND NOT EXISTS (
      SELECT 1 FROM employee x
      WHERE x.id <> e.id
        AND COALESCE(x.is_hidden, false) = false
        AND lower(x.employee_number) = lower(e.employee_number || 'alt')
  );

-- 2) Überflüssig gewordenen Alias «Nalt» entfernen (ist jetzt die Hauptnummer)
DELETE FROM employee_number_alias a
USING employee e
WHERE a.employee_id = e.id
  AND e.employee_number ~ '^\d+alt$'
  AND lower(a.number) = lower(e.employee_number);

COMMIT;
