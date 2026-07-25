-- Walter 25.07.2026: ToDo «Kündigung nach Sperrfrist» = Kritisch
-- (nur bei durchgehender AU). Auch idempotent in Program.cs.
UPDATE dashboard_warning_config
SET severity_base = 'critical',
    warn_color    = 'red',
    label         = 'Kündigung möglich (durchgehende AU / Sperrfrist)',
    todo_priority = CASE WHEN todo_priority > 20 THEN 12 ELSE todo_priority END
WHERE category = 'kuendigung_sperrfrist_ende';
