-- Walter 26.07.2026: Austritt-ToDo bis zum Austrittstag (inkl.), danach weg.
-- Vorher: Karte erst NACH dem Austritt («MA noch aktiv»).
-- In TablePlus ausführen (optional — Startup in Program.cs macht das idempotent).

UPDATE dashboard_warning_config
   SET label = 'Austritt steht bevor',
       warn_days = 30,
       escalate_days = 7,
       severity_base = 'warning',
       severity_escalated = 'critical',
       is_date_based = TRUE
 WHERE category = 'exit_pending_active';
