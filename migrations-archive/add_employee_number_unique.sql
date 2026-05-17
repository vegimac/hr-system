-- ════════════════════════════════════════════════════════════════════
-- UNIQUE-Constraint auf employee.employee_number
-- ════════════════════════════════════════════════════════════════════
-- Verhindert versehentliche Doppel-Nummern beim manuellen Anlegen oder
-- beim Re-Import. Notwendig für die saubere Trennung zwischen aktuellen
-- (z.B. "580096") und importierten ehemaligen MA (z.B. "580096alt").
-- ════════════════════════════════════════════════════════════════════

-- Sicherheits-Check: Wenn schon Duplikate da sind, abbrechen mit Liste
DO $$
DECLARE
    dup_list text;
BEGIN
    SELECT string_agg(employee_number || ' (' || cnt || 'x)', ', ')
    INTO dup_list
    FROM (
        SELECT employee_number, COUNT(*) AS cnt
        FROM employee
        WHERE employee_number IS NOT NULL AND employee_number <> ''
        GROUP BY employee_number
        HAVING COUNT(*) > 1
    ) dups;

    IF dup_list IS NOT NULL THEN
        RAISE EXCEPTION 'Doppelte employee_number gefunden: %. Bitte vor UNIQUE-Constraint bereinigen.', dup_list;
    END IF;
END $$;

-- UNIQUE-Constraint setzen (idempotent)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uk_employee_number') THEN
        ALTER TABLE employee
            ADD CONSTRAINT uk_employee_number UNIQUE (employee_number);
        RAISE NOTICE 'UNIQUE-Constraint uk_employee_number wurde gesetzt.';
    ELSE
        RAISE NOTICE 'UNIQUE-Constraint uk_employee_number existiert bereits — übersprungen.';
    END IF;
END $$;
