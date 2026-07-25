-- Walter 25.07.2026: ToDo «Kündigung möglich» wenn Sperrfrist Art. 336c abgelaufen.
-- Auch idempotent in Program.cs geseedet — TablePlus-Variante für manuelles Nachziehen.
INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated,
     is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('kuendigung_sperrfrist_ende', 'Kündigung möglich (Sperrfrist Ende)', TRUE, 90, NULL,
     'warning', NULL, TRUE, 21, 25, 'red')
ON CONFLICT (category) DO UPDATE SET
    label         = EXCLUDED.label,
    warn_days     = COALESCE(dashboard_warning_config.warn_days, EXCLUDED.warn_days),
    severity_base = EXCLUDED.severity_base,
    todo_priority = CASE WHEN dashboard_warning_config.todo_priority = 100
                         THEN EXCLUDED.todo_priority
                         ELSE dashboard_warning_config.todo_priority END,
    warn_color    = CASE WHEN dashboard_warning_config.warn_color = 'none'
                         THEN EXCLUDED.warn_color
                         ELSE dashboard_warning_config.warn_color END;
