-- AHV-Nummer-fehlt-Warnung (Walter-Vorgabe 06.08.2026, kritisch)
-- In TablePlus ausführen (läuft auch idempotent beim Server-Start in Program.cs).
INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('ahv_nummer_fehlt', 'AHV-Nummer fehlt', TRUE, NULL, NULL, 'critical', NULL, FALSE, 26, 16, 'red')
ON CONFLICT (category) DO NOTHING;
