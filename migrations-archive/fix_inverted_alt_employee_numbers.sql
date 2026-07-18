-- ════════════════════════════════════════════════════════════════════
-- Archiv-Personalnummern: fehlendes «alt»-Suffix nachziehen
-- (Walter 18.07.2026)
--
-- Fall A — Sweeba Akhtar: Haupt «581039», Alias «581039alt»
--   (Sync hat Suffix abgestreift → Haupt/Alias vertauscht)
-- Fall B — Viktoria Mallo: Haupt «750223», Alias «104393alt»
--   (Austritt 2022 / Pre-Mirus, Haupt ohne «alt»)
--
-- TablePlus: diesen Block ausführen (kein psql-Wrapper). Idempotent.
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- ── Vorschau Fall A (Haupt N + Alias Nalt) ──────────────────────────
-- SELECT e.id, e.first_name, e.last_name, e.employee_number AS haupt,
--        a.number AS alias_nalt, e.exit_date
-- FROM employee e
-- JOIN employee_number_alias a ON a.employee_id = e.id
-- WHERE e.employee_number ~ '^\d+$'
--   AND lower(a.number) = lower(e.employee_number || 'alt')
--   AND COALESCE(e.is_hidden, false) = false
-- ORDER BY e.employee_number;

-- ── Vorschau Fall B (Pre-Mirus-Austritt, nackte Hauptnummer) ────────
-- SELECT e.id, e.first_name, e.last_name, e.employee_number AS haupt,
--        e.exit_date, e.is_active, e.entry_date
-- FROM employee e
-- WHERE e.employee_number ~ '^\d+$'
--   AND COALESCE(e.is_hidden, false) = false
--   AND e.exit_date IS NOT NULL
--   AND e.exit_date < DATE '2025-01-01'
--   AND NOT EXISTS (
--       SELECT 1 FROM employee_number_alias a
--       WHERE a.employee_id = e.id
--         AND lower(a.number) = lower(e.employee_number || 'alt')
--   )
-- ORDER BY e.employee_number;

-- ══ Fall A: Haupt N → Nalt, wenn Alias Nalt existiert ═══════════════
UPDATE employee e
SET employee_number = e.employee_number || 'alt'
FROM employee_number_alias a
WHERE a.employee_id = e.id
  AND e.employee_number ~ '^\d+$'
  AND lower(a.number) = lower(e.employee_number || 'alt')
  AND COALESCE(e.is_hidden, false) = false
  AND NOT EXISTS (
      SELECT 1 FROM employee x
      WHERE x.id <> e.id
        AND COALESCE(x.is_hidden, false) = false
        AND lower(x.employee_number) = lower(e.employee_number || 'alt')
  );

-- Überflüssigen Alias «Nalt» entfernen (ist jetzt die Hauptnummer)
DELETE FROM employee_number_alias a
USING employee e
WHERE a.employee_id = e.id
  AND e.employee_number ~ '^\d+alt$'
  AND lower(a.number) = lower(e.employee_number);

-- ══ Fall B: Pre-Mirus-Austritt (exit < 1.1.2025), nackte Hauptnummer ═
--    → «alt» anhängen (Viktoria Mallo 750223 u.a.)
UPDATE employee e
SET employee_number = e.employee_number || 'alt'
WHERE e.employee_number ~ '^\d+$'
  AND COALESCE(e.is_hidden, false) = false
  AND e.exit_date IS NOT NULL
  AND e.exit_date < DATE '2025-01-01'
  AND NOT EXISTS (
      SELECT 1 FROM employee x
      WHERE x.id <> e.id
        AND COALESCE(x.is_hidden, false) = false
        AND lower(x.employee_number) = lower(e.employee_number || 'alt')
  );

COMMIT;
